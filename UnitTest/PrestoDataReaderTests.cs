using System;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NewLife.Presto;

namespace UnitTest;

/// <summary>数据读取器测试</summary>
public class PrestoDataReaderTests
{
    private static (PrestoDataReader Reader, FakePrestoHandler Handler, PrestoConnection Connection) CreateReader(String json, CommandBehavior behavior = CommandBehavior.Default, String? next = null)
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson(json);
        if (next != null) handler.EnqueueJson(next);
        var connection = TestHelper.CreateOpenConnection(handler);
        var command = new PrestoCommand("select 1", connection);
        var reader = (PrestoDataReader)command.ExecuteReader(behavior);

        return (reader, handler, connection);
    }

    [Fact(DisplayName = "分页读取")]
    public void ReadMultiplePages()
    {
        var (reader, handler, _) = CreateReader(
            """{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["1"]], "nextUri": "http://localhost:8080/v1/statement/q1/1", "stats": {"state":"RUNNING","processedRows":1} }""",
            next: """{ "id": "q1", "columns": [{"name":"n","type":"integer"}], "data": [["2"]], "stats": {"state":"FINISHED","processedRows":2}, "warnings": [{"message":"w"}] }""");

        using (reader)
        {
            Assert.Equal(1, reader.FieldCount);
            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.True(reader.Read());
            Assert.Equal(2, reader.GetInt32(0));
            Assert.False(reader.Read());
            Assert.Equal(-1, reader.RecordsAffected);
            Assert.Equal("FINISHED", reader.Statistics!.State);
            Assert.Single(reader.Warnings);
            Assert.Equal("q1", reader.QueryId);
        }

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact(DisplayName = "HasRows 惰性拉页")]
    public void HasRowsLazy()
    {
        var (reader, handler, _) = CreateReader(
            """{ "columns": [{"name":"n","type":"integer"}], "data": [], "nextUri": "http://localhost:8080/v1/statement/q1/1" }""",
            next: """{ "columns": [{"name":"n","type":"integer"}], "data": [["1"]] }""");

        using (reader)
        {
            Assert.True(reader.HasRows);
            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt32(0));
        }

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact(DisplayName = "无数据时 HasRows 为 false")]
    public void HasRowsFalse()
    {
        var (reader, _, _) = CreateReader("""{ "columns": [{"name":"n","type":"integer"}], "data": [] }""");

        using (reader)
        {
            Assert.False(reader.HasRows);
            Assert.False(reader.Read());
        }
    }

    [Fact(DisplayName = "SchemaOnly 无数据行")]
    public void SchemaOnlyHasRowsFalse()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "columns": [{"name":"n","type":"integer"}], "data": [], "nextUri": "http://localhost:8080/v1/statement/q1/1" }""");
        handler.EnqueueJson("""{ "id": "q1" }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        var command = new PrestoCommand("select 1", connection);
        using var reader = command.ExecuteReader(CommandBehavior.SchemaOnly);

        Assert.False(reader.HasRows);
        Assert.False(reader.Read());
        Assert.Equal(1, reader.FieldCount);
    }

    [Fact(DisplayName = "类型化取值")]
    public void TypedGetters()
    {
        const String json = """
        {
          "columns": [
            {"name":"b","type":"boolean"},
            {"name":"t","type":"tinyint"},
            {"name":"s","type":"smallint"},
            {"name":"i","type":"integer"},
            {"name":"l","type":"bigint"},
            {"name":"f","type":"real"},
            {"name":"d","type":"double"},
            {"name":"m","type":"decimal(10,2)"},
            {"name":"v","type":"varchar(10)"},
            {"name":"bin","type":"varbinary"},
            {"name":"dt","type":"date"},
            {"name":"tm","type":"time"},
            {"name":"ts","type":"timestamp(3)"},
            {"name":"tstz","type":"timestamp(3) with time zone"},
            {"name":"u","type":"uuid"},
            {"name":"ip","type":"ipaddress"},
            {"name":"iv","type":"interval day to second"},
            {"name":"arr","type":"array(varchar)"},
            {"name":"js","type":"json"},
            {"name":"unknown","type":"no_such_type"},
            {"name":"tmtz","type":"time(3) with time zone"},
            {"name":"ivy","type":"interval year to month"},
            {"name":"geo","type":"geometry"},
            {"name":"c","type":"char(1)"}
          ],
          "data": [[
            "true","12","1234","123456","1234567890123","1.5","2.5","3.75","x","AQI=",
            "2020-01-02","03:04:05.006","2020-01-02 03:04:05.006","2020-01-02 03:04:05.006 +08:00",
            "00112233-4455-6677-8899-aabbccddeeff","127.0.0.1","1 02:03:04.005","[\"a\"]","{}",
            "zzz","03:04:05.006 +08:00","1-2","AQI=","c"
          ]]
        }
        """;

        var (reader, _, _) = CreateReader(json);
        using (reader)
        {
            Assert.True(reader.Read());
            Assert.True(reader.GetBoolean(0));
            Assert.Equal((SByte)12, reader.GetSByte(1));
            Assert.Equal((Int16)1234, reader.GetInt16(2));
            Assert.Equal(123456, reader.GetInt32(3));
            Assert.Equal(1234567890123L, reader.GetInt64(4));
            Assert.Equal(1.5f, reader.GetFloat(5));
            Assert.Equal(2.5d, reader.GetDouble(6));
            Assert.Equal(3.75m, reader.GetDecimal(7));
            Assert.Equal("x", reader.GetString(8));
            Assert.Equal(new Byte[] { 1, 2 }, reader.GetValue(9));
            Assert.Equal(new DateTime(2020, 1, 2), reader.GetDateTime(10));
            Assert.Equal(new TimeSpan(0, 3, 4, 5, 6), reader.GetValue(11));
            Assert.Equal(new DateTime(2020, 1, 2, 3, 4, 5, 6), reader.GetDateTime(12));
            Assert.Equal(new DateTimeOffset(2020, 1, 2, 3, 4, 5, 6, TimeSpan.FromHours(8)), reader.GetValue(13));
            Assert.Equal(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), reader.GetGuid(14));
            Assert.Equal(IPAddress.Parse("127.0.0.1"), reader.GetValue(15));
            Assert.Equal(new TimeSpan(1, 2, 3, 4, 5), reader.GetValue(16));
            Assert.Equal("[\"a\"]", reader.GetString(17));
            Assert.Equal("{}", reader.GetString(18));
            Assert.Equal("zzz", reader.GetString(19));
            Assert.Equal("03:04:05.006 +08:00", reader.GetString(20));
            Assert.Equal("1-2", reader.GetString(21));
            Assert.Equal(new Byte[] { 1, 2 }, reader.GetValue(22));
            Assert.Equal('c', reader.GetChar(23));

            Assert.Equal(typeof(Int64), reader.GetFieldType(4));
            Assert.Equal("bigint", reader.GetDataTypeName(4));
            Assert.Equal("l", reader.GetName(4));
            Assert.Equal(4, reader.GetOrdinal("L"));
            Assert.Equal(4, reader.GetOrdinal("l"));
        }
    }

    [Fact(DisplayName = "空值处理")]
    public void NullValues()
    {
        var (reader, _, _) = CreateReader("""{ "columns": [{"name":"n","type":"integer"}], "data": [[null],[null]] }""");

        using (reader)
        {
            Assert.True(reader.Read());
            Assert.True(reader.IsDBNull(0));
            Assert.Equal(DBNull.Value, reader.GetValue(0));
            Assert.Throws<InvalidCastException>(() => reader.GetInt32(0));
            Assert.Throws<InvalidCastException>(() => reader.GetFieldValue<Int32>(0));
            Assert.True(reader.Read());
        }
    }

    [Fact(DisplayName = "值转换失败包含列信息")]
    public void InvalidConversion()
    {
        var (reader, _, _) = CreateReader("""{ "columns": [{"name":"n","type":"bigint"}], "data": [["abc"]] }""");

        using (reader)
        {
            Assert.True(reader.Read());
            var ex = Assert.Throws<PrestoException>(() => reader.GetInt64(0));
            Assert.Contains("n", ex.Message);
            Assert.Contains("bigint", ex.Message);
            Assert.Contains("abc", ex.Message);
        }
    }

    [Fact(DisplayName = "非法时间戳文本走通用解析并报错")]
    public void InvalidTimestampText()
    {
        var (reader, _, _) = CreateReader("""{ "columns": [{"name":"ts","type":"timestamp(3)"}], "data": [["2020-99-99 99:99:99"]] }""");

        using (reader)
        {
            Assert.True(reader.Read());
            var ex = Assert.Throws<PrestoException>(() => reader.GetValue(0));
            Assert.Contains("ts", ex.Message);
            Assert.Contains("timestamp(3)", ex.Message);
        }
    }

    [Fact(DisplayName = "日期列的宽松格式回退到通用解析")]
    public void LooseDateText()
    {
        var (reader, _, _) = CreateReader("""{ "columns": [{"name":"d","type":"date"}], "data": [["2020-1-2"]] }""");

        using (reader)
        {
            Assert.True(reader.Read());
            Assert.Equal(new DateTime(2020, 1, 2), reader.GetDateTime(0));
        }
    }

    [Fact(DisplayName = "GetValues 空数组与 CloseConnection 行为")]
    public void GetValuesGuards()
    {
        var (reader, _, connection) = CreateReader("""{ "columns": [{"name":"n","type":"integer"}], "data": [["1"]] }""", CommandBehavior.CloseConnection);

        Assert.True(reader.Read());
        Assert.Throws<ArgumentNullException>(() => reader.GetValues(null!));

        reader.Close();

        // CloseConnection 关闭读取器时一并关闭连接，重复关闭保持幂等
        Assert.Equal(ConnectionState.Closed, connection.State);
        reader.Close();
    }

    [Fact(DisplayName = "更新语句页面的受影响行数")]
    public void UpdatePageAffectedRows()
    {
        var (reader, _, _) = CreateReader("""{ "id": "q1", "updateType": "INSERT", "updateCount": 3 }""");

        using (reader)
        {
            Assert.False(reader.Read());
            Assert.Equal(3, reader.RecordsAffected);
        }
    }

    [Fact(DisplayName = "无当前行与越界访问")]
    public void InvalidAccess()
    {
        var (reader, _, _) = CreateReader("""{ "columns": [{"name":"n","type":"integer"}], "data": [["1"]] }""");

        using (reader)
        {
            Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
            Assert.Throws<IndexOutOfRangeException>(() => reader.GetName(9));
            Assert.Throws<IndexOutOfRangeException>(() => reader.GetValue(-1));
            Assert.True(reader.Read());
            Assert.Throws<IndexOutOfRangeException>(() => reader.GetValue(9));
            Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("missing"));
            Assert.Throws<ArgumentNullException>(() => reader.GetOrdinal(null!));
        }
    }

    [Fact(DisplayName = "GetValues、索引器与 SchemaTable")]
    public void GetValuesAndSchemaTable()
    {
        var (reader, _, _) = CreateReader("""{ "columns": [{"name":"id","type":"bigint"},{"name":"name","type":"varchar(20)"}], "data": [["1","a"]] }""");

        using (reader)
        {
            Assert.True(reader.Read());

            var values = new Object[2];
            Assert.Equal(2, reader.GetValues(values));
            Assert.Equal(1L, values[0]);
            Assert.Equal("a", values[1]);
            Assert.Equal(1L, reader[0]);
            Assert.Equal("a", reader["name"]);
            Assert.Equal(1L, reader.GetProviderSpecificValue(0));
            Assert.Equal(typeof(Int64), reader.GetProviderSpecificFieldType(0));

            var schema = reader.GetSchemaTable();
            Assert.Equal(2, schema.Rows.Count);
            Assert.Equal("id", schema.Rows[0]["ColumnName"]);
            Assert.Equal(0, schema.Rows[0]["ColumnOrdinal"]);
            Assert.Equal(typeof(Int64), schema.Rows[0]["DataType"]);
            Assert.Equal("bigint", schema.Rows[0]["DataTypeName"]);
            Assert.True((Boolean)schema.Rows[0]["AllowDBNull"]);
            Assert.Equal(20, schema.Rows[1]["ColumnSize"]);
            Assert.Same(schema, reader.GetSchemaTable());
        }
    }

    [Fact(DisplayName = "GetBytes 与 GetChars")]
    public void BinaryAndCharacters()
    {
        var (reader, _, _) = CreateReader("""{ "columns": [{"name":"bin","type":"varbinary"},{"name":"txt","type":"varchar"}], "data": [["AQID","abc"]] }""");

        using (reader)
        {
            Assert.True(reader.Read());

            Assert.Equal(3, reader.GetBytes(0, 0, null, 0, 0));
            var buffer = new Byte[3];
            Assert.Equal(2, reader.GetBytes(0, 1, buffer, 0, 2));
            Assert.Equal(new Byte[] { 2, 3, 0 }, buffer);
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, -1, buffer, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, 0, buffer, 0, -1));

            Assert.Equal(3, reader.GetChars(1, 0, null, 0, 0));
            var chars = new Char[3];
            Assert.Equal(2, reader.GetChars(1, 1, chars, 0, 2));
            Assert.Equal(new[] { 'b', 'c', '\0' }, chars);
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetChars(1, -1, chars, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetChars(1, 0, chars, 0, -1));
        }
    }

    [Fact(DisplayName = "枚举器遍历当前行")]
    public void Enumerator()
    {
        var (reader, _, _) = CreateReader("""{ "columns": [{"name":"n","type":"integer"}], "data": [["1"]] }""");

        using (reader)
        {
            var records = reader.Cast<System.Data.IDataRecord>().ToArray();
            Assert.Single(records);
            Assert.Equal(1, records[0]["n"]);
        }
    }

    [Fact(DisplayName = "关闭未完成的读取器发送 DELETE")]
    public async Task CloseCancelsIncomplete()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "columns": [{"name":"n","type":"integer"}], "data": [], "nextUri": "http://localhost:8080/v1/statement/q1/1" }""");
        handler.EnqueueJson("""{ "id": "q1" }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        var command = new PrestoCommand("select 1", connection);
        var reader = (PrestoDataReader)command.ExecuteReader();

        reader.Close();
        await reader.CancelTask!;

        Assert.True(reader.IsClosed);
        Assert.Throws<InvalidOperationException>(() => reader.Read());
        Assert.Throws<InvalidOperationException>(() => reader.GetSchemaTable());
        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
    }

    [Fact(DisplayName = "单结果集与异步读取")]
    public async Task NextResultAndReadAsync()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "columns": [{"name":"n","type":"integer"}], "data": [["1"]], "nextUri": "http://localhost:8080/v1/statement/q1/1" }""");
        handler.EnqueueJson("""{ "columns": [{"name":"n","type":"integer"}], "data": [["2"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        var command = new PrestoCommand("select 1", connection);
        using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();

        Assert.False(reader.NextResult());
        Assert.False(await reader.NextResultAsync(CancellationToken.None));
        Assert.True(await reader.ReadAsync(cts.Token));
        Assert.Equal(1, reader.GetInt32(0));
        Assert.True(await reader.ReadAsync(cts.Token));
        Assert.Equal(2, reader.GetInt32(0));
        Assert.False(await reader.ReadAsync(CancellationToken.None));
    }

    [Fact(DisplayName = "受影响行数与更新类型")]
    public void RecordsAffectedAndUpdateType()
    {
        var (reader, _, _) = CreateReader("""{ "updateType": "INSERT", "updateCount": 7, "columns": [], "data": [] }""");

        using (reader)
        {
            Assert.Equal(7, reader.RecordsAffected);
            Assert.Equal("INSERT", reader.UpdateType);
            Assert.Equal(0, reader.Depth);
            Assert.False(reader.Read());
        }
    }

    [Fact(DisplayName = "读取器释放后关闭连接")]
    public void DisposeCloseConnection()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "columns": [{"name":"n","type":"integer"}], "data": [["1"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        var command = new PrestoCommand("select 1", connection);
        var reader = command.ExecuteReader(CommandBehavior.CloseConnection);

        reader.Dispose();

        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact(DisplayName = "布尔值兼容多种写法")]
    public void BooleanVariants()
    {
        var (reader, _, _) = CreateReader("""
        { "columns": [
            {"name":"a","type":"boolean"},
            {"name":"b","type":"boolean"},
            {"name":"c","type":"boolean"},
            {"name":"d","type":"boolean"}],
          "data": [["true","false","1","0"]] }
        """);

        using (reader)
        {
            Assert.True(reader.Read());
            Assert.True(reader.GetBoolean(0));
            Assert.False(reader.GetBoolean(1));
            Assert.True(reader.GetBoolean(2));
            Assert.False(reader.GetBoolean(3));
        }
    }

    [Fact(DisplayName = "非法布尔值抛出转换异常")]
    public void InvalidBoolean()
    {
        var (reader, _, _) = CreateReader("""{ "columns": [{"name":"b","type":"boolean"}], "data": [["yes"]] }""");

        using (reader)
        {
            Assert.True(reader.Read());
            Assert.Throws<PrestoException>(() => reader.GetBoolean(0));
        }
    }

    [Fact(DisplayName = "GetByte 与 GetFieldValue 转换")]
    public void ByteAndGenericValue()
    {
        var (reader, _, _) = CreateReader("""
        { "columns": [{"name":"t","type":"tinyint"},{"name":"l","type":"bigint"}], "data": [["12","1"]] }
        """);

        using (reader)
        {
            Assert.True(reader.Read());
            Assert.Equal((Byte)12, reader.GetByte(0));
            Assert.Equal(1.0d, reader.GetFieldValue<Double>(1));
            Assert.Equal(1L, reader.GetFieldValue<Object>(1));
        }
    }

    [Fact(DisplayName = "GetDateTime 兼容带时区与字符串列")]
    public void DateTimeConversions()
    {
        var (reader, _, _) = CreateReader("""
        { "columns": [
            {"name":"tz","type":"timestamp(3) with time zone"},
            {"name":"txt","type":"varchar"}],
          "data": [["2020-01-02 03:04:05.006 +08:00","2021-02-03 04:05:06.007"]] }
        """);

        using (reader)
        {
            Assert.True(reader.Read());
            Assert.Equal(new DateTime(2020, 1, 2, 3, 4, 5, 6), reader.GetDateTime(0));
            Assert.Equal(new DateTime(2021, 2, 3, 4, 5, 6, 7), reader.GetDateTime(1));
        }
    }

    [Fact(DisplayName = "非法日期抛出转换异常")]
    public void InvalidDate()
    {
        var (reader, _, _) = CreateReader("""{ "columns": [{"name":"d","type":"date"}], "data": [["not-a-date"]] }""");

        using (reader)
        {
            Assert.True(reader.Read());
            Assert.Throws<PrestoException>(() => reader.GetDateTime(0));
        }
    }

    [Fact(DisplayName = "异步单行读取取消剩余查询")]
    public async Task AsyncSingleRow()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "columns": [{"name":"n","type":"integer"}], "data": [["1"],["2"]], "nextUri": "http://localhost:8080/v1/statement/q1/1" }""");
        handler.EnqueueJson("""{ "id": "q1" }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        var command = new PrestoCommand("select 1", connection);
        using var reader = (PrestoDataReader)await command.ExecuteReaderAsync(CommandBehavior.SingleRow, CancellationToken.None);

        Assert.True(await reader.ReadAsync(CancellationToken.None));
        Assert.Equal(1, reader.GetInt32(0));
        Assert.False(await reader.ReadAsync(CancellationToken.None));
        await reader.CancelTask!;
        Assert.Contains(handler.Requests, e => e.Method == HttpMethod.Delete);
    }

    [Fact(DisplayName = "同步拉页超时映射为命令超时")]
    public void FetchTimeoutSync()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "columns": [{"name":"n","type":"integer"}], "data": [], "nextUri": "http://localhost:8080/v1/statement/q1/1" }""");
        handler.EnqueueDelay(TimeSpan.FromSeconds(5), """{ "columns": [{"name":"n","type":"integer"}], "data": [["1"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        var command = new PrestoCommand("select 1", connection) { CommandTimeout = 1 };
        using var reader = command.ExecuteReader();

        var ex = Assert.Throws<PrestoException>(() => reader.Read());

        Assert.Equal(NewLife.Localization.Translate("Command timeout of {0} seconds expired.", 1), ex.Message);
    }

    [Fact(DisplayName = "异步拉页超时映射为命令超时")]
    public async Task FetchTimeoutAsync()
    {
        var handler = new FakePrestoHandler();
        handler.EnqueueJson("""{ "columns": [{"name":"n","type":"integer"}], "data": [], "nextUri": "http://localhost:8080/v1/statement/q1/1" }""");
        handler.EnqueueDelay(TimeSpan.FromSeconds(5), """{ "columns": [{"name":"n","type":"integer"}], "data": [["1"]] }""");
        var connection = TestHelper.CreateOpenConnection(handler);
        var command = new PrestoCommand("select 1", connection) { CommandTimeout = 1 };
        using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

        var ex = await Assert.ThrowsAsync<PrestoException>(() => reader.ReadAsync(CancellationToken.None));

        Assert.Equal(NewLife.Localization.Translate("Command timeout of {0} seconds expired.", 1), ex.Message);
    }

    [Fact(DisplayName = "未知字段类型按文本返回")]
    public void ObjectFieldFallback()
    {
        var column = new PrestoColumn { Name = "o", Type = "object", FieldType = typeof(Object) };

        Assert.Equal("abc", PrestoValueConverter.Convert("abc", column));
    }

    [Fact(DisplayName = "SchemaTable 输出 decimal 精度")]
    public void DecimalSchema()
    {
        var (reader, _, _) = CreateReader("""{ "columns": [{"name":"m","type":"decimal(10,2)"}], "data": [["1.23"]] }""");

        using (reader)
        {
            var schema = reader.GetSchemaTable();
            Assert.Equal((Int16)10, schema.Rows[0]["NumericPrecision"]);
            Assert.Equal((Int16)2, schema.Rows[0]["NumericScale"]);
        }
    }

    [Fact(DisplayName = "SchemaTable 兼容无参数与缺右括号类型")]
    public void SchemaEdgeTypes()
    {
        var (reader, _, _) = CreateReader("""
        { "columns": [
            {"name":"a","type":"decimal"},
            {"name":"b","type":"varchar"},
            {"name":"c","type":"decimal(10"},
            {"name":"d","type":"decimal(10,)"},
            {"name":"e","type":"varchar(5)"}],
          "data": [["1.1","x","2.2","3.3","abc"]] }
        """);

        using (reader)
        {
            var schema = reader.GetSchemaTable();
            Assert.Equal((Int16)0, schema.Rows[0]["NumericPrecision"]);
            Assert.Equal(-1, schema.Rows[1]["ColumnSize"]);
            Assert.Equal((Int16)0, schema.Rows[2]["NumericPrecision"]);
            Assert.Equal((Int16)10, schema.Rows[3]["NumericPrecision"]);
            Assert.Equal(5, schema.Rows[4]["ColumnSize"]);
        }
    }

    [Fact(DisplayName = "受影响行数超过 Int32 时截断")]
    public void RecordsAffectedClamp()
    {
        var (reader, _, _) = CreateReader("""{ "updateCount": 5000000000, "columns": [], "data": [] }""");

        using (reader)
        {
            Assert.Equal(Int32.MaxValue, reader.RecordsAffected);
        }
    }

    [Fact(DisplayName = "GetValues 支持较小目标数组")]
    public void GetValuesSmallArray()
    {
        var (reader, _, _) = CreateReader("""{ "columns": [{"name":"a","type":"integer"},{"name":"b","type":"integer"}], "data": [["1","2"]] }""");

        using (reader)
        {
            Assert.True(reader.Read());
            var values = new Object[1];
            Assert.Equal(1, reader.GetValues(values));
            Assert.Equal(1, values[0]);
        }
    }

    [Fact(DisplayName = "重复列名按首个匹配")]
    public void DuplicateColumnNames()
    {
        var (reader, _, _) = CreateReader("""{ "columns": [{"name":"n","type":"integer"},{"name":"N","type":"integer"}], "data": [["1","2"]] }""");

        using (reader)
        {
            Assert.True(reader.Read());
            Assert.Equal(0, reader.GetOrdinal("n"));
        }
    }

    [Fact(DisplayName = "值转换器直接覆盖特殊类型")]
    public void ValueConverterDirect()
    {
        var single = new PrestoColumn { Name = "f", Type = "real", FieldType = typeof(Single) };
        Assert.Equal(1.5f, PrestoValueConverter.Convert(1.5f, single));
        Assert.Equal(Single.NaN, PrestoValueConverter.Convert("NaN", single));
        Assert.Equal(Single.PositiveInfinity, PrestoValueConverter.Convert("Infinity", single));
        Assert.Equal(Single.NegativeInfinity, PrestoValueConverter.Convert("-Infinity", single));

        var dbl = new PrestoColumn { Name = "d", Type = "double", FieldType = typeof(Double) };
        Assert.Equal(1.5d, PrestoValueConverter.Convert(1.5d, dbl));
        Assert.Equal(Double.NaN, PrestoValueConverter.Convert("NaN", dbl));
        Assert.Equal(Double.PositiveInfinity, PrestoValueConverter.Convert("Infinity", dbl));
        Assert.Equal(Double.NegativeInfinity, PrestoValueConverter.Convert("-Infinity", dbl));

        var boolean = new PrestoColumn { Name = "b", Type = "boolean", FieldType = typeof(Boolean) };
        Assert.True((Boolean)PrestoValueConverter.Convert(true, boolean));

        var date = new PrestoColumn { Name = "dt", Type = "date", FieldType = typeof(DateTime) };
        var now = new DateTime(2020, 1, 2, 3, 4, 5, 6);
        Assert.Equal(now, PrestoValueConverter.Convert(now, date));

        var offset = new PrestoColumn { Name = "dto", Type = "timestamp with time zone", FieldType = typeof(DateTimeOffset) };
        var dto = new DateTimeOffset(2020, 1, 2, 3, 4, 5, 6, TimeSpan.FromHours(8));
        Assert.Equal(dto, PrestoValueConverter.Convert(dto, offset));

        var interval = new PrestoColumn { Name = "iv", Type = "interval day to second", FieldType = typeof(TimeSpan) };
        Assert.Equal(new TimeSpan(1, 2, 3, 4, 5), PrestoValueConverter.Convert(new TimeSpan(1, 2, 3, 4, 5), interval));
        Assert.Equal(new TimeSpan(0, 3, 4, 5), PrestoValueConverter.Convert("03:04:05", interval));
        Assert.Equal(new TimeSpan(-1, -2, -3, -4, -5), PrestoValueConverter.Convert("-1 02:03:04.005", interval));

        var binary = new PrestoColumn { Name = "bin", Type = "varbinary", FieldType = typeof(Byte[]) };
        Assert.Equal(new Byte[] { 1, 2 }, PrestoValueConverter.Convert(new Byte[] { 1, 2 }, binary));
        Assert.Equal(new Byte[] { 1, 2 }, PrestoValueConverter.Convert("AQI=", binary));
        Assert.Throws<PrestoException>(() => PrestoValueConverter.Convert("%%%", binary));

        var text = new PrestoColumn { Name = "s", Type = "varchar", FieldType = typeof(String) };
        Assert.Equal("123", PrestoValueConverter.Convert(123, text));
        Assert.Equal("True", PrestoValueConverter.Convert(true, text));

        Assert.True((Boolean)PrestoValueConverter.Convert("true", boolean));
        Assert.False((Boolean)PrestoValueConverter.Convert("0", boolean));
        Assert.Throws<PrestoException>(() => PrestoValueConverter.Convert("2020-13-99", date));
    }

    [Fact(DisplayName = "数字快速转换覆盖全部目标类型")]
    public void NumberFastPaths()
    {
        static PrestoColumn Column(Type type) => new() { Name = "n", Type = "n", FieldType = type };

        Assert.Equal(2L, PrestoValueConverter.Convert(2, Column(typeof(Int64))));
        Assert.Equal(2d, PrestoValueConverter.Convert(2, Column(typeof(Double))));
        Assert.Equal(2f, PrestoValueConverter.Convert(2, Column(typeof(Single))));
        Assert.Equal(2m, PrestoValueConverter.Convert(2, Column(typeof(Decimal))));
        Assert.Equal((Int16)2, PrestoValueConverter.Convert(2, Column(typeof(Int16))));
        Assert.Equal((SByte)2, PrestoValueConverter.Convert(2, Column(typeof(SByte))));

        Assert.Equal(2, PrestoValueConverter.Convert(2L, Column(typeof(Int32))));
        Assert.Equal(2d, PrestoValueConverter.Convert(2L, Column(typeof(Double))));
        Assert.Equal(2f, PrestoValueConverter.Convert(2L, Column(typeof(Single))));
        Assert.Equal(2m, PrestoValueConverter.Convert(2L, Column(typeof(Decimal))));
        Assert.Equal((Int16)2, PrestoValueConverter.Convert(2L, Column(typeof(Int16))));
        Assert.Equal((SByte)2, PrestoValueConverter.Convert(2L, Column(typeof(SByte))));

        Assert.Throws<PrestoException>(() => PrestoValueConverter.Convert(Int64.MaxValue, Column(typeof(Int32))));
        Assert.Throws<PrestoException>(() => PrestoValueConverter.Convert(300, Column(typeof(SByte))));
    }
}
