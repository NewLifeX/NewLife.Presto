using System;
using System.Collections.Generic;
using NewLife.Presto;

namespace UnitTest;

/// <summary>复杂类型结构化取值测试</summary>
public class PrestoComplexTypeTests
{
    [Fact(DisplayName = "默认仍返回原始字符串")]
    public void DefaultString()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(Result("array(integer)", JsonString("[1, 2, 3]")));
        var connection = TestHelper.CreateOpenConnection(handler);

        using var command = connection.CreateCommand("select array[1,2,3]");
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(typeof(String), reader.GetFieldType(0));
        Assert.Equal("[1, 2, 3]", reader.GetValue(0));
    }

    [Fact(DisplayName = "默认模式下真实数组/映射/行单元格返回原始 JSON 文本")]
    public void DefaultRawComplexCells()
    {
        var cases = new (String Type, String Cell, String Expected)[]
        {
            ("array(integer)", "[1, 2, 3]", "[1, 2, 3]"),
            ("map(varchar, integer)", "{\"a\": 1, \"b\": 2}", "{\"a\": 1, \"b\": 2}"),
            ("row(name varchar, age integer)", "[\"alice\", 30]", "[\"alice\", 30]"),
            ("array(map(varchar, integer))", "[{\"a\": 1}, {\"b\": 2}]", "[{\"a\": 1}, {\"b\": 2}]"),
        };

        foreach (var item in cases)
        {
            var handler = new FakePrestoHandler();
            handler.EnqueueJson(Result(item.Type, item.Cell));
            var connection = TestHelper.CreateOpenConnection(handler);

            using var command = connection.CreateCommand("select 1");
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(typeof(String), reader.GetFieldType(0));
            Assert.Equal(item.Expected, reader.GetValue(0));
        }
    }

    [Fact(DisplayName = "对象树写成 JSON 文本")]
    public void WriteJsonTree()
    {
        Assert.Equal("null", PrestoJson.WriteJson(null));
        Assert.Equal("\"a\\\"b\\\\c\\n\"", PrestoJson.WriteJson("a\"b\\c\n"));
        Assert.Equal("\"\\u0001\"", PrestoJson.WriteJson("\u0001"));
        Assert.Equal("\"\\b\\f\\r\\t\"", PrestoJson.WriteJson("\b\f\r\t"));
        Assert.Equal("true", PrestoJson.WriteJson(true));
        Assert.Equal("12.5", PrestoJson.WriteJson(12.5m));
        Assert.Equal("[1,\"x\",null]", PrestoJson.WriteJson(new List<Object?> { 1, "x", null }));
        Assert.Equal("{\"a\":[1,2],\"b\":{\"c\":false}}", PrestoJson.WriteJson(new Dictionary<String, Object?>
        {
            ["a"] = new List<Object?> { 1, 2 },
            ["b"] = new Dictionary<String, Object?> { ["c"] = false },
        }));
    }

    [Fact(DisplayName = "旧目标对象树在复杂列上还原为 JSON 文本")]
    public void ComplexTreeConvertedToJson()
    {
        var column = new PrestoColumn { Name = "c", Type = "array(integer)", FieldType = typeof(String) };

        Assert.Equal("[1,2]", PrestoValueConverter.Convert(new List<Object?> { 1, 2 }, column));
        Assert.Equal("[1, 2]", PrestoValueConverter.Convert("[1, 2]", column));
    }

    [Fact(DisplayName = "结构化模式下真实数组单元格解析为对象数组")]
    public void StructuredRawArrayCell()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(Result("array(integer)", "[1, 2, 3]"));
        var connection = TestHelper.CreateOpenConnection(handler, ";ComplexTypes=Structured");

        using var command = connection.CreateCommand("select array[1,2,3]");
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(typeof(Object), reader.GetFieldType(0));

        var values = Assert.IsType<Object[]>(reader.GetValue(0));
        Assert.Equal(new Object?[] { 1, 2, 3 }, values);
    }

    [Fact(DisplayName = "array 结构化取值")]
    public void ArrayStructured()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(Result("array(integer)", JsonString("[1, 2, 3]")));
        var connection = TestHelper.CreateOpenConnection(handler, ";ComplexTypes=Structured");

        using var command = connection.CreateCommand("select array[1,2,3]");
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(typeof(Object), reader.GetFieldType(0));

        var values = Assert.IsType<Object[]>(reader.GetValue(0));
        Assert.Equal(new Object?[] { 1, 2, 3 }, values);
        Assert.Equal("array(integer)", reader.GetDataTypeName(0));
    }

    [Fact(DisplayName = "map 结构化取值")]
    public void MapStructured()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(Result("map(varchar, integer)", JsonString("{\"a\": 1, \"b\": 2}")));
        var connection = TestHelper.CreateOpenConnection(handler, ";ComplexTypes=Structured");

        using var command = connection.CreateCommand("select map(array['a','b'], array[1,2])");
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());

        var values = Assert.IsAssignableFrom<IDictionary<String, Object?>>(reader.GetValue(0));
        Assert.Equal(1, values["a"]);
        Assert.Equal(2, values["b"]);
    }

    [Fact(DisplayName = "row 结构化取值")]
    public void RowStructured()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(Result("row(name varchar, age integer)", JsonString("[\"alice\", 30]")));
        var connection = TestHelper.CreateOpenConnection(handler, ";ComplexTypes=Structured");

        using var command = connection.CreateCommand("select cast(row('alice', 30) as row(name varchar, age integer))");
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());

        var values = Assert.IsType<Object[]>(reader.GetValue(0));
        Assert.Equal("alice", values[0]);
        Assert.Equal(30, values[1]);
    }

    [Fact(DisplayName = "嵌套 array(map) 结构化取值")]
    public void NestedStructured()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(Result("array(map(varchar, integer))", JsonString("[{\"a\": 1}, {\"b\": 2}]")));
        var connection = TestHelper.CreateOpenConnection(handler, ";ComplexTypes=Structured");

        using var command = connection.CreateCommand("select array[map(array['a'], array[1])]");
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());

        var list = Assert.IsType<Object[]>(reader.GetValue(0));
        Assert.Equal(2, list.Length);
        var first = Assert.IsAssignableFrom<IDictionary<String, Object?>>(list[0]);
        Assert.Equal(1, first["a"]);
        var second = Assert.IsAssignableFrom<IDictionary<String, Object?>>(list[1]);
        Assert.Equal(2, second["b"]);
    }

    [Fact(DisplayName = "畸形值回退原始字符串")]
    public void MalformedFallsBack()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(Result("array(integer)", JsonString("[broken")));
        var connection = TestHelper.CreateOpenConnection(handler, ";ComplexTypes=Structured");

        using var command = connection.CreateCommand("select array[1]");
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("[broken", reader.GetValue(0));
    }

    [Fact(DisplayName = "非 JSON 文本与未知字段类型回退")]
    public void ScalarTextFallsBack()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(Result("array(integer)", JsonString("not-json")));
        var connection = TestHelper.CreateOpenConnection(handler, ";ComplexTypes=Structured");

        using var command = connection.CreateCommand("select array[1]");
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("not-json", reader.GetValue(0));

        var column = new PrestoColumn { Name = "c", Type = "custom", FieldType = typeof(Uri) };
        Assert.Equal("text", PrestoValueConverter.Convert("text", column));
    }

    [Fact(DisplayName = "结构化解析的内部边界")]
    public void ParserEdges()
    {
        var array = PrestoTypeSignature.Parse("array(integer)");
        var map = PrestoTypeSignature.Parse("map(varchar, integer)");
        var row = PrestoTypeSignature.Parse("row(a integer, b integer)");

        Assert.Null(PrestoComplexValueParser.Parse(null, array));
        Assert.Equal("5", PrestoComplexValueParser.Parse("5", PrestoTypeSignature.Parse("varchar")));
        Assert.Equal("x", PrestoComplexValueParser.Parse("x", array));
        Assert.Equal("[", PrestoComplexValueParser.Parse("[", array));
        Assert.Equal("{", PrestoComplexValueParser.Parse("{", map));
        Assert.Equal("{}", PrestoComplexValueParser.Parse("{}", array));
        Assert.Equal("[]", PrestoComplexValueParser.Parse("[]", map));
        Assert.Equal("[]", PrestoComplexValueParser.Parse("[]", PrestoTypeSignature.Parse("map(varchar, integer)")));

        // 数组元素类型缺失时保持原值
        var bare = new PrestoTypeSignature { BaseType = "array" };
        var values = Assert.IsType<Object[]>(PrestoComplexValueParser.Parse("[1, 2]", bare));
        Assert.Equal(2, values.Length);
        Assert.Equal(1L, Convert.ToInt64(values[0], System.Globalization.CultureInfo.InvariantCulture));

        // map 值类型缺失与空值
        var bareMap = new PrestoTypeSignature { BaseType = "map" };
        var mapValues = Assert.IsAssignableFrom<IDictionary<String, Object?>>(PrestoComplexValueParser.Parse("{\"a\": 1}", bareMap));
        Assert.Equal(1L, Convert.ToInt64(mapValues["a"], System.Globalization.CultureInfo.InvariantCulture));
        var nullMap = Assert.IsAssignableFrom<IDictionary<String, Object?>>(PrestoComplexValueParser.Parse("{\"a\": null}", map));
        Assert.Null(nullMap["a"]);

        // row 字段少于/多于声明的补齐
        var shortRow = Assert.IsType<Object[]>(PrestoComplexValueParser.Parse("[1]", row));
        Assert.Equal(2, shortRow.Length);
        Assert.Equal(1, shortRow[0]);
        Assert.Null(shortRow[1]);

        var longRow = Assert.IsType<Object[]>(PrestoComplexValueParser.Parse("[1, 2, 3]", row));
        Assert.Equal(3, longRow.Length);
        Assert.Equal(3, longRow[2]);
    }

    [Fact(DisplayName = "畸形类型签名回退")]
    public void MalformedSignature()
    {
        Assert.Equal(String.Empty, PrestoTypeSignature.Parse("array(integer").BaseType);
        Assert.Equal(String.Empty, PrestoTypeSignature.Parse("(").BaseType);
        Assert.Equal("decimal", PrestoTypeSignature.Parse("decimal(10").BaseType);

        var unnamed = PrestoTypeSignature.Parse("row(integer)");
        Assert.Equal("row", unnamed.BaseType);
        Assert.Equal(new[] { String.Empty }, unnamed.FieldNames!);
        Assert.Equal("integer", unnamed.Fields![0].BaseType);

        var quoted = PrestoTypeSignature.Parse("row(\"a\"\"b\" varchar)");
        Assert.Equal(new[] { "a\"b" }, quoted.FieldNames!);
    }

    [Fact(DisplayName = "类型签名解析")]
    public void SignatureParsing()
    {
        var array = PrestoTypeSignature.Parse("array(integer)");
        Assert.Equal("array", array.BaseType);
        Assert.Equal("integer", array.Element!.BaseType);

        var map = PrestoTypeSignature.Parse("map(varchar, array(decimal(10,2)))");
        Assert.Equal("map", map.BaseType);
        Assert.Equal("varchar", map.Key!.BaseType);
        Assert.Equal("array", map.Value!.BaseType);
        Assert.Equal("decimal", map.Value!.Element!.BaseType);

        var row = PrestoTypeSignature.Parse("row(name varchar, age integer, tags array(varchar))");
        Assert.Equal("row", row.BaseType);
        Assert.Equal(new[] { "name", "age", "tags" }, row.FieldNames!);
        Assert.Equal("array", row.Fields![2].BaseType);

        var quoted = PrestoTypeSignature.Parse("row(\"first name\" varchar)");
        Assert.Equal(new[] { "first name" }, quoted.FieldNames!);

        var timestamp = PrestoTypeSignature.Parse("timestamp(3) with time zone");
        Assert.Equal("timestamp", timestamp.BaseType);
        Assert.False(timestamp.IsComplex);

        var angle = PrestoTypeSignature.Parse("map<varchar, integer>");
        Assert.Equal("map", angle.BaseType);
        Assert.Equal("integer", angle.Value!.BaseType);
    }

    [Fact(DisplayName = "复杂类型模式解析与别名")]
    public void BuilderOptions()
    {
        Assert.Equal(PrestoComplexType.String, new PrestoConnectionStringBuilder("Host=localhost").ComplexTypes);
        Assert.Equal(PrestoComplexType.Structured, new PrestoConnectionStringBuilder("ComplexTypes=Structured").ComplexTypes);
        Assert.Equal(PrestoComplexType.Structured, new PrestoConnectionStringBuilder("StructuredTypes=Object").ComplexTypes);
        Assert.Equal(PrestoComplexType.String, new PrestoConnectionStringBuilder("ComplexType=Raw").ComplexTypes);
        Assert.Throws<ArgumentException>(() => new PrestoConnectionStringBuilder("ComplexTypes=Bad").Validate());
    }

    private static String Result(String type, String cell)
        => "{\"id\":\"q1\",\"columns\":[{\"name\":\"c\",\"type\":\"" + type + "\"}],\"data\":[[" + cell + "]]}";

    private static String JsonString(String value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
