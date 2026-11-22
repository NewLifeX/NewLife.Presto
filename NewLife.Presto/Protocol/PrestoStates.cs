namespace NewLife.Presto;

/// <summary>查询状态常量</summary>
/// <remarks>协议里 <c>stats.state</c> 是高频出现的短字符串，复用常量可省掉每页一次分配。</remarks>
internal static class PrestoStates
{
    /// <summary>排队中</summary>
    public const String Queued = "QUEUED";

    /// <summary>执行中</summary>
    public const String Running = "RUNNING";

    /// <summary>已完成</summary>
    public const String Finished = "FINISHED";

    /// <summary>已失败</summary>
    public const String Failed = "FAILED";

    /// <summary>已取消</summary>
    public const String Canceled = "CANCELED";
}
