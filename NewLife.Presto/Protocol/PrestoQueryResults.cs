using System.Collections.Generic;

namespace NewLife.Presto;

/// <summary>单页查询结果</summary>
/// <remarks>
/// 与 Presto/Trino REST v1 协议一一对应：<see cref="NextUri"/> 缺失表示查询结束；
/// <see cref="Data"/> 可能出现在中间页，读取器按页消费，不假设页大小。
/// </remarks>
public sealed class PrestoQueryResults
{
    /// <summary>查询编号</summary>
    public String? Id { get; init; }

    /// <summary>查询信息页地址</summary>
    public String? InfoUri { get; init; }

    /// <summary>下一页/下一次轮询地址；为空表示结束</summary>
    public String? NextUri { get; init; }

    /// <summary>列定义</summary>
    public IList<PrestoColumn> Columns { get; init; } = [];

    /// <summary>当前页数据行</summary>
    public IList<IList<Object?>> Data { get; init; } = [];

    /// <summary>当前页数据编码；空表示直接协议</summary>
    internal String? DataEncoding { get; init; }

    /// <summary>当前页的 Spooling 分段；按结果顺序排列</summary>
    internal IList<PrestoQuerySegment> Segments { get; init; } = [];

    /// <summary>查询统计</summary>
    public PrestoQueryStats? Stats { get; init; }

    /// <summary>警告列表</summary>
    public IList<PrestoWarning> Warnings { get; init; } = [];

    /// <summary>错误对象；非空表示失败</summary>
    public PrestoQueryError? Error { get; init; }

    /// <summary>写操作类型</summary>
    public String? UpdateType { get; init; }

    /// <summary>受影响行数</summary>
    public Int64? UpdateCount { get; init; }

    /// <summary>是否已结束</summary>
    public Boolean IsComplete => NextUri.IsNullOrEmpty();
}
