namespace NewLife.Presto;

/// <summary>Presto/Trino 请求头名称</summary>
/// <remarks>两套协议仅头前缀不同：PrestoDB 使用 X-Presto-*，Trino 351 起使用 X-Trino-*</remarks>
public static class PrestoHeaders
{
    /// <summary>获取协议头前缀</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>X-Presto- 或 X-Trino-</returns>
    public static String GetPrefix(PrestoProtocol protocol) => protocol == PrestoProtocol.Trino ? "X-Trino-" : "X-Presto-";

    /// <summary>用户</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>请求头名称</returns>
    public static String User(PrestoProtocol protocol) => GetPrefix(protocol) + "User";

    /// <summary>Catalog</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>请求头名称</returns>
    public static String Catalog(PrestoProtocol protocol) => GetPrefix(protocol) + "Catalog";

    /// <summary>Schema</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>请求头名称</returns>
    public static String Schema(PrestoProtocol protocol) => GetPrefix(protocol) + "Schema";

    /// <summary>会话属性</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>请求头名称</returns>
    public static String Session(PrestoProtocol protocol) => GetPrefix(protocol) + "Session";

    /// <summary>来源标识</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>请求头名称</returns>
    public static String Source(PrestoProtocol protocol) => GetPrefix(protocol) + "Source";

    /// <summary>时区</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>请求头名称</returns>
    public static String TimeZone(PrestoProtocol protocol) => GetPrefix(protocol) + "Time-Zone";

    /// <summary>语言</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>请求头名称</returns>
    public static String Language(PrestoProtocol protocol) => GetPrefix(protocol) + "Language";

    /// <summary>客户端标签</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>请求头名称</returns>
    public static String ClientTags(PrestoProtocol protocol) => GetPrefix(protocol) + "Client-Tags";

    /// <summary>客户端信息</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>请求头名称</returns>
    public static String ClientInfo(PrestoProtocol protocol) => GetPrefix(protocol) + "Client-Info";

    /// <summary>资源组</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>请求头名称</returns>
    public static String ResourceGroup(PrestoProtocol protocol) => GetPrefix(protocol) + "Resource-Group";

    /// <summary>链路令牌</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>请求头名称</returns>
    public static String TraceToken(PrestoProtocol protocol) => GetPrefix(protocol) + "Trace-Token";

    /// <summary>角色</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>请求头名称</returns>
    public static String Role(PrestoProtocol protocol) => GetPrefix(protocol) + "Role";

    /// <summary>事务</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>请求头名称</returns>
    public static String Transaction(PrestoProtocol protocol) => GetPrefix(protocol) + "Transaction";

    /// <summary>服务端设置 Catalog</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>响应头名称</returns>
    public static String SetCatalog(PrestoProtocol protocol) => GetPrefix(protocol) + "Set-Catalog";

    /// <summary>服务端设置 Schema</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>响应头名称</returns>
    public static String SetSchema(PrestoProtocol protocol) => GetPrefix(protocol) + "Set-Schema";

    /// <summary>服务端设置会话属性</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>响应头名称</returns>
    public static String SetSession(PrestoProtocol protocol) => GetPrefix(protocol) + "Set-Session";

    /// <summary>服务端清除会话属性</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>响应头名称</returns>
    public static String ClearSession(PrestoProtocol protocol) => GetPrefix(protocol) + "Clear-Session";

    /// <summary>服务端设置角色</summary>
    /// <param name="protocol">协议类型</param>
    /// <returns>响应头名称</returns>
    public static String SetRole(PrestoProtocol protocol) => GetPrefix(protocol) + "Set-Role";

    /// <summary>判断请求头是否由驱动管理，不允许调用方覆盖</summary>
    /// <param name="name">请求头名称</param>
    /// <returns>是否受管</returns>
    public static Boolean IsManaged(String? name)
    {
        if (name.IsNullOrEmpty()) return false;

        var text = name.Trim();
        return text.EqualIgnoreCase("Authorization", "User-Agent", "Content-Type", "Content-Length", "Accept", "Accept-Encoding", "Host") ||
            text.StartsWithIgnoreCase("X-Presto-", "X-Trino-");
    }
}
