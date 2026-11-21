using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NewLife;
using NewLife.Presto;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length >= 2 && args[0].Equals("--connstr", StringComparison.OrdinalIgnoreCase))
{
    return RunReal(args[1]);
}

return await RunFakeServer();

static async Task<Int32> RunFakeServer()
{
    var port = GetFreePort();
    var prefix = $"http://localhost:{port}/";
    using var listener = new HttpListener();
    listener.Prefixes.Add(prefix);
    listener.Start();
    Console.WriteLine($"假 Presto 服务已启动：{prefix}");

    var server = Task.Run(async () =>
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => HandleAsync(context, port));
        }
    });

    try
    {
        var connectionString = $"Host=localhost;Port={port};Catalog=hive;Schema=default;User=test;Password=secret;MaxRetries=1;RetryInterval=0";
        using var connection = new PrestoConnection(connectionString);
        connection.Open();

        using var scalarCommand = connection.CreateCommand("select 42");
        var scalar = scalarCommand.ExecuteScalar();
        Assert(Convert.ToInt32(scalar) == 42, $"ExecuteScalar 期望 42，实际 {scalar}");

        using var readerCommand = connection.CreateCommand("select * from t");
        using var reader = readerCommand.ExecuteReader();
        var rows = 0;
        while (reader.Read())
        {
            rows++;
            Console.WriteLine($"行 {rows}: {reader.GetInt64(0)} {reader.GetString(1)}");
        }
        Assert(rows == 2, $"期望 2 行，实际 {rows}");

        using var updateCommand = connection.CreateCommand("insert into t values (1)");
        var affected = updateCommand.ExecuteNonQuery();
        Assert(affected == 3, $"ExecuteNonQuery 期望 3，实际 {affected}");

        var catalogs = connection.GetSchema("Catalogs");
        Assert(catalogs.Rows.Count == 1 && (String)catalogs.Rows[0]["CATALOG_NAME"] == "hive", "GetSchema(Catalogs) 结果不正确");

        Console.WriteLine();
        Console.WriteLine("PASS NewLife.Presto 端到端冒烟");
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL {ex.GetType().Name}: {ex.Message}");
        return 1;
    }
    finally
    {
        listener.Stop();
        try { await server; } catch { /* 退出阶段忽略监听器异常 */ }
    }
}

static Int32 RunReal(String connectionString)
{
    try
    {
        using var connection = new PrestoConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand("select 1 as n, 'x' as s");
        using var reader = command.ExecuteReader();
        Assert(reader.Read(), "查询没有返回数据");
        Console.WriteLine($"n={reader.GetInt32(0)}, s={reader.GetString(1)}");
        Console.WriteLine($"server={connection.ServerVersion}");
        Console.WriteLine("PASS NewLife.Presto 真库冒烟");
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL {ex.GetType().Name}: {ex.Message}");
        return 1;
    }
}

static async Task HandleAsync(HttpListenerContext context, Int32 port)
{
    var request = context.Request;
    var response = context.Response;
    try
    {
        var body = String.Empty;
        if (request.InputStream != null)
        {
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            body = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        var path = request.Url?.AbsolutePath ?? "/";
        var json = String.Empty;
        var status = 200;

        if (request.HttpMethod == "POST" && path == "/v1/statement")
        {
            if (body.IndexOf("insert", StringComparison.OrdinalIgnoreCase) >= 0)
                json = """{"id":"q3","updateType":"INSERT","updateCount":3}""";
            else if (body.IndexOf("SHOW CATALOGS", StringComparison.OrdinalIgnoreCase) >= 0)
                json = """{"id":"q4","columns":[{"name":"Catalog","type":"varchar"}],"data":[["hive"]]}""";
            else if (body.IndexOf("42", StringComparison.Ordinal) >= 0)
                json = """{"id":"q1","columns":[{"name":"n","type":"integer"}],"data":[["42"]]}""";
            else
                json = $$"""{"id":"q2","columns":[{"name":"id","type":"bigint"},{"name":"name","type":"varchar"}],"data":[["1","a"]],"nextUri":"http://localhost:{{port}}/v1/statement/q2/1"}""";
        }
        else if (request.HttpMethod == "GET" && path == "/v1/statement/q2/1")
        {
            json = """{"id":"q2","columns":[{"name":"id","type":"bigint"},{"name":"name","type":"varchar"}],"data":[["2","b"]]}""";
        }
        else if (request.HttpMethod == "GET" && path == "/v1/info")
        {
            json = """{"nodeVersion":{"version":"468"},"environment":"fake","coordinator":true}""";
        }
        else if (request.HttpMethod == "DELETE")
        {
            status = 204;
        }
        else
        {
            status = 404;
            json = """{"error":{"message":"not found","errorCode":404}}""";
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        if (bytes.Length > 0) await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
    }
    catch
    {
        // 冒烟服务端的异常不改变客户端结果，客户端会收到连接错误
    }
    finally
    {
        response.Close();
    }
}

static Int32 GetFreePort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static void Assert(Boolean condition, String message)
{
    if (!condition) throw new InvalidOperationException(message);
}
