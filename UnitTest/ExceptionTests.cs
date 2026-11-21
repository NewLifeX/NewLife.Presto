using System.Data.Common;
using System.Globalization;
using NewLife;
using NewLife.Presto;

namespace UnitTest;

/// <summary>异常、不支持能力与驱动元数据测试</summary>
[Collection("Localization")]
public class ExceptionTests
{
    /// <summary>查询失败异常保留消息</summary>
    [Fact(DisplayName = "查询失败异常保留消息")]
    public void QueryFailedIncludesMessage()
    {
        var ex = PrestoException.QueryFailed("line 1:1: boom");

        Assert.IsType<PrestoException>(ex);
        Assert.IsAssignableFrom<DbException>(ex);
        Assert.Contains("boom", ex.Message);
    }

    /// <summary>查询失败异常保留内部异常</summary>
    [Fact(DisplayName = "查询失败异常保留内部异常")]
    public void QueryFailedKeepsInnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = PrestoException.QueryFailed("boom", inner);

        Assert.Same(inner, ex.InnerException);
    }

    [Fact(DisplayName = "错误对象只带消息时异常可用")]
    public void QueryFailedMinimalError()
    {
        var ex = PrestoException.QueryFailed(new PrestoQueryError { Message = "m" }, null, null);

        Assert.Contains("m", ex.Message);
        Assert.Equal(0, ex.PrestoErrorCode);
        Assert.Throws<ArgumentNullException>(() => PrestoException.QueryFailed((PrestoQueryError)null!, null, null));
    }

    /// <summary>提交失败异常保留 HTTP 状态</summary>
    [Fact(DisplayName = "提交失败异常保留 HTTP 状态")]
    public void SubmitFailedKeepsStatus()
    {
        var ex = PrestoException.SubmitFailed(503);

        Assert.Equal(503, ex.HttpStatusCode);
        Assert.Contains("503", ex.Message);
    }

    /// <summary>服务端错误详情可初始化</summary>
    [Fact(DisplayName = "服务端错误详情可初始化")]
    public void ServerDetailsArePreserved()
    {
        var ex = new PrestoException("failed")
        {
            QueryId = "q1",
            PrestoErrorCode = 47,
            ErrorName = "TABLE_NOT_FOUND",
            ErrorType = "USER_ERROR",
            PrestoSqlState = "42S02",
            HttpStatusCode = 400,
        };

        Assert.Equal("q1", ex.QueryId);
        Assert.Equal(47, ex.PrestoErrorCode);
        Assert.Equal("TABLE_NOT_FOUND", ex.ErrorName);
        Assert.Equal("USER_ERROR", ex.ErrorType);
        Assert.Equal("42S02", ex.PrestoSqlState);
        Assert.Equal(400, ex.HttpStatusCode);
    }

    /// <summary>不支持能力抛出明确异常</summary>
    [Fact(DisplayName = "不支持能力抛出明确异常")]
    public void UnsupportedCapabilities()
    {
        var saved = Localization.Culture;
        try
        {
            Localization.Culture = CultureInfo.GetCultureInfo("en");

            Assert.Equal("The driver does not support transactions.", PrestoUnsupported.Transactions().Message);
            Assert.Contains("auto-increment", PrestoUnsupported.AutoIncrement().Message);
            Assert.Contains("CommandType.Text", PrestoUnsupported.CommandType(4).Message);
            Assert.Contains("4", PrestoUnsupported.CommandType(4).Message);
            Assert.Equal("Multiple statements are not supported.", PrestoUnsupported.MultipleStatements().Message);
        }
        finally
        {
            Localization.Culture = saved;
        }
    }

    /// <summary>驱动元数据可用</summary>
    [Fact(DisplayName = "驱动元数据可用")]
    public void DriverMetadata()
    {
        Assert.False(PrestoDriver.Title.IsNullOrEmpty());
        Assert.False(PrestoDriver.Description.IsNullOrEmpty());
        Assert.True(PrestoDriver.Version.Major >= 1);
    }
}
