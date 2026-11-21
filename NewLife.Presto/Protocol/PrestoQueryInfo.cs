using System.Collections.Generic;

namespace NewLife.Presto;

/// <summary>查询诊断信息</summary>
/// <remarks>
/// 对应协调器 `/v1/query` 与 `/v1/query/{queryId}` 的返回对象；
/// 兼容 PrestoDB 与 Trino 的字段差异（`queryId`/`id`、`queryStats`/`stats`、`errorInfo`/`error`），
/// 未映射字段保留在 <see cref="Raw"/> 中。
/// </remarks>
public sealed class PrestoQueryInfo
{
    /// <summary>查询编号</summary>
    public String? Id { get; init; }

    /// <summary>查询状态，如 QUEUED/RUNNING/FINISHED/FAILED</summary>
    public String? State { get; init; }

    /// <summary>SQL 文本</summary>
    public String? Query { get; init; }

    /// <summary>提交用户</summary>
    public String? User { get; init; }

    /// <summary>客户端来源</summary>
    public String? Source { get; init; }

    /// <summary>Catalog</summary>
    public String? Catalog { get; init; }

    /// <summary>Schema</summary>
    public String? Schema { get; init; }

    /// <summary>查询统计</summary>
    public PrestoQueryStats? Stats { get; init; }

    /// <summary>查询错误</summary>
    public PrestoQueryError? Error { get; init; }

    /// <summary>原始返回对象</summary>
    public IDictionary<String, Object?>? Raw { get; init; }
}
