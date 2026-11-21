using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NewLife.Presto;

/// <summary>Presto 类型签名树</summary>
/// <remarks>
/// 支持 `array(integer)`、`map(varchar, integer)`、`row(name varchar, age integer)` 及尖括号写法；
/// 标量类型保留完整签名文本，供取值时按基础类型转换。解析失败时退化为标量，取值回退原始字符串。
/// </remarks>
internal sealed class PrestoTypeSignature
{
    #region 属性
    /// <summary>基础类型名（小写）</summary>
    public String BaseType { get; init; } = String.Empty;

    /// <summary>完整类型文本</summary>
    public String Text { get; init; } = String.Empty;

    /// <summary>数组元素类型</summary>
    public PrestoTypeSignature? Element { get; init; }

    /// <summary>映射键类型</summary>
    public PrestoTypeSignature? Key { get; init; }

    /// <summary>映射值类型</summary>
    public PrestoTypeSignature? Value { get; init; }

    /// <summary>行字段名</summary>
    public String[]? FieldNames { get; init; }

    /// <summary>行字段类型</summary>
    public PrestoTypeSignature[]? Fields { get; init; }

    /// <summary>是否复杂类型</summary>
    public Boolean IsComplex => BaseType is "array" or "map" or "row";
    #endregion

    #region 解析
    /// <summary>解析类型签名</summary>
    /// <param name="text">类型签名</param>
    /// <returns>类型签名树；无法识别时退化为标量</returns>
    public static PrestoTypeSignature Parse(String? text)
    {
        if (text.IsNullOrWhiteSpace()) return new PrestoTypeSignature { BaseType = String.Empty, Text = String.Empty };

        var parser = new Parser(text.Trim());
        try
        {
            return parser.ParseType();
        }
        catch
        {
            // 签名不合法时保持原有字符串降级行为
            return new PrestoTypeSignature { BaseType = String.Empty, Text = text.Trim() };
        }
    }

    private sealed class Parser
    {
        private readonly String _text;
        private Int32 _index;

        public Parser(String text) => _text = text;

        public PrestoTypeSignature ParseType()
        {
            SkipSpace();

            var start = _index;
            var name = ReadIdentifier();
            if (name.Length == 0)
            {
                // 无法识别时消费一个字符，避免死循环
                if (_index < _text.Length) _index++;

                return new PrestoTypeSignature { BaseType = String.Empty, Text = _text[start.._index] };
            }

            var lower = name.ToLowerInvariant();
            SkipSpace();

            if (lower is "array" or "map" or "row" && _index < _text.Length && (_text[_index] == '(' || _text[_index] == '<'))
            {
                var close = _text[_index] == '(' ? ')' : '>';
                _index++;

                PrestoTypeSignature signature;
                if (lower == "array")
                {
                    var element = ParseType();
                    signature = new PrestoTypeSignature { BaseType = lower, Element = element, Text = _text[start.._index] };
                }
                else if (lower == "map")
                {
                    var key = ParseType();
                    Expect(',');
                    var value = ParseType();
                    signature = new PrestoTypeSignature { BaseType = lower, Key = key, Value = value, Text = _text[start.._index] };
                }
                else
                {
                    signature = ParseRow(start);
                }

                Expect(close);

                return new PrestoTypeSignature
                {
                    BaseType = signature.BaseType,
                    Text = _text[start.._index],
                    Element = signature.Element,
                    Key = signature.Key,
                    Value = signature.Value,
                    FieldNames = signature.FieldNames,
                    Fields = signature.Fields,
                };
            }

            // 标量：跳过参数括号与 with time zone 后缀
            if (_index < _text.Length && _text[_index] == '(') SkipBalanced('(', ')');
            SkipSpace();
            var remainder = _text[_index..];
            if (remainder.StartsWith("with time zone", StringComparison.OrdinalIgnoreCase)) _index += "with time zone".Length;

            return new PrestoTypeSignature { BaseType = lower, Text = _text[start.._index].Trim() };
        }

        private PrestoTypeSignature ParseRow(Int32 start)
        {
            var names = new List<String>();
            var fields = new List<PrestoTypeSignature>();

            while (true)
            {
                SkipSpace();
                if (_index >= _text.Length || _text[_index] == ')') break;

                var fieldStart = _index;
                var fieldName = ReadFieldName();
                SkipSpace();

                PrestoTypeSignature fieldType;
                if (fieldName.Length > 0 && _index < _text.Length && _text[_index] != ',' && _text[_index] != ')')
                {
                    fieldType = ParseType();
                }
                else
                {
                    // 没有字段名，把已读内容当作类型重新解析
                    _index = fieldStart;
                    fieldName = String.Empty;
                    fieldType = ParseType();
                }

                names.Add(fieldName);
                fields.Add(fieldType);

                SkipSpace();
                if (_index < _text.Length && _text[_index] == ',')
                {
                    _index++;
                    continue;
                }

                break;
            }

            return new PrestoTypeSignature
            {
                BaseType = "row",
                Text = _text[start.._index],
                FieldNames = names.ToArray(),
                Fields = fields.ToArray(),
            };
        }

        private String ReadFieldName()
        {
            if (_index < _text.Length && _text[_index] == '"')
            {
                _index++;
                var builder = new StringBuilder();
                while (_index < _text.Length)
                {
                    var ch = _text[_index++];
                    if (ch == '"')
                    {
                        if (_index < _text.Length && _text[_index] == '"')
                        {
                            builder.Append('"');
                            _index++;
                            continue;
                        }

                        break;
                    }

                    builder.Append(ch);
                }

                return builder.ToString();
            }

            return ReadIdentifier();
        }

        private String ReadIdentifier()
        {
            var start = _index;
            while (_index < _text.Length)
            {
                var ch = _text[_index];
                if (Char.IsLetterOrDigit(ch) || ch == '_' || ch == '$') _index++;
                else break;
            }

            return _text[start.._index];
        }

        private void SkipBalanced(Char open, Char close)
        {
            var depth = 0;
            while (_index < _text.Length)
            {
                var ch = _text[_index++];
                if (ch == open) depth++;
                else if (ch == close && --depth == 0) return;
            }
        }

        private void Expect(Char expected)
        {
            SkipSpace();
            if (_index < _text.Length && _text[_index] == expected)
            {
                _index++;
                return;
            }

            throw new FormatException(String.Format(CultureInfo.InvariantCulture, "Expected '{0}' at position {1}.", expected, _index));
        }

        private void SkipSpace()
        {
            while (_index < _text.Length && Char.IsWhiteSpace(_text[_index])) _index++;
        }
    }
    #endregion
}
