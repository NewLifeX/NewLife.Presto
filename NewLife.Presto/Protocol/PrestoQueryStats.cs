using System.Collections.Generic;

namespace NewLife.Presto;

/// <summary>查询统计</summary>
/// <remarks>字段缺失时保留默认值；未知字段保存在 <see cref="Raw"/> 中，便于诊断与向后兼容。</remarks>
public sealed class PrestoQueryStats
{
    private IDictionary<String, Object?>? _raw;
    private String? _rawJson;

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
    /// <remarks>网络路径按需解析，未访问时不产生字典开销</remarks>
    public IDictionary<String, Object?>? Raw
    {
        get => _raw ??= PrestoJson.ParseObject(RawJson);
        init => _raw = value;
    }

    /// <summary>原始统计 JSON，供 <see cref="Raw"/> 延迟解析</summary>
    internal String? RawJson
    {
        get => _rawJson;
        init => _rawJson = value;
    }
}
