#if NET6_0_OR_GREATER
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
    /// <returns>查询结果</returns>
    public static PrestoQueryResults ParseResultsUtf8(ReadOnlySpan<Byte> utf8, Boolean structured = false)
    {
        utf8 = TrimBom(utf8);
        if (IsBlank(utf8)) throw new PrestoException(Localization.Translate("The server returned an empty response."));

        try
        {
            var reader = CreateReader(utf8);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("expected a JSON object");

            return ReadResults(ref reader, utf8, structured);
        }
        catch (Exception ex) when (ex is not PrestoException)
        {
            throw new PrestoException(Localization.Translate("Failed to parse the server response: {0}", ex.Message), ex);
        }
    }

    /// <summary>解析单页查询结果</summary>
    /// <param name="utf8">UTF-8 JSON 字节</param>
    /// <param name="structured">复杂类型是否返回结构化对象</param>
    /// <returns>查询结果</returns>
    public static PrestoQueryResults ParseResultsUtf8(Byte[] utf8, Boolean structured = false) => ParseResultsUtf8(new ReadOnlySpan<Byte>(utf8), structured);

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

            return ReadData(ref reader, null, CountArrayItems(utf8, (Int32)reader.TokenStartIndex));
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

    private static PrestoQueryResults ReadResults(ref Utf8JsonReader reader, ReadOnlySpan<Byte> utf8, Boolean structured)
    {
        String? id = null;
        String? infoUri = null;
        String? nextUri = null;
        String? updateType = null;
        Int64? updateCount = null;
        List<PrestoColumn>? columns = null;
        IList<IList<Object?>>? data = null;
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
                id = ReadText(ref reader);
            }
            else if (reader.ValueTextEquals("infoUri"))
            {
                reader.Read();
                infoUri = ReadText(ref reader);
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
                    data = ReadData(ref reader, columns, CountArrayItems(utf8, (Int32)reader.TokenStartIndex));
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
                stats = ReadStats(ref reader, utf8);
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
            Columns = columns ?? [],
            Data = data ?? [],
            DataEncoding = dataEncoding,
            Segments = segments ?? [],
            Stats = stats,
            Warnings = warnings ?? [],
            Error = error,
            UpdateType = updateType,
            UpdateCount = updateCount,
        };
    }

    private static List<PrestoColumn> ReadColumns(ref Utf8JsonReader reader, Boolean structured)
    {
        var list = new List<PrestoColumn>();
        if (reader.TokenType != JsonTokenType.StartArray) return list;

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

        return list;
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
    private static PrestoQueryStats? ReadStats(ref Utf8JsonReader reader, ReadOnlySpan<Byte> utf8)
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
                state = ReadText(ref reader);
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
            RawUtf8 = utf8[start..end].ToArray(),
        };
    }

    private static Int32 ReadInt32(ref Utf8JsonReader reader)
    {
        var value = ReadInt64(ref reader);

        return value is >= Int32.MinValue and <= Int32.MaxValue ? (Int32)value.GetValueOrDefault() : 0;
    }

    private static List<PrestoWarning> ReadWarnings(ref Utf8JsonReader reader)
    {
        var list = new List<PrestoWarning>();
        if (reader.TokenType != JsonTokenType.StartArray) return list;

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

        return list;
    }
    #endregion

    #region 数据行
    private static readonly IList<Object?> _emptyRow = Array.Empty<Object?>();
    private static readonly Object _trueValue = true;
    private static readonly Object _falseValue = false;

    private static List<IList<Object?>> ReadData(ref Utf8JsonReader reader, List<PrestoColumn>? columns, Int32 capacity = 0)
    {
        var list = capacity > 0 ? new List<IList<Object?>>(capacity) : new List<IList<Object?>>();

        var arity = columns?.Count ?? 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;

            if (reader.TokenType == JsonTokenType.StartArray)
                list.Add(ReadRow(ref reader, columns, arity));
            else
            {
                // 非数组行按空行处理，与字典路径一致
                SkipValue(ref reader);
                list.Add(_emptyRow);
            }
        }

        return list;
    }

    private static IList<Object?> ReadRow(ref Utf8JsonReader reader, List<PrestoColumn>? columns, Int32 arity)
    {
        if (arity <= 0)
        {
            var cells = new List<Object?>(4);
            var index = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray) break;

                cells.Add(ReadCell(ref reader, columns, index));
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
                row[count] = ReadCell(ref reader, columns, count);
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

    private static Object? ReadCell(ref Utf8JsonReader reader, List<PrestoColumn>? columns, Int32 ordinal)
    {
        var type = ordinal >= 0 && columns != null && ordinal < columns.Count ? columns[ordinal].FieldType : null;

        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => ReadNumber(ref reader, type),
            JsonTokenType.True => _trueValue,
            JsonTokenType.False => _falseValue,
            JsonTokenType.Null => null,
            JsonTokenType.StartObject => ReadDictionary(ref reader),
            JsonTokenType.StartArray => ReadList(ref reader), _ => null,
        };
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

    private static Dictionary<String, Object?> ReadDictionary(ref Utf8JsonReader reader)
    {
        var dic = new Dictionary<String, Object?>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;

            var key = reader.GetString()!;
            reader.Read();
            dic[key] = ReadCell(ref reader, null, -1);
        }

        return dic;
    }

    private static List<Object?> ReadList(ref Utf8JsonReader reader)
    {
        var list = new List<Object?>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;

            list.Add(ReadCell(ref reader, null, -1));
        }

        return list;
    }
    #endregion

    #region 辅助
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
