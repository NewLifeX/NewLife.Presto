using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace NewLife.Presto;

/// <summary>Presto/Trino 命令</summary>
/// <remarks>
/// 仅支持 <see cref="CommandType.Text"/>；一次提交一条 SQL。
/// `@name` 参数在客户端替换为字面量；取消与超时通过 CTS 贯穿提交、轮询与读取。
/// </remarks>
public sealed class PrestoCommand : DbCommand
{
    #region 字段
    private readonly PrestoParameterCollection _parameters = new();
    private PrestoConnection? _connection;
    private String _commandText = String.Empty;
    private CommandType _commandType = CommandType.Text;
    private Int32 _commandTimeout = -1;
    private CancellationTokenSource? _cancellation;
    private CancellationTokenSource? _timeoutSource;
    private PrestoStatementClient? _client;
    private String? _lastQueryId;
    #endregion

    #region 属性
    /// <summary>连接</summary>
    public new PrestoConnection? Connection
    {
        get => _connection;
        set => _connection = value;
    }

    /// <summary>连接</summary>
    protected override DbConnection? DbConnection
    {
        get => _connection;
        set
        {
            if (value != null && value is not PrestoConnection)
                throw new ArgumentException(Localization.Translate("The connection must be a PrestoConnection."), nameof(value));

            _connection = (PrestoConnection?)value;
        }
    }

    /// <summary>命令文本</summary>
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
    [System.Diagnostics.CodeAnalysis.AllowNull]
#endif
    public override String CommandText
    {
        get => _commandText;
        set => _commandText = value ?? String.Empty;
    }

    /// <summary>命令超时秒数。0 表示不限制；未显式设置时取连接配置</summary>
    public override Int32 CommandTimeout
    {
        get => _commandTimeout >= 0 ? _commandTimeout : _connection?.Settings.CommandTimeout ?? 30;
        set => _commandTimeout = value;
    }

    /// <summary>命令类型。仅支持 Text</summary>
    public override CommandType CommandType
    {
        get => _commandType;
        set
        {
            if (value != CommandType.Text) throw PrestoUnsupported.CommandType((Int32)value);

            _commandType = value;
        }
    }

    /// <summary>设计时可见。恒为 false</summary>
    public override Boolean DesignTimeVisible { get; set; }

    /// <summary>更新行来源</summary>
    public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;

    /// <summary>参数集合</summary>
    protected override DbParameterCollection DbParameterCollection => _parameters;

    /// <summary>事务。Presto/Trino 不支持</summary>
    protected override DbTransaction? DbTransaction
    {
        get => null;
        set
        {
            if (value != null) throw PrestoUnsupported.Transactions();
        }
    }

    /// <summary>最近一次查询编号</summary>
    public String? LastQueryId => _client?.QueryId ?? _lastQueryId;
    #endregion

    #region 构造
    /// <summary>实例化命令</summary>
    public PrestoCommand() { }

    /// <summary>实例化命令</summary>
    /// <param name="commandText">命令文本</param>
    public PrestoCommand(String commandText) => CommandText = commandText;

    /// <summary>实例化命令</summary>
    /// <param name="commandText">命令文本</param>
    /// <param name="connection">连接</param>
    public PrestoCommand(String commandText, PrestoConnection connection)
    {
        CommandText = commandText;
        Connection = connection;
    }

    /// <summary>实例化命令</summary>
    /// <param name="connection">连接</param>
    public PrestoCommand(PrestoConnection connection) => Connection = connection;
    #endregion

    #region 执行
    /// <summary>执行非查询命令</summary>
    /// <returns>受影响行数；无法判断时为 -1</returns>
    public override Int32 ExecuteNonQuery() => ExecuteNonQueryAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>执行非查询命令</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>受影响行数；无法判断时为 -1</returns>
    public override async Task<Int32> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        using var reader = await ExecuteReaderCoreAsync(CommandBehavior.Default, cancellationToken).ConfigureAwait(false);

        var count = reader.RecordsAffected;
        if (count < 0 && reader.FieldCount == 1 && reader.GetName(0).EqualIgnoreCase("rows"))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var value = reader.GetValue(0);
                if (value != DBNull.Value)
                {
                    var result = System.Convert.ToInt64(value, CultureInfo.InvariantCulture);
                    count = result > Int32.MaxValue ? Int32.MaxValue : (Int32)result;
                }
            }
        }
        else
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) { }
            count = reader.RecordsAffected;
        }

        if (count < 0 && !reader.UpdateType.IsNullOrEmpty()) count = 0;

        return count;
    }

    /// <summary>执行查询并返回首行首列</summary>
    /// <returns>首行首列；无数据时为 null</returns>
    public override Object? ExecuteScalar() => ExecuteScalarAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>执行查询并返回首行首列</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>首行首列；无数据时为 null</returns>
    public override async Task<Object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        using var reader = await ExecuteReaderCoreAsync(CommandBehavior.Default, cancellationToken).ConfigureAwait(false);

        if (reader.FieldCount == 0) return null;
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        return reader.GetValue(0);
    }

    /// <summary>执行查询并返回读取器</summary>
    /// <param name="behavior">命令行为</param>
    /// <returns>读取器</returns>
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        => ExecuteReaderCoreAsync(behavior, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>执行查询并返回读取器</summary>
    /// <param name="behavior">命令行为</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>读取器</returns>
    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        => ExecuteReaderCoreCoreAsync(behavior, cancellationToken);

    private async Task<DbDataReader> ExecuteReaderCoreCoreAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        => await ExecuteReaderCoreAsync(behavior, cancellationToken).ConfigureAwait(false);

    private async Task<PrestoDataReader> ExecuteReaderCoreAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        var connection = _connection;
        if (connection == null || connection.State != ConnectionState.Open)
            throw new InvalidOperationException(Localization.Translate("The connection is not open."));

        ValidateBehavior(behavior);

        if (CommandText.IsNullOrWhiteSpace())
            throw new InvalidOperationException(Localization.Translate("CommandText must not be empty."));

        var sql = PrestoParameterBinder.Bind(CommandText, _parameters);
        var timeout = CommandTimeout;
        var timeoutSource = timeout > 0 ? new CancellationTokenSource(TimeSpan.FromSeconds(timeout)) : null;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource?.Token ?? CancellationToken.None);

        _cancellation = cancellation;
        _timeoutSource = timeoutSource;
        _client = connection.CreateStatementClient();

        try
        {
            var results = await _client.SubmitAsync(sql, cancellation.Token).ConfigureAwait(false);

            // 真实服务端的首包是 QUEUED/RUNNING，不含列定义；推进到出现列或查询结束，否则读取器拿不到字段
            results = await FetchToColumnsAsync(_client, results, cancellation.Token).ConfigureAwait(false);
            _lastQueryId = results.Id ?? _lastQueryId;

            if ((behavior & CommandBehavior.SchemaOnly) != 0)
            {
                // 只需要列信息，拿到列定义后立即取消，避免服务端继续计算数据
                await _client.CancelAsync().ConfigureAwait(false);
            }

            connection.RegisterCommand(this);

            return new PrestoDataReader(_client, this, results, behavior, cancellation.Token, timeoutSource, timeout);
        }
        catch (OperationCanceledException ex) when (timeoutSource?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
        {
            DisposeExecution();
            throw PrestoException.CommandTimeout(timeout, ex);
        }
        catch
        {
            DisposeExecution();
            throw;
        }
    }

    /// <summary>按页推进，直到出现列定义或查询结束</summary>
    private static async Task<PrestoQueryResults> FetchToColumnsAsync(PrestoStatementClient client, PrestoQueryResults results, CancellationToken cancellationToken)
    {
        // DDL/DML 没有列定义，循环在查询结束时退出；列定义必定先于数据行出现，因此不会丢行
        while (results.Columns.Count == 0 && !results.NextUri.IsNullOrEmpty())
        {
            results = await client.FetchAsync(results.NextUri!, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    private static void ValidateBehavior(CommandBehavior behavior)
    {
        const CommandBehavior supported = CommandBehavior.Default | CommandBehavior.SingleResult | CommandBehavior.SchemaOnly | CommandBehavior.SingleRow | CommandBehavior.CloseConnection;
        if ((behavior & ~supported) != 0) throw PrestoUnsupported.CommandBehavior((Int32)behavior);
    }
    #endregion

    #region 取消
    /// <summary>取消当前命令</summary>
    public override void Cancel()
    {
        _cancellation?.Cancel();

        var client = _client;
        if (client != null) _ = client.CancelAsync();
    }

    /// <summary>读取器关闭时释放执行上下文</summary>
    /// <param name="reader">读取器</param>
    internal void OnReaderClosed(PrestoDataReader reader)
    {
        _connection?.UnregisterCommand(this);

        _client = null;
        _cancellation?.Dispose();
        _cancellation = null;
        _timeoutSource?.Dispose();
        _timeoutSource = null;
    }

    private void DisposeExecution()
    {
        _client = null;
        _cancellation?.Dispose();
        _cancellation = null;
        _timeoutSource?.Dispose();
        _timeoutSource = null;
    }
    #endregion

    #region 其它
    /// <summary>创建参数</summary>
    /// <returns>参数</returns>
    protected override DbParameter CreateDbParameter() => new PrestoParameter();

    /// <summary>准备命令。Presto 无服务端预编译，空实现</summary>
    public override void Prepare() { }

    /// <summary>释放命令</summary>
    /// <param name="disposing">是否释放托管资源</param>
    protected override void Dispose(Boolean disposing)
    {
        if (disposing)
        {
            Cancel();
            DisposeExecution();
        }

        base.Dispose(disposing);
    }
    #endregion
}
