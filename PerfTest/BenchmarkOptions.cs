using System.Globalization;

namespace PerfTest;

/// <summary>基准测试参数</summary>
public sealed class BenchmarkOptions
{
    /// <summary>引擎名称，用于报告标注</summary>
    public String Engine { get; set; } = "trino";

    /// <summary>服务端主机</summary>
    public String Host { get; set; } = "127.0.0.1";

    /// <summary>服务端端口</summary>
    public Int32 Port { get; set; } = 8080;

    /// <summary>用户名</summary>
    public String User { get; set; } = "perftest";

    /// <summary>Catalog</summary>
    public String Catalog { get; set; } = "system";

    /// <summary>Schema</summary>
    public String Schema { get; set; } = "information_schema";

    /// <summary>扫描行数</summary>
    public Int32 Rows { get; set; } = 15000;

    /// <summary>预热次数</summary>
    public Int32 Warmup { get; set; } = 3;

    /// <summary>连接建立采样次数</summary>
    public Int32 ConnectIterations { get; set; } = 10;

    /// <summary>标量查询采样次数</summary>
    public Int32 ScalarIterations { get; set; } = 30;

    /// <summary>聚合查询采样次数</summary>
    public Int32 AggregateIterations { get; set; } = 20;

    /// <summary>大结果集采样次数</summary>
    public Int32 ScanIterations { get; set; } = 5;

    /// <summary>并发数</summary>
    public Int32 Concurrency { get; set; } = 8;

    /// <summary>每个并发任务的查询次数</summary>
    public Int32 ConcurrencyIterations { get; set; } = 5;

    /// <summary>Markdown 报告输出路径</summary>
    public String? Output { get; set; }

    /// <summary>整轮重复次数，用于抵消机器抖动</summary>
    public Int32 Repeat { get; set; } = 1;

    /// <summary>只探测连通性，不跑完整基准</summary>
    public Boolean ProbeOnly { get; set; }

    /// <summary>只运行名称包含该子串的客户端</summary>
    public String? Only { get; set; }

    /// <summary>跳过名称包含该子串的客户端</summary>
    public String? Exclude { get; set; }

    /// <summary>解析命令行参数</summary>
    /// <param name="args">命令行参数</param>
    public static BenchmarkOptions Parse(String[] args)
    {
        var options = new BenchmarkOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            String Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"参数 {key} 缺少取值");

            switch (key.TrimStart('-').ToLowerInvariant())
            {
                case "engine": options.Engine = Next(); break;
                case "host": options.Host = Next(); break;
                case "port": options.Port = AsInt(Next()); break;
                case "user": options.User = Next(); break;
                case "catalog": options.Catalog = Next(); break;
                case "schema": options.Schema = Next(); break;
                case "rows": options.Rows = AsInt(Next()); break;
                case "warmup": options.Warmup = AsInt(Next()); break;
                case "iterations": options.ScalarIterations = AsInt(Next()); break;
                case "connect": options.ConnectIterations = AsInt(Next()); break;
                case "aggregate": options.AggregateIterations = AsInt(Next()); break;
                case "scan": options.ScanIterations = AsInt(Next()); break;
                case "concurrency": options.Concurrency = AsInt(Next()); break;
                case "concurrent-iterations": options.ConcurrencyIterations = AsInt(Next()); break;
                case "out": options.Output = Next(); break;
                case "repeat": options.Repeat = Math.Max(1, AsInt(Next())); break;
                case "only": options.Only = Next(); break;
                case "exclude": options.Exclude = Next(); break;
                case "probe": options.ProbeOnly = true; break;
                case "help" or "h":
                    Console.WriteLine(Usage);
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"未知参数 {key}，使用 --help 查看用法");
            }
        }

        return options;
    }

    private static Int32 AsInt(String value) => Int32.Parse(value, CultureInfo.InvariantCulture);

    /// <summary>用法说明</summary>
    public static String Usage =>
        """
        用法：dotnet run --project PerfTest -- [选项]

          --engine <trino|presto>   引擎名称，仅用于报告标注（默认 trino）
          --host <主机>             默认 127.0.0.1
          --port <端口>             默认 8080
          --user <用户>             默认 perftest
          --catalog <目录>          默认 system
          --schema <架构>           默认 information_schema
          --rows <行数>             扫描行数，默认 15000
          --warmup <次数>           预热次数，默认 3
          --connect <次数>          连接建立采样，默认 10
          --iterations <次数>       标量查询采样，默认 30
          --aggregate <次数>        聚合查询采样，默认 20
          --scan <次数>             大结果集采样，默认 5
          --concurrency <并发数>    并发数，默认 8
          --concurrent-iterations <次数> 每个并发任务的查询次数，默认 5
          --out <路径>              输出 Markdown 报告
          --repeat <次数>           整轮重复次数，默认 1
          --only <关键字>           只运行名称包含该关键字的客户端
          --exclude <关键字>        跳过名称包含该关键字的客户端
          --probe                   只做连通性探测
          --help                    显示本帮助
        """;
}


