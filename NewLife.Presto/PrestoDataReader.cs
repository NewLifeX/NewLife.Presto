using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NewLife.Presto;

/// <summary>Presto/Trino 数据读取器</summary>
/// <remarks>
/// 按页流式消费结果：构造时只持有首个结果页，当前页读完后按 `nextUri` 拉取下一页。
/// `HasRows` 首次访问时会继续拉页直到出现数据行或查询结束，以保证语义正确。
/// </remarks>
public sealed class PrestoDataReader : DbDataReader
{
    #region 字段
    private readonly PrestoStatementClient _client;
    private readonly PrestoCommand _command;
    private readonly IList<PrestoColumn> _columns;
    private readonly CommandBehavior _behavior;
    private readonly CancellationToken _executionToken;
    private readonly CancellationTokenSource? _timeoutCts;
    private readonly Int32 _commandTimeout;
    private readonly Boolean _singleRow;
    private readonly List<PrestoWarning> _warnings = [];

    private PrestoQueryResults _page = null!;
    private IList<IList<Object?>> _rows = null!;
    private Object?[]? _cells;
    private Int32 _arity;
    private Int32 _rowCount;
    private Int32 _rowIndex = -1;
    private Boolean _closed;
    private Boolean _completed;
    private Boolean _hasRows;
    private Boolean _hasRowsKnown;
    private Boolean _singleRowRead;
    private DataTable? _schemaTable;
    private Dictionary<String, Int32>? _ordinalMap;
    private PrestoQueryStats? _stats;
    private Int64 _recordsAffected = -1;
    private Task? _cancelTask;
    private static readonly Task _cancelCompleted = Task.FromResult(0);
    private static readonly IList<IList<Object?>> _emptyRows = new IList<Object?>[0];
    #endregion

    #region 构造
    internal PrestoDataReader(
        PrestoStatementClient client,
        PrestoCommand command,
        PrestoQueryResults page,
        CommandBehavior behavior,
        CancellationToken executionToken,
        CancellationTokenSource? timeoutCts,
        Int32 commandTimeout)
    {
        _client = client;
        _command = command;
        _columns = page.Columns;
        BindPage(page);
        _behavior = behavior;
        _executionToken = executionToken;
        _timeoutCts = timeoutCts;
        _commandTimeout = commandTimeout;
        _singleRow = (behavior & CommandBehavior.SingleRow) != 0;
        _stats = page.Stats;
        _recordsAffected = page.UpdateCount ?? -1;
        _completed = page.NextUri.IsNullOrEmpty();

        AddWarnings(page.Warnings);

        if ((behavior & CommandBehavior.SchemaOnly) != 0)
        {
            _completed = true;
            _hasRowsKnown = true;
        }
        else if (_rowCount > 0)
        {
            _hasRows = true;
            _hasRowsKnown = true;
        }

        if (_completed) _client.MarkCompleted();
    }
    #endregion

    #region 属性
    /// <summary>字段数</summary>
    public override Int32 FieldCount => _columns.Count;

    /// <summary>是否包含数据行</summary>
    /// <remarks>首次访问可能触发拉页；SchemaOnly 下恒为 false。</remarks>
    public override Boolean HasRows
    {
        get
        {
            if (!_hasRowsKnown) EnsureHasRows();

            return _hasRows;
        }
    }

    /// <summary>受影响行数；无写操作时为 -1</summary>
    public override Int32 RecordsAffected => _recordsAffected > Int32.MaxValue ? Int32.MaxValue : (Int32)_recordsAffected;

    /// <summary>嵌套深度。Presto 单结果集，恒为 0</summary>
    public override Int32 Depth => 0;

    /// <summary>是否已关闭</summary>
    public override Boolean IsClosed => _closed;

    /// <summary>查询编号</summary>
    public String? QueryId => _client.QueryId;

    /// <summary>最近一次查询统计</summary>
    public PrestoQueryStats? Statistics => _stats;

    /// <summary>警告列表</summary>
    public IList<PrestoWarning> Warnings => _warnings;

    /// <summary>最近一页的写操作类型</summary>
    public String? UpdateType => _page.UpdateType;

    /// <summary>最近一次后台取消任务。用于诊断与测试等待取消完成</summary>
    internal Task? CancelTask => _cancelTask;

    /// <summary>按序号获取当前行值</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>值</returns>
    public override Object this[Int32 ordinal] => GetValue(ordinal);

    /// <summary>按列名获取当前行值</summary>
    /// <param name="name">列名</param>
    /// <returns>值</returns>
    public override Object this[String name] => GetValue(GetOrdinal(name));
    #endregion

    #region 读取
    /// <summary>推进到下一行</summary>
    /// <returns>是否存在下一行</returns>
    public override Boolean Read()
    {
        EnsureOpen();

        if (_singleRow && _singleRowRead)
        {
            CancelRemaining();
            return false;
        }

        while (true)
        {
            if (_rowIndex + 1 < _rowCount)
            {
                _rowIndex++;
                _hasRows = true;
                _hasRowsKnown = true;

                if (_singleRow)
                {
                    _singleRowRead = true;
                    CancelRemaining();
                }

                return true;
            }

            if (_completed) return false;

            FetchNextPage();
        }
    }

    /// <summary>异步推进到下一行</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否存在下一行</returns>
    public override async Task<Boolean> ReadAsync(CancellationToken cancellationToken)
    {
        EnsureOpen();

        if (_singleRow && _singleRowRead)
        {
            await CancelRemainingAsync().ConfigureAwait(false);
            return false;
        }

        while (true)
        {
            if (_rowIndex + 1 < _rowCount)
            {
                _rowIndex++;
                _hasRows = true;
                _hasRowsKnown = true;

                if (_singleRow)
                {
                    _singleRowRead = true;
                    await CancelRemainingAsync().ConfigureAwait(false);
                }

                return true;
            }

            if (_completed) return false;

            await FetchNextPageAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Presto 只有单结果集</summary>
    /// <returns>恒为 false</returns>
    public override Boolean NextResult() => false;

    /// <summary>Presto 只有单结果集</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>恒为 false</returns>
    public override Task<Boolean> NextResultAsync(CancellationToken cancellationToken) => Task.FromResult(false);
    #endregion

    #region 取值
    /// <summary>获取当前行指定列的值</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>转换后的值；空值为 <see cref="DBNull.Value"/></returns>
    public override Object GetValue(Int32 ordinal)
    {
        CheckOrdinal(ordinal);
        CheckRow();

        var value = GetCell(ordinal);
        if (value == null || value is DBNull) return DBNull.Value;

        return PrestoValueConverter.Convert(value, _columns[ordinal]);
    }

    /// <summary>复制当前行全部值</summary>
    /// <param name="values">目标数组</param>
    /// <returns>复制的列数</returns>
    public override Int32 GetValues(Object[] values)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));

        var count = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < count; i++) values[i] = GetValue(i);

        return count;
    }

    /// <summary>是否为空值</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>是否为空</returns>
    public override Boolean IsDBNull(Int32 ordinal)
    {
        CheckOrdinal(ordinal);
        CheckRow();

        var value = GetCell(ordinal);
        return value == null || value is DBNull;
    }

    /// <summary>获取列名</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>列名</returns>
    public override String GetName(Int32 ordinal)
    {
        CheckOrdinal(ordinal);
        return _columns[ordinal].Name;
    }

    /// <summary>获取列序号</summary>
    /// <param name="name">列名</param>
    /// <returns>列序号</returns>
    public override Int32 GetOrdinal(String name)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));

        if (_ordinalMap == null)
        {
            _ordinalMap = new Dictionary<String, Int32>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < _columns.Count; i++)
            {
                if (!_ordinalMap.ContainsKey(_columns[i].Name)) _ordinalMap[_columns[i].Name] = i;
            }
        }

        if (_ordinalMap.TryGetValue(name, out var ordinal)) return ordinal;

        throw new IndexOutOfRangeException(Localization.Translate("Column '{0}' was not found.", name));
    }

    /// <summary>获取列的 .NET 类型</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>.NET 类型</returns>
    [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetFieldType(Int32 ordinal)
    {
        CheckOrdinal(ordinal);
        return _columns[ordinal].FieldType;
    }

    /// <summary>获取列的 Presto 类型名</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>原始类型签名</returns>
    public override String GetDataTypeName(Int32 ordinal)
    {
        CheckOrdinal(ordinal);
        return _columns[ordinal].Type;
    }

    /// <summary>获取当前行指定列的值</summary>
    /// <typeparam name="T">目标类型</typeparam>
    /// <param name="ordinal">列序号</param>
    /// <returns>转换后的值</returns>
    public override T GetFieldValue<T>(Int32 ordinal)
    {
        var value = GetValue(ordinal);
        if (value is T typed) return typed;

        if (value == DBNull.Value) throw new InvalidCastException(Localization.Translate("The value is NULL."));

        return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
    }

    /// <summary>获取布尔值</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>布尔值</returns>
    public override Boolean GetBoolean(Int32 ordinal) => Convert.ToBoolean(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <summary>获取字节</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>字节</returns>
    public override Byte GetByte(Int32 ordinal) => Convert.ToByte(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <summary>获取有符号字节。ADO.NET 没有 GetSByte，tinyint 可直接使用</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>有符号字节</returns>
    public SByte GetSByte(Int32 ordinal) => Convert.ToSByte(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <summary>获取字符</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>字符</returns>
    public override Char GetChar(Int32 ordinal) => Convert.ToChar(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <summary>获取日期时间</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>日期时间</returns>
    public override DateTime GetDateTime(Int32 ordinal)
    {
        var value = GetValue(ordinal);
        if (value is DateTime dateTime) return dateTime;
        if (value is DateTimeOffset offset) return offset.DateTime;

        return Convert.ToDateTime(value, CultureInfo.InvariantCulture);
    }

    /// <summary>获取十进制数</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>十进制数</returns>
    public override Decimal GetDecimal(Int32 ordinal) => Convert.ToDecimal(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <summary>获取双精度浮点数</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>双精度浮点数</returns>
    public override Double GetDouble(Int32 ordinal) => Convert.ToDouble(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <summary>获取单精度浮点数</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>单精度浮点数</returns>
    public override Single GetFloat(Int32 ordinal) => Convert.ToSingle(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <summary>获取 Guid</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>Guid</returns>
    public override Guid GetGuid(Int32 ordinal) => (Guid)GetValue(ordinal);

    /// <summary>获取 Int16</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>Int16</returns>
    public override Int16 GetInt16(Int32 ordinal) => Convert.ToInt16(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <summary>获取 Int32</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>Int32</returns>
    public override Int32 GetInt32(Int32 ordinal) => Convert.ToInt32(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <summary>获取 Int64</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>Int64</returns>
    public override Int64 GetInt64(Int32 ordinal) => Convert.ToInt64(GetValue(ordinal), CultureInfo.InvariantCulture);

    /// <summary>获取字符串</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>字符串</returns>
    public override String GetString(Int32 ordinal) => (String)GetValue(ordinal);

    /// <summary>获取提供程序特定值</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>值</returns>
    public override Object GetProviderSpecificValue(Int32 ordinal) => GetValue(ordinal);

    /// <summary>获取提供程序特定类型</summary>
    /// <param name="ordinal">列序号</param>
    /// <returns>类型</returns>
    [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetProviderSpecificFieldType(Int32 ordinal) => GetFieldType(ordinal);

    /// <summary>读取二进制数据到缓冲区</summary>
    /// <param name="ordinal">列序号</param>
    /// <param name="dataOffset">数据起始偏移</param>
    /// <param name="buffer">目标缓冲区；为空时返回总长度</param>
    /// <param name="bufferOffset">缓冲区起始偏移</param>
    /// <param name="length">读取长度</param>
    /// <returns>实际读取的字节数</returns>
    public override Int64 GetBytes(Int32 ordinal, Int64 dataOffset, Byte[]? buffer, Int32 bufferOffset, Int32 length)
    {
        var bytes = (Byte[])GetValue(ordinal);
        if (buffer == null) return bytes.Length;
        if (dataOffset < 0 || dataOffset > bytes.Length) throw new ArgumentOutOfRangeException(nameof(dataOffset));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

        var count = Math.Min(length, bytes.Length - (Int32)dataOffset);
        Array.Copy(bytes, (Int32)dataOffset, buffer, bufferOffset, count);

        return count;
    }

    /// <summary>读取字符数据到缓冲区</summary>
    /// <param name="ordinal">列序号</param>
    /// <param name="dataOffset">数据起始偏移</param>
    /// <param name="buffer">目标缓冲区；为空时返回总长度</param>
    /// <param name="bufferOffset">缓冲区起始偏移</param>
    /// <param name="length">读取长度</param>
    /// <returns>实际读取的字符数</returns>
    public override Int64 GetChars(Int32 ordinal, Int64 dataOffset, Char[]? buffer, Int32 bufferOffset, Int32 length)
    {
        var text = GetString(ordinal);
        if (buffer == null) return text.Length;
        if (dataOffset < 0 || dataOffset > text.Length) throw new ArgumentOutOfRangeException(nameof(dataOffset));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

        var count = Math.Min(length, text.Length - (Int32)dataOffset);
        text.CopyTo((Int32)dataOffset, buffer, bufferOffset, count);

        return count;
    }

    /// <summary>获取遍历当前读取器的枚举器</summary>
    /// <returns>枚举器</returns>
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, false);
    #endregion

    #region SchemaTable
    /// <summary>获取结果集架构表</summary>
    /// <returns>SchemaTable</returns>
    public override DataTable GetSchemaTable()
    {
        EnsureOpen();

        return _schemaTable ??= PrestoSchemaTable.Build(_columns);
    }
    #endregion

    #region 关闭
    /// <summary>关闭读取器</summary>
    public override void Close()
    {
        if (_closed) return;

        // 取消是尽力而为的后台任务，避免调用方为释放服务端资源多等一次往返
        _closed = true;
        if (!_completed) CancelRemaining();

        _rows = [];
        _cells = null;
        _rowCount = 0;
        _schemaTable = null;
        _command.OnReaderClosed(this);

        if ((_behavior & CommandBehavior.CloseConnection) != 0) _command.Connection?.Close();
    }

    /// <summary>释放读取器</summary>
    /// <param name="disposing">是否释放托管资源</param>
    protected override void Dispose(Boolean disposing)
    {
        Close();

        base.Dispose(disposing);
    }
    #endregion

    #region 辅助
    private void EnsureOpen()
    {
        if (_closed) throw new InvalidOperationException(Localization.Translate("Reader is closed."));
    }

    private void CheckOrdinal(Int32 ordinal)
    {
        if (ordinal < 0 || ordinal >= _columns.Count)
            throw new IndexOutOfRangeException(Localization.Translate("Column index {0} is out of range.", ordinal));
    }

    private void CheckRow()
    {
        EnsureOpen();

        if (_rowIndex < 0 || _rowIndex >= _rowCount)
            throw new InvalidOperationException(Localization.Translate("No current row is available."));
    }

    private void EnsureHasRows()
    {
        while (true)
        {
            if (_rowCount > 0)
            {
                _hasRows = true;
                _hasRowsKnown = true;
                return;
            }

            if (_completed)
            {
                _hasRows = false;
                _hasRowsKnown = true;
                return;
            }

            FetchNextPage();
        }
    }

    private void FetchNextPage()
    {
        if (_completed) return;

        var nextUri = _page.NextUri!;
        PrestoQueryResults results;
        try
        {
            results = _client.Fetch(nextUri, _executionToken);
        }
        catch (OperationCanceledException ex) when (_timeoutCts?.IsCancellationRequested == true)
        {
            throw PrestoException.CommandTimeout(_commandTimeout, ex);
        }

        AcceptPage(results);
    }

    private async Task FetchNextPageAsync(CancellationToken cancellationToken)
    {
        if (_completed) return;

        var nextUri = _page.NextUri!;

        // 调用方未提供可取消令牌时直接复用执行令牌，省去每页一个链接 CTS
        using var linked = cancellationToken.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _executionToken) : null;
        var token = linked != null ? linked.Token : _executionToken;
        PrestoQueryResults results;
        try
        {
            results = await _client.FetchAsync(nextUri, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (_timeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
        {
            throw PrestoException.CommandTimeout(_commandTimeout, ex);
        }

        AcceptPage(results);
    }

    private void AcceptPage(PrestoQueryResults results)
    {
        BindPage(results);
        _rowIndex = -1;

        if (results.Stats != null) _stats = results.Stats;
        if (results.UpdateCount.HasValue) _recordsAffected = results.UpdateCount.Value;
        AddWarnings(results.Warnings);

        _completed = results.NextUri.IsNullOrEmpty();
        if (_completed) _client.MarkCompleted();
    }

    /// <summary>绑定当前结果页，优先使用扁平行数组，避免逐行对象数组分配</summary>
    private void BindPage(PrestoQueryResults page)
    {
        _page = page;

        var cells = page.CellBuffer;
        if (cells != null)
        {
            _cells = cells;
            _arity = page.CellArity;
            _rowCount = page.RowCount;
            _rows = _emptyRows;
        }
        else
        {
            _cells = null;
            _arity = 0;
            _rows = page.Data;
            _rowCount = _rows.Count;
        }
    }

    private Object? GetCell(Int32 ordinal) => _cells != null ? _cells[_rowIndex * _arity + ordinal] : _rows[_rowIndex][ordinal];

    private void AddWarnings(IEnumerable<PrestoWarning> warnings)
    {
        foreach (var item in warnings) _warnings.Add(item);
    }

    private void CancelRemaining()
    {
        if (_completed) return;

        _completed = true;
        _cancelTask = _client.CancelAsync();
    }

    private Task CancelRemainingAsync()
    {
        if (_completed) return _cancelCompleted;

        _completed = true;
        _cancelTask = _client.CancelAsync();

        return _cancelCompleted;
    }
    #endregion
}

/// <summary>结果值转换器</summary>
internal static class PrestoValueConverter
{
    /// <summary>按列类型转换值</summary>
    /// <param name="value">原始值</param>
    /// <param name="column">列定义</param>
    /// <returns>转换后的值</returns>
    public static Object Convert(Object value, PrestoColumn column)
    {
        try
        {
            var fieldType = column.FieldType;

            // 复杂类型在结构化模式下返回 Object[]/字典
            if (fieldType == typeof(Object)) return PrestoComplexValueParser.Parse(value, PrestoTypeSignature.Parse(column.Type))!;

            // 值类型已与列类型一致时直接返回，避免数字与字符串往返转换（扫描大结果集的热路径）
            if (value.GetType() == fieldType) return value;

            // 数字列就近转换，语义与“文本再解析”一致，但不产生字符串
            if (value is Int32 int32)
            {
                if (fieldType == typeof(Int64)) return (Int64)int32;
                if (fieldType == typeof(Double)) return (Double)int32;
                if (fieldType == typeof(Single)) return (Single)int32;
                if (fieldType == typeof(Decimal)) return (Decimal)int32;
                if (fieldType == typeof(Int16)) return (Int16)int32;
                if (fieldType == typeof(SByte)) return checked((SByte)int32);
            }
            else if (value is Int64 int64)
            {
                if (fieldType == typeof(Int32)) return checked((Int32)int64);
                if (fieldType == typeof(Double)) return (Double)int64;
                if (fieldType == typeof(Single)) return (Single)int64;
                if (fieldType == typeof(Decimal)) return (Decimal)int64;
                if (fieldType == typeof(Int16)) return checked((Int16)int64);
                if (fieldType == typeof(SByte)) return checked((SByte)int64);
            }

            if (fieldType == typeof(String))
            {
                // 复杂列要 JSON 文本：net6+ 解析阶段就是原始切片，旧目标的字典路径留下对象树，这里补成文本
                if (value is not String && PrestoTypeMapper.IsComplex(column.Type)) return PrestoJson.WriteJson(value);

                return PrestoJson.ToText(value)!;
            }
            if (fieldType == typeof(Boolean)) return ToBoolean(value);
            if (fieldType == typeof(SByte)) return System.Convert.ToSByte(PrestoJson.ToText(value)!, CultureInfo.InvariantCulture);
            if (fieldType == typeof(Int16)) return System.Convert.ToInt16(PrestoJson.ToText(value), CultureInfo.InvariantCulture);
            if (fieldType == typeof(Int32)) return System.Convert.ToInt32(PrestoJson.ToText(value), CultureInfo.InvariantCulture);
            if (fieldType == typeof(Int64)) return System.Convert.ToInt64(PrestoJson.ToText(value), CultureInfo.InvariantCulture);
            if (fieldType == typeof(Single)) return ToSingle(value);
            if (fieldType == typeof(Double)) return ToDouble(value);
            if (fieldType == typeof(Decimal)) return Decimal.Parse(PrestoJson.ToText(value)!, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (fieldType == typeof(DateTime)) return ToDateTime(value);
            if (fieldType == typeof(DateTimeOffset)) return ToDateTimeOffset(value);
            if (fieldType == typeof(TimeSpan)) return ToTimeSpan(value);
            if (fieldType == typeof(Guid)) return Guid.Parse(PrestoJson.ToText(value)!);
            if (fieldType == typeof(IPAddress)) return IPAddress.Parse(PrestoJson.ToText(value)!);
            if (fieldType == typeof(Byte[])) return ToBytes(value);

            return PrestoJson.ToText(value)!;
        }
        catch (Exception ex) when (ex is not PrestoException)
        {
            throw new PrestoException(
                Localization.Translate(
                    "Cannot convert column '{0}' of type '{1}' to {2}. Value: {3}",
                    column.Name,
                    column.Type,
                    column.FieldType.Name,
                    PrestoDiagnostics.Truncate(PrestoJson.ToText(value), 128)),
                ex);
        }
    }

    /// <summary>按类型签名转换标量值，供复杂类型元素递归使用</summary>
    /// <param name="value">原始值</param>
    /// <param name="type">类型签名</param>
    /// <returns>转换后的值</returns>
    internal static Object ConvertScalar(Object value, String? type)
    {
        var column = new PrestoColumn
        {
            Name = String.Empty,
            Type = type ?? String.Empty,
            FieldType = PrestoTypeMapper.GetFieldType(type),
        };

        return Convert(value, column);
    }

    private static Boolean ToBoolean(Object value)
    {
        var text = PrestoJson.ToText(value);
        if (String.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1") return true;
        if (String.Equals(text, "false", StringComparison.OrdinalIgnoreCase) || text == "0") return false;

        return Boolean.Parse(text!);
    }

    private static Single ToSingle(Object value)
    {
        var text = PrestoJson.ToText(value);
        if (String.Equals(text, "NaN", StringComparison.OrdinalIgnoreCase)) return Single.NaN;
        if (String.Equals(text, "Infinity", StringComparison.OrdinalIgnoreCase)) return Single.PositiveInfinity;
        if (String.Equals(text, "-Infinity", StringComparison.OrdinalIgnoreCase)) return Single.NegativeInfinity;

        return Single.Parse(text!, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static Double ToDouble(Object value)
    {
        var text = PrestoJson.ToText(value);
        if (String.Equals(text, "NaN", StringComparison.OrdinalIgnoreCase)) return Double.NaN;
        if (String.Equals(text, "Infinity", StringComparison.OrdinalIgnoreCase)) return Double.PositiveInfinity;
        if (String.Equals(text, "-Infinity", StringComparison.OrdinalIgnoreCase)) return Double.NegativeInfinity;

        return Double.Parse(text!, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static DateTime ToDateTime(Object value)
    {
        var text = PrestoJson.ToText(value)!;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            return DateTime.SpecifyKind(result, DateTimeKind.Unspecified);

        return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }

    private static DateTimeOffset ToDateTimeOffset(Object value) => DateTimeOffset.Parse(PrestoJson.ToText(value)!, CultureInfo.InvariantCulture, DateTimeStyles.None);

    private static TimeSpan ToTimeSpan(Object value)
    {
        var text = PrestoJson.ToText(value)!;
        var negative = text.StartsWith("-");
        var body = negative ? text[1..] : text;
        var index = body.IndexOf(' ');
        if (index > 0)
        {
            var days = Int32.Parse(body[..index], CultureInfo.InvariantCulture);
            var time = TimeSpan.Parse(body[(index + 1)..], CultureInfo.InvariantCulture);
            var result = TimeSpan.FromDays(days) + time;

            return negative ? result.Negate() : result;
        }

        return TimeSpan.Parse(body, CultureInfo.InvariantCulture);
    }

    private static Byte[] ToBytes(Object value) => PrestoJson.ToText(value).ToBase64()!;
}
