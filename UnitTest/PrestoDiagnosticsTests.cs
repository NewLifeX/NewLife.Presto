using System;
using System.Collections.Generic;
using System.Diagnostics;
using NewLife.Presto;

namespace UnitTest;

/// <summary>诊断与脱敏测试</summary>
public class PrestoDiagnosticsTests
{
    [Fact(DisplayName = "连接串脱敏")]
    public void RedactConnectionString()
    {
        const String text = "Host=localhost;User=test;Password=p@ss;AccessToken=jwt-value";

        var redacted = PrestoDiagnostics.RedactConnectionString(text);

        Assert.DoesNotContain("p@ss", redacted);
        Assert.DoesNotContain("jwt-value", redacted);
        Assert.Contains("***", redacted);
        Assert.Contains("localhost", redacted);
    }

    [Fact(DisplayName = "脱敏边界")]
    public void RedactEdges()
    {
        Assert.Equal(String.Empty, PrestoDiagnostics.RedactConnectionString(null));
        Assert.Equal(String.Empty, PrestoDiagnostics.RedactConnectionString(""));
        Assert.Equal("***", PrestoDiagnostics.RedactConnectionString("not a connection string"));
    }

    [Fact(DisplayName = "截断诊断文本")]
    public void Truncate()
    {
        Assert.Equal(String.Empty, PrestoDiagnostics.Truncate(null, 10));
        Assert.Equal("abc", PrestoDiagnostics.Truncate("abc", 10));
        Assert.Equal("abc", PrestoDiagnostics.Truncate("abc", 0));
        Assert.Equal("ab...", PrestoDiagnostics.Truncate("abcdef", 2));
    }

    [Fact(DisplayName = "ActivitySource 输出执行跨度")]
    public void ActivitySpans()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PrestoDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var builder = new PrestoConnectionStringBuilder("Host=localhost;Port=9999;Catalog=hive;Protocol=Trino");
        var options = PrestoConnectionOptions.From(builder);

        using (var span = PrestoDiagnostics.StartOpen(options)) Assert.True(span.IsActive);
        using (var span = PrestoDiagnostics.StartExecute(options, "select 1"))
        {
            span.SetTag("custom", 1);
            span.SetError(new PrestoException("boom") { PrestoErrorCode = 47 });
        }
        using (var span = PrestoDiagnostics.StartFetch(options, new Uri("http://localhost/v1/statement/q1/1"))) Assert.True(span.IsActive);
        using (var span = PrestoDiagnostics.StartCancel(options)) Assert.True(span.IsActive);

        Assert.Equal(4, activities.Count);
        var execute = activities.Find(e => e.OperationName == "Execute")!;
        Assert.Equal("trino", execute.GetTagItem("db.system.name"));
        Assert.Equal("localhost", execute.GetTagItem("server.address"));
        Assert.Equal(9999, execute.GetTagItem("server.port"));
        Assert.Equal("hive", execute.GetTagItem("db.namespace"));
        Assert.Equal("select 1", execute.GetTagItem("db.query.text"));
        Assert.Equal(1, execute.GetTagItem("custom"));
        Assert.Equal("47", execute.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Error, execute.Status);

        var fetch = activities.Find(e => e.OperationName == "Fetch")!;
        Assert.Contains("v1/statement", (String)fetch.GetTagItem("url.full")!);
    }

    [Fact(DisplayName = "非错误异常标记类型")]
    public void ActivityNonPrestoError()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PrestoDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);

        var builder = new PrestoConnectionStringBuilder("Host=localhost");
        var options = PrestoConnectionOptions.From(builder);

        using (var span = PrestoDiagnostics.StartExecute(options, null))
        {
            span.SetError(new InvalidOperationException("bad"));
        }

        var activity = Assert.Single(activities);
        Assert.Equal(typeof(InvalidOperationException).FullName, activity.GetTagItem("error.type"));
        Assert.Null(activity.GetTagItem("db.query.text"));
    }
}
