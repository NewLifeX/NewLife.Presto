using System;
using System.Collections.Generic;
using NewLife.Presto;

namespace UnitTest;

/// <summary>JSON 投影测试</summary>
public class PrestoJsonTests
{
    [Fact(DisplayName = "解析完整结果页")]
    public void ParseFullResults()
    {
        const String json = """
        {
          "id": "q1",
          "infoUri": "http://localhost/ui",
          "nextUri": "http://localhost/next",
          "columns": [
            { "name": "a", "type": "bigint" },
            { "name": "b", "typeSignature": { "rawType": "varchar(10)" } },
            { "name": "c" }
          ],
          "data": [ ["1", null], ["2", "x"] ],
          "stats": { "state": "FINISHED", "elapsedTimeMillis": 12, "processedRows": 2, "processedBytes": 3, "totalSplits": 1, "completedSplits": 1, "runningSplits": 0, "unknown": "ignored" },
          "warnings": [ { "message": "w", "warningCode": 7, "errorName": "E", "errorType": "T" } ],
          "updateType": "INSERT",
          "updateCount": 2,
          "unknown": { "x": 1 }
        }
        """;

        var results = PrestoJson.ParseResults(json);

        Assert.Equal("q1", results.Id);
        Assert.Equal("http://localhost/ui", results.InfoUri);
        Assert.Equal("http://localhost/next", results.NextUri);
        Assert.False(results.IsComplete);
        Assert.Equal(3, results.Columns.Count);
        Assert.Equal("a", results.Columns[0].Name);
        Assert.Equal(typeof(Int64), results.Columns[0].FieldType);
        Assert.Equal("varchar(10)", results.Columns[1].Type);
        Assert.Equal(typeof(String), results.Columns[1].FieldType);
        Assert.Equal(String.Empty, results.Columns[2].Type);
        Assert.Equal(2, results.Data.Count);
        Assert.Equal("1", results.Data[0][0]);
        Assert.Null(results.Data[0][1]);
        Assert.Equal("FINISHED", results.Stats!.State);
        Assert.Equal(12, results.Stats.ElapsedTimeMillis);
        Assert.Equal(2, results.Stats.ProcessedRows);
        Assert.Equal(3, results.Stats.ProcessedBytes);
        Assert.Equal(1, results.Stats.TotalSplits);
        Assert.Equal(1, results.Stats.CompletedSplits);
        Assert.Equal(0, results.Stats.RunningSplits);
        Assert.NotNull(results.Stats.Raw);
        Assert.Single(results.Warnings);
        Assert.Equal("w", results.Warnings[0].Message);
        Assert.Equal(7, results.Warnings[0].WarningCode);
        Assert.Equal("INSERT", results.UpdateType);
        Assert.Equal(2, results.UpdateCount);
    }

    [Fact(DisplayName = "缺失字段使用默认值")]
    public void ParseMissingFields()
    {
        var results = PrestoJson.ParseResults("{}");

        Assert.Null(results.Id);
        Assert.Null(results.NextUri);
        Assert.True(results.IsComplete);
        Assert.Empty(results.Columns);
        Assert.Empty(results.Data);
        Assert.Null(results.Stats);
        Assert.Empty(results.Warnings);
        Assert.Null(results.Error);
        Assert.Null(results.UpdateType);
        Assert.Null(results.UpdateCount);
    }

    [Fact(DisplayName = "解析错误对象")]
    public void ParseError()
    {
        const String json = """
        {
          "id": "q2",
          "error": {
            "message": "Table not found",
            "errorCode": 47,
            "errorName": "TABLE_NOT_FOUND",
            "errorType": "USER_ERROR",
            "sqlState": "42S02",
            "failureInfo": { "type": "io.trino.spi.TrinoException" },
            "errorLocation": { "lineNumber": 1, "columnNumber": 8 }
          }
        }
        """;

        var error = PrestoJson.ParseResults(json).Error!;

        Assert.Equal("Table not found", error.Message);
        Assert.Equal(47, error.ErrorCode);
        Assert.Equal("TABLE_NOT_FOUND", error.ErrorName);
        Assert.Equal("USER_ERROR", error.ErrorType);
        Assert.Equal("42S02", error.SqlState);
        Assert.Equal("io.trino.spi.TrinoException", error.FailureType);
        Assert.Equal("1:8", error.Location);
        Assert.NotNull(error.FailureInfo);
        Assert.NotNull(error.ErrorLocation);
    }

    [Fact(DisplayName = "错误码兼容字符串且缺失字段为默认值")]
    public void ParseErrorWithStringCode()
    {
        const String json = """{ "error": { "message": "m", "errorCode": "55" } }""";

        var error = PrestoJson.ParseResults(json).Error!;

        Assert.Equal(55, error.ErrorCode);
        Assert.Null(error.FailureType);
        Assert.Null(error.Location);
    }

    [Fact(DisplayName = "解析服务端信息")]
    public void ParseServerInfo()
    {
        const String json = """{ "nodeVersion": { "version": "468" }, "environment": "prod", "coordinator": true, "starting": "false", "uptime": "1d" }""";

        var info = PrestoJson.ParseServerInfo(json);

        Assert.Equal("468", info.NodeVersion);
        Assert.Equal("prod", info.Environment);
        Assert.True(info.Coordinator);
        Assert.False(info.Starting);
        Assert.Equal("1d", info.Uptime);
        Assert.NotNull(info.Raw);
    }

    [Fact(DisplayName = "服务端版本兼容字符串")]
    public void ParseServerInfoStringVersion()
    {
        var info = PrestoJson.ParseServerInfo("""{ "nodeVersion": "468" }""");

        Assert.Equal("468", info.NodeVersion);
        Assert.Null(info.Coordinator);
    }

    [Fact(DisplayName = "服务端版本空字符串回退空")]
    public void ParseServerInfoEmptyVersion()
    {
        var info = PrestoJson.ParseServerInfo("""{ "nodeVersion": "" }""");

        Assert.True(String.IsNullOrEmpty(info.NodeVersion));
    }

    [Fact(DisplayName = "列类型缺失时使用 typeSignature")]
    public void ParseColumnSignature()
    {
        var results = PrestoJson.ParseResults("""{ "columns": [ { "name": "a", "typeSignature": { "rawType": "integer" } } ] }""");

        Assert.Single(results.Columns);
        Assert.Equal("integer", results.Columns[0].Type);
    }

    [Fact(DisplayName = "错误位置缺列号")]
    public void ErrorLocationPartial()
    {
        var error = PrestoJson.ParseResults("""{ "error": { "message": "m", "errorLocation": { "lineNumber": 3 } } }""").Error!;
        Assert.Equal("3:0", error.Location);

        var empty = PrestoJson.ParseResults("""{ "error": { "message": "m", "errorLocation": {} } }""").Error!;
        Assert.Null(empty.Location);
    }

    [Theory(DisplayName = "非法响应抛出异常")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[]")]
    public void InvalidResponseThrows(String json)
    {
        Assert.Throws<PrestoException>(() => PrestoJson.ParseResults(json));
    }

    [Fact(DisplayName = "空对象解析")]
    public void ParseEmptyObject()
    {
        Assert.Null(PrestoJson.ParseObject(""));
        Assert.Null(PrestoJson.ParseObject("   "));
        Assert.Empty(PrestoJson.ParseObject("{}")!);
    }

    [Fact(DisplayName = "数值读取兼容多种类型")]
    public void ReadNumbers()
    {
        var dic = new Dictionary<String, Object?>
        {
            ["i"] = 1,
            ["l"] = 2L,
            ["d"] = 3.9d,
            ["f"] = 4.1f,
            ["m"] = 5.5m,
            ["s"] = "6",
            ["bad"] = "x",
            ["null"] = null,
            ["big"] = (Int64)Int32.MaxValue + 1,
        };

        Assert.Equal(1, PrestoJson.GetNullableInt64(dic, "i"));
        Assert.Equal(2, PrestoJson.GetNullableInt64(dic, "l"));
        Assert.Equal(3, PrestoJson.GetNullableInt64(dic, "d"));
        Assert.Equal(4, PrestoJson.GetNullableInt64(dic, "f"));
        Assert.Equal(5, PrestoJson.GetNullableInt64(dic, "m"));
        Assert.Equal(6, PrestoJson.GetNullableInt64(dic, "s"));
        Assert.Null(PrestoJson.GetNullableInt64(dic, "bad"));
        Assert.Null(PrestoJson.GetNullableInt64(dic, "null"));
        Assert.Null(PrestoJson.GetNullableInt64(dic, "missing"));
        Assert.Null(PrestoJson.GetNullableInt32(dic, "big"));
        Assert.Equal(0, PrestoJson.GetInt64(dic, "bad"));
        Assert.Equal(1, PrestoJson.GetInt32(dic, "i"));
    }

    [Fact(DisplayName = "布尔读取兼容字符串")]
    public void ReadBooleans()
    {
        var dic = new Dictionary<String, Object?>
        {
            ["b"] = true,
            ["t"] = "true",
            ["f"] = "false",
            ["bad"] = "x",
            ["null"] = null,
        };

        Assert.True(PrestoJson.GetNullableBoolean(dic, "b"));
        Assert.True(PrestoJson.GetNullableBoolean(dic, "t"));
        Assert.False(PrestoJson.GetNullableBoolean(dic, "f"));
        Assert.Null(PrestoJson.GetNullableBoolean(dic, "bad"));
        Assert.Null(PrestoJson.GetNullableBoolean(dic, "null"));
        Assert.Null(PrestoJson.GetNullableBoolean(dic, "missing"));
    }

    [Fact(DisplayName = "标量转文本")]
    public void TextConversion()
    {
        Assert.Null(PrestoJson.ToText(null));
        Assert.Equal("x", PrestoJson.ToText("x"));
        Assert.Equal("1", PrestoJson.ToText(1));
        Assert.Equal("True", PrestoJson.ToText(true));
    }

    [Fact(DisplayName = "GetString 只转换标量")]
    public void GetStringScalarOnly()
    {
        var dic = new Dictionary<String, Object?>
        {
            ["i"] = 1,
            ["b"] = true,
            ["list"] = new List<Object?> { 1 },
            ["dic"] = new Dictionary<String, Object?> { ["x"] = 1 },
        };

        Assert.Equal("1", PrestoJson.GetString(dic, "i"));
        Assert.Equal("True", PrestoJson.GetString(dic, "b"));
        Assert.Null(PrestoJson.GetString(dic, "list"));
        Assert.Null(PrestoJson.GetString(dic, "dic"));
        Assert.Null(PrestoJson.GetString(dic, "missing"));
        Assert.Null(PrestoJson.GetString(null, "i"));
    }

    [Fact(DisplayName = "类型断言失败返回空")]
    public void AsTypeFailures()
    {
        Assert.Null(PrestoJson.AsDictionary("x"));
        Assert.Null(PrestoJson.AsList("x"));
        Assert.Null(PrestoJson.ParseError("x"));
        Assert.Null(PrestoJson.ParseStats("x"));

        var dic = new Dictionary<String, Object?> { ["i"] = 1 };
        Assert.Same(dic, PrestoJson.AsDictionary(dic));
        var list = new List<Object?> { 1 };
        Assert.Same(list, PrestoJson.AsList(list));
    }

    [Fact(DisplayName = "警告数组忽略非对象元素")]
    public void WarningsIgnoreInvalidItems()
    {
        const String json = """{ "warnings": [ 1, null, { "message": "w" } ] }""";

        var results = PrestoJson.ParseResults(json);

        Assert.Single(results.Warnings);
        Assert.Equal("w", results.Warnings[0].Message);
    }

    [Fact(DisplayName = "列数组忽略非对象元素")]
    public void ColumnsIgnoreInvalidItems()
    {
        const String json = """{ "columns": [ 1, null, { "name": "a", "type": "integer" } ] }""";

        var results = PrestoJson.ParseResults(json);

        Assert.Single(results.Columns);
    }

    [Fact(DisplayName = "数据行非数组按空行处理")]
    public void DataRowNotArray()
    {
        const String json = """{ "data": [ 1, [ "a" ] ] }""";

        var results = PrestoJson.ParseResults(json);

        Assert.Equal(2, results.Data.Count);
        Assert.Empty(results.Data[0]);
        Assert.Equal("a", results.Data[1][0]);
    }
}
