using System.Data.Common;

namespace NewLife.Presto;

/// <summary>Presto/Trino 驱动异常</summary>
/// <remarks>保留服务端错误码、SQLState、查询编号与 HTTP 状态，便于定位问题；消息不包含密码与令牌</remarks>
public class PrestoException : DbException
{
    /// <summary>实例化驱动异常</summary>
    /// <param name="message">异常消息</param>
    public PrestoException(String message) : base(message) { }

    /// <summary>实例化驱动异常</summary>
    /// <param name="message">异常消息</param>
    /// <param name="innerException">内部异常</param>
    public PrestoException(String message, Exception? innerException) : base(message, innerException) { }

    /// <summary>服务端查询编号</summary>
    public String? QueryId { get; init; }

    /// <summary>服务端错误码</summary>
    public Int32 PrestoErrorCode { get; init; }

    /// <summary>服务端错误名，如 TABLE_NOT_FOUND</summary>
    public String? ErrorName { get; init; }

    /// <summary>错误类型，如 USER_ERROR</summary>
    public String? ErrorType { get; init; }

    /// <summary>SQLState</summary>
    public String? PrestoSqlState { get; init; }

    /// <summary>HTTP 状态码</summary>
    public Int32? HttpStatusCode { get; init; }

    /// <summary>原始 failureInfo 对象</summary>
    public Object? FailureInfo { get; init; }

    /// <summary>原始 errorLocation 对象</summary>
    public Object? ErrorLocation { get; init; }

    /// <summary>构造查询失败异常</summary>
    /// <param name="message">服务端错误消息</param>
    /// <param name="innerException">内部异常</param>
    /// <returns>驱动异常</returns>
    public static PrestoException QueryFailed(String message, Exception? innerException = null)
        => new(Localization.Translate("Query failed: {0}", message), innerException);

    /// <summary>从服务端错误对象构造异常</summary>
    /// <param name="error">服务端错误</param>
    /// <param name="queryId">查询编号</param>
    /// <param name="httpStatusCode">HTTP 状态码</param>
    /// <param name="innerException">内部异常</param>
    /// <returns>驱动异常</returns>
    public static PrestoException QueryFailed(PrestoQueryError error, String? queryId, Int32? httpStatusCode, Exception? innerException = null)
    {
        if (error == null) throw new ArgumentNullException(nameof(error));

        var message = Localization.Translate("Query failed: {0}", error.Message ?? String.Empty);
        if (!queryId.IsNullOrEmpty()) message += $" [queryId={queryId}]";
        if (error.ErrorCode != 0) message += $" [errorCode={error.ErrorCode}]";
        if (!error.ErrorName.IsNullOrEmpty()) message += $" [errorName={error.ErrorName}]";
        if (!error.SqlState.IsNullOrEmpty()) message += $" [sqlState={error.SqlState}]";

        return new PrestoException(message, innerException)
        {
            QueryId = queryId,
            PrestoErrorCode = error.ErrorCode,
            ErrorName = error.ErrorName,
            ErrorType = error.ErrorType,
            PrestoSqlState = error.SqlState,
            HttpStatusCode = httpStatusCode,
            FailureInfo = error.FailureInfo,
            ErrorLocation = error.ErrorLocation,
        };
    }

    /// <summary>构造连接超时异常</summary>
    /// <param name="seconds">超时秒数</param>
    /// <param name="innerException">内部异常</param>
    /// <returns>驱动异常</returns>
    public static PrestoException ConnectionTimeout(Int32 seconds, Exception? innerException = null)
        => new(Localization.Translate("Connection timeout of {0} seconds expired.", seconds), innerException);

    /// <summary>构造命令超时异常</summary>
    /// <param name="seconds">超时秒数</param>
    /// <param name="innerException">内部异常</param>
    /// <returns>驱动异常</returns>
    public static PrestoException CommandTimeout(Int32 seconds, Exception? innerException = null)
        => new(Localization.Translate("Command timeout of {0} seconds expired.", seconds), innerException);

    /// <summary>构造语句提交失败异常</summary>
    /// <param name="statusCode">HTTP 状态码</param>
    /// <returns>驱动异常</returns>
    public static PrestoException SubmitFailed(Int32 statusCode)
        => new(Localization.Translate("Statement submission failed with HTTP {0}.", statusCode)) { HttpStatusCode = statusCode };
}

/// <summary>驱动明确不支持的 ADO.NET 能力</summary>
/// <remarks>Presto/Trino 没有事务、自增列与多语句；统一抛出带本地化消息的 <see cref="NotSupportedException"/></remarks>
public static class PrestoUnsupported
{
    /// <summary>事务不支持</summary>
    /// <returns>异常</returns>
    public static NotSupportedException Transactions() => new(Localization.Translate("The driver does not support transactions."));

    /// <summary>自增列不支持</summary>
    /// <returns>异常</returns>
    public static NotSupportedException AutoIncrement() => new(Localization.Translate("The driver does not support auto-increment columns; use a business key (Guid/snowflake) or set AllowInsertIdentity."));

    /// <summary>命令类型不支持</summary>
    /// <param name="commandType">命令类型</param>
    /// <returns>异常</returns>
    public static NotSupportedException CommandType(Int32 commandType) => new(Localization.Translate("Command type {0} is not supported; use CommandType.Text.", commandType));

    /// <summary>命令行为不支持</summary>
    /// <param name="behavior">命令行为</param>
    /// <returns>异常</returns>
    public static NotSupportedException CommandBehavior(Int32 behavior) => new(Localization.Translate("Command behavior {0} is not supported.", behavior));

    /// <summary>多语句不支持</summary>
    /// <returns>异常</returns>
    public static NotSupportedException MultipleStatements() => new(Localization.Translate("Multiple statements are not supported."));
}

/// <summary>驱动信息</summary>
public static class PrestoDriver
{
    private static readonly Version _version = typeof(PrestoDriver).Assembly.GetName().Version!;

    /// <summary>驱动标题，按当前界面语言翻译</summary>
    public static String Title => Localization.Translate("NewLife Presto Driver");

    /// <summary>驱动描述，按当前界面语言翻译</summary>
    public static String Description => Localization.Translate("Pure C# Presto/Trino ADO.NET driver with zero third-party dependencies, HTTP REST v1 protocol, full async/await and XCode ORM integration. MIT license. Supports net45~net11.");

    /// <summary>驱动版本</summary>
    public static Version Version => _version;
}
