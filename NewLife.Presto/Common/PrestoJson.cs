using System.Collections.Generic;
using System.Globalization;
using NewLife.Serialization;

namespace NewLife.Presto;

/// <summary>Presto/Trino JSON 响应投影</summary>
/// <remarks>
/// 使用 NewLife.Core 的 <see cref="JsonParser"/> 字典模型，宽松解析：未知字段忽略、缺失字段使用默认值。
/// 解析失败统一抛出 <see cref="PrestoException"/>，保留内部异常便于定位。
/// </remarks>
internal static class PrestoJson
{
    #region 解析
    /// <summary>解析 JSON 对象</summary>
    /// <param name="json">JSON 文本</param>
    /// <returns>字典；空文本返回空</returns>
    public static IDictionary<String, Object?>? ParseObject(String? json)
    {
        if (json.IsNullOrWhiteSpace()) return null;

        try
        {
            return JsonParser.Decode(json);
        }
        catch (Exception ex)
        {
            throw new PrestoException(Localization.Translate("Failed to parse the server response: {0}", ex.Message), ex);
        }
    }

    /// <summary>解析 JSON 数组</summary>
    /// <param name="json">JSON 文本</param>
    /// <returns>数组；空文本或非数组返回空</returns>
    public static IList<Object?>? ParseArray(String? json)
    {
        if (json.IsNullOrWhiteSpace()) return null;

        var text = json.Trim();
        if (text.Length == 0 || text[0] != '[') return null;

        try
        {
            // JsonParser.Decode 只接受对象，包一层对象再取数组
            var dic = JsonParser.Decode("{\"items\":" + text + "}");

            return dic?["items"] as IList<Object?>;
        }
        catch (Exception ex)
        {
            throw new PrestoException(Localization.Translate("Failed to parse the server response: {0}", ex.Message), ex);
        }
    }

    /// <summary>解析单页查询结果</summary>
    /// <param name="json">JSON 文本</param>
    /// <param name="structured">复杂类型是否返回结构化对象</param>
    /// <returns>查询结果</returns>
    public static PrestoQueryResults ParseResults(String? json, Boolean structured = false)
    {
        var dic = ParseObject(json) ?? throw new PrestoException(Localization.Translate("The server returned an empty response."));

        var data = dic.TryGetValue("data", out var dataValue) ? dataValue : null;
        var spooled = AsDictionary(data);
        return new PrestoQueryResults
        {
            Id = GetString(dic, "id"),
            InfoUri = GetString(dic, "infoUri"),
            NextUri = GetString(dic, "nextUri"),
            Columns = ParseColumns(dic.TryGetValue("columns", out var columns) ? columns : null, structured),
            Data = spooled == null ? ParseData(data) : [],
            DataEncoding = spooled == null ? null : GetString(spooled, "encoding"),
            Segments = spooled == null ? [] : ParseSegments(spooled.TryGetValue("segments", out var segments) ? segments : null),
            Stats = ParseStats(dic.TryGetValue("stats", out var stats) ? stats : null),
            Warnings = ParseWarnings(dic.TryGetValue("warnings", out var warnings) ? warnings : null),
            Error = ParseError(dic.TryGetValue("error", out var error) ? error : null),
            UpdateType = GetString(dic, "updateType"),
            UpdateCount = GetNullableInt64(dic, "updateCount"),
        };
    }

    /// <summary>解析服务端信息</summary>
    /// <param name="json">JSON 文本</param>
    /// <returns>服务端信息</returns>
    public static PrestoServerInfo ParseServerInfo(String? json)
    {
        var dic = ParseObject(json) ?? throw new PrestoException(Localization.Translate("The server returned an empty response."));

        return ParseServerInfo(dic);
    }

    /// <summary>解析服务端信息</summary>
    /// <param name="value">已解析的对象</param>
    /// <returns>服务端信息</returns>
    public static PrestoServerInfo ParseServerInfo(Object? value)
    {
        var dic = AsDictionary(value) ?? throw new PrestoException(Localization.Translate("The server returned an empty response."));

        var version = GetString(dic, "nodeVersion");
        if (version.IsNullOrEmpty() && dic.TryGetValue("nodeVersion", out var node) && AsDictionary(node) is { } nodeDic)
            version = GetString(nodeDic, "version");

        return new PrestoServerInfo
        {
            NodeVersion = version,
            Environment = GetString(dic, "environment"),
            Coordinator = GetNullableBoolean(dic, "coordinator"),
            Starting = GetNullableBoolean(dic, "starting"),
            Uptime = GetString(dic, "uptime"),
            Raw = dic,
        };
    }

    /// <summary>解析查询诊断信息</summary>
    /// <param name="value">查询对象</param>
    /// <returns>查询信息</returns>
    public static PrestoQueryInfo ParseQueryInfo(Object? value)
    {
        var dic = AsDictionary(value) ?? throw new PrestoException(Localization.Translate("The server returned an empty response."));
        var session = dic.TryGetValue("session", out var sessionValue) ? AsDictionary(sessionValue) : null;
        var stats = dic.TryGetValue("queryStats", out var queryStats) ? queryStats : dic.TryGetValue("stats", out var statsValue) ? statsValue : null;
        var error = dic.TryGetValue("errorInfo", out var errorInfo) ? errorInfo : dic.TryGetValue("error", out var errorValue) ? errorValue : null;

        return new PrestoQueryInfo
        {
            Id = GetString(dic, "queryId") ?? GetString(dic, "id"),
            State = GetString(dic, "state"),
            Query = GetString(dic, "query"),
            User = GetString(session, "user"),
            Source = GetString(session, "source"),
            Catalog = GetString(session, "catalog"),
            Schema = GetString(session, "schema"),
            Stats = ParseStats(stats),
            Error = error == null ? null : ParseError(error),
            Raw = dic,
        };
    }

    /// <summary>解析错误对象</summary>
    /// <param name="value">错误对象</param>
    /// <returns>错误模型；非对象返回空</returns>
    public static PrestoQueryError? ParseError(Object? value)
    {
        if (AsDictionary(value) is not { } dic) return null;

        var failure = dic.TryGetValue("failureInfo", out var failureInfo) ? failureInfo : null;
        var location = dic.TryGetValue("errorLocation", out var errorLocation) ? errorLocation : null;

        return new PrestoQueryError
        {
            Message = GetString(dic, "message"),
            ErrorCode = GetInt32(dic, "errorCode"),
            ErrorName = GetString(dic, "errorName"),
            ErrorType = GetString(dic, "errorType"),
            SqlState = GetString(dic, "sqlState"),
            FailureType = AsDictionary(failure) is { } failureDic ? GetString(failureDic, "type") : null,
            Location = FormatLocation(location),
            FailureInfo = failure,
            ErrorLocation = location,
        };
    }

    /// <summary>解析统计对象</summary>
    /// <param name="value">统计对象</param>
    /// <returns>统计模型；非对象返回空</returns>
    public static PrestoQueryStats? ParseStats(Object? value)
    {
        if (AsDictionary(value) is not { } dic) return null;

        return new PrestoQueryStats
        {
            State = GetString(dic, "state"),
            QueuedTimeMillis = GetInt64(dic, "queuedTimeMillis"),
            ElapsedTimeMillis = GetInt64(dic, "elapsedTimeMillis"),
            ProcessedRows = GetInt64(dic, "processedRows"),
            ProcessedBytes = GetInt64(dic, "processedBytes"),
            TotalSplits = GetInt32(dic, "totalSplits"),
            CompletedSplits = GetInt32(dic, "completedSplits"),
            RunningSplits = GetInt32(dic, "runningSplits"),
            Raw = dic,
        };
    }

    /// <summary>解析警告列表</summary>
    /// <param name="value">警告数组</param>
    /// <returns>警告列表</returns>
    public static List<PrestoWarning> ParseWarnings(Object? value)
    {
        var list = new List<PrestoWarning>();
        if (AsList(value) is not { } items) return list;

        foreach (var item in items)
        {
            if (AsDictionary(item) is not { } dic) continue;

            list.Add(new PrestoWarning
            {
                Message = GetString(dic, "message"),
                WarningCode = GetInt32(dic, "warningCode"),
                ErrorName = GetString(dic, "errorName"),
                ErrorType = GetString(dic, "errorType"),
            });
        }

        return list;
    }

    private static List<PrestoColumn> ParseColumns(Object? value, Boolean structured)
    {
        var list = new List<PrestoColumn>();
        if (AsList(value) is not { } items) return list;

        foreach (var item in items)
        {
            if (AsDictionary(item) is not { } dic) continue;

            var type = GetString(dic, "type");
            if (type.IsNullOrEmpty() && dic.TryGetValue("typeSignature", out var signature) && AsDictionary(signature) is { } signatureDic)
                type = GetString(signatureDic, "rawType");
            type ??= String.Empty;

            list.Add(new PrestoColumn
            {
                Name = GetString(dic, "name") ?? String.Empty,
                Type = type,
                FieldType = PrestoTypeMapper.GetFieldType(type, structured),
            });
        }

        return list;
    }

    private static List<IList<Object?>> ParseData(Object? value)
    {
        var list = new List<IList<Object?>>();
        if (AsList(value) is not { } rows) return list;

        foreach (var row in rows)
        {
            var values = new List<Object?>();
            if (AsList(row) is { } cells)
            {
                foreach (var cell in cells) values.Add(cell);
            }
            list.Add(values);
        }

        return list;
    }

    private static List<PrestoQuerySegment> ParseSegments(Object? value)
    {
        var list = new List<PrestoQuerySegment>();
        if (AsList(value) is not { } items) return list;

        foreach (var item in items)
        {
            if (AsDictionary(item) is not { } dic) continue;

            list.Add(new PrestoQuerySegment
            {
                Type = GetString(dic, "type"),
                Data = GetString(dic, "data"),
                Uri = GetString(dic, "uri"),
                AckUri = GetString(dic, "ackUri"),
                Headers = ParseHeaders(dic.TryGetValue("headers", out var headers) ? headers : null),
            });
        }

        return list;
    }

    private static Dictionary<String, IList<String>> ParseHeaders(Object? value)
    {
        var headers = new Dictionary<String, IList<String>>(StringComparer.OrdinalIgnoreCase);
        if (AsDictionary(value) is not { } dic) return headers;

        foreach (var item in dic)
        {
            if (AsList(item.Value) is not { } values) continue;

            var list = new List<String>();
            foreach (var text in values)
            {
                if (ToText(text) is { } line) list.Add(line);
            }
            headers[item.Key] = list;
        }

        return headers;
    }

    /// <summary>解析分段中的行数组</summary>
    /// <param name="json">JSON 数组文本</param>
    /// <returns>行列表</returns>
    public static IList<IList<Object?>> ParseRows(String? json)
    {
        if (json.IsNullOrWhiteSpace()) return [];

        if (ParseArray(json) is not { } rows)
            throw new PrestoException(Localization.Translate("Failed to parse the server response: {0}", "expected a JSON array of rows"));

        return ParseData(rows);
    }

    private static String? FormatLocation(Object? value)
    {
        if (AsDictionary(value) is not { } dic) return null;

        var line = GetNullableInt32(dic, "lineNumber");
        var column = GetNullableInt32(dic, "columnNumber");
        if (line == null && column == null) return null;

        return $"{line.GetValueOrDefault()}:{column.GetValueOrDefault()}";
    }
    #endregion

    #region 读取
    /// <summary>按类型读取对象</summary>
    /// <param name="value">原始值</param>
    /// <returns>字典；非对象返回空</returns>
    public static IDictionary<String, Object?>? AsDictionary(Object? value) => value as IDictionary<String, Object?>;

    /// <summary>按类型读取数组</summary>
    /// <param name="value">原始值</param>
    /// <returns>数组；非数组返回空</returns>
    public static IList<Object?>? AsList(Object? value) => value as IList<Object?>;

    /// <summary>读取字符串</summary>
    /// <param name="dic">字典</param>
    /// <param name="key">键</param>
    /// <returns>字符串；不存在返回空</returns>
    public static String? GetString(IDictionary<String, Object?>? dic, String key)
    {
        if (dic == null || !dic.TryGetValue(key, out var value) || value == null) return null;
        if (value is String text) return text;
        if (value is IDictionary<String, Object?> || value is IList<Object?>) return null;

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    /// <summary>读取 Int32</summary>
    /// <param name="dic">字典</param>
    /// <param name="key">键</param>
    /// <returns>数值；无法解析返回 0</returns>
    public static Int32 GetInt32(IDictionary<String, Object?>? dic, String key) => GetNullableInt32(dic, key) ?? 0;

    /// <summary>读取可空 Int32</summary>
    /// <param name="dic">字典</param>
    /// <param name="key">键</param>
    /// <returns>数值；无法解析返回空</returns>
    public static Int32? GetNullableInt32(IDictionary<String, Object?>? dic, String key)
    {
        var value = GetNullableInt64(dic, key);
        if (value == null || value < Int32.MinValue || value > Int32.MaxValue) return null;

        return (Int32)value.Value;
    }

    /// <summary>读取 Int64</summary>
    /// <param name="dic">字典</param>
    /// <param name="key">键</param>
    /// <returns>数值；无法解析返回 0</returns>
    public static Int64 GetInt64(IDictionary<String, Object?>? dic, String key) => GetNullableInt64(dic, key) ?? 0;

    /// <summary>读取可空 Int64</summary>
    /// <param name="dic">字典</param>
    /// <param name="key">键</param>
    /// <returns>数值；无法解析返回空</returns>
    public static Int64? GetNullableInt64(IDictionary<String, Object?>? dic, String key)
    {
        if (dic == null || !dic.TryGetValue(key, out var value) || value == null) return null;

        switch (value)
        {
            case Int32 n: return n;
            case Int64 n: return n;
            case Double d: return (Int64)d;
            case Single f: return (Int64)f;
            case Decimal m: return (Int64)m;
            case String s when Int64.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n): return n;
            default: return null;
        }
    }

    /// <summary>读取可空 Boolean</summary>
    /// <param name="dic">字典</param>
    /// <param name="key">键</param>
    /// <returns>布尔值；无法解析返回空</returns>
    public static Boolean? GetNullableBoolean(IDictionary<String, Object?>? dic, String key)
    {
        if (dic == null || !dic.TryGetValue(key, out var value) || value == null) return null;

        switch (value)
        {
            case Boolean b: return b;
            case String s when Boolean.TryParse(s, out var b): return b;
            default: return null;
        }
    }

    /// <summary>把 JSON 标量转为文本</summary>
    /// <param name="value">原始值</param>
    /// <returns>文本；空值返回空</returns>
    public static String? ToText(Object? value)
    {
        if (value == null) return null;
        if (value is String text) return text;

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }
    #endregion
}
