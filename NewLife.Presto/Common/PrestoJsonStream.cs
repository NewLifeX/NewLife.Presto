#if NET6_0_OR_GREATER
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace NewLife.Presto;

/// <summary>Presto/Trino JSON 响应的 UTF-8 流式解析</summary>
/// <remarks>
/// 现代目标使用 <see cref="Utf8JsonReader"/> 直接在响应字节上解析，避免整页文本与中间字典；
/// 只有 <c>stats</c>/<c>error</c>/<c>updateType</c> 等小对象保留原始字典，保证 <c>Raw</c> 诊断信息不变。
/// 与字典路径（<c>JsonParser</c>）保持一致的取值语义：小整数为 <see cref="Int32"/>、大整数为 <see cref="Int64"/>、小数为 <see cref="Double"/>。
/// </remarks>
internal static partial class PrestoJson
{
    #region 解析
    /// <summary>解析单页查询结果</summary>
    /// <param name="utf8">UTF-8 JSON 字节</param>
    /// <param name="structured">复杂类型是否返回结构化对象</param>
    /// <param name="knownId">上一页的查询编号，内容相同则复用，避免每页重复分配</param>
    /// <param name="knownInfoUri">上一页的查询信息地址，内容相同则复用</param>
    /// <returns>查询结果</returns>
    public static PrestoQueryResults ParseResultsUtf8(ReadOnlySpan<Byte> utf8, Boolean structured = false, String? knownId = null, String? knownInfoUri = null)
    {
        utf8 = TrimBom(utf8);
        if (IsBlank(utf8)) throw new PrestoException(Localization.Translate("The server returned an empty response."));

        try
        {
            var reader = CreateReader(utf8);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("expected a JSON object");

            return ReadResults(ref reader, utf8, structured, knownId, knownInfoUri);
        }
        catch (Exception ex) when (ex is not PrestoException)
        {
            throw new PrestoException(Localization.Translate("Failed to parse the server response: {0}", ex.Message), ex);
        }
    }

    /// <summary>解析单页查询结果</summary>
    /// <param name="utf8">UTF-8 JSON 字节</param>
    /// <param name="structured">复杂类型是否返回结构化对象</param>
    /// <param name="knownId">上一页的查询编号，内容相同则复用</param>
    /// <param name="knownInfoUri">上一页的查询信息地址，内容相同则复用</param>
    /// <returns>查询结果</returns>
    public static PrestoQueryResults ParseResultsUtf8(Byte[] utf8, Boolean structured = false, String? knownId = null, String? knownInfoUri = null)
        => ParseResultsUtf8(new ReadOnlySpan<Byte>(utf8), structured, knownId, knownInfoUri);

    /// <summary>解析分段中的行数组</summary>
    /// <param name="utf8">UTF-8 JSON 字节</param>
    /// <returns>行列表</returns>
    public static IList<IList<Object?>> ParseRowsUtf8(ReadOnlySpan<Byte> utf8)
    {
        utf8 = TrimBom(utf8);
        if (IsBlank(utf8)) return [];

        try
        {
            var reader = CreateReader(utf8);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("expected a JSON array of rows");

            return ReadData(ref reader, null, utf8, CountArrayItems(utf8, (Int32)reader.TokenStartIndex));
        }
        catch (Exception ex) when (ex is not PrestoException)
        {
            throw new PrestoException(Localization.Translate("Failed to parse the server response: {0}", ex.Message), ex);
        }
    }

    private static Utf8JsonReader CreateReader(ReadOnlySpan<Byte> utf8) => new(utf8, new JsonReaderOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    });

    private static PrestoQueryResults ReadResults(ref Utf8JsonReader reader, ReadOnlySpan<Byte> utf8, Boolean structured, String? knownId, String? knownInfoUri)
    {
        String? id = null;
        String? infoUri = null;
        String? nextUri = null;
        String? updateType = null;
        Int64? updateCount = null;
        List<PrestoColumn>? columns = null;
        IList<IList<Object?>>? data = null;
        Object?[]? cellBuffer = null;
        var cellArity = 0;
        var rowCount = 0;
        String? dataEncoding = null;
        IList<PrestoQuerySegment>? segments = null;
        PrestoQueryStats? stats = null;
        IList<PrestoWarning>? warnings = null;
        PrestoQueryError? error = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;

            if (reader.ValueTextEquals("id"))
            {
                reader.Read();
                id = ReadSharedText(ref reader, knownId);
            }
            else if (reader.ValueTextEquals("infoUri"))
            {
                reader.Read();
                infoUri = ReadSharedText(ref reader, knownInfoUri);
            }
            else if (reader.ValueTextEquals("nextUri"))
            {
                reader.Read();
                nextUri = ReadText(ref reader);
            }
            else if (reader.ValueTextEquals("updateType"))
            {
                reader.Read();
                updateType = ReadText(ref reader);
            }
            else if (reader.ValueTextEquals("updateCount"))
            {
                reader.Read();
                updateCount = ReadInt64(ref reader);
            }
            else if (reader.ValueTextEquals("columns"))
            {
                reader.Read();
                columns = ReadColumns(ref reader, structured);
            }
            else if (reader.ValueTextEquals("data"))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    var page = ReadPageData(ref reader, columns, utf8, CountArrayItems(utf8, (Int32)reader.TokenStartIndex));
                    if (page.Cells != null)
                    {
                        cellBuffer = page.Cells;
                        cellArity = page.Arity;
                        rowCount = page.Count;
                    }
                    else
                        data = page.Rows;
                }
                else if (reader.TokenType == JsonTokenType.StartObject)
                {
                    // Spooling：小对象，保留字典模型
                    var spooled = AsDictionary(ParseObject(GetRawJson(ref reader, utf8)));
                    dataEncoding = GetString(spooled, "encoding");
                    segments = spooled == null ? [] : ParseSegments(spooled.TryGetValue("segments", out var value) ? value : null);
                }
                else
                    SkipValue(ref reader);
            }
            else if (reader.ValueTextEquals("stats"))
            {
                reader.Read();

                // 中间页只解析进度字段，原始 JSON 留给终页；字段顺序不同的服务端仍按原样保留
                stats = ReadStats(ref reader, utf8, nextUri.IsNullOrEmpty());
            }
            else if (reader.ValueTextEquals("warnings"))
            {
                reader.Read();
                warnings = ReadWarnings(ref reader);
            }
            else if (reader.ValueTextEquals("error"))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.StartObject)
                    error = ParseError(ParseObject(GetRawJson(ref reader, utf8)));
                else
                    SkipValue(ref reader);
            }
            else
            {
                reader.Read();
                SkipValue(ref reader);
            }
        }

        return new PrestoQueryResults
        {
            Id = id,
            InfoUri = infoUri,
            NextUri = nextUri,
            Columns = columns is { Count: > 0 } ? columns : PrestoQueryResults.NoColumns,
            // 扁平数据由 CellBuffer 承载时 Data 留空，首次访问再展开
            Data = data!,
            CellBuffer = cellBuffer,
            CellArity = cellArity,
            RowCount = rowCount,
            DataEncoding = dataEncoding,
            Segments = segments ?? [],
            Stats = stats,
            Warnings = warnings is { Count: > 0 } ? warnings : PrestoQueryResults.NoWarnings,
            Error = error,
            UpdateType = updateType,
            UpdateCount = updateCount,
        };
    }

    private static List<PrestoColumn>? ReadColumns(ref Utf8JsonReader reader, Boolean structured)
    {
        // 没有列的页（queued/executing）直接返回空，省掉一个空列表对象
        if (reader.TokenType != JsonTokenType.StartArray) return null;

        var list = new List<PrestoColumn>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                SkipValue(ref reader);
                continue;
            }

            String? name = null;
            String? type = null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;

                if (reader.ValueTextEquals("name"))
                {
                    reader.Read();
                    name = ReadText(ref reader);
                }
                else if (reader.ValueTextEquals("type"))
                {
                    reader.Read();
                    type = ReadText(ref reader);
                }
                else if (reader.ValueTextEquals("typeSignature"))
                {
                    reader.Read();
                    type ??= ReadRawTypeSignature(ref reader);
                }
                else
                {
                    reader.Read();
                    SkipValue(ref reader);
                }
            }

            type ??= String.Empty;
            list.Add(new PrestoColumn
            {
                Name = name ?? String.Empty,
                Type = type,
                FieldType = PrestoTypeMapper.GetFieldType(type, structured),
            });
        }

        return list.Count > 0 ? list : null;
    }

    /// <summary>读取 typeSignature.rawType；类型缺失时返回空</summary>
    private static String? ReadRawTypeSignature(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            SkipValue(ref reader);
            return null;
        }

        String? rawType = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;

            if (reader.ValueTextEquals("rawType"))
            {
                reader.Read();
                rawType = ReadText(ref reader);
            }
            else
            {
                reader.Read();
                SkipValue(ref reader);
            }
        }

        return rawType;
    }

    /// <summary>直接读取统计字段，原始 JSON 保留给 <see cref="PrestoQueryStats.Raw"/> 延迟解析</summary>
    /// <param name="reader">JSON 读取器</param>
    /// <param name="utf8">整页字节</param>
    /// <param name="keepRaw">是否保留原始 JSON；中间页只需要进度字段，省掉每页一次切片拷贝</param>
    private static PrestoQueryStats? ReadStats(ref Utf8JsonReader reader, ReadOnlySpan<Byte> utf8, Boolean keepRaw = true)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            SkipValue(ref reader);
            return null;
        }

        var start = (Int32)reader.TokenStartIndex;
        String? state = null;
        var queuedTime = 0L;
        var elapsedTime = 0L;
        var processedRows = 0L;
        var processedBytes = 0L;
        var totalSplits = 0;
        var completedSplits = 0;
        var runningSplits = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;

            if (reader.ValueTextEquals("state"))
            {
                reader.Read();
                state = ReadState(ref reader);
            }
            else if (reader.ValueTextEquals("queuedTimeMillis"))
            {
                reader.Read();
                queuedTime = ReadInt64(ref reader) ?? 0;
            }
            else if (reader.ValueTextEquals("elapsedTimeMillis"))
            {
                reader.Read();
                elapsedTime = ReadInt64(ref reader) ?? 0;
            }
            else if (reader.ValueTextEquals("processedRows"))
            {
                reader.Read();
                processedRows = ReadInt64(ref reader) ?? 0;
            }
            else if (reader.ValueTextEquals("processedBytes"))
            {
                reader.Read();
                processedBytes = ReadInt64(ref reader) ?? 0;
            }
            else if (reader.ValueTextEquals("totalSplits"))
            {
                reader.Read();
                totalSplits = ReadInt32(ref reader);
            }
            else if (reader.ValueTextEquals("completedSplits"))
            {
                reader.Read();
                completedSplits = ReadInt32(ref reader);
            }
            else if (reader.ValueTextEquals("runningSplits"))
            {
                reader.Read();
                runningSplits = ReadInt32(ref reader);
            }
            else
            {
                reader.Read();
                SkipValue(ref reader);
            }
        }

        var end = (Int32)reader.BytesConsumed;

        return new PrestoQueryStats
        {
            State = state,
            QueuedTimeMillis = queuedTime,
            ElapsedTimeMillis = elapsedTime,
            ProcessedRows = processedRows,
            ProcessedBytes = processedBytes,
            TotalSplits = totalSplits,
            CompletedSplits = completedSplits,
            RunningSplits = runningSplits,
            RawUtf8 = keepRaw ? utf8[start..end].ToArray() : null,
        };
    }

    private static Int32 ReadInt32(ref Utf8JsonReader reader)
    {
        var value = ReadInt64(ref reader);

        return value is >= Int32.MinValue and <= Int32.MaxValue ? (Int32)value.GetValueOrDefault() : 0;
    }

    private static List<PrestoWarning>? ReadWarnings(ref Utf8JsonReader reader)
    {
        // 绝大多数页的 warnings 是空数组，直接返回空省掉一个列表对象
        if (reader.TokenType != JsonTokenType.StartArray) return null;

        var list = new List<PrestoWarning>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                SkipValue(ref reader);
                continue;
            }

            String? message = null;
            var code = 0;
            String? errorName = null;
            String? errorType = null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;

                if (reader.ValueTextEquals("message"))
                {
                    reader.Read();
                    message = ReadText(ref reader);
                }
                else if (reader.ValueTextEquals("warningCode"))
                {
                    reader.Read();
                    code = (Int32)(ReadInt64(ref reader) ?? 0);
                }
                else if (reader.ValueTextEquals("errorName"))
                {
                    reader.Read();
                    errorName = ReadText(ref reader);
                }
                else if (reader.ValueTextEquals("errorType"))
                {
                    reader.Read();
                    errorType = ReadText(ref reader);
                }
                else
                {
                    reader.Read();
                    SkipValue(ref reader);
                }
            }

            list.Add(new PrestoWarning
            {
                Message = message,
                WarningCode = code,
                ErrorName = errorName,
                ErrorType = errorType,
            });
        }

        return list.Count > 0 ? list : null;
    }
    #endregion

    #region 数据行
    private static readonly IList<Object?> _emptyRow = Array.Empty<Object?>();
    private static readonly Object _trueValue = true;
    private static readonly Object _falseValue = false;

    /// <summary>单页扁平行数组的单元格上限，超过则退回行列表，避免单页缓冲区过大</summary>
    /// <remarks>
    /// 扁平行数组比逐行数组更省：少掉每行一个对象数组和列表扩容，宽表页（如 16 列 × 5000 行 = 8 万单元格）
    /// 实测快 40%。因此上限放到 26 万单元格（最坏 2 MB 单块数组），足以覆盖 5000 行 × 52 列以内的常见宽表。
    /// </remarks>
    private const Int32 MaxFlatCells = 262144;

    /// <summary>单页数据：扁平行数组或行列表二选一</summary>
    private sealed class PageData
    {
        /// <summary>扁平存放的单元格，按行连续排列</summary>
        public Object?[]? Cells;

        /// <summary>每行列数</summary>
        public Int32 Arity;

        /// <summary>行数</summary>
        public Int32 Count;

        /// <summary>行列表，仅在无法扁平存放或行列数不一致时使用</summary>
        public IList<IList<Object?>>? Rows;
    }

    private static List<IList<Object?>> ReadData(ref Utf8JsonReader reader, List<PrestoColumn>? columns, ReadOnlySpan<Byte> utf8, Int32 capacity = 0)
    {
        var list = capacity > 0 ? new List<IList<Object?>>(capacity) : new List<IList<Object?>>();

        var arity = columns?.Count ?? 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;

            if (reader.TokenType == JsonTokenType.StartArray)
                list.Add(ReadRow(ref reader, columns, utf8, arity));
            else
            {
                // 非数组行按空行处理，与字典路径一致
                SkipValue(ref reader);
                list.Add(_emptyRow);
            }
        }

        return list;
    }

    private static PageData ReadPageData(ref Utf8JsonReader reader, List<PrestoColumn>? columns, ReadOnlySpan<Byte> utf8, Int32 capacity)
    {
        var arity = columns?.Count ?? 0;

        // 单页单元格总量较小时用一整块数组存放，省掉每行一个对象数组的开销；超限退回行列表，避免大对象堆
        if (arity > 0 && capacity > 0 && capacity <= MaxFlatCells / arity) return ReadFlatData(ref reader, columns, utf8, arity, capacity);

        return new PageData { Rows = ReadData(ref reader, columns, utf8, capacity) };
    }

    private static PageData ReadFlatData(ref Utf8JsonReader reader, List<PrestoColumn>? columns, ReadOnlySpan<Byte> utf8, Int32 arity, Int32 capacity)
    {
        var cells = new Object?[capacity * arity];
        Int32[]? counts = null;
        var count = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;

            if (count == capacity)
            {
                capacity *= 2;
                Array.Resize(ref cells, capacity * arity);
                if (counts != null) Array.Resize(ref counts, capacity);
            }

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                // 非数组行按空行处理，与字典路径一致
                SkipValue(ref reader);
                counts ??= NewRowCounts(capacity, arity, count);
                counts[count] = 0;
                count++;
                continue;
            }

            var offset = count * arity;
            var index = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray) break;

                if (index < arity)
                    cells[offset + index] = ReadCell(ref reader, columns, index, utf8);
                else
                    SkipValue(ref reader);

                index++;
            }

            // 行列数不一致时记下实际单元格数，收尾时退回行列表，保持“保留实际单元格”的语义
            if (counts != null)
                counts[count] = index < arity ? index : arity;
            else if (index != arity)
            {
                counts = NewRowCounts(capacity, arity, count);
                counts[count] = index < arity ? index : arity;
            }

            count++;
        }

        if (count * arity != cells.Length) Array.Resize(ref cells, count * arity);

        if (counts == null) return new PageData { Cells = cells, Arity = arity, Count = count };

        var rows = new List<IList<Object?>>(count);
        for (var i = 0; i < count; i++)
        {
            var length = counts[i];
            var row = new Object?[length];
            if (length > 0) Array.Copy(cells, i * arity, row, 0, length);
            rows.Add(row);
        }

        return new PageData { Rows = rows };
    }

    /// <summary>新建每行实际单元格数数组；此前读到的行都是规整行，按列数补齐</summary>
    private static Int32[] NewRowCounts(Int32 capacity, Int32 arity, Int32 count)
    {
        var counts = new Int32[capacity];
        for (var i = 0; i < count; i++) counts[i] = arity;

        return counts;
    }

    private static IList<Object?> ReadRow(ref Utf8JsonReader reader, List<PrestoColumn>? columns, ReadOnlySpan<Byte> utf8, Int32 arity)
    {
        if (arity <= 0)
        {
            var cells = new List<Object?>(4);
            var index = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray) break;

                cells.Add(ReadCell(ref reader, columns, index, utf8));
                index++;
            }

            return cells;
        }

        var row = new Object?[arity];
        var count = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;

            if (count < arity)
                row[count] = ReadCell(ref reader, columns, count, utf8);
            else
                SkipValue(ref reader);

            count++;
        }

        // 多余单元格已忽略；少于列数时按实际数量裁剪
        return count < arity ? TrimRow(row, count) : row;
    }

    private static IList<Object?> TrimRow(Object?[] row, Int32 count)
    {
        if (count <= 0) return _emptyRow;

        var copy = new Object?[count];
        Array.Copy(row, copy, count);

        return copy;
    }

    private static Object? ReadCell(ref Utf8JsonReader reader, List<PrestoColumn>? columns, Int32 ordinal, ReadOnlySpan<Byte> utf8)
    {
        var column = ordinal >= 0 && columns != null && ordinal < columns.Count ? columns[ordinal] : null;

        return reader.TokenType switch
        {
            JsonTokenType.String => ReadString(ref reader, column),
            JsonTokenType.Number => ReadNumber(ref reader, column?.FieldType),
            JsonTokenType.True => _trueValue,
            JsonTokenType.False => _falseValue,
            JsonTokenType.Null => null,
            // 复杂列在非结构化模式下只需要原始 JSON 文本，直接切片，避免先建对象树再转回字符串
            JsonTokenType.StartObject => IsComplexText(column) ? GetRawJson(ref reader, utf8) : ReadDictionary(ref reader, utf8),
            JsonTokenType.StartArray => IsComplexText(column) ? GetRawJson(ref reader, utf8) : ReadList(ref reader, utf8), _ => null,
        };
    }

    /// <summary>复杂列且未启用结构化模式：单元格按原始 JSON 文本返回</summary>
    private static Boolean IsComplexText(PrestoColumn? column)
        => column != null && column.FieldType == typeof(String) && PrestoTypeMapper.IsComplex(column.Type);

    private static Object? ReadString(ref Utf8JsonReader reader, PrestoColumn? column)
    {
        // 文本形态的数值与日期直接在 UTF-8 字节上转换，省掉中间字符串；
        // 含转义字符时字节与明文不一致，解析会失败并回退到字符串，语义不变
        if (column != null)
        {
            var span = reader.ValueSpan;
            if (column.FieldType == typeof(Decimal))
            {
                if (Utf8Parser.TryParse(span, out Decimal number, out var consumed) && consumed == span.Length) return number;
            }
            else if (column.FieldType == typeof(DateTime) && column.Type == "date" && TryParseDate(span, out var date)) return date;
        }

        return reader.GetString();
    }

    /// <summary>按 yyyy-MM-dd 解析日期；格式不符返回 false，交给后续通用转换报错</summary>
    private static Boolean TryParseDate(ReadOnlySpan<Byte> span, out DateTime value)
    {
        value = default;
        if (span.Length != 10 || span[4] != '-' || span[7] != '-') return false;

        var year = (span[0] - '0') * 1000 + (span[1] - '0') * 100 + (span[2] - '0') * 10 + (span[3] - '0');
        var month = (span[5] - '0') * 10 + (span[6] - '0');
        var day = (span[8] - '0') * 10 + (span[9] - '0');
        if (year < 1 || month < 1 || month > 12 || day < 1 || day > 31) return false;

        try
        {
            value = new DateTime(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static Object ReadNumber(ref Utf8JsonReader reader, Type? type)
    {
        if (type == typeof(Int64))
        {
            if (reader.TryGetInt64(out var l)) return l;
        }
        else if (type == typeof(Int32))
        {
            if (reader.TryGetInt32(out var i)) return i;
        }
        else if (type == typeof(Double))
        {
            if (reader.TryGetDouble(out var d)) return d;
        }

        if (reader.TryGetInt32(out var i32)) return i32;
        if (reader.TryGetInt64(out var i64)) return i64;

        // 合法 JSON 数字总能转成 Double（溢出得到 ±Infinity）
        _ = reader.TryGetDouble(out var dbl);
        return dbl;
    }

    private static Dictionary<String, Object?> ReadDictionary(ref Utf8JsonReader reader, ReadOnlySpan<Byte> utf8)
    {
        var dic = new Dictionary<String, Object?>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;

            var key = reader.GetString()!;
            reader.Read();
            dic[key] = ReadCell(ref reader, null, -1, utf8);
        }

        return dic;
    }

    private static List<Object?> ReadList(ref Utf8JsonReader reader, ReadOnlySpan<Byte> utf8)
    {
        var list = new List<Object?>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;

            list.Add(ReadCell(ref reader, null, -1, utf8));
        }

        return list;
    }
    #endregion

    #region 辅助
    /// <summary>读取文本；与上一页相同的字符串直接复用，避免每页重复分配</summary>
    private static String? ReadSharedText(ref Utf8JsonReader reader, String? known)
    {
        if (known != null && reader.TokenType == JsonTokenType.String && reader.ValueTextEquals(known)) return known;

        return ReadText(ref reader);
    }

    /// <summary>读取查询状态；常见状态复用常量实例</summary>
    private static String? ReadState(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            if (reader.ValueTextEquals(PrestoStates.Queued)) return PrestoStates.Queued;
            if (reader.ValueTextEquals(PrestoStates.Running)) return PrestoStates.Running;
            if (reader.ValueTextEquals(PrestoStates.Finished)) return PrestoStates.Finished;
            if (reader.ValueTextEquals(PrestoStates.Failed)) return PrestoStates.Failed;
            if (reader.ValueTextEquals(PrestoStates.Canceled)) return PrestoStates.Canceled;
        }

        return ReadText(ref reader);
    }

    private static String? ReadText(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                return Encoding.UTF8.GetString(reader.ValueSpan);
            case JsonTokenType.True:
                return "True";
            case JsonTokenType.False:
                return "False";
            default:
                SkipValue(ref reader);
                return null;
        }
    }

    private static Int64? ReadInt64(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var value)) return value;

                // 合法 JSON 数字总能转成 Double（溢出得到 ±Infinity）
                _ = reader.TryGetDouble(out var number);
                return (Int64)number;
            case JsonTokenType.String:
                return Int64.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
            default:
                SkipValue(ref reader);
                return null;
        }
    }

    /// <summary>读取当前值的原始 JSON 文本；非对象/数组的标量也按文本返回</summary>
    private static String GetRawJson(ref Utf8JsonReader reader, ReadOnlySpan<Byte> utf8)
    {
        var start = (Int32)reader.TokenStartIndex;
        SkipValue(ref reader);
        var end = (Int32)reader.BytesConsumed;

        return Encoding.UTF8.GetString(utf8[start..end]);
    }

    /// <summary>跳过当前值及其子节点；标量无需移动</summary>
    private static void SkipValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject && reader.TokenType != JsonTokenType.StartArray) return;

        var depth = reader.CurrentDepth;
        while (reader.Read())
        {
            if ((reader.TokenType == JsonTokenType.EndObject || reader.TokenType == JsonTokenType.EndArray) && reader.CurrentDepth == depth) break;
        }
    }

    /// <summary>预扫描数组第一层元素个数，用于精确预分配行列表</summary>
    /// <param name="utf8">UTF-8 JSON 字节</param>
    /// <param name="start">数组 '[' 的偏移</param>
    /// <returns>元素个数；扫描失败返回 0</returns>
    private static Int32 CountArrayItems(ReadOnlySpan<Byte> utf8, Int32 start)
    {
        var depth = 0;
        var count = 0;
        for (var i = start; i < utf8.Length; i++)
        {
            var b = utf8[i];
            if (b == (Byte)'"')
            {
                for (i++; i < utf8.Length; i++)
                {
                    if (utf8[i] == (Byte)'\\') i++;
                    else if (utf8[i] == (Byte)'"') break;
                }
            }
            else if (b == (Byte)'[')
            {
                depth++;
                if (depth == 2) count++;
            }
            else if (b == (Byte)']')
            {
                depth--;
                if (depth == 0) break;
            }
        }

        return count;
    }

    private static ReadOnlySpan<Byte> TrimBom(ReadOnlySpan<Byte> utf8)
        => utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF ? utf8[3..] : utf8;

    private static Boolean IsBlank(ReadOnlySpan<Byte> utf8)
    {
        foreach (var b in utf8)
        {
            if (b != (Byte)' ' && b != (Byte)'\t' && b != (Byte)'\r' && b != (Byte)'\n') return false;
        }

        return true;
    }
    #endregion
}
#endif
