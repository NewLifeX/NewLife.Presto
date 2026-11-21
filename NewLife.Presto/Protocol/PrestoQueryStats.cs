using System.Collections.Generic;

namespace NewLife.Presto;

/// <summary>查询统计</summary>
/// <remarks>字段缺失时保留默认值；未知字段保存在 <see cref="Raw"/> 中，便于诊断与向后兼容。</remarks>
public sealed class PrestoQueryStats
{
    /// <summary>服务端状态字符串</summary>
    public String? State { get; init; }

    /// <summary>排队耗时毫秒</summary>
    public Int64 QueuedTimeMillis { get; init; }

    /// <summary>执行耗时毫秒</summary>
    public Int64 ElapsedTimeMillis { get; init; }

    /// <summary>处理行数</summary>
    public Int64 ProcessedRows { get; init; }

    /// <summary>处理字节数</summary>
    public Int64 ProcessedBytes { get; init; }

    /// <summary>总分片数</summary>
    public Int32 TotalSplits { get; init; }

    /// <summary>已完成分片数</summary>
    public Int32 CompletedSplits { get; init; }

    /// <summary>运行中分片数</summary>
    public Int32 RunningSplits { get; init; }

    /// <summary>原始统计对象</summary>
    public IDictionary<String, Object?>? Raw { get; init; }
}
