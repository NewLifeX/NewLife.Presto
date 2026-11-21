using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using NewLife.Presto;

namespace UnitTest;

/// <summary>重试策略测试</summary>
public class PrestoRetryPolicyTests
{
    [Theory(DisplayName = "瞬态状态判定")]
    [InlineData(429, true)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(504, true)]
    [InlineData(500, false)]
    [InlineData(404, false)]
    [InlineData(200, false)]
    public void IsTransientStatus(Int32 status, Boolean expected)
    {
        Assert.Equal(expected, PrestoRetryPolicy.IsTransient(status));
        Assert.Equal(expected, PrestoRetryPolicy.IsTransient((HttpStatusCode)status));
    }

    [Fact(DisplayName = "负数参数归零")]
    public void NegativeValuesClamp()
    {
        var policy = new PrestoRetryPolicy(-1, -1);

        Assert.Equal(0, policy.MaxRetries);
        Assert.Equal(0, policy.Interval);
        Assert.Equal(TimeSpan.Zero, policy.GetDelay(null, 1));
    }

    [Fact(DisplayName = "指数退避并带抖动")]
    public void ExponentialBackoff()
    {
        var policy = new PrestoRetryPolicy(3, 200);

        var first = policy.GetDelay(null, 1);
        Assert.InRange(first.TotalMilliseconds, 160, 240);

        var third = policy.GetDelay(null, 3);
        Assert.InRange(third.TotalMilliseconds, 640, 960);

        var capped = policy.GetDelay(null, 20);
        Assert.InRange(capped.TotalMilliseconds, 24000, 36000);
    }

    [Fact(DisplayName = "优先使用 Retry-After 秒数")]
    public void RetryAfterDelta()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));

        var policy = new PrestoRetryPolicy(3, 200);

        Assert.Equal(TimeSpan.FromSeconds(5), policy.GetDelay(response, 1));
    }

    [Fact(DisplayName = "支持 Retry-After HTTP 日期")]
    public void RetryAfterDate()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(5));

        var policy = new PrestoRetryPolicy(3, 200);
        var delay = policy.GetDelay(response, 1);

        Assert.InRange(delay.TotalSeconds, 0, 6);
    }

    [Fact(DisplayName = "过期的 Retry-After 日期返回零")]
    public void RetryAfterPastDate()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(-5));

        var policy = new PrestoRetryPolicy(3, 200);

        Assert.Equal(TimeSpan.Zero, policy.GetDelay(response, 1));
    }
}
