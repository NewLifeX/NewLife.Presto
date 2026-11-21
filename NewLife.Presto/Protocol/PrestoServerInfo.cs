using System.Collections.Generic;

namespace NewLife.Presto;

/// <summary>服务端信息</summary>
/// <remarks>对应 `/v1/info`，仅在 TestConnection 或显式调用时请求。</remarks>
public sealed class PrestoServerInfo
{
    /// <summary>节点版本</summary>
    public String? NodeVersion { get; init; }

    /// <summary>环境名称</summary>
    public String? Environment { get; init; }

    /// <summary>是否协调器</summary>
    public Boolean? Coordinator { get; init; }

    /// <summary>是否正在启动</summary>
    public Boolean? Starting { get; init; }

    /// <summary>运行时长</summary>
    public String? Uptime { get; init; }

    /// <summary>原始响应对象</summary>
    public IDictionary<String, Object?>? Raw { get; init; }
}
