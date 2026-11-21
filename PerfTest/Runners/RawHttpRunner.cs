using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PerfTest.Runners;

/// <summary>原始 HTTP v1 协议基线，用于衡量客户端库额外引入的开销</summary>
public sealed class RawHttpRunner : IRunner
{
    private readonly String _host;
    private readonly Int32 _port;
    private readonly String _user;
    private readonly String _catalog;
    private readonly String _schema;
    private readonly String _headerPrefix;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly List<String> _columnTypes = [];

    /// <summary>以端点信息构造</summary>
    /// <param name="host">主机</param>
    /// <param name="port">端口</param>
    /// <param name="user">用户名</param>
    /// <param name="catalog">Catalog</param>
    /// <param name="schema">Schema</param>
    /// <param name="isPresto">是否按 PrestoDB 协议头访问</param>
    public RawHttpRunner(String host, Int32 port, String user, String catalog, String schema, Boolean isPresto)
    {
        _host = host;
        _port = port;
        _user = user;
        _catalog = catalog;
        _schema = schema;
        _headerPrefix = isPresto ? "X-Presto-" : "X-Trino-";
    }

    /// <summary>显示名称</summary>
    public String Name => "原始 HTTP v1";

    /// <summary>客户端类别</summary>
    public String Category => "协议基线";

    /// <summary>目标端点描述</summary>
    public String Target => $"{_host}:{_port}";

    /// <summary>读取到的数据校验和</summary>
    public Int64 Checksum { get; private set; }

    /// <summary>基线复用同一个 HttpClient，无需显式打开</summary>
    public void Open() { }

    /// <summary>释放资源</summary>
    public void Close() => _http.Dispose();

    /// <summary>执行标量查询，返回首行首列</summary>
    /// <param name="sql">查询语句</param>
    public Int64 ExecuteScalar(String sql)
    {
        var rows = Execute(sql, false);
        return rows.Count == 0 ? 0 : Convert.ToInt64(rows[0][0].GetInt64());
    }

    /// <summary>读取全部结果行，返回行数</summary>
    /// <param name="sql">查询语句</param>
    public Int32 ReadAll(String sql)
    {
        var rows = Execute(sql, true);
        var sum = 0L;
        foreach (var row in rows)
        {
            if (row.Count > 0 && row[0].ValueKind == JsonValueKind.Number) sum += row[0].GetInt64();
        }

        Checksum = sum;
        return rows.Count;
    }

    /// <summary>读取全部结果行并累计多列校验值，返回行数</summary>
    /// <param name="sql">查询语句</param>
    public Int64 ReadColumns(String sql)
    {
        var rows = Execute(sql, true);
        var types = _columnTypes;
        var sum = 0L;
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Count && i < types.Count; i++)
            {
                var cell = row[i];
                if (cell.ValueKind == JsonValueKind.Null) continue;

                var type = types[i];
                if (type.StartsWith("bigint") || type.StartsWith("integer")) sum += cell.GetInt64();
                else if (type.StartsWith("varchar")) sum += (cell.GetString() ?? String.Empty).Length;
                else if (type.StartsWith("decimal")) sum += (Int64)(Decimal.Parse(cell.GetString() ?? "0", CultureInfo.InvariantCulture) * 100);
                else if (type.StartsWith("date")) sum += DateTime.ParseExact(cell.GetString() ?? String.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayOfYear;
            }
        }

        Checksum = sum;
        return rows.Count;
    }

    /// <summary>提交查询并跟随 nextUri 拉取全部结果</summary>
    /// <param name="sql">查询语句</param>
    /// <param name="keepRows">是否保留全部数据行，false 时取到首个数据页即返回</param>
    private List<List<JsonElement>> Execute(String sql, Boolean keepRows)
    {
        var rows = new List<List<JsonElement>>();
        _columnTypes.Clear();
        var uri = $"http://{_host}:{_port}/v1/statement";
        var first = true;
        while (!String.IsNullOrEmpty(uri))
        {
            using var request = new HttpRequestMessage(first ? HttpMethod.Post : HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation(_headerPrefix + "User", _user);
            request.Headers.TryAddWithoutValidation(_headerPrefix + "Catalog", _catalog);
            request.Headers.TryAddWithoutValidation(_headerPrefix + "Schema", _schema);
            request.Headers.TryAddWithoutValidation(_headerPrefix + "Source", "NewLife.PerfTest");
            if (first) request.Content = new StringContent(sql, Encoding.UTF8, new MediaTypeHeaderValue("text/plain"));

            using var response = _http.Send(request);
            response.EnsureSuccessStatusCode();

            using var stream = response.Content.ReadAsStream();
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var error))
                throw new InvalidOperationException(error.TryGetProperty("message", out var message) ? message.GetString() : "查询失败");

            if (_columnTypes.Count == 0 && root.TryGetProperty("columns", out var columns) && columns.ValueKind == JsonValueKind.Array)
            {
                foreach (var column in columns.EnumerateArray())
                    _columnTypes.Add(column.TryGetProperty("type", out var type) ? type.GetString() ?? String.Empty : String.Empty);
            }

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in data.EnumerateArray())
                {
                    var cells = new List<JsonElement>();
                    foreach (var cell in row.EnumerateArray()) cells.Add(cell.Clone());
                    rows.Add(cells);
                    if (!keepRows) break;
                }
            }

            if (!keepRows && rows.Count > 0) break;

            uri = root.TryGetProperty("nextUri", out var next) ? next.GetString() : null;
            first = false;
        }

        return rows;
    }

    /// <summary>释放资源</summary>
    public void Dispose() => _http.Dispose();
}
