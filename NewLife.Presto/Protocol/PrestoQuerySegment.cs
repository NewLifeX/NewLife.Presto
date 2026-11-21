using System.Collections.Generic;

namespace NewLife.Presto;

/// <summary>Spooling 结果页中的数据分段</summary>
/// <remarks>
/// Trino 466+ 的 Spooling 协议把 <c>data</c> 从行数组改成“编码 + 分段数组”对象。
/// 分段分两种：<c>inline</c> 直接内嵌 Base64 数据，<c>spooled</c> 指向对象存储并在读取后确认。
/// </remarks>
internal sealed class PrestoQuerySegment
{
    /// <summary>分段类型：inline / spooled</summary>
    public String? Type { get; init; }

    /// <summary>内嵌分段的 Base64 数据</summary>
    public String? Data { get; init; }

    /// <summary>对象存储下载地址</summary>
    public String? Uri { get; init; }

    /// <summary>确认地址，读取完成后 GET 释放分段</summary>
    public String? AckUri { get; init; }

    /// <summary>下载与确认请求必须原样携带的请求头</summary>
    public IDictionary<String, IList<String>> Headers { get; init; } = new Dictionary<String, IList<String>>(StringComparer.OrdinalIgnoreCase);
}
