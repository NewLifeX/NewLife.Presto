using System.Net;
using System.Net.Http;

namespace NewLife.Presto;

/// <summary>瞬态错误重试策略</summary>
/// <remarks>
/// 仅对 429/502/503/504 的 GET/轮询请求重试；POST 提交不自动重试，避免服务端已接收查询时重复执行。
/// 优先使用服务端 `Retry-After`（秒数或 HTTP 日期），否则按初始间隔指数退避并加入抖动。
/// </remarks>
internal sealed class PrestoRetryPolicy
{
    #region 属性
    private const Int32 MaxDelayMillis = 30000;
    private static readonly Random _random = new();

    /// <summary>最大重试次数</summary>
    public Int32 MaxRetries { get; }

    /// <summary>初始重试间隔毫秒数</summary>
    public Int32 Interval { get; }
    #endregion

    #region 构造
    /// <summary>实例化重试策略</summary>
    /// <param name="maxRetries">最大重试次数</param>
    /// <param name="interval">初始间隔毫秒数</param>
    public PrestoRetryPolicy(Int32 maxRetries, Int32 interval)
    {
        MaxRetries = maxRetries < 0 ? 0 : maxRetries;
        Interval = interval < 0 ? 0 : interval;
    }
    #endregion

    #region 方法
    /// <summary>判断 HTTP 状态是否为可重试的瞬态错误</summary>
    /// <param name="statusCode">HTTP 状态码</param>
    /// <returns>是否瞬态</returns>
    public static Boolean IsTransient(Int32 statusCode) => statusCode is 429 or 502 or 503 or 504;

    /// <summary>判断 HTTP 状态是否为可重试的瞬态错误</summary>
    /// <param name="statusCode">HTTP 状态</param>
    /// <returns>是否瞬态</returns>
    public static Boolean IsTransient(HttpStatusCode statusCode) => IsTransient((Int32)statusCode);

    /// <summary>计算下一次重试前等待时间</summary>
    /// <param name="response">上一次响应，可为空</param>
    /// <param name="attempt">第几次重试，从 1 开始</param>
    /// <returns>等待时间</returns>
    public TimeSpan GetDelay(HttpResponseMessage? response, Int32 attempt)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter != null)
        {
            if (retryAfter.Delta.HasValue) return retryAfter.Delta.Value;

            if (retryAfter.Date.HasValue)
            {
                var delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
            }
        }

        if (Interval <= 0) return TimeSpan.Zero;

        var millis = Interval * Math.Pow(2, Math.Max(0, attempt - 1));
        if (millis > MaxDelayMillis) millis = MaxDelayMillis;

        // 抖动 80%~120%，避免大量客户端同时重试
        millis *= 0.8 + NextDouble() * 0.4;

        return TimeSpan.FromMilliseconds(millis);
    }

    private static Double NextDouble()
    {
        lock (_random) return _random.NextDouble();
    }
    #endregion
}
