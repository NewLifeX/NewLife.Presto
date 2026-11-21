namespace NewLife.Presto;

/// <summary>服务端查询错误</summary>
public sealed class PrestoQueryError
{
    /// <summary>错误消息</summary>
    public String? Message { get; init; }

    /// <summary>错误码</summary>
    public Int32 ErrorCode { get; init; }

    /// <summary>错误名，如 TABLE_NOT_FOUND</summary>
    public String? ErrorName { get; init; }

    /// <summary>错误类型，如 USER_ERROR</summary>
    public String? ErrorType { get; init; }

    /// <summary>SQLState</summary>
    public String? SqlState { get; init; }

    /// <summary>失败类型，如 io.trino.spi.TrinoException</summary>
    public String? FailureType { get; init; }

    /// <summary>错误位置，如 1:8</summary>
    public String? Location { get; init; }

    /// <summary>原始 failureInfo 对象</summary>
    public Object? FailureInfo { get; init; }

    /// <summary>原始 errorLocation 对象</summary>
    public Object? ErrorLocation { get; init; }
}
