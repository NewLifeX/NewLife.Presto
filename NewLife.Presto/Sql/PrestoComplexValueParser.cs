using System.Collections.Generic;
using NewLife.Serialization;

namespace NewLife.Presto;

/// <summary>复杂类型值解析器</summary>
/// <remarks>
/// 把 Presto 返回的 JSON 文本值按类型签名转成结构化对象：
/// `array` → <see cref="Object"/>[]、`map` → <see cref="IDictionary{TKey,TValue}"/>、`row` → <see cref="Object"/>[]。
/// 解析失败时回退原始字符串，避免因为个别畸形值中断整行读取。
/// </remarks>
internal static class PrestoComplexValueParser
{
    /// <summary>按类型签名解析值</summary>
    /// <param name="value">原始值，可能是 JSON 文本或已解析对象</param>
    /// <param name="signature">类型签名</param>
    /// <returns>结构化值；无法解析时返回原始文本</returns>
    public static Object? Parse(Object? value, PrestoTypeSignature signature)
    {
        if (value == null) return null;

        var text = PrestoJson.ToText(value) ?? String.Empty;
        if (signature == null) return text;
        if (!signature.IsComplex) return PrestoValueConverter.ConvertScalar(value, signature.Text);

        var decoded = value is IList<Object?> || value is IDictionary<String, Object?> ? value : Decode(text);
        if (decoded == null || decoded is String) return decoded as String ?? text;

        return signature.BaseType switch
        {
            "array" => ParseArray(decoded, signature.Element, text),
            "map" => ParseMap(decoded, signature.Value, text),
            "row" => ParseRow(decoded, signature, text),
            _ => text,
        };
    }

    private static Object ParseArray(Object decoded, PrestoTypeSignature? element, String fallback)
    {
        if (decoded is not IList<Object?> items) return fallback;

        var result = new Object?[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            result[i] = element == null ? items[i] : Parse(items[i], element);
        }

        return result;
    }

    private static Object ParseMap(Object decoded, PrestoTypeSignature? value, String fallback)
    {
        if (decoded is not IDictionary<String, Object?> dic) return fallback;

        var result = new Dictionary<String, Object?>(dic.Count, StringComparer.Ordinal);
        foreach (var item in dic)
        {
            result[item.Key] = value == null ? item.Value : Parse(item.Value, value);
        }

        return result;
    }

    private static Object ParseRow(Object decoded, PrestoTypeSignature signature, String fallback)
    {
        if (decoded is not IList<Object?> items) return fallback;

        var fields = signature.Fields;
        if (fields == null) return fallback;

        var count = Math.Max(items.Count, fields.Length);
        var result = new Object?[count];
        for (var i = 0; i < count; i++)
        {
            var item = i < items.Count ? items[i] : null;
            result[i] = i < fields.Length ? Parse(item, fields[i]) : item;
        }

        return result;
    }

    private static Object? Decode(String text)
    {
        try
        {
            var trimmed = text.TrimStart();
            if (trimmed.Length == 0) return null;

            if (trimmed[0] is '[' or '{')
            {
                var dic = JsonParser.Decode("{\"items\":" + trimmed + "}");

                return dic != null && dic.TryGetValue("items", out var value) ? value : null;
            }

            var wrapper = JsonParser.Decode("{\"items\":[" + trimmed + "]}");
            if (wrapper != null && wrapper.TryGetValue("items", out var list) && list is IList<Object?> items && items.Count > 0) return items[0];

            return text;
        }
        catch
        {
            return text;
        }
    }
}
