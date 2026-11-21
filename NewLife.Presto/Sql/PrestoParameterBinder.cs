using System.Collections;
using System.Globalization;
using System.Text;

namespace NewLife.Presto;

/// <summary>参数绑定器</summary>
/// <remarks>
/// Presto/Trino REST 协议没有服务端绑定参数，这里把 `@name` 替换为安全字面量。
/// 扫描时跳过单引号字符串、双引号标识符、行注释与块注释（支持嵌套块注释），避免误替换。
/// </remarks>
public static class PrestoParameterBinder
{
    #region 绑定
    /// <summary>绑定参数并返回最终 SQL</summary>
    /// <param name="sql">原始 SQL</param>
    /// <param name="parameters">参数集合</param>
    /// <returns>替换后的 SQL</returns>
    public static String Bind(String sql, PrestoParameterCollection parameters)
    {
        if (sql == null) throw new ArgumentNullException(nameof(sql));

        var map = BuildMap(parameters);

        var builder = new StringBuilder(sql.Length + 32);
        var missing = new List<String>();
        var index = 0;
        while (index < sql.Length)
        {
            var ch = sql[index];

            if (ch == '\'')
            {
                CopyQuoted(sql, ref index, '\'', builder);
                continue;
            }
            if (ch == '"')
            {
                CopyQuoted(sql, ref index, '"', builder);
                continue;
            }
            if (ch == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                CopyLineComment(sql, ref index, builder);
                continue;
            }
            if (ch == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                CopyBlockComment(sql, ref index, builder);
                continue;
            }
            if (ch == '@' && IsParameterStart(sql, index))
            {
                var start = index + 1;
                var end = start;
                while (end < sql.Length && IsNamePart(sql[end])) end++;

                var name = sql.Substring(start, end - start);
                if (map.TryGetValue(name, out var value))
                    builder.Append(FormatValue(value));
                else
                {
                    var token = "@" + name;
                    if (!missing.Contains(token)) missing.Add(token);
                }

                index = end;
                continue;
            }

            builder.Append(ch);
            index++;
        }

        if (missing.Count > 0)
            throw new PrestoException(Localization.Translate("Parameter '{0}' was not found.", String.Join(", ", missing.ToArray())));

        return builder.ToString();
    }

    private static Dictionary<String, Object?> BuildMap(PrestoParameterCollection? parameters)
    {
        var map = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase);
        if (parameters == null) return map;

        foreach (var item in parameters)
        {
            if (item is not PrestoParameter parameter) continue;

            var name = NormalizeName(parameter.ParameterName);
            if (name.IsNullOrEmpty()) throw new PrestoException(Localization.Translate("Parameter name cannot be empty."));
            if (parameter.Direction != System.Data.ParameterDirection.Input) throw new NotSupportedException(Localization.Translate("Only input parameters are supported."));

            map[name] = parameter.Value;
        }

        return map;
    }

    /// <summary>归一化参数名，去掉前导 @</summary>
    /// <param name="name">参数名</param>
    /// <returns>不含 @ 的参数名</returns>
    public static String NormalizeName(String? name)
    {
        if (name.IsNullOrWhiteSpace()) return String.Empty;

        var text = name.Trim();
        return text.StartsWith("@") ? text[1..] : text;
    }
    #endregion

    #region 格式化
    /// <summary>把 .NET 值格式化为 Presto/Trino 字面量</summary>
    /// <param name="value">参数值</param>
    /// <returns>SQL 字面量</returns>
    public static String FormatValue(Object? value)
    {
        if (value == null || value is DBNull) return "NULL";

        switch (value)
        {
            case Boolean b:
                return b ? "true" : "false";
            case Byte or SByte or Int16 or UInt16 or Int32 or UInt32 or Int64 or UInt64:
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
            case Single f:
                EnsureFinite(f);
                return f.ToString("R", CultureInfo.InvariantCulture);
            case Double d:
                EnsureFinite(d);
                return d.ToString("R", CultureInfo.InvariantCulture);
            case Decimal m:
                return m.ToString(CultureInfo.InvariantCulture);
            case String s:
                return Quote(s);
            case Char c:
                return Quote(c.ToString());
            case DateTime dt:
                return "TIMESTAMP '" + dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + "'";
            case DateTimeOffset dto:
                return "TIMESTAMP '" + dto.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture) + "'";
#if NET6_0_OR_GREATER
            case DateOnly date:
                return "DATE '" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "'";
            case TimeOnly time:
                return "TIME '" + time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + "'";
#endif
            case TimeSpan ts:
                return FormatInterval(ts);
            case Byte[] bytes:
                return "X'" + bytes.ToHex() + "'";
            case Guid guid:
                return "UUID '" + guid.ToString("D") + "'";
            case Enum e:
                return Quote(Convert.ToString(e, CultureInfo.InvariantCulture) ?? String.Empty);
            case IEnumerable enumerable:
                return FormatCollection(enumerable);
            default:
                return Quote(Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty);
        }
    }

    private static void EnsureFinite(Single value)
    {
        if (Single.IsNaN(value) || Single.IsInfinity(value))
            throw new PrestoException(Localization.Translate("Special floating-point values (NaN and Infinity) are not supported as parameters."));
    }

    private static void EnsureFinite(Double value)
    {
        if (Double.IsNaN(value) || Double.IsInfinity(value))
            throw new PrestoException(Localization.Translate("Special floating-point values (NaN and Infinity) are not supported as parameters."));
    }

    private static String Quote(String value) => "'" + value.Replace("'", "''") + "'";

    private static String FormatInterval(TimeSpan value)
    {
        if (value == TimeSpan.MinValue)
            throw new PrestoException(Localization.Translate("The parameter value is outside the supported interval range."));

        var negative = value < TimeSpan.Zero;
        var abs = negative ? value.Negate() : value;
        var text = String.Format(
            CultureInfo.InvariantCulture,
            "{0}{1} {2:00}:{3:00}:{4:00}.{5:000}",
            negative ? "-" : "",
            abs.Days,
            abs.Hours,
            abs.Minutes,
            abs.Seconds,
            abs.Milliseconds);

        return "INTERVAL '" + text + "' DAY TO SECOND";
    }

    private static String FormatCollection(IEnumerable values)
    {
        var builder = new StringBuilder("(");
        var any = false;
        foreach (var item in values)
        {
            if (any) builder.Append(", ");
            builder.Append(FormatValue(item));
            any = true;
        }

        // 空集合按 (NULL) 处理：IN (NULL) 的结果是不匹配任何行，与空集合语义一致
        if (!any) builder.Append("NULL");

        builder.Append(')');
        return builder.ToString();
    }
    #endregion

    #region 扫描辅助
    private static void CopyQuoted(String sql, ref Int32 index, Char quote, StringBuilder builder)
    {
        builder.Append(sql[index++]);
        while (index < sql.Length)
        {
            var ch = sql[index++];
            builder.Append(ch);

            if (ch == quote)
            {
                // 双写引号是转义，继续留在字符串/标识符内
                if (index < sql.Length && sql[index] == quote)
                {
                    builder.Append(sql[index++]);
                    continue;
                }

                return;
            }
        }
    }

    private static void CopyLineComment(String sql, ref Int32 index, StringBuilder builder)
    {
        while (index < sql.Length)
        {
            var ch = sql[index++];
            builder.Append(ch);
            if (ch == '\r' || ch == '\n') return;
        }
    }

    private static void CopyBlockComment(String sql, ref Int32 index, StringBuilder builder)
    {
        var depth = 0;
        while (index < sql.Length)
        {
            if (index + 1 < sql.Length && sql[index] == '/' && sql[index + 1] == '*')
            {
                builder.Append("/*");
                index += 2;
                depth++;
                continue;
            }
            if (index + 1 < sql.Length && sql[index] == '*' && sql[index + 1] == '/')
            {
                builder.Append("*/");
                index += 2;
                depth--;
                if (depth <= 0) return;
                continue;
            }

            builder.Append(sql[index++]);
        }
    }

    private static Boolean IsNameStart(Char ch) => ch == '_' || (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');

    private static Boolean IsNamePart(Char ch) => IsNameStart(ch) || (ch >= '0' && ch <= '9');

    private static Boolean IsParameterStart(String sql, Int32 index)
    {
        if (index + 1 >= sql.Length || !IsNameStart(sql[index + 1])) return false;

        // 参数前必须是分隔符；标识符内部或 @@ 转义都不按参数处理
        if (index == 0) return true;

        var previous = sql[index - 1];
        return !IsNamePart(previous) && previous != '@';
    }
    #endregion
}
