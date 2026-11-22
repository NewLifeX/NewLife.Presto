using System;
using System.Collections.Generic;
using NewLife.Presto;

namespace UnitTest;

/// <summary>JSON 投影测试</summary>
public class PrestoJsonTests
{
    [Fact(DisplayName = "数组解析遇到非法 JSON 抛出驱动异常")]
    public void ParseArrayInvalid()
    {
        Assert.Null(PrestoJson.ParseArray(null));
        Assert.Null(PrestoJson.ParseArray("   "));
        Assert.Null(PrestoJson.ParseArray("""{ "a": 1 }"""));
        Assert.Throws<PrestoException>(() => PrestoJson.ParseArray("[{]"));
        Assert.Throws<PrestoException>(() => PrestoJson.ParseResults("{broken"));
    }

    [Fact(DisplayName = "查询诊断对象兼容字段差异")]
    public void QueryInfoVariants()
    {
        var plain = PrestoJson.ParseQueryInfo(PrestoJson.ParseObject("""{ "id": "q1", "state": "RUNNING" }"""));
        Assert.Equal("q1", plain.Id);
        Assert.Null(plain.User);
        Assert.Null(plain.Stats);
        Assert.Null(plain.Error);

        var failed = PrestoJson.ParseQueryInfo(PrestoJson.ParseObject("""{ "queryId": "q2", "stats": { "state": "FAILED" }, "error": { "message": "e" } }"""));
        Assert.Equal("q2", failed.Id);
        Assert.Equal("FAILED", failed.Stats!.State);
        Assert.Equal("e", failed.Error!.Message);
    }

    [Fact(DisplayName = "数组解析边界")]
    public void ParseArrayEdges()
    {
        Assert.Null(PrestoJson.ParseArray(""));
        Assert.Null(PrestoJson.ParseArray("{ }"));
        Assert.Equal(2, PrestoJson.ParseArray("""[{"a":1},{"b":2}]""")!.Count);
    }

    [Fact(DisplayName = "查询编号在未执行时为空")]
    public void LastQueryIdBeforeExecution()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "id": "q9", "columns": [], "data": [] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        using var command = connection.CreateCommand("select 1");

        Assert.Null(command.LastQueryId);
        command.ExecuteNonQuery();
        Assert.Equal("q9", command.LastQueryId);
    }

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
        Assert.Null(results.Stats.Raw);  // 中间页只保留进度字段

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

    [Fact(DisplayName = "数字单元格按列类型直出")]
    public void NumbersUseColumnTypes()
    {
        const String json = """
        {
          "columns": [
            { "name": "b", "type": "bigint" },
            { "name": "i", "type": "integer" },
            { "name": "d", "type": "double" },
            { "name": "s", "type": "varchar" }
          ],
          "data": [ [1, 2, 1.5, "x"], [2147483648, null, 2, null] ]
        }
        """;

        var results = PrestoJson.ParseResults(json);

        Assert.IsType<Int64>(results.Data[0][0]);
        Assert.IsType<Int32>(results.Data[0][1]);
        Assert.IsType<Double>(results.Data[0][2]);
        Assert.Equal("x", results.Data[0][3]);
        Assert.Equal(2147483648L, results.Data[1][0]);
        Assert.Null(results.Data[1][1]);
        Assert.Equal(2d, results.Data[1][2]);
    }

    [Fact(DisplayName = "JSON 单元格保留嵌套结构")]
    public void NestedJsonCells()
    {
        const String json = """{ "data": [ [ { "a": 1 }, [ 1, "x" ], null ] ] }""";

        var results = PrestoJson.ParseResults(json);

        var dic = Assert.IsAssignableFrom<IDictionary<String, Object?>>(results.Data[0][0]);
        Assert.Equal(1, dic["a"]);
        var list = Assert.IsAssignableFrom<IList<Object?>>(results.Data[0][1]);
        Assert.Equal(1, list[0]);
        Assert.Equal("x", list[1]);
        Assert.Null(results.Data[0][2]);
    }

    [Fact(DisplayName = "统计原始对象按需解析且保留未知字段")]
    public void StatsRawIsLazyButComplete()
    {
        const String json = """{ "stats": { "state": "FINISHED", "processedRows": 3, "queuedTimeMillis": 5, "unknown": "kept" } }""";

        var stats = PrestoJson.ParseResults(json).Stats!;

        Assert.Equal("FINISHED", stats.State);
        Assert.Equal(3, stats.ProcessedRows);
        Assert.Equal(5, stats.QueuedTimeMillis);
        Assert.Equal("kept", stats.Raw!["unknown"]);
    }

    [Fact(DisplayName = "UTF-8 字节解析支持 BOM 与转义")]
    public void Utf8BomAndEscapes()
    {
        var bytes = new Byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes("""{ "id": "q\u0031", "data": [ [ "a\u00e9" ] ] }""")).ToArray();

        var results = PrestoJson.ParseResultsUtf8(bytes);

        Assert.Equal("q1", results.Id);
        Assert.Equal("aé", results.Data[0][0]);
    }

    [Fact(DisplayName = "空字节响应抛出驱动异常")]
    public void Utf8EmptyBody()
    {
        Assert.Throws<PrestoException>(() => PrestoJson.ParseResultsUtf8(Array.Empty<Byte>()));
        Assert.Throws<PrestoException>(() => PrestoJson.ParseResultsUtf8("  "u8.ToArray()));
        Assert.Throws<PrestoException>(() => PrestoJson.ParseResultsUtf8("[]"u8.ToArray()));
        Assert.Empty(PrestoJson.ParseRowsUtf8(Array.Empty<Byte>()));
        Assert.Throws<PrestoException>(() => PrestoJson.ParseRowsUtf8("{}"u8.ToArray()));

        // 两字节正文覆盖 BOM 长度判断的短路分支
        Assert.Null(PrestoJson.ParseResultsUtf8("{}"u8.ToArray()).Id);
    }

    [Fact(DisplayName = "字段类型不符合预期时宽容降级")]
    public void StreamingTolerantFields()
    {
        const String json = """
        { "data": 5, "warnings": 1, "columns": 2, "error": "e", "stats": 3, "id": 5, "updateType": true, "updateCount": 1.5 }
        """;

        var results = PrestoJson.ParseResults(json);

        Assert.Equal("5", results.Id);
        Assert.Equal("True", results.UpdateType);
        Assert.Equal(1L, results.UpdateCount);
        Assert.Empty(results.Columns);
        Assert.Empty(results.Data);
        Assert.Empty(results.Warnings);
        Assert.Null(results.Error);
        Assert.Null(results.Stats);
    }

    [Fact(DisplayName = "元数据字符串兼容数字与布尔")]
    public void StreamingTextValues()
    {
        const String json = """{ "id": 12, "nextUri": true, "columns": [ { "name": true, "type": false } ], "updateType": { "a": 1 } }""";

        var results = PrestoJson.ParseResults(json);

        Assert.Equal("12", results.Id);
        Assert.Equal("True", results.NextUri);
        Assert.Equal("True", results.Columns[0].Name);
        Assert.Equal("False", results.Columns[0].Type);
        Assert.Null(results.UpdateType);
    }

    [Fact(DisplayName = "updateCount 兼容字符串与非法值")]
    public void StreamingUpdateCountVariants()
    {
        Assert.Equal(7L, PrestoJson.ParseResults("""{ "updateCount": "7" }""").UpdateCount);
        Assert.Null(PrestoJson.ParseResults("""{ "updateCount": true }""").UpdateCount);
    }

    [Fact(DisplayName = "统计状态覆盖失败/取消/未知，整型越界归零")]
    public void StreamingStateAndInt32Edges()
    {
        Assert.Equal("FAILED", PrestoJson.ParseResults("""{ "stats": { "state": "FAILED" } }""").Stats!.State);
        Assert.Equal("CANCELED", PrestoJson.ParseResults("""{ "stats": { "state": "CANCELED" } }""").Stats!.State);
        Assert.Equal("PLANNING", PrestoJson.ParseResults("""{ "stats": { "state": "PLANNING" } }""").Stats!.State);

        // 超出 Int32 上下限的计数都按 0 处理，不影响其它字段
        var high = PrestoJson.ParseResults("""{ "stats": { "state": "RUNNING", "nodes": 5000000000, "completedSplits": 3 } }""").Stats!;
        Assert.Equal(0, high.TotalSplits);
        Assert.Equal(3, high.CompletedSplits);

        var low = PrestoJson.ParseResults("""{ "stats": { "state": "RUNNING", "nodes": -5000000000 } }""").Stats!;
        Assert.Equal(0, low.TotalSplits);
    }

    [Fact(DisplayName = "两字节 BOM 前缀与仅 BOM 正文的边界")]
    public void Utf8PartialBom()
    {
        Assert.Throws<PrestoException>(() => PrestoJson.ParseResultsUtf8(new Byte[] { 0xEF, 0xBB }));

        // 只有 BOM 的正文等同于空响应
        Assert.Empty(PrestoJson.ParseRowsUtf8(new Byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Fact(DisplayName = "空数组与非数组的列/警告降级为空")]
    public void StreamingEmptyCollections()
    {
        Assert.Empty(PrestoJson.ParseResults("""{ "warnings": [] }""").Warnings);
        Assert.Empty(PrestoJson.ParseResults("""{ "warnings": "x" }""").Warnings);
        Assert.Empty(PrestoJson.ParseResults("""{ "columns": [] }""").Columns);
    }

    [Fact(DisplayName = "布尔与空值单元格")]
    public void StreamingBooleanAndNullCells()
    {
        var results = PrestoJson.ParseResults("""
        {
          "columns": [ { "name": "a", "type": "boolean" }, { "name": "b", "type": "boolean" }, { "name": "c", "type": "varchar" } ],
          "data": [ [ true, false, null ] ]
        }
        """);

        Assert.Equal(true, results.Data[0][0]);
        Assert.Equal(false, results.Data[0][1]);
        Assert.Null(results.Data[0][2]);
    }

    [Fact(DisplayName = "扁平行中空行保留实际单元格，多格行按列数裁剪")]
    public void StreamingFlatIrregularRows()
    {
        var results = PrestoJson.ParseResults("""
        {
          "columns": [ { "name": "a", "type": "integer" } ],
          "data": [ [ 1 ], [], [ 2, 3 ] ]
        }
        """);

        Assert.Equal(3, results.Data.Count);
        Assert.Single(results.Data[0]);
        Assert.Empty(results.Data[1]);
        Assert.Single(results.Data[2]);
        Assert.Equal(2, results.Data[2][0]);
    }

    [Fact(DisplayName = "时间戳带时区或分隔符不符时回退字符串")]
    public void StreamingTimestampFallbacks()
    {
        var results = PrestoJson.ParseResults("""
        {
          "columns": [ { "name": "t", "type": "timestamp(3)" } ],
          "data": [ [ "2020-01-02 03:04:05.678+08:00" ], [ "2020/01/02 03:04:05" ], [ "2020-01-02" ] ]
        }
        """);

        for (var i = 0; i < results.Data.Count; i++) Assert.IsType<String>(results.Data[i][0]);
    }

    [Fact(DisplayName = "类型签名与警告字段宽容解析")]
    public void StreamingColumnsAndWarningsDetails()
    {
        const String json = """
        {
          "columns": [
            { "name": "a", "typeSignature": 5 },
            { "name": "b", "typeSignature": { "rawType": "integer", "arguments": [] }, "extra": 1 },
            { "name": "c", "typeSignature": { "other": 1 } },
            { "name": "d", "type": "bigint", "typeSignature": { "rawType": "integer" } }
          ],
          "warnings": [ { "message": "w", "warningCode": "7", "extra": { "x": 1 } } ]
        }
        """;

        var results = PrestoJson.ParseResults(json);

        Assert.Equal(4, results.Columns.Count);
        Assert.Equal(String.Empty, results.Columns[0].Type);
        Assert.Equal("integer", results.Columns[1].Type);
        Assert.Equal(String.Empty, results.Columns[2].Type);
        Assert.Equal("bigint", results.Columns[3].Type);
        Assert.Equal(7, results.Warnings[0].WarningCode);
    }

    [Fact(DisplayName = "无列定义时数字按 JSON 语义解析")]
    public void StreamingGenericNumbers()
    {
        const String json = """{ "data": [ [ 1, 2147483648, 1.5, true, false, null, 1e400 ] ] }""";

        var row = PrestoJson.ParseResults(json).Data[0];

        Assert.IsType<Int32>(row[0]);
        Assert.IsType<Int64>(row[1]);
        Assert.IsType<Double>(row[2]);
        Assert.Equal(true, row[3]);
        Assert.Equal(false, row[4]);
        Assert.Null(row[5]);
        Assert.Equal(Double.PositiveInfinity, row[6]);
    }

    [Fact(DisplayName = "数据行与列数不一致时保留实际单元格")]
    public void StreamingRowArityMismatch()
    {
        const String json = """
        {
          "columns": [ { "name": "a", "type": "integer" }, { "name": "b", "type": "integer" } ],
          "data": [ [ 1, 2, 3 ], [ 4 ] ]
        }
        """;

        var results = PrestoJson.ParseResults(json);

        Assert.Equal(2, results.Data[0].Count);
        Assert.Single(results.Data[1]);
        Assert.Equal(4, results.Data[1][0]);
    }

    [Fact(DisplayName = "空行与分片计数越界按默认值处理")]
    public void StreamingRowAndStatsEdges()
    {
        const String json = """
        {
          "columns": [ { "name": "a", "type": "integer" }, { "name": "b", "type": "integer" } ],
          "data": [ [] ],
          "stats": { "totalSplits": 5000000000, "completedSplits": -1 }
        }
        """;

        var results = PrestoJson.ParseResults(json);

        Assert.Empty(results.Data[0]);
        Assert.Equal(0, results.Stats!.TotalSplits);
        Assert.Equal(-1, results.Stats.CompletedSplits);
    }

    [Fact(DisplayName = "相同查询编号与信息地址跨页复用字符串")]
    public void ReuseSharedPageStrings()
    {
        const String json = """{ "id": "q1", "infoUri": "http://localhost/ui", "columns": [], "data": [] }""";
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        var first = PrestoJson.ParseResultsUtf8(bytes);
        var second = PrestoJson.ParseResultsUtf8(bytes, false, first.Id, first.InfoUri);

        Assert.Same(first.Id, second.Id);
        Assert.Same(first.InfoUri, second.InfoUri);

        var third = PrestoJson.ParseResultsUtf8(bytes, false, "other", "other-uri");
        Assert.Equal("q1", third.Id);
        Assert.NotSame("other", third.Id);
    }

    [Fact(DisplayName = "终页保留统计原始 JSON")]
    public void TerminalPageKeepsRawStats()
    {
        const String json = """
        {
          "id": "q1",
          "columns": [ { "name": "a", "type": "bigint" } ],
          "data": [ [1] ],
          "stats": { "state": "FINISHED", "processedRows": 1, "custom": 5 }
        }
        """;

        var results = PrestoJson.ParseResults(json);

        Assert.True(results.IsComplete);
        Assert.Equal("FINISHED", results.Stats!.State);
        Assert.NotNull(results.Stats.Raw);
        Assert.Equal(5, Convert.ToInt32(results.Stats.Raw!["custom"]));
    }

    [Fact(DisplayName = "文本数值与日期按字节直转，异常格式回退字符串")]
    public void StreamingTextNumbersAndDates()
    {
        const String json = """
        {
          "columns": [
            { "name": "d1", "type": "decimal(12,2)" },
            { "name": "d2", "type": "decimal(12,2)" },
            { "name": "d3", "type": "decimal(12,2)" },
            { "name": "t1", "type": "date" },
            { "name": "t2", "type": "date" },
            { "name": "t3", "type": "date" }
          ],
          "data": [ [ "12.50", "abc", "1e999", "2020-01-02", "2020-02-31", "2020-1-2" ] ]
        }
        """;

        var results = PrestoJson.ParseResults(json);
        var row = results.Data[0];

        Assert.IsType<Decimal>(row[0]);
        Assert.Equal(12.5m, row[0]);
        Assert.IsType<String>(row[1]);
        Assert.IsType<String>(row[2]);
        Assert.IsType<DateTime>(row[3]);
        Assert.Equal(new DateTime(2020, 1, 2), row[3]);
        Assert.IsType<String>(row[4]);
        Assert.IsType<String>(row[5]);

        // 年月日越界都退回字符串，交给取值阶段按原有语义报错
        var invalid = PrestoJson.ParseResults("""
        {
          "columns": [ { "name": "t", "type": "date" } ],
          "data": [ [ "0000-01-01" ], [ "2020-00-01" ], [ "2020-13-01" ], [ "2020-01-00" ], [ "2020-01-32" ] ]
        }
        """);
        for (var i = 0; i < invalid.Data.Count; i++) Assert.IsType<String>(invalid.Data[i][0]);
    }

    [Fact(DisplayName = "时间戳按字节直转，带时区或异常格式回退字符串")]
    public void StreamingTimestamps()
    {
        const String json = """
        {
          "columns": [
            { "name": "t1", "type": "timestamp(3)" },
            { "name": "t2", "type": "timestamp(3)" },
            { "name": "t3", "type": "timestamp" },
            { "name": "t4", "type": "timestamp(9)" },
            { "name": "t5", "type": "timestamp(3) with time zone" },
            { "name": "t6", "type": "timestamp(3)" }
          ],
          "data": [[
            "2020-01-02 03:04:05.678",
            "2020-01-02 03:04:05.000",
            "2020-01-02 03:04:05",
            "2020-01-02 03:04:05.123456789",
            "2020-01-02 03:04:05.678 UTC",
            "2020-01-02 03:04:05.678"
          ]]
        }
        """;

        var results = PrestoJson.ParseResults(json);
        var row = results.Data[0];

        Assert.IsType<DateTime>(row[0]);
        Assert.Equal(new DateTime(2020, 1, 2, 3, 4, 5, 678), row[0]);
        Assert.Equal(new DateTime(2020, 1, 2, 3, 4, 5), row[1]);
        Assert.Equal(new DateTime(2020, 1, 2, 3, 4, 5), row[2]);
        // 纳秒只保留到 100 纳秒刻度
        Assert.Equal(new DateTime(2020, 1, 2, 3, 4, 5).AddTicks(1234567), row[3]);
        // 带时区的列映射到 DateTimeOffset，解析阶段保留文本
        Assert.IsType<String>(row[4]);

        // 月/日/时/分/秒越界与不同分隔符都回退字符串
        var invalid = PrestoJson.ParseResults("""
        {
          "columns": [ { "name": "t", "type": "timestamp(3)" } ],
          "data": [ [ "2020-13-01 00:00:00" ], [ "2020-01-01 25:00:00" ], [ "2020-01-01T00:00:00" ], [ "2020-01-01 00:00" ], [ "2020-01-01 00:00:00." ] ]
        }
        """);
        for (var i = 0; i < invalid.Data.Count; i++) Assert.IsType<String>(invalid.Data[i][0]);
    }

    [Fact(DisplayName = "非数组行触发扁平行数组扩容后按实际单元格回退")]
    public void StreamingNonArrayRowsGrowBuffer()
    {
        const String json = """
        {
          "columns": [ { "name": "a", "type": "integer" }, { "name": "b", "type": "integer" } ],
          "data": [ null, null, [ 1, 2 ] ]
        }
        """;

        var results = PrestoJson.ParseResults(json);

        Assert.Equal(3, results.Data.Count);
        Assert.Empty(results.Data[0]);
        Assert.Empty(results.Data[1]);
        Assert.Equal(2, results.Data[2].Count);
        Assert.Equal(1, results.Data[2][0]);
    }

    [Fact(DisplayName = "整页单元格超过阈值时退回行列表并保留实际单元格")]
    public void StreamingWidePageFallsBackToRows()
    {
        // 8 列 × 33000 行 = 26.4 万单元格，刚好越过扁平数组上限（26.2 万）
        const Int32 Columns = 8;
        var cells = new List<String>();
        for (var i = 0; i < 33000; i++)
        {
            cells.Add("[" + String.Join(",", Enumerable.Repeat(i.ToString(), Columns)) + "]");
        }

        // 首行多一格、次行少七格，覆盖回退路径的裁剪与忽略逻辑
        cells[0] = "[" + String.Join(",", Enumerable.Repeat("0", Columns + 1)) + "]";
        cells[1] = "[1]";

        var columns = "[" + String.Join(",", Enumerable.Range(0, Columns).Select(i => $$"""{ "name": "c{{i}}", "type": "integer" }""")) + "]";
        var json = $$"""
        {
          "columns": {{columns}},
          "data": [ {{String.Join(",", cells)}} ]
        }
        """;

        var results = PrestoJson.ParseResults(json);

        Assert.Equal(33000, results.Data.Count);
        Assert.Equal(Columns, results.Data[0].Count);
        Assert.Single(results.Data[1]);
        Assert.Equal(Columns, results.Data[32999].Count);
        Assert.Equal(32999, results.Data[32999][Columns - 1]);
    }

    [Fact(DisplayName = "宽页在扁平数组上限内不再退回行列表")]
    public void StreamingWidePageKeepsFlatBuffer()
    {
        // 16 列 × 5000 行 = 8 万单元格；旧上限（3.2 万）会退回行列表，现在应保持扁平存储
        const Int32 Columns = 16;
        var cells = new List<String>();
        for (var i = 0; i < 5000; i++)
        {
            cells.Add("[" + String.Join(",", Enumerable.Repeat(i.ToString(), Columns)) + "]");
        }

        var columns = "[" + String.Join(",", Enumerable.Range(0, Columns).Select(i => $$"""{ "name": "c{{i}}", "type": "integer" }""")) + "]";
        var json = $$"""
        {
          "columns": {{columns}},
          "data": [ {{String.Join(",", cells)}} ]
        }
        """;

        var results = PrestoJson.ParseResults(json);

        Assert.NotNull(results.CellBuffer);
        Assert.Equal(Columns, results.CellArity);
        Assert.Equal(5000, results.RowCount);
        Assert.Equal(4999, results.Data[4999][Columns - 1]);
    }

    [Fact(DisplayName = "扁平行缓冲区缺行或列数异常时返回空集")]
    public void MaterializeGuards()
    {
        Assert.Empty(new PrestoQueryResults().Data);
        Assert.Empty(new PrestoQueryResults { CellBuffer = [1], CellArity = 0, RowCount = 1 }.Data);
        Assert.Empty(new PrestoQueryResults { CellBuffer = [1], CellArity = 1, RowCount = 0 }.Data);
    }

    [Fact(DisplayName = "数据为对象时按 Spooling 描述解析")]
    public void StreamingSpooledDataObject()
    {
        var results = PrestoJson.ParseResults("""{ "data": { "encoding": "json" } }""");

        Assert.Equal("json", results.DataEncoding);
        Assert.Empty(results.Segments);
        Assert.Empty(results.Data);
    }

    [Fact(DisplayName = "updateCount 非法字符串返回空")]
    public void StreamingUpdateCountInvalid()
    {
        Assert.Null(PrestoJson.ParseResults("""{ "updateCount": "abc" }""").UpdateCount);
    }

    [Fact(DisplayName = "统计 Raw 支持字典与延迟解析两种来源")]
    public void StatsRawSources()
    {
        var dic = new Dictionary<String, Object?> { ["state"] = "X" };
        var direct = PrestoJson.ParseStats(dic)!;
        Assert.Same(dic, direct.Raw);

        var lazy = PrestoJson.ParseResults("""{ "stats": { "state": "Y" } }""").Stats!;
        Assert.Equal("Y", lazy.Raw!["state"]);
        Assert.Same(lazy.Raw, lazy.Raw);

        var none = new PrestoQueryStats();
        Assert.Null(none.Raw);
    }
}
