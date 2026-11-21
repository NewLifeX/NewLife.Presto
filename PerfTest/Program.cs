using PerfTest;
using PerfTest.Runners;

var options = BenchmarkOptions.Parse(args);
var descriptors = BuildDescriptors(options);
if (!String.IsNullOrEmpty(options.Only))
    descriptors = descriptors.Where(e => e.Name.Contains(options.Only, StringComparison.OrdinalIgnoreCase)).ToList();
if (!String.IsNullOrEmpty(options.Exclude))
    descriptors = descriptors.Where(e => !e.Name.Contains(options.Exclude, StringComparison.OrdinalIgnoreCase)).ToList();

Console.WriteLine($"端点 {options.Host}:{options.Port}（{options.Engine}），扫描 {options.Rows} 行");
Console.WriteLine();

var runner = new BenchmarkRunner(options, Console.WriteLine);
var result = await runner.RunAsync(descriptors).ConfigureAwait(false);

if (!String.IsNullOrEmpty(options.Output))
{
    File.WriteAllText(options.Output, ReportBuilder.Build(options, result));
    Console.WriteLine($"报告已写入 {options.Output}");
}

Console.WriteLine();
Console.WriteLine(ReportBuilder.BuildConsole(result));

static List<RunnerDescriptor> BuildDescriptors(BenchmarkOptions options)
{
    var isPresto = String.Equals(options.Engine, "presto", StringComparison.OrdinalIgnoreCase);
    var protocol = isPresto ? "Presto" : "Trino";
    var trinoHeaders = isPresto ? "false" : "true";

    return
    [
        new RunnerDescriptor("NewLife.Presto 1.0", "ADO",
            () => new NewLifeRunner($"Host={options.Host};Port={options.Port};User={options.User};Catalog={options.Catalog};Schema={options.Schema};Protocol={protocol};MaxRetries=0")),
        new RunnerDescriptor("Trino.Ado 1.1.0", "ADO",
            () => new TrinoAdoRunner($"Host={options.Host};Port={options.Port};User={options.User};Catalog={options.Catalog};Schema={options.Schema};EnableSSL=false;ServerType={protocol}")),
        new RunnerDescriptor("Trino.Client.ADO 1.0.11", "ADO",
            () => new TrinoClientAdoRunner(options.Host, options.Port, options.Catalog, options.Schema, options.User, isPresto)),
        new RunnerDescriptor("NReco.PrestoAdo 1.1.0（默认）", "ADO",
            () => new NRecoRunner($"Host={options.Host};Port={options.Port};User={options.User};Catalog={options.Catalog};Schema={options.Schema};TrinoHeaders={trinoHeaders}")),
        new RunnerDescriptor("NReco.PrestoAdo 1.1.0（CheckInterval=50）", "ADO",
            () => new NRecoRunner($"Host={options.Host};Port={options.Port};User={options.User};Catalog={options.Catalog};Schema={options.Schema};TrinoHeaders={trinoHeaders};CheckInterval=50")),
        new RunnerDescriptor("原始 HTTP v1", "协议基线",
            () => new RawHttpRunner(options.Host, options.Port, options.User, options.Catalog, options.Schema, isPresto)),
    ];
}
