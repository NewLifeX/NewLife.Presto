using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NewLife.Log;

namespace NewLife.Presto;

/// <summary>语句客户端</summary>
/// <remarks>
/// 负责 `/v1/statement` 提交、`nextUri` 轮询、结果页解析与 `DELETE` 取消。
/// 提交请求不自动重试；轮询请求对 429/502/503/504 按重试策略处理。
/// </remarks>
internal sealed class PrestoStatementClient
{
    #region 字段
    private readonly HttpClient _client;
    private readonly PrestoConnectionOptions _options;
    private readonly PrestoSessionState _session;
    private readonly PrestoRetryPolicy _retry;
    private readonly ILog? _log;
    private readonly Uri _submitUri;
    private String? _queryId;
    private String? _infoUri;
    private String? _nextUri;
    private Boolean _completed;
    private String? _basicAuthorization;
    private Task<String?>? _basicAuthTask;
    private readonly PrestoBearerCache _bearerCache = new();
    private Boolean _syncUnsupported;
    private Boolean _finished;
    private static readonly String _userAgent = "NewLife.Presto/" + PrestoDriver.Version;
    #endregion

    #region 属性
    /// <summary>JWT 令牌刷新回调</summary>
    public Func<String>? AccessTokenProvider { get; set; }

    /// <summary>OAuth2 令牌提供者</summary>
    public PrestoOAuthTokenProvider? OAuthTokenProvider { get; set; }

    /// <summary>当前查询编号</summary>
    public String? QueryId => _queryId;

    /// <summary>当前轮询地址</summary>
    public String? NextUri => _nextUri;

    /// <summary>查询是否已结束</summary>
    public Boolean IsCompleted => _completed;
    #endregion

    #region 构造
    /// <summary>实例化语句客户端</summary>
    /// <param name="client">HTTP 客户端</param>
    /// <param name="options">连接配置</param>
    /// <param name="session">会话状态</param>
    /// <param name="log">日志</param>
    public PrestoStatementClient(HttpClient client, PrestoConnectionOptions options, PrestoSessionState session, ILog? log = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _retry = options.RetryPolicy;
        _submitUri = options.SubmitUri;
        _log = log;
    }
    #endregion

    #region 提交与轮询
    /// <summary>提交 SQL 语句</summary>
    /// <param name="sql">SQL 文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>首个结果页</returns>
    public async Task<PrestoQueryResults> SubmitAsync(String sql, CancellationToken cancellationToken)
    {
        var uri = _submitUri;
        using var span = PrestoDiagnostics.StartExecute(_options, sql);
        try
        {
            var authorization = await GetAuthorizationAsync(cancellationToken).ConfigureAwait(false);
            using var request = CreateRequestCore(HttpMethod.Post, uri, authorization);
            request.Content = new StringContent(sql, Encoding.UTF8, "text/plain");

            using var response = await SendOnceAsync(request, cancellationToken).ConfigureAwait(false);
            using var body = await PrestoResponseBody.ReadAsync(response, cancellationToken).ConfigureAwait(false);
            _session.ApplyResponseHeaders(response);

            if (!response.IsSuccessStatusCode) throw CreateHttpException(uri, response, body.GetText());

            var results = body.ParseResults(_options.ComplexTypes == PrestoComplexType.Structured, _queryId, _infoUri);
            await ResolveSegmentsAsync(results, cancellationToken).ConfigureAwait(false);
            Apply(results);

            if (results.Error != null) throw CreateQueryException(results);

            return results;
        }
        catch (Exception ex)
        {
            span.SetError(ex);
            throw;
        }
    }

    /// <summary>拉取下一页</summary>
    /// <param name="nextUri">下一页地址</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>结果页</returns>
    public async Task<PrestoQueryResults> FetchAsync(String nextUri, CancellationToken cancellationToken)
    {
        var uri = ResolveUri(nextUri);
        using var span = PrestoDiagnostics.StartFetch(_options, uri);
        try
        {
            var attempt = 0;
            while (true)
            {
                var authorization = await GetAuthorizationAsync(cancellationToken).ConfigureAwait(false);
                using var request = CreateRequestCore(HttpMethod.Get, uri, authorization);
                using var response = await SendOnceAsync(request, cancellationToken).ConfigureAwait(false);

                var status = (Int32)response.StatusCode;
                if (PrestoRetryPolicy.IsTransient(status) && attempt < _retry.MaxRetries)
                {
                    var delay = _retry.GetDelay(response, attempt + 1);
                    _session.ApplyResponseHeaders(response);
                    attempt++;
                    _log?.Debug("Retrying request after HTTP {0}; attempt {1} of {2}.", status, attempt, _retry.MaxRetries);

                    if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                using var body = await PrestoResponseBody.ReadAsync(response, cancellationToken).ConfigureAwait(false);
                _session.ApplyResponseHeaders(response);

                if (!response.IsSuccessStatusCode) throw CreateHttpException(uri, response, body.GetText());

                var results = body.ParseResults(_options.ComplexTypes == PrestoComplexType.Structured, _queryId, _infoUri);
                await ResolveSegmentsAsync(results, cancellationToken).ConfigureAwait(false);
                Apply(results);

                if (results.Error != null) throw CreateQueryException(results);

                return results;
            }
        }
        catch (Exception ex)
        {
            span.SetError(ex);
            throw;
        }
    }

    /// <summary>同步提交 SQL 语句</summary>
    /// <param name="sql">SQL 文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>首个结果页</returns>
    public PrestoQueryResults Submit(String sql, CancellationToken cancellationToken)
    {
        if (!_syncUnsupported)
        {
            try
            {
                return SubmitSync(sql, cancellationToken);
            }
            catch (NotSupportedException)
            {
                // 自定义 HttpMessageHandler 未实现同步 Send，后续统一回退异步通道
                _syncUnsupported = true;
            }
        }

        return SubmitAsync(sql, cancellationToken).GetAwaiter().GetResult();
    }

    private PrestoQueryResults SubmitSync(String sql, CancellationToken cancellationToken)
    {
        var uri = _submitUri;
        using var span = PrestoDiagnostics.StartExecute(_options, sql);
        try
        {
            using var request = CreateRequest(HttpMethod.Post, uri, cancellationToken);
            request.Content = new StringContent(sql, Encoding.UTF8, "text/plain");

            using var response = SendOnce(request, cancellationToken);
            using var body = PrestoResponseBody.Read(response, cancellationToken);
            _session.ApplyResponseHeaders(response);

            if (!response.IsSuccessStatusCode) throw CreateHttpException(uri, response, body.GetText());

            var results = body.ParseResults(_options.ComplexTypes == PrestoComplexType.Structured, _queryId, _infoUri);
            ResolveSegments(results, cancellationToken);
            Apply(results);

            if (results.Error != null) throw CreateQueryException(results);

            return results;
        }
        catch (Exception ex)
        {
            span.SetError(ex);
            throw;
        }
    }

    /// <summary>同步拉取下一页</summary>
    /// <param name="nextUri">下一页地址</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>结果页</returns>
    public PrestoQueryResults Fetch(String nextUri, CancellationToken cancellationToken)
    {
        if (!_syncUnsupported)
        {
            try
            {
                return FetchSync(nextUri, cancellationToken);
            }
            catch (NotSupportedException)
            {
                _syncUnsupported = true;
            }
        }

        return FetchAsync(nextUri, cancellationToken).GetAwaiter().GetResult();
    }

    private PrestoQueryResults FetchSync(String nextUri, CancellationToken cancellationToken)
    {
        var uri = ResolveUri(nextUri);
        using var span = PrestoDiagnostics.StartFetch(_options, uri);
        try
        {
            var attempt = 0;
            while (true)
            {
                using var request = CreateRequest(HttpMethod.Get, uri, cancellationToken);
                using var response = SendOnce(request, cancellationToken);

                var status = (Int32)response.StatusCode;
                if (PrestoRetryPolicy.IsTransient(status) && attempt < _retry.MaxRetries)
                {
                    var delay = _retry.GetDelay(response, attempt + 1);
                    _session.ApplyResponseHeaders(response);
                    attempt++;
                    _log?.Debug("Retrying request after HTTP {0}; attempt {1} of {2}.", status, attempt, _retry.MaxRetries);

                    if (delay > TimeSpan.Zero) Task.Delay(delay, cancellationToken).GetAwaiter().GetResult();
                    continue;
                }

                using var body = PrestoResponseBody.Read(response, cancellationToken);
                _session.ApplyResponseHeaders(response);

                if (!response.IsSuccessStatusCode) throw CreateHttpException(uri, response, body.GetText());

                var results = body.ParseResults(_options.ComplexTypes == PrestoComplexType.Structured, _queryId, _infoUri);
                ResolveSegments(results, cancellationToken);
                Apply(results);

                if (results.Error != null) throw CreateQueryException(results);

                return results;
            }
        }
        catch (Exception ex)
        {
            span.SetError(ex);
            throw;
        }
    }

    /// <summary>获取服务端信息</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务端信息</returns>
    public async Task<PrestoServerInfo> GetServerInfoAsync(CancellationToken cancellationToken)
    {
        var uri = new Uri(_options.BaseAddress, "v1/info");
        using var span = PrestoDiagnostics.StartFetch(_options, uri);
        try
        {
            var body = await GetBodyAsync(uri, cancellationToken).ConfigureAwait(false);

            return PrestoJson.ParseServerInfo(body);
        }
        catch (Exception ex)
        {
            span.SetError(ex);
            throw;
        }
    }

    /// <summary>获取查询列表</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>查询信息列表</returns>
    public async Task<IList<PrestoQueryInfo>> GetQueriesAsync(CancellationToken cancellationToken)
    {
        var uri = new Uri(_options.BaseAddress, "v1/query");
        using var span = PrestoDiagnostics.StartFetch(_options, uri);
        try
        {
            var body = await GetBodyAsync(uri, cancellationToken).ConfigureAwait(false);
            var items = PrestoJson.ParseArray(body);

            var list = new List<PrestoQueryInfo>();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item == null) continue;

                    list.Add(PrestoJson.ParseQueryInfo(item));
                }
            }

            return list;
        }
        catch (Exception ex)
        {
            span.SetError(ex);
            throw;
        }
    }

    /// <summary>获取单个查询详情</summary>
    /// <param name="queryId">查询编号</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>查询信息</returns>
    public async Task<PrestoQueryInfo> GetQueryInfoAsync(String queryId, CancellationToken cancellationToken)
    {
        if (queryId.IsNullOrWhiteSpace()) throw new ArgumentException(Localization.Translate("Query id cannot be empty."), nameof(queryId));

        var uri = new Uri(_options.BaseAddress, "v1/query/" + Uri.EscapeDataString(queryId));
        using var span = PrestoDiagnostics.StartFetch(_options, uri);
        try
        {
            var body = await GetBodyAsync(uri, cancellationToken).ConfigureAwait(false);

            return PrestoJson.ParseQueryInfo(PrestoJson.ParseObject(body));
        }
        catch (Exception ex)
        {
            span.SetError(ex);
            throw;
        }
    }

    /// <summary>获取节点列表</summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>节点信息列表</returns>
    /// <remarks>Trino 返回节点数组，PrestoDB 返回单个节点对象；两种形态都归一化为列表</remarks>
    public async Task<IList<PrestoServerInfo>> GetNodesAsync(CancellationToken cancellationToken)
    {
        var uri = new Uri(_options.BaseAddress, "v1/node");
        using var span = PrestoDiagnostics.StartFetch(_options, uri);
        try
        {
            var body = await GetBodyAsync(uri, cancellationToken).ConfigureAwait(false);

            var list = new List<PrestoServerInfo>();
            var items = PrestoJson.ParseArray(body);
            if (items == null)
            {
                list.Add(PrestoJson.ParseServerInfo(body));
                return list;
            }

            foreach (var item in items)
            {
                if (item == null) continue;

                list.Add(PrestoJson.ParseServerInfo(item));
            }

            return list;
        }
        catch (Exception ex)
        {
            span.SetError(ex);
            throw;
        }
    }
    #endregion

    #region Spooling
    /// <summary>把 Spooling 分段解析为行，按分段顺序追加到当前页</summary>
    private async Task ResolveSegmentsAsync(PrestoQueryResults results, CancellationToken cancellationToken)
    {
        var segments = results.Segments;
        if (segments.Count == 0) return;

        if (!PrestoSpooling.IsSupported(results.DataEncoding))
            throw new PrestoException(Localization.Translate("Unsupported query data encoding '{0}'. Only uncompressed JSON is supported.", results.DataEncoding ?? String.Empty));

        foreach (var segment in segments)
        {
            if (segment.Type.EqualIgnoreCase(PrestoSpooling.InlineType))
            {
                foreach (var row in PrestoSpooling.DecodeInline(segment)) results.Data.Add(row);
            }
            else if (segment.Type.EqualIgnoreCase(PrestoSpooling.SpooledType))
            {
                if (segment.Uri.IsNullOrEmpty()) throw PrestoSpooling.InvalidSegment(segment.Type);

                var rows = await DownloadSegmentAsync(segment, cancellationToken).ConfigureAwait(false);
                foreach (var row in rows) results.Data.Add(row);

                await AcknowledgeSegmentAsync(segment).ConfigureAwait(false);
            }
            else
                throw PrestoSpooling.InvalidSegment(segment.Type);
        }
    }

    /// <summary>从对象存储下载分段；只携带分段自带的请求头，不带会话头与认证头</summary>
    private async Task<IList<IList<Object?>>> DownloadSegmentAsync(PrestoQuerySegment segment, CancellationToken cancellationToken)
    {
        var uri = ResolveUri(segment.Uri!);
        using var request = CreateSegmentRequest(HttpMethod.Get, uri, segment);
        using var response = await SendOnceAsync(request, cancellationToken).ConfigureAwait(false);

        using var body = await PrestoResponseBody.ReadAsync(response, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw CreateHttpException(uri, response, body.GetText());

        return body.ParseRows();
    }

    /// <summary>确认分段已读取，失败只记录调试日志；服务端 TTL 会兜底清理</summary>
    private async Task AcknowledgeSegmentAsync(PrestoQuerySegment segment)
    {
        if (segment.AckUri.IsNullOrEmpty()) return;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var request = CreateSegmentRequest(HttpMethod.Get, ResolveUri(segment.AckUri!), segment);
            using var response = await _client.SendAsync(request, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.Debug("Failed to acknowledge the spooled segment: {0}", ex.Message);
        }
    }

    private static HttpRequestMessage CreateSegmentRequest(HttpMethod method, Uri uri, PrestoQuerySegment segment)
    {
        var request = new HttpRequestMessage(method, uri);
        foreach (var item in segment.Headers)
        {
            foreach (var value in item.Value)
            {
                request.Headers.TryAddWithoutValidation(item.Key, value);
            }
        }
        if (!segment.Headers.ContainsKey("Accept-Encoding")) request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");

        return request;
    }
    #endregion

    #region 取消
    /// <summary>尽力取消服务端查询</summary>
    /// <returns>异步任务</returns>
    public async Task CancelAsync()
    {
        var next = _nextUri;
        if (next.IsNullOrEmpty()) return;

        // 服务端已进入终态（数据页常见 FINISHED + nextUri）时无需再发 DELETE，省掉一次往返与分配
        if (_finished) return;

        _nextUri = null;
        using var span = PrestoDiagnostics.StartCancel(_options);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var authorization = await GetAuthorizationAsync(cts.Token).ConfigureAwait(false);
            using var request = CreateRequestCore(HttpMethod.Delete, ResolveUri(next!), authorization);
            using var response = await _client.SendAsync(request, cts.Token).ConfigureAwait(false);
            _session.ApplyResponseHeaders(response);
        }
        catch (Exception ex)
        {
            _log?.Debug("Failed to cancel the query: {0}", ex.Message);
            span.SetError(ex);
        }
    }

    /// <summary>标记查询结束</summary>
    public void MarkCompleted()
    {
        _nextUri = null;
        _completed = true;
    }
    #endregion

    #region 辅助
    private Task<String?> GetAuthorizationAsync(CancellationToken cancellationToken)
    {
        // Basic 认证头只与连接凭据有关，按语句客户端缓存，避免每次请求重复 Base64
        if (_options.Auth == PrestoAuth.Basic) return _basicAuthTask ??= Task.FromResult<String?>(_basicAuthorization ??= PrestoAuthProvider.BuildBasic(_options));

        // JWT 与显式令牌回调每请求都会重新读取令牌，但令牌通常不变，按令牌值缓存认证头
        if (TryGetBearerToken(out var token)) return GetBearerTask(token);

        return PrestoAuthProvider.GetAuthorizationAsync(_options, AccessTokenProvider, OAuthTokenProvider, cancellationToken);
    }

    private String? GetAuthorization(CancellationToken cancellationToken)
    {
        // Basic 认证头只与连接凭据有关，按语句客户端缓存，避免每次请求重复 Base64
        if (_options.Auth == PrestoAuth.Basic) return _basicAuthorization ??= PrestoAuthProvider.BuildBasic(_options);

        // JWT 与显式令牌回调每请求都会重新读取令牌，但令牌通常不变，按令牌值缓存认证头
        if (TryGetBearerToken(out var token)) return GetBearerTask(token).GetAwaiter().GetResult();

        // OAuth2 可能需要在令牌端点做异步请求，同步路径阻塞等待
        if (_options.Auth == PrestoAuth.OAuth2)
            return PrestoAuthProvider.GetAuthorizationAsync(_options, AccessTokenProvider, OAuthTokenProvider, cancellationToken).GetAwaiter().GetResult();

        return PrestoAuthProvider.GetAuthorization(_options, AccessTokenProvider);
    }

    /// <summary>取出可缓存的 Bearer 令牌；缺失令牌时交给 <see cref="PrestoAuthProvider"/> 统一报错</summary>
    private Boolean TryGetBearerToken(out String token)
    {
        token = null!;

        var auth = _options.Auth;
        if (auth != PrestoAuth.Jwt && auth != PrestoAuth.OAuth2) return false;

        var value = AccessTokenProvider?.Invoke();
        if (value.IsNullOrEmpty() && auth == PrestoAuth.Jwt) value = _options.AccessToken;
        if (value.IsNullOrEmpty()) return false;

        token = value;
        return true;
    }

    /// <summary>按令牌值缓存 Bearer 认证头。长令牌拼接一次约 600 字节，单次查询要发 5~6 个请求，不能每请求重拼</summary>
    private Task<String?> GetBearerTask(String token) => _bearerCache.Get(token);

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, CancellationToken cancellationToken)
        => CreateRequestCore(method, uri, GetAuthorization(cancellationToken));

    private HttpRequestMessage CreateRequestCore(HttpMethod method, Uri uri, String? authorization)
    {
        var request = new HttpRequestMessage(method, uri);

        foreach (var item in _session.BuildHeaders())
        {
            request.Headers.TryAddWithoutValidation(item.Key, item.Value);
        }

        if (!authorization.IsNullOrEmpty()) request.Headers.TryAddWithoutValidation("Authorization", authorization);

        request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (_options.Compression) request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
        if (_options.Spooling && method == HttpMethod.Post)
            request.Headers.TryAddWithoutValidation(PrestoHeaders.QueryDataEncoding(_options.Protocol), PrestoSpooling.SupportedEncodings);

        return request;
    }

    /// <summary>同步发送请求；现代目标走真同步通道，低版本框架桥接异步</summary>
    private HttpResponseMessage SendOnce(HttpRequestMessage request, CancellationToken cancellationToken)
    {
#if NET6_0_OR_GREATER
        try
        {
            return _client.Send(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient 自身超时（未收到响应头）时转成连接超时
            throw PrestoException.ConnectionTimeout(_options.ConnectionTimeout, ex);
        }
#else
        return SendOnceAsync(request, cancellationToken).GetAwaiter().GetResult();
#endif
    }

    private void ResolveSegments(PrestoQueryResults results, CancellationToken cancellationToken)
    {
        if (results.Segments.Count == 0) return;

        // Spooling 分段下载保持异步实现，同步路径只在该少见分支阻塞等待
        ResolveSegmentsAsync(results, cancellationToken).GetAwaiter().GetResult();
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient 自身超时（未收到响应头）时转成连接超时
            throw PrestoException.ConnectionTimeout(_options.ConnectionTimeout, ex);
        }
    }

    /// <summary>发送只读 GET 请求，瞬态错误按策略重试</summary>
    private async Task<String> GetBodyAsync(Uri uri, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            var authorization = await GetAuthorizationAsync(cancellationToken).ConfigureAwait(false);
            using var request = CreateRequestCore(HttpMethod.Get, uri, authorization);
            using var response = await SendOnceAsync(request, cancellationToken).ConfigureAwait(false);

            var status = (Int32)response.StatusCode;
            if (PrestoRetryPolicy.IsTransient(status) && attempt < _retry.MaxRetries)
            {
                var delay = _retry.GetDelay(response, attempt + 1);
                _session.ApplyResponseHeaders(response);
                attempt++;
                _log?.Debug("Retrying request after HTTP {0}; attempt {1} of {2}.", status, attempt, _retry.MaxRetries);

                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            _session.ApplyResponseHeaders(response);

            if (!response.IsSuccessStatusCode) throw CreateHttpException(uri, response, body);

            return body;
        }
    }

    private static async Task<String> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content == null) return String.Empty;

#if NET6_0_OR_GREATER
        using var body = await PrestoResponseBody.ReadAsync(response, cancellationToken).ConfigureAwait(false);
        return body.GetText();
#else
        _ = cancellationToken;
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
    }

    private Uri ResolveUri(String nextUri)
    {
        // Unix 下 Uri.TryCreate("/path", Absolute) 会给出 file:// 绝对地址，因此只把 http(s) 视为绝对地址
        if (Uri.TryCreate(nextUri, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) return uri;

        return new Uri(_options.BaseAddress, nextUri);
    }

    private void Apply(PrestoQueryResults results)
    {
        if (!results.Id.IsNullOrEmpty()) _queryId = results.Id;
        if (!results.InfoUri.IsNullOrEmpty()) _infoUri = results.InfoUri;
        _nextUri = results.NextUri;
        _completed = results.NextUri.IsNullOrEmpty();

        var state = results.Stats?.State;
        if (state != null && IsTerminalState(state)) _finished = true;
    }

    private static Boolean IsTerminalState(String state)
        => state.Equals(PrestoStates.Finished, StringComparison.OrdinalIgnoreCase)
        || state.Equals(PrestoStates.Failed, StringComparison.OrdinalIgnoreCase)
        || state.Equals(PrestoStates.Canceled, StringComparison.OrdinalIgnoreCase)
        || state.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase);

    private PrestoException CreateHttpException(Uri uri, HttpResponseMessage response, String body)
    {
        var status = (Int32)response.StatusCode;
        PrestoQueryError? error = null;
        try
        {
            var dic = PrestoJson.ParseObject(body);
            if (dic != null && dic.TryGetValue("error", out var value)) error = PrestoJson.ParseError(value);
        }
        catch (PrestoException)
        {
            // 非 JSON 错误页：保留下面的通用 HTTP 异常
        }

        PrestoException ex;
        if (error != null)
            ex = PrestoException.QueryFailed(error, _queryId, status);
        else
        {
            var message = Localization.Translate("Request to '{0}' failed with HTTP {1}.", uri, status);
            var text = PrestoDiagnostics.Truncate(body, 1024);
            if (!text.IsNullOrEmpty()) message += " " + text;

            ex = new PrestoException(message) { HttpStatusCode = status };
        }

        ex.Data["response"] = PrestoDiagnostics.Truncate(body, 4096);
        return ex;
    }

    private PrestoException CreateQueryException(PrestoQueryResults results)
    {
        var error = results.Error!;

        return PrestoException.QueryFailed(error, results.Id ?? _queryId, null);
    }
    #endregion
}
