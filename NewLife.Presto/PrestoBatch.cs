#if NET6_0_OR_GREATER
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace NewLife.Presto;

/// <summary>Presto/Trino 批量命令</summary>
/// <remarks>
/// REST 协议没有服务端批量语义，命令按添加顺序逐条提交。
/// <see cref="DbBatch.ExecuteNonQuery()"/> 汇总各命令的受影响行数（全部未知时返回 -1）；
/// <see cref="DbBatch.ExecuteReader(CommandBehavior)"/> 与 <see cref="DbBatch.ExecuteScalar()"/> 返回最后一条命令的结果。
/// </remarks>
public sealed class PrestoBatch : DbBatch
{
    #region 字段
    private readonly PrestoBatchCommandCollection _commands = new();
    private PrestoConnection? _connection;
    private Int32 _timeout = -1;
    private CancellationTokenSource? _cancellation;
    private CancellationTokenSource? _timeoutSource;
    #endregion

    #region 属性
    /// <summary>批量命令集合</summary>
    protected override DbBatchCommandCollection DbBatchCommands => _commands;

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

    /// <summary>事务。Presto/Trino 不支持</summary>
    protected override DbTransaction? DbTransaction
    {
        get => null;
        set
        {
            if (value != null) throw PrestoUnsupported.Transactions();
        }
    }

    /// <summary>命令超时秒数。0 表示不限制；未显式设置时取连接配置</summary>
    public override Int32 Timeout
    {
        get => _timeout >= 0 ? _timeout : _connection?.Settings.CommandTimeout ?? 30;
        set => _timeout = value;
    }
    #endregion

    #region 执行
    /// <summary>执行全部命令并返回受影响行数之和</summary>
    /// <returns>受影响行数之和；全部无法判断时为 -1</returns>
    public override Int32 ExecuteNonQuery() => ExecuteNonQueryAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>执行全部命令并返回受影响行数之和</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>受影响行数之和；全部无法判断时为 -1</returns>
    public override async Task<Int32> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        var commands = GetCommands();
        if (commands.Count == 0) return 0;

        var cancellation = CreateCancellation(cancellationToken);
        try
        {
            var total = 0;
            var known = false;
            foreach (var item in commands)
            {
                using var command = CreateCommand(item);
                var count = await command.ExecuteNonQueryAsync(cancellation.Token).ConfigureAwait(false);
                if (item is PrestoBatchCommand presto) presto.RecordsAffectedInternal = count;

                if (count >= 0)
                {
                    total += count;
                    known = true;
                }
            }

            return known ? total : -1;
        }
        finally
        {
            ReleaseCancellation();
        }
    }

    /// <summary>执行全部命令并返回最后一条命令的首行首列</summary>
    /// <returns>首行首列；无数据时为 null</returns>
    public override Object? ExecuteScalar() => ExecuteScalarAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>执行全部命令并返回最后一条命令的首行首列</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>首行首列；无数据时为 null</returns>
    public override async Task<Object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        var commands = GetCommands();
        if (commands.Count == 0) return null;

        var cancellation = CreateCancellation(cancellationToken);
        try
        {
            for (var i = 0; i < commands.Count - 1; i++)
            {
                using var command = CreateCommand(commands[i]);
                var count = await command.ExecuteNonQueryAsync(cancellation.Token).ConfigureAwait(false);
                if (commands[i] is PrestoBatchCommand presto) presto.RecordsAffectedInternal = count;
            }

            var last = commands[^1];
            using var final = CreateCommand(last);
            using var reader = await final.ExecuteReaderAsync(cancellation.Token).ConfigureAwait(false);

            if (reader.FieldCount == 0) return null;
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

            return reader.GetValue(0);
        }
        finally
        {
            ReleaseCancellation();
        }
    }

    /// <summary>执行查询并返回最后一条命令的读取器</summary>
    /// <param name="behavior">命令行为</param>
    /// <returns>读取器</returns>
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        => ExecuteReaderCoreAsync(behavior, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>执行查询并返回最后一条命令的读取器</summary>
    /// <param name="behavior">命令行为</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>读取器</returns>
    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
        => await ExecuteReaderCoreAsync(behavior, cancellationToken).ConfigureAwait(false);

    private async Task<DbDataReader> ExecuteReaderCoreAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        var commands = GetCommands();
        if (commands.Count == 0) throw new InvalidOperationException(Localization.Translate("The batch contains no commands."));

        // 读取器在关闭前持续使用取消令牌，执行范围由批次释放
        var cancellation = CreateCancellation(cancellationToken);
        try
        {
            for (var i = 0; i < commands.Count - 1; i++)
            {
                using var command = CreateCommand(commands[i]);
                var count = await command.ExecuteNonQueryAsync(cancellation.Token).ConfigureAwait(false);
                if (commands[i] is PrestoBatchCommand presto) presto.RecordsAffectedInternal = count;
            }

            var last = commands[^1];
            var final = CreateCommand(last);

            return await final.ExecuteReaderAsync(behavior, cancellation.Token).ConfigureAwait(false);
        }
        catch
        {
            ReleaseCancellation();
            throw;
        }
    }
    #endregion

    #region 取消与准备
    /// <summary>取消当前批次</summary>
    public override void Cancel() => _cancellation?.Cancel();

    /// <summary>准备命令。Presto 无服务端预编译，空实现</summary>
    public override void Prepare() { }

    /// <summary>准备命令。Presto 无服务端预编译，空实现</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>异步任务</returns>
    public override Task PrepareAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    #endregion

    #region 辅助
    /// <summary>创建批量命令</summary>
    /// <returns>批量命令</returns>
    protected override DbBatchCommand CreateDbBatchCommand() => new PrestoBatchCommand();

    private List<DbBatchCommand> GetCommands()
    {
        var list = new List<DbBatchCommand>(_commands.Count);
        foreach (var item in _commands) list.Add(item);

        return list;
    }

    private PrestoCommand CreateCommand(DbBatchCommand batchCommand)
    {
        var connection = _connection!;
        if (batchCommand.CommandText.IsNullOrWhiteSpace())
            throw new InvalidOperationException(Localization.Translate("CommandText must not be empty."));

        var command = connection.CreateCommand(batchCommand.CommandText);
        command.CommandTimeout = Timeout;

        foreach (DbParameter parameter in batchCommand.Parameters)
        {
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private CancellationTokenSource CreateCancellation(CancellationToken cancellationToken)
    {
        var connection = _connection;
        if (connection == null || connection.State != ConnectionState.Open)
            throw new InvalidOperationException(Localization.Translate("The connection is not open."));

        ReleaseCancellation();

        var timeout = Timeout;
        _timeoutSource = timeout > 0 ? new CancellationTokenSource(TimeSpan.FromSeconds(timeout)) : null;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _timeoutSource?.Token ?? CancellationToken.None);

        return _cancellation;
    }

    private void ReleaseCancellation()
    {
        _cancellation?.Dispose();
        _cancellation = null;
        _timeoutSource?.Dispose();
        _timeoutSource = null;
    }

    /// <summary>释放批次</summary>
    public override void Dispose()
    {
        Cancel();
        ReleaseCancellation();

        base.Dispose();
    }
    #endregion
}

/// <summary>批量命令项</summary>
public sealed class PrestoBatchCommand : DbBatchCommand
{
    #region 字段
    private readonly PrestoParameterCollection _parameters = new();
    private String _commandText = String.Empty;
    private CommandType _commandType = CommandType.Text;
    private Int32 _recordsAffected = -1;
    #endregion

    #region 属性
    /// <summary>命令文本</summary>
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
    [System.Diagnostics.CodeAnalysis.AllowNull]
#endif
    public override String CommandText
    {
        get => _commandText;
        set => _commandText = value ?? String.Empty;
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

    /// <summary>受影响行数。尚未执行时为 -1</summary>
    public override Int32 RecordsAffected => _recordsAffected;

    /// <summary>参数集合</summary>
    protected override DbParameterCollection DbParameterCollection => _parameters;

    /// <summary>是否支持创建参数</summary>
#if NET7_0_OR_GREATER
    public override Boolean CanCreateParameter => true;
#endif

    internal Int32 RecordsAffectedInternal { set => _recordsAffected = value; }
    #endregion

    #region 方法
    /// <summary>创建参数</summary>
    /// <returns>参数</returns>
#if NET7_0_OR_GREATER
    public override DbParameter CreateParameter() => new PrestoParameter();
#else
    public DbParameter CreateParameter() => new PrestoParameter();
#endif
    #endregion
}

/// <summary>批量命令集合</summary>
public sealed class PrestoBatchCommandCollection : DbBatchCommandCollection
{
    private readonly List<DbBatchCommand> _items = [];

    /// <summary>命令数量</summary>
    public override Int32 Count => _items.Count;

    /// <summary>是否只读</summary>
    public override Boolean IsReadOnly => false;

    /// <summary>添加命令</summary>
    /// <param name="item">命令</param>
    public override void Add(DbBatchCommand item) => _items.Add(item);

    /// <summary>清空命令</summary>
    public override void Clear() => _items.Clear();

    /// <summary>是否包含命令</summary>
    /// <param name="item">命令</param>
    /// <returns>是否包含</returns>
    public override Boolean Contains(DbBatchCommand item) => _items.Contains(item);

    /// <summary>复制到数组</summary>
    /// <param name="array">目标数组</param>
    /// <param name="arrayIndex">起始索引</param>
    public override void CopyTo(DbBatchCommand[] array, Int32 arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <summary>获取枚举器</summary>
    /// <returns>枚举器</returns>
    public override IEnumerator<DbBatchCommand> GetEnumerator() => _items.GetEnumerator();

    /// <summary>获取命令索引</summary>
    /// <param name="item">命令</param>
    /// <returns>索引；不存在返回 -1</returns>
    public override Int32 IndexOf(DbBatchCommand item) => _items.IndexOf(item);

    /// <summary>插入命令</summary>
    /// <param name="index">索引</param>
    /// <param name="item">命令</param>
    public override void Insert(Int32 index, DbBatchCommand item) => _items.Insert(index, item);

    /// <summary>移除命令</summary>
    /// <param name="item">命令</param>
    /// <returns>是否移除</returns>
    public override Boolean Remove(DbBatchCommand item) => _items.Remove(item);

    /// <summary>按索引移除命令</summary>
    /// <param name="index">索引</param>
    public override void RemoveAt(Int32 index) => _items.RemoveAt(index);

    /// <summary>按索引获取命令</summary>
    /// <param name="index">索引</param>
    /// <returns>命令</returns>
    protected override DbBatchCommand GetBatchCommand(Int32 index) => _items[index];

    /// <summary>按索引设置命令</summary>
    /// <param name="index">索引</param>
    /// <param name="batchCommand">命令</param>
    protected override void SetBatchCommand(Int32 index, DbBatchCommand batchCommand) => _items[index] = batchCommand;
}
#endif
