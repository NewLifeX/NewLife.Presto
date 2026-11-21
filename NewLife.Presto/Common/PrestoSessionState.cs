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
    private readonly String _headerUser;
    private readonly String _headerCatalog;
    private readonly String _headerSchema;
    private readonly String _headerSource;
    private readonly String _headerTimeZone;
    private readonly String _headerLanguage;
    private readonly String _headerClientTags;
    private readonly String _headerClientInfo;
    private readonly String _headerResourceGroup;
    private readonly String _headerTraceToken;
    private readonly String _headerRole;
    private readonly String _headerSession;
    private readonly String _headerSetCatalog;
    private readonly String _headerSetSchema;
    private readonly String _headerSetRole;
    private readonly String _headerSetSession;
    private readonly String _headerClearSession;
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

        // 请求头名称与协议绑定，构造时算一次，避免每次请求/响应重复拼接
        _headerUser = PrestoHeaders.User(Protocol);
        _headerCatalog = PrestoHeaders.Catalog(Protocol);
        _headerSchema = PrestoHeaders.Schema(Protocol);
        _headerSource = PrestoHeaders.Source(Protocol);
        _headerTimeZone = PrestoHeaders.TimeZone(Protocol);
        _headerLanguage = PrestoHeaders.Language(Protocol);
        _headerClientTags = PrestoHeaders.ClientTags(Protocol);
        _headerClientInfo = PrestoHeaders.ClientInfo(Protocol);
        _headerResourceGroup = PrestoHeaders.ResourceGroup(Protocol);
        _headerTraceToken = PrestoHeaders.TraceToken(Protocol);
        _headerRole = PrestoHeaders.Role(Protocol);
        _headerSession = PrestoHeaders.Session(Protocol);
        _headerSetCatalog = PrestoHeaders.SetCatalog(Protocol);
        _headerSetSchema = PrestoHeaders.SetSchema(Protocol);
        _headerSetRole = PrestoHeaders.SetRole(Protocol);
        _headerSetSession = PrestoHeaders.SetSession(Protocol);
        _headerClearSession = PrestoHeaders.ClearSession(Protocol);
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
            [_headerUser] = User,
        };

        if (!Catalog.IsNullOrEmpty()) headers[_headerCatalog] = Catalog!;
        if (!Schema.IsNullOrEmpty()) headers[_headerSchema] = Schema!;
        if (!Source.IsNullOrEmpty()) headers[_headerSource] = Source!;
        if (!TimeZone.IsNullOrEmpty()) headers[_headerTimeZone] = TimeZone!;
        if (!Language.IsNullOrEmpty()) headers[_headerLanguage] = Language!;
        if (!ClientTags.IsNullOrEmpty()) headers[_headerClientTags] = ClientTags!;
        if (!ClientInfo.IsNullOrEmpty()) headers[_headerClientInfo] = ClientInfo!;
        if (!ResourceGroup.IsNullOrEmpty()) headers[_headerResourceGroup] = ResourceGroup!;
        if (!TraceToken.IsNullOrEmpty()) headers[_headerTraceToken] = TraceToken!;
        if (!Role.IsNullOrEmpty()) headers[_headerRole] = Role!;

        if (SessionProperties.Count > 0)
        {
            var builder = new StringBuilder();
            foreach (var item in SessionProperties)
            {
                if (builder.Length > 0) builder.Append(',');
                builder.Append(item.Key).Append('=').Append(item.Value);
            }
            headers[_headerSession] = builder.ToString();
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

        var value = GetHeader(response, _headerSetCatalog);
        if (!value.IsNullOrEmpty())
        {
            Catalog = value;
            changed = true;
        }

        value = GetHeader(response, _headerSetSchema);
        if (!value.IsNullOrEmpty())
        {
            Schema = value;
            changed = true;
        }

        value = GetHeader(response, _headerSetRole);
        if (!value.IsNullOrEmpty())
        {
            Role = value;
            changed = true;
        }

        foreach (var item in GetHeaderValues(response, _headerSetSession))
        {
            foreach (var pair in SplitValues(item))
            {
                var index = pair.IndexOf('=');
                if (index <= 0) continue;

                SessionProperties[pair[..index].Trim()] = pair[(index + 1)..].Trim();
                changed = true;
            }
        }

        foreach (var item in GetHeaderValues(response, _headerClearSession))
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
