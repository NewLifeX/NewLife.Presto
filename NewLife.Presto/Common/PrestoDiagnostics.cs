using System.Data.Common;
#if NET6_0_OR_GREATER
using System.Diagnostics;
#endif

namespace NewLife.Presto;

/// <summary>驱动诊断埋点与脱敏工具</summary>
/// <remarks>
/// `net6.0+` 通过 <c>System.Diagnostics.ActivitySource</c> 输出 Open/Execute/Fetch/Cancel 跨度，
/// 订阅名称为 `NewLife.Presto`；低版本框架为空实现，不引入额外依赖。
/// </remarks>
internal static class PrestoDiagnostics
{
    #region 属性
    /// <summary>ActivitySource 名称</summary>
    public const String SourceName = "NewLife.Presto";

#if NET6_0_OR_GREATER
    private static readonly ActivitySource _source = new(SourceName, typeof(PrestoDiagnostics).Assembly.GetName().Version!.ToString());
#endif
    #endregion

    #region 跨度
    /// <summary>开始连接打开跨度</summary>
    /// <param name="options">连接配置</param>
    /// <returns>跨度句柄</returns>
    public static PrestoActivity StartOpen(PrestoConnectionOptions options) => Start("Open", options);

    /// <summary>开始命令执行跨度</summary>
    /// <param name="options">连接配置</param>
    /// <param name="sql">SQL 文本</param>
    /// <returns>跨度句柄</returns>
    public static PrestoActivity StartExecute(PrestoConnectionOptions options, String? sql)
    {
        var span = Start("Execute", options);
        if (span.IsActive && !sql.IsNullOrEmpty()) span.SetTag("db.query.text", sql);
        return span;
    }

    /// <summary>开始结果页拉取跨度</summary>
    /// <param name="options">连接配置</param>
    /// <param name="uri">下一页地址</param>
    /// <returns>跨度句柄</returns>
    public static PrestoActivity StartFetch(PrestoConnectionOptions options, Uri? uri)
    {
        var span = Start("Fetch", options);
        if (span.IsActive && uri != null) span.SetTag("url.full", uri.ToString());
        return span;
    }

    /// <summary>开始取消跨度</summary>
    /// <param name="options">连接配置</param>
    /// <returns>跨度句柄</returns>
    public static PrestoActivity StartCancel(PrestoConnectionOptions options) => Start("Cancel", options);

    private static PrestoActivity Start(String name, PrestoConnectionOptions options)
    {
#if NET6_0_OR_GREATER
        var activity = _source.StartActivity(name, ActivityKind.Client);
        if (activity == null) return default;

        activity.SetTag("db.system.name", options.Protocol == PrestoProtocol.Trino ? "trino" : "presto");
        if (!options.Host.IsNullOrEmpty()) activity.SetTag("server.address", options.Host);
        if (options.Port != 8080) activity.SetTag("server.port", options.Port);
        if (!options.Catalog.IsNullOrEmpty()) activity.SetTag("db.namespace", options.Catalog);

        return new PrestoActivity(activity);
#else
        _ = name;
        _ = options;
        return default;
#endif
    }
    #endregion

    #region 脱敏
    /// <summary>脱敏连接字符串中的密码与令牌</summary>
    /// <param name="connectionString">原始连接字符串</param>
    /// <returns>可安全写入日志的连接字符串</returns>
    public static String RedactConnectionString(String? connectionString)
    {
        if (connectionString.IsNullOrEmpty()) return connectionString ?? String.Empty;

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            foreach (var key in new[] { "Password", "Pwd", "AccessToken", "Token" })
            {
                if (builder.ContainsKey(key)) builder[key] = "***";
            }

            return builder.ConnectionString;
        }
        catch
        {
            // 无法解析时宁可不输出原文
            return "***";
        }
    }

    /// <summary>截断诊断文本，避免超长响应进入异常或日志</summary>
    /// <param name="text">原始文本</param>
    /// <param name="maxLength">最大长度</param>
    /// <returns>截断后的文本</returns>
    public static String Truncate(String? text, Int32 maxLength)
    {
        if (text.IsNullOrEmpty()) return String.Empty;
        if (maxLength <= 0 || text.Length <= maxLength) return text;

        return text[..maxLength] + "...";
    }
    #endregion
}

/// <summary>跨度句柄</summary>
internal readonly struct PrestoActivity : IDisposable
{
#if NET6_0_OR_GREATER
    private readonly Activity? _activity;

    /// <summary>实例化跨度句柄</summary>
    /// <param name="activity">底层活动</param>
    public PrestoActivity(Activity? activity) => _activity = activity;

    /// <summary>是否已采集</summary>
    public Boolean IsActive => _activity != null;

    /// <summary>设置标签</summary>
    /// <param name="key">键</param>
    /// <param name="value">值</param>
    public void SetTag(String key, Object? value) => _activity?.SetTag(key, value);

    /// <summary>标记异常</summary>
    /// <param name="exception">异常</param>
    public void SetError(Exception exception)
    {
        var activity = _activity;
        if (activity == null) return;

        if (exception is PrestoException presto && presto.PrestoErrorCode > 0)
        {
            var code = presto.PrestoErrorCode.ToString();
            activity.SetTag("error.type", code);
            activity.SetTag("db.response.status_code", code);
        }
        else
            activity.SetTag("error.type", exception.GetType().FullName);

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            ["exception.type"] = exception.GetType().FullName,
            ["exception.message"] = exception.Message,
            ["exception.stacktrace"] = exception.ToString(),
        }));
    }

    /// <summary>结束跨度</summary>
    public void Dispose() => _activity?.Dispose();
#else
    /// <summary>是否已采集</summary>
    public Boolean IsActive => false;

    /// <summary>设置标签</summary>
    public void SetTag(String key, Object? value) { }

    /// <summary>标记异常</summary>
    public void SetError(Exception exception) { }

    /// <summary>结束跨度</summary>
    public void Dispose() { }
#endif
}
