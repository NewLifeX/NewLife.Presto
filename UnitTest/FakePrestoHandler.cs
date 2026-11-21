using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTest;

/// <summary>按队列返回响应的假 HTTP 处理器，记录请求方法与正文</summary>
public sealed class FakePrestoHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    /// <summary>收到的请求</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>收到的请求正文</summary>
    public List<String> Bodies { get; } = [];

    /// <summary>最后一次请求正文</summary>
    public String? LastBody => Bodies.Count > 0 ? Bodies[^1] : null;

    /// <summary>入队一个响应工厂</summary>
    public FakePrestoHandler Enqueue(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> factory)
    {
        _responses.Enqueue(factory);
        return this;
    }

    /// <summary>入队 JSON 响应</summary>
    public FakePrestoHandler EnqueueJson(String json, HttpStatusCode status = HttpStatusCode.OK, Action<HttpResponseMessage>? configure = null)
        => Enqueue((request, token) =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            configure?.Invoke(response);
            return Task.FromResult(response);
        });

    /// <summary>入队纯文本响应</summary>
    public FakePrestoHandler EnqueueText(String text, HttpStatusCode status = HttpStatusCode.OK)
        => Enqueue((request, token) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "text/plain") }));

    /// <summary>入队延迟 JSON 响应</summary>
    public FakePrestoHandler EnqueueDelay(TimeSpan delay, String json, HttpStatusCode status = HttpStatusCode.OK)
        => Enqueue(async (request, token) =>
        {
            await Task.Delay(delay, token).ConfigureAwait(false);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });

    /// <summary>入队一个会抛出异常的响应</summary>
    public FakePrestoHandler EnqueueException(Exception exception)
        => Enqueue((request, token) => Task.FromException<HttpResponseMessage>(exception));

    /// <inheritdoc />
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        => SendAsync(request, cancellationToken).GetAwaiter().GetResult();

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? String.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Requests.Add(request);
        Bodies.Add(body);

        if (_responses.Count == 0) throw new InvalidOperationException("没有可用的假响应");

        return await _responses.Dequeue()(request, cancellationToken).ConfigureAwait(false);
    }
}
