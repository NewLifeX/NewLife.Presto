using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace NewLife.Presto;

/// <summary>会话上下文</summary>
/// <remarks>
/// 保存请求头需要发送的会话状态，并把服务端返回的 Set-Catalog/Set-Schema/Set-Session/Clear-Session/Set-Role
/// 应用到本地，保证后续请求沿用服务端会话。连接对象非线程安全，本类型也不做额外同步。
/// </remarks>
internal sealed class PrestoSessionState
{
    #region 属性
    /// <summary>协议类型</summary>
    public PrestoProtocol Protocol { get; }

    /// <summary>用户</summary>
    public String User { get; set; }

    /// <summary>Catalog</summary>
    public String? Catalog { get; set; }

    /// <summary>Schema</summary>
    public String? Schema { get; set; }

    /// <summary>来源标识</summary>
    public String? Source { get; set; }

    /// <summary>时区</summary>
    public String? TimeZone { get; set; }

    /// <summary>语言</summary>
    public String? Language { get; set; }

    /// <summary>客户端标签</summary>
    public String? ClientTags { get; set; }

    /// <summary>客户端信息</summary>
    public String? ClientInfo { get; set; }

    /// <summary>资源组</summary>
    public String? ResourceGroup { get; set; }

    /// <summary>链路令牌</summary>
    public String? TraceToken { get; set; }

    /// <summary>角色</summary>
    public String? Role { get; set; }

    /// <summary>会话属性</summary>
    public IDictionary<String, String> SessionProperties { get; }

    /// <summary>自定义请求头</summary>
    public IDictionary<String, String> CustomHeaders { get; }

    private IDictionary<String, String>? _headers;
    #endregion

    #region 构造
    /// <summary>从连接配置构建会话状态</summary>
    /// <param name="options">连接配置</param>
    public PrestoSessionState(PrestoConnectionOptions options)
    {
        Protocol = options.Protocol;
        User = options.User;
        Catalog = options.Catalog;
        Schema = options.Schema;
        Source = options.Source;
        TimeZone = options.TimeZone;
        Language = options.Language;
        ClientTags = options.ClientTags;
        ClientInfo = options.ClientInfo;
        ResourceGroup = options.ResourceGroup;
        TraceToken = options.TraceToken;
        Role = options.Role;
        SessionProperties = new Dictionary<String, String>(options.SessionProperties, StringComparer.OrdinalIgnoreCase);
        CustomHeaders = new Dictionary<String, String>(options.CustomHeaders, StringComparer.OrdinalIgnoreCase);
    }
    #endregion

    #region 方法
    /// <summary>切换 Catalog 与 Schema</summary>
    /// <param name="catalog">Catalog</param>
    /// <param name="schema">Schema，可为空</param>
    public void ChangeCatalog(String catalog, String? schema)
    {
        Catalog = catalog;
        Schema = schema;
        _headers = null;
    }

    /// <summary>生成请求头。结果按会话状态缓存，会话变化后重建</summary>
    /// <returns>请求头集合</returns>
    public IDictionary<String, String> BuildHeaders()
    {
        var cached = _headers;
        if (cached != null) return cached;

        var headers = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase)
        {
            [PrestoHeaders.User(Protocol)] = User,
        };

        if (!Catalog.IsNullOrEmpty()) headers[PrestoHeaders.Catalog(Protocol)] = Catalog!;
        if (!Schema.IsNullOrEmpty()) headers[PrestoHeaders.Schema(Protocol)] = Schema!;
        if (!Source.IsNullOrEmpty()) headers[PrestoHeaders.Source(Protocol)] = Source!;
        if (!TimeZone.IsNullOrEmpty()) headers[PrestoHeaders.TimeZone(Protocol)] = TimeZone!;
        if (!Language.IsNullOrEmpty()) headers[PrestoHeaders.Language(Protocol)] = Language!;
        if (!ClientTags.IsNullOrEmpty()) headers[PrestoHeaders.ClientTags(Protocol)] = ClientTags!;
        if (!ClientInfo.IsNullOrEmpty()) headers[PrestoHeaders.ClientInfo(Protocol)] = ClientInfo!;
        if (!ResourceGroup.IsNullOrEmpty()) headers[PrestoHeaders.ResourceGroup(Protocol)] = ResourceGroup!;
        if (!TraceToken.IsNullOrEmpty()) headers[PrestoHeaders.TraceToken(Protocol)] = TraceToken!;
        if (!Role.IsNullOrEmpty()) headers[PrestoHeaders.Role(Protocol)] = Role!;

        if (SessionProperties.Count > 0)
        {
            var builder = new StringBuilder();
            foreach (var item in SessionProperties)
            {
                if (builder.Length > 0) builder.Append(',');
                builder.Append(item.Key).Append('=').Append(item.Value);
            }
            headers[PrestoHeaders.Session(Protocol)] = builder.ToString();
        }

        foreach (var item in CustomHeaders)
        {
            headers[item.Key] = item.Value;
        }

        _headers = headers;

        return headers;
    }

    /// <summary>应用服务端返回的会话指令</summary>
    /// <param name="response">HTTP 响应</param>
    /// <returns>是否识别到会话指令</returns>
    public Boolean ApplyResponseHeaders(HttpResponseMessage response)
    {
        if (response == null) return false;

        var changed = false;
        var protocol = Protocol;

        var value = GetHeader(response, PrestoHeaders.SetCatalog(protocol));
        if (!value.IsNullOrEmpty())
        {
            Catalog = value;
            changed = true;
        }

        value = GetHeader(response, PrestoHeaders.SetSchema(protocol));
        if (!value.IsNullOrEmpty())
        {
            Schema = value;
            changed = true;
        }

        value = GetHeader(response, PrestoHeaders.SetRole(protocol));
        if (!value.IsNullOrEmpty())
        {
            Role = value;
            changed = true;
        }

        foreach (var item in GetHeaderValues(response, PrestoHeaders.SetSession(protocol)))
        {
            foreach (var pair in SplitValues(item))
            {
                var index = pair.IndexOf('=');
                if (index <= 0) continue;

                SessionProperties[pair[..index].Trim()] = pair[(index + 1)..].Trim();
                changed = true;
            }
        }

        foreach (var item in GetHeaderValues(response, PrestoHeaders.ClearSession(protocol)))
        {
            foreach (var name in SplitValues(item))
            {
                if (name.IsNullOrWhiteSpace()) continue;

                SessionProperties.Remove(name.Trim());
                changed = true;
            }
        }

        if (changed) _headers = null;

        return changed;
    }

    private static String? GetHeader(HttpResponseMessage response, String name)
    {
        if (!response.Headers.TryGetValues(name, out var values)) return null;

        foreach (var value in values)
        {
            if (!value.IsNullOrWhiteSpace()) return value.Trim();
        }

        return null;
    }

    private static IEnumerable<String> GetHeaderValues(HttpResponseMessage response, String name)
    {
        if (!response.Headers.TryGetValues(name, out var values)) yield break;

        foreach (var value in values)
        {
            if (!value.IsNullOrWhiteSpace()) yield return value;
        }
    }

    private static IEnumerable<String> SplitValues(String value)
    {
        foreach (var item in value.Split(','))
        {
            if (!item.IsNullOrWhiteSpace()) yield return item.Trim();
        }
    }
    #endregion
}
