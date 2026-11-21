using System.Data;
using NewLife.Presto;

namespace UnitTest;

/// <summary>参数绑定测试</summary>
public class PrestoParameterBinderTests
{
    [Fact(DisplayName = "替换简单与重复参数")]
    public void BindSimpleAndRepeated()
    {
        var parameters = new PrestoParameterCollection
        {
            new PrestoParameter("@id", 1),
            new PrestoParameter("@Name", "x"),
        };

        var sql = PrestoParameterBinder.Bind("select * from t where a = @id and b = @name or c = @ID", parameters);

        Assert.Equal("select * from t where a = 1 and b = 'x' or c = 1", sql);
    }

    [Fact(DisplayName = "整型 0 作为参数值而不是类型")]
    public void ZeroValueBindsAsValue()
    {
        // 回归：曾因存在 (String, DbType) 重载，new PrestoParameter("@zero", 0) 被解析为该重载，值丢失并绑定为 NULL
        var parameter = new PrestoParameter("@zero", 0);
        Assert.Equal(0, parameter.Value);

        var parameters = new PrestoParameterCollection { parameter };
        Assert.Equal("select 0", PrestoParameterBinder.Bind("select @zero", parameters));
    }

    [Fact(DisplayName = "跳过字符串、标识符与注释")]
    public void SkipLiteralsAndComments()
    {
        var parameters = new PrestoParameterCollection { new PrestoParameter("@id", 7) };
        var sql = "select '@id', \"@id\", '''@id''', @id -- @id\n/* @id /* nested @id */ @id */";

        var result = PrestoParameterBinder.Bind(sql, parameters);

        Assert.Equal("select '@id', \"@id\", '''@id''', 7 -- @id\n/* @id /* nested @id */ @id */", result);
    }

    [Theory(DisplayName = "非参数位置的 @ 保持原样")]
    [InlineData("select @", "select @")]
    [InlineData("select @@x", "select @@x")]
    [InlineData("select @1", "select @1")]
    [InlineData("select a@b", "select a@b")]
    public void KeepNonParameterAt(String sql, String expected)
    {
        Assert.Equal(expected, PrestoParameterBinder.Bind(sql, new PrestoParameterCollection()));
    }

    [Fact(DisplayName = "缺失参数抛出异常")]
    public void MissingParameterThrows()
    {
        var ex = Assert.Throws<PrestoException>(() => PrestoParameterBinder.Bind("select @a, @b, @a", new PrestoParameterCollection()));

        Assert.Contains("@a", ex.Message);
        Assert.Contains("@b", ex.Message);
    }

    [Fact(DisplayName = "空参数名抛出异常")]
    public void EmptyParameterNameThrows()
    {
        var parameters = new PrestoParameterCollection { new PrestoParameter { Value = 1 } };

        Assert.Throws<PrestoException>(() => PrestoParameterBinder.Bind("select 1", parameters));
    }

    [Fact(DisplayName = "非输入参数抛出异常")]
    public void OutputParameterThrows()
    {
        var parameters = new PrestoParameterCollection { new PrestoParameter { ParameterName = "@x", Direction = ParameterDirection.Output } };

        Assert.Throws<NotSupportedException>(() => PrestoParameterBinder.Bind("select @x", parameters));
    }

    [Fact(DisplayName = "无参数 SQL 原样返回")]
    public void NoParameterReturnsSame()
    {
        const String sql = "select 1";

        Assert.Equal(sql, PrestoParameterBinder.Bind(sql, new PrestoParameterCollection()));
    }

    [Theory(DisplayName = "未闭合的字符串与注释不抛异常")]
    [InlineData("select 'abc")]
    [InlineData("select 1 -- comment")]
    [InlineData("select 1 --cr\rreturn")]
    [InlineData("select 1 /* comment")]
    public void UnterminatedTokens(String sql)
    {
        Assert.Equal(sql, PrestoParameterBinder.Bind(sql, new PrestoParameterCollection()));
    }

    [Fact(DisplayName = "参数位于语句开头")]
    public void ParameterAtStart()
    {
        var parameters = new PrestoParameterCollection { new PrestoParameter("@x", 1) };

        Assert.Equal("1", PrestoParameterBinder.Bind("@x", parameters));
    }

    [Fact(DisplayName = "SQL 与参数集合为空")]
    public void NullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => PrestoParameterBinder.Bind(null!, new PrestoParameterCollection()));
        Assert.Equal("select 1", PrestoParameterBinder.Bind("select 1", null!));
    }

    [Fact(DisplayName = "格式化基础标量")]
    public void FormatScalars()
    {
        Assert.Equal("NULL", PrestoParameterBinder.FormatValue(null));
        Assert.Equal("NULL", PrestoParameterBinder.FormatValue(DBNull.Value));
        Assert.Equal("true", PrestoParameterBinder.FormatValue(true));
        Assert.Equal("false", PrestoParameterBinder.FormatValue(false));
        Assert.Equal("1", PrestoParameterBinder.FormatValue((Byte)1));
        Assert.Equal("-1", PrestoParameterBinder.FormatValue((SByte)(-1)));
        Assert.Equal("2", PrestoParameterBinder.FormatValue((Int16)2));
        Assert.Equal("3", PrestoParameterBinder.FormatValue((UInt16)3));
        Assert.Equal("4", PrestoParameterBinder.FormatValue(4));
        Assert.Equal("5", PrestoParameterBinder.FormatValue((UInt32)5));
        Assert.Equal("6", PrestoParameterBinder.FormatValue(6L));
        Assert.Equal("7", PrestoParameterBinder.FormatValue(7UL));
        Assert.Equal("1.5", PrestoParameterBinder.FormatValue(1.5f));
        Assert.Equal("2.5", PrestoParameterBinder.FormatValue(2.5d));
        Assert.Equal("3.75", PrestoParameterBinder.FormatValue(3.75m));
        Assert.Equal("'it''s'", PrestoParameterBinder.FormatValue("it's"));
        Assert.Equal("'a'", PrestoParameterBinder.FormatValue('a'));
    }

    [Fact(DisplayName = "格式化日期与时间")]
    public void FormatDateTimes()
    {
        var date = new DateTime(2020, 1, 2, 3, 4, 5, 6);
        var offset = new DateTimeOffset(2020, 1, 2, 3, 4, 5, 6, TimeSpan.FromHours(8));

        Assert.Equal("TIMESTAMP '2020-01-02 03:04:05.006'", PrestoParameterBinder.FormatValue(date));
        Assert.Equal("TIMESTAMP '2020-01-02 03:04:05.006 +08:00'", PrestoParameterBinder.FormatValue(offset));
        Assert.Equal("DATE '2020-01-02'", PrestoParameterBinder.FormatValue(new DateOnly(2020, 1, 2)));
        Assert.Equal("TIME '03:04:05.006'", PrestoParameterBinder.FormatValue(new TimeOnly(3, 4, 5, 6)));
    }

    [Fact(DisplayName = "格式化时间间隔")]
    public void FormatIntervals()
    {
        var span = new TimeSpan(1, 2, 3, 4, 5);
        var negative = new TimeSpan(-1, -2, -3, -4, -5);

        Assert.Equal("INTERVAL '1 02:03:04.005' DAY TO SECOND", PrestoParameterBinder.FormatValue(span));
        Assert.Equal("INTERVAL '-1 02:03:04.005' DAY TO SECOND", PrestoParameterBinder.FormatValue(negative));
        Assert.Throws<PrestoException>(() => PrestoParameterBinder.FormatValue(TimeSpan.MinValue));
    }

    [Fact(DisplayName = "格式化二进制、Guid 与枚举")]
    public void FormatBinaryGuidEnum()
    {
        var bytes = new Byte[] { 0x01, 0xAB };
        var guid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        Assert.Equal("X'01AB'", PrestoParameterBinder.FormatValue(bytes));
        Assert.Equal("UUID '00112233-4455-6677-8899-aabbccddeeff'", PrestoParameterBinder.FormatValue(guid));
        Assert.Equal("'Monday'", PrestoParameterBinder.FormatValue(DayOfWeek.Monday));
    }

    [Fact(DisplayName = "格式化集合")]
    public void FormatCollections()
    {
        Assert.Equal("(1, 2, 3)", PrestoParameterBinder.FormatValue(new[] { 1, 2, 3 }));
        Assert.Equal("('a', 'b')", PrestoParameterBinder.FormatValue(new[] { "a", "b" }));
        Assert.Equal("(NULL)", PrestoParameterBinder.FormatValue(Array.Empty<Int32>()));
        Assert.Equal("((1, 2), (3, 4))", PrestoParameterBinder.FormatValue(new[] { new[] { 1, 2 }, new[] { 3, 4 } }));
    }

    [Fact(DisplayName = "特殊浮点值抛出异常")]
    public void SpecialFloatThrows()
    {
        Assert.Throws<PrestoException>(() => PrestoParameterBinder.FormatValue(Single.NaN));
        Assert.Throws<PrestoException>(() => PrestoParameterBinder.FormatValue(Single.PositiveInfinity));
        Assert.Throws<PrestoException>(() => PrestoParameterBinder.FormatValue(Double.NaN));
        Assert.Throws<PrestoException>(() => PrestoParameterBinder.FormatValue(Double.NegativeInfinity));
    }

    [Fact(DisplayName = "格式化未知对象为字符串")]
    public void FormatUnknownObject()
    {
        Assert.Equal("'1.2.3.4'", PrestoParameterBinder.FormatValue(new Version(1, 2, 3, 4)));
    }

    [Theory(DisplayName = "归一化参数名")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData(" @name ", "name")]
    [InlineData("name", "name")]
    public void NormalizeNames(String? name, String expected)
    {
        Assert.Equal(expected, PrestoParameterBinder.NormalizeName(name));
    }
}
