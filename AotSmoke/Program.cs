using System.Globalization;
using System.Text;
using NewLife;
using NewLife.Presto;
using NewLife.Serialization;

namespace AotSmoke;

/// <summary>NewLife.Presto NativeAOT 冒烟探针。逐场景 PASS/FAIL，非零退出码即失败</summary>
/// <remarks>
/// 默认覆盖不依赖数据库的离线路径：程序集加载、连接字符串、协议头、类型映射、多语言、JSON 解析、异常与参数绑定。
/// 附加 --no-db 与默认行为一致；附加 <c>--connstr</c> 时额外执行真库场景（S9），验证 AOT 下的 HTTP 与结果解析链路。
/// </remarks>
static class Program
{
    private static Int32 _fail;

    private static readonly String[] Languages =
    [
        "ar", "bn", "cs", "da", "de", "el", "en", "es", "fa", "fi", "fr", "he", "hi", "hu", "id", "it",
        "ja", "ko", "ms", "nb", "nl", "pl", "pt", "ro", "ru", "sr", "sv", "th", "tr", "uk", "vi", "zh-Hans", "zh-Hant",
    ];

    static Int32 Main(String[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* 部分终端不支持设置编码 */ }

        var noDb = args.Contains("--no-db");
        var connectionString = GetArgument(args, "--connstr");
        Console.WriteLine($"[Env] AOT={!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported} NoDb={noDb} Db={!connectionString.IsNullOrEmpty()}");
        Console.WriteLine($"[Env] Driver={typeof(PrestoDriver).Assembly.GetName().Version}");

        Run("S1 Assembly", () =>
        {
            var asm = typeof(PrestoDriver).Assembly;
            Assert(asm.GetName().Name == "NewLife.Presto", "assembly name");
            Assert(!PrestoDriver.Version.ToString().IsNullOrEmpty(), "version");
        });

        Run("S2 ConnectionString", () =>
        {
            var builder = new PrestoConnectionStringBuilder("Server=presto.local;Database=hive;Owner=default;User ID=test;Pwd=secret;TrinoHeaders=true");
            Assert(builder.Host == "presto.local", "host alias");
            Assert(builder.Port == 8080, "default port");
            Assert(builder.Catalog == "hive", "catalog alias");
            Assert(builder.Schema == "default", "schema alias");
            Assert(builder.Protocol == PrestoProtocol.Trino, "trino headers");
            Assert(builder.Auth == PrestoAuth.Basic, "basic auth");
            builder.Validate();
        });

        Run("S3 Headers", () =>
        {
            Assert(PrestoHeaders.User(PrestoProtocol.Presto) == "X-Presto-User", "presto header");
            Assert(PrestoHeaders.User(PrestoProtocol.Trino) == "X-Trino-User", "trino header");
            Assert(PrestoHeaders.IsManaged("Authorization"), "managed authorization");
            Assert(!PrestoHeaders.IsManaged("X-Tenant"), "custom header");
        });

        Run("S4 TypeMapping", () =>
        {
            Assert(PrestoTypeMapper.GetFieldType("bigint") == typeof(Int64), "bigint");
            Assert(PrestoTypeMapper.GetFieldType("decimal(18,4)") == typeof(Decimal), "decimal");
            Assert(PrestoTypeMapper.GetFieldType("timestamp(3) with time zone") == typeof(DateTimeOffset), "timestamp tz");
            Assert(PrestoTypeMapper.GetFieldType("array(varchar)") == typeof(String), "array degradation");
            Assert(PrestoTypeMapper.IsComplex("map(varchar, bigint)"), "map complex");
        });

        Run("S5 Localization", () =>
        {
            var saved = Localization.Culture;
            try
            {
                Localization.Culture = CultureInfo.GetCultureInfo("en");
                var english = Localization.Translate("Host cannot be empty.");
                foreach (var name in Languages)
                {
                    Localization.Culture = CultureInfo.GetCultureInfo(name);
                    var text = Localization.Translate("Host cannot be empty.");
                    Assert(!text.IsNullOrEmpty(), name);
                    if (name != "en") Assert(text != english, $"{name} untranslated");
                }
            }
            finally
            {
                Localization.Culture = saved;
            }
        });

        Run("S6 Json", () =>
        {
            const String json = "{\"id\":\"q1\",\"columns\":[{\"name\":\"n\",\"type\":\"integer\"}],\"data\":[[\"1\"]]}";
            var map = JsonParser.Decode(json);
            Assert(map != null && map.ContainsKey("id"), "id");
            Assert(map!["id"] as String == "q1", "id value");
            var columns = map["columns"] as IList<Object?>;
            Assert(columns != null && columns.Count == 1, "columns");
        });

        Run("S7 Exceptions", () =>
        {
            var ex = PrestoException.SubmitFailed(503);
            Assert(ex.HttpStatusCode == 503, "status");
            Assert(!ex.Message.IsNullOrEmpty(), "message");
            Assert(PrestoUnsupported.Transactions() is NotSupportedException, "transactions");
        });

        Run("S8 ParameterBinder", () =>
        {
            var parameters = new PrestoParameterCollection { new PrestoParameter("@id", 1) };
            var sql = PrestoParameterBinder.Bind("select @id, '@id'", parameters);
            Assert(sql == "select 1, '@id'", "bind");
            Assert(PrestoParameterBinder.FormatValue(new DateTime(2020, 1, 2, 3, 4, 5, 6)).StartsWith("TIMESTAMP"), "datetime");
            Assert(PrestoParameterBinder.FormatValue(new Byte[] { 1, 2 }) == "X'0102'", "binary");
        });

        if (!connectionString.IsNullOrEmpty())
        {
            Run("S9 RealServer", () =>
            {
                using var connection = new PrestoConnection(connectionString);
                connection.Open();

                using var command = connection.CreateCommand("select 1 as n, 'x' as s");
                using var reader = command.ExecuteReader();
                Assert(reader.Read(), "row");
                Assert(reader.GetInt32(0) == 1, "n");
                Assert(reader.GetString(1) == "x", "s");
                Assert(!reader.Read(), "single row");
                Assert(!connection.ServerVersion.IsNullOrEmpty(), "server version");
            });
        }

        Console.WriteLine();
        if (_fail > 0)
        {
            Console.WriteLine($"FAILED: {_fail}");
            return 1;
        }

        Console.WriteLine("ALL PASSED");
        return 0;
    }

    private static void Run(String name, Action action)
    {
        try
        {
            action();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            _fail++;
            Console.WriteLine($"FAIL {name}: {ex.Message}");
        }
    }

    private static void Assert(Boolean condition, String message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static String GetArgument(String[] args, String name)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : String.Empty;
    }
}
