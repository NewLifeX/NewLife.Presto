namespace NewLife.Presto;

/// <summary>查询警告</summary>
public sealed class PrestoWarning
{
    /// <summary>警告消息</summary>
    public String? Message { get; init; }

    /// <summary>警告码</summary>
    public Int32 WarningCode { get; init; }

    /// <summary>错误名</summary>
    public String? ErrorName { get; init; }

    /// <summary>错误类型</summary>
    public String? ErrorType { get; init; }
}
