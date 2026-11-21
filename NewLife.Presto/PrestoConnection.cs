using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NewLife.Log;

namespace NewLife.Presto;

/// <summary>Presto/Trino 连接</summary>
/// <remarks>
/// 连接对象按 ADO.NET 约定非线程安全；共享 <see cref="HttpClient"/> 线程安全。
/// Open 默认不发起网络请求，`TestConnection=true` 时才请求 `/v1/info`。
/// </remarks>
public sealed class PrestoConnection : DbConnection
{
    #region 字段
    private PrestoConnectionStringBuilder _settings = new();
    private PrestoConnectionOptions? _options;
    private PrestoSessionState? _session;
    private HttpClient? _client;
    private readonly HttpMessageHandler? _handler;
    private readonly Boolean _ownsClient;
    private readonly List<PrestoCommand> _commands = [];
    private ConnectionState _state = ConnectionState.Closed;
    private PrestoServerInfo? _serverInfo;
    #endregion

    #region 构造
    /// <summary>实例化连接</summary>
    public PrestoConnection() { }

    /// <summary>实例化连接</summary>
    /// <param name="connectionString">连接字符串</param>
    public PrestoConnection(String connectionString) => ConnectionString = connectionString;

    /// <summary>实例化连接，注入自定义 HTTP 处理器。用于测试或自定义传输</summary>
    /// <param name="connectionString">连接字符串</param>
    /// <param name="handler">HTTP 处理器</param>
    internal PrestoConnection(String connectionString, HttpMessageHandler handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _ownsClient = true;
        ConnectionString = connectionString;
    }
    #endregion

    #region 属性
    /// <summary>连接字符串</summary>
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
    [System.Diagnostics.CodeAnalysis.AllowNull]
#endif
    public override String ConnectionString
    {
        get => _settings.ConnectionString;
        set
        {
            if (State != ConnectionState.Closed)
                throw new InvalidOperationException(Localization.Translate("The connection string cannot be changed while the connection is open."));

            _settings = new PrestoConnectionStringBuilder(value ?? String.Empty);
        }
    }

    /// <summary>连接配置</summary>
    public PrestoConnectionStringBuilder Settings => _settings;

    /// <summary>当前数据库，即 Catalog</summary>
    public override String Database => _session?.Catalog ?? _settings.Catalog;

    /// <summary>当前 Catalog</summary>
    public String Catalog => _session?.Catalog ?? _settings.Catalog;

    /// <summary>当前 Schema</summary>
    public String Schema => _session?.Schema ?? _settings.Schema;

    /// <summary>协调器主机</summary>
    public override String DataSource => _settings.Host;

    /// <summary>服务端版本。连接打开时按需请求 `/v1/info`</summary>
    public override String ServerVersion
    {
        get
        {
            if (_serverInfo?.NodeVersion is { Length: > 0 } version) return version;
            if (State != ConnectionState.Open) return String.Empty;

            _serverInfo ??= GetServerInfo();

            return _serverInfo?.NodeVersion ?? String.Empty;
        }
    }

    /// <summary>连接状态</summary>
    public override ConnectionState State => _state;

    /// <summary>单次 HTTP 请求超时秒数</summary>
    public override Int32 ConnectionTimeout => _settings.ConnectionTimeout;

    /// <summary>日志。默认 <see cref="XTrace.Log"/></summary>
    public ILog? Log { get; set; } = XTrace.Log;

    /// <summary>JWT 令牌刷新回调。每次请求前调用，返回空时回退连接字符串中的 AccessToken</summary>
    public Func<String>? AccessTokenProvider { get; set; }

    /// <summary>最近一次获取的服务端信息</summary>
    public PrestoServerInfo? ServerInfo => _serverInfo;

    /// <summary>Schema 集合缓存</summary>
    internal Dictionary<String, PrestoSchemaCacheEntry> SchemaCache { get; } = new(StringComparer.Ordinal);

    /// <summary>Schema 集合缓存有效期</summary>
    internal TimeSpan SchemaCacheExpire { get; set; } = TimeSpan.FromSeconds(30);
    #endregion

    #region 打开与关闭
    /// <summary>打开连接</summary>
    public override void Open() => OpenAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>打开连接</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>异步任务</returns>
    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (State == ConnectionState.Open) throw new InvalidOperationException(Localization.Translate("The connection is already open."));
        if (State == ConnectionState.Broken) Close();

        var options = PrestoConnectionOptions.From(_settings);
        using var span = PrestoDiagnostics.StartOpen(options);
        try
        {
            _options = options;
            _session = new PrestoSessionState(options);
            _client = _handler != null
                ? new HttpClient(_handler, false) { BaseAddress = options.BaseAddress, Timeout = Timeout.InfiniteTimeSpan }
                : PrestoHttpClientCache.Get(options);

            if (options.TrustServerCertificate)
                Log?.Warn("TrustServerCertificate is enabled; server certificate validation is disabled.");

            SetState(ConnectionState.Open);

            if (options.TestConnection) _serverInfo = await GetServerInfoAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            span.SetError(ex);

            if (_ownsClient) _client?.Dispose();
            _client = null;
            _session = null;
            _options = null;
            SetState(ConnectionState.Closed);

            throw;
        }
    }

    /// <summary>关闭连接</summary>
    public override void Close()
    {
        if (State == ConnectionState.Closed) return;

        PrestoCommand[] commands;
        lock (_commands)
        {
            commands = _commands.ToArray();
            _commands.Clear();
        }
        foreach (var command in commands) command.Cancel();

        if (_ownsClient) _client?.Dispose();

        _client = null;
        _session = null;
        _options = null;
        _serverInfo = null;
        SchemaCache.Clear();

        SetState(ConnectionState.Closed);
    }

    /// <summary>切换 Catalog 与 Schema</summary>
    /// <param name="databaseName">`catalog` 或 `catalog.schema`</param>
    public override void ChangeDatabase(String databaseName)
    {
        EnsureOpen();

        if (databaseName.IsNullOrWhiteSpace())
            throw new ArgumentException(Localization.Translate("Database name cannot be empty."), nameof(databaseName));

        var parts = databaseName.Split(['.'], 2);
        var catalog = parts[0].Trim();
        var schema = parts.Length > 1 ? parts[1].Trim() : _session?.Schema;

        _session?.ChangeCatalog(catalog, schema);
        _settings.Catalog = catalog;
        if (parts.Length > 1) _settings.Schema = schema ?? String.Empty;

        SchemaCache.Clear();
    }
    #endregion

    #region 创建对象
    /// <summary>创建命令</summary>
    /// <returns>命令</returns>
    public new PrestoCommand CreateCommand() => new() { Connection = this };

    /// <summary>创建命令</summary>
    /// <param name="commandText">命令文本</param>
    /// <returns>命令</returns>
    public PrestoCommand CreateCommand(String commandText) => new(commandText, this);

    /// <summary>创建命令</summary>
    /// <returns>命令</returns>
    protected override DbCommand CreateDbCommand() => new PrestoCommand { Connection = this };

    /// <summary>开始事务。Presto/Trino 不支持</summary>
    /// <param name="isolationLevel">隔离级别</param>
    /// <returns>不会返回</returns>
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw PrestoUnsupported.Transactions();
    #endregion

    #region 元数据
    /// <summary>获取元数据集合</summary>
    /// <returns>MetaDataCollections</returns>
    public new DataTable GetSchema() => GetSchema("MetaDataCollections");

    /// <summary>获取指定元数据集合</summary>
    /// <param name="collectionName">集合名</param>
    /// <returns>元数据表</returns>
    public new DataTable GetSchema(String collectionName) => GetSchema(collectionName, null);

    /// <summary>获取指定元数据集合</summary>
    /// <param name="collectionName">集合名</param>
    /// <param name="restrictionValues">过滤值</param>
    /// <returns>元数据表</returns>
    public override DataTable GetSchema(String collectionName, String?[]? restrictionValues)
    {
        EnsureOpen();

        return PrestoMetaData.GetSchema(this, collectionName, restrictionValues);
    }

    /// <summary>获取服务端信息</summary>
    /// <returns>服务端信息</returns>
    public PrestoServerInfo GetServerInfo() => GetServerInfoAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>获取服务端信息</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务端信息</returns>
    public async Task<PrestoServerInfo> GetServerInfoAsync(CancellationToken cancellationToken)
    {
        EnsureOpen();

        var client = CreateStatementClient();
        var info = await client.GetServerInfoAsync(cancellationToken).ConfigureAwait(false);
        _serverInfo = info;

        return info;
    }
    #endregion

    #region 内部协作
    /// <summary>创建语句客户端</summary>
    /// <returns>语句客户端</returns>
    internal PrestoStatementClient CreateStatementClient()
    {
        if (_client == null || _options == null || _session == null)
            throw new InvalidOperationException(Localization.Translate("The connection is not open."));

        return new PrestoStatementClient(_client, _options, _session, Log) { AccessTokenProvider = AccessTokenProvider };
    }

    /// <summary>注册活跃命令</summary>
    /// <param name="command">命令</param>
    internal void RegisterCommand(PrestoCommand command)
    {
        lock (_commands) _commands.Add(command);
    }

    /// <summary>注销活跃命令</summary>
    /// <param name="command">命令</param>
    internal void UnregisterCommand(PrestoCommand command)
    {
        lock (_commands) _commands.Remove(command);
    }

    /// <summary>确保连接已打开</summary>
    internal void EnsureOpen()
    {
        if (State != ConnectionState.Open)
            throw new InvalidOperationException(Localization.Translate("The connection is not open."));
    }

    private void SetState(ConnectionState state)
    {
        if (_state == state) return;

        var old = _state;
        _state = state;
        OnStateChange(new StateChangeEventArgs(old, state));
    }
    #endregion

    #region 释放
    /// <summary>释放连接</summary>
    /// <param name="disposing">是否释放托管资源</param>
    protected override void Dispose(Boolean disposing)
    {
        if (disposing) Close();

        base.Dispose(disposing);
    }
    #endregion
}
