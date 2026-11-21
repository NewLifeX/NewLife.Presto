using System.Text;

namespace PerfTest;

/// <summary>Markdown 报告生成</summary>
public static class ReportBuilder
{
    /// <summary>生成完整报告</summary>
    /// <param name="options">运行参数</param>
    /// <param name="result">测试结果</param>
    public static String Build(BenchmarkOptions options, BenchmarkResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Presto/Trino .NET 客户端性能对比");
        sb.AppendLine();
        sb.AppendLine($"> 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}　引擎：{options.Engine}　端点：{options.Host}:{options.Port}");
        sb.AppendLine($"> 运行参数：扫描 {options.Rows} 行，标量 {options.ScalarIterations} 次，聚合 {options.AggregateIterations} 次，扫描 {options.ScanIterations} 次，连接 {options.ConnectIterations} 次，并发 {options.Concurrency} x {options.ConcurrencyIterations}，重复 {options.Repeat} 轮");
        sb.AppendLine();

        sb.AppendLine("## 1. 客户端与可用性");
        sb.AppendLine();
        sb.AppendLine("| 客户端 | 类别 | 可用 | 说明 |");
        sb.AppendLine("|--------|------|------|------|");
        foreach (var status in result.Statuses)
            sb.AppendLine($"| {status.Name} | {status.Category} | {(status.Available ? "是" : "否")} | {Escape(status.Note ?? String.Empty)} |");
        sb.AppendLine();

        sb.AppendLine("## 2. 结果");
        sb.AppendLine();

        var scenarios = result.Results.Select(e => e.Scenario).Distinct().ToList();
        if (scenarios.Count == 0)
        {
            sb.AppendLine("没有产生有效结果。");
            return sb.ToString();
        }

        var collapsed = Statistics.Collapse(result.Results);
        foreach (var scenario in scenarios)
        {
            var rows = collapsed.Where(e => e.Representative.Scenario == scenario).OrderBy(e => e.Representative.MeanMs).ToList();
            sb.AppendLine($"### {scenario}");
            sb.AppendLine();
            if (scenario.Contains("并发"))
            {
                sb.AppendLine("| 客户端 | 总耗时(ms) | 吞吐(次/秒) | 分配(MB) | 轮间波动 | 备注 |");
                sb.AppendLine("|--------|-----------|-------------|----------|----------|------|");
                foreach (var item in rows)
                {
                    var row = item.Representative;
                    sb.AppendLine($"| {row.Client} | {Statistics.Format(row.MeanMs * row.Samples)} | {Statistics.Format(row.Throughput)} | {Statistics.Format(row.AllocKb / 1024)} | {FormatSpread(item)} | {Escape(row.Error ?? String.Empty)} |");
                }
            }
            else
            {
                sb.AppendLine("| 客户端 | 均值(ms) | P50(ms) | P95(ms) | 最快(ms) | 每次分配(KB) | 采样 | 轮间波动 | 备注 |");
                sb.AppendLine("|--------|---------|---------|---------|----------|--------------|------|----------|------|");
                foreach (var item in rows)
                {
                    var row = item.Representative;
                    sb.AppendLine($"| {row.Client} | {Statistics.Format(row.MeanMs)} | {Statistics.Format(row.P50Ms)} | {Statistics.Format(row.P95Ms)} | {Statistics.Format(row.MinMs)} | {Statistics.Format(row.AllocKb)} | {row.Samples} | {FormatSpread(item)} | {Escape(row.Error ?? String.Empty)} |");
                }
            }
            sb.AppendLine();
        }

        sb.AppendLine("## 3. 说明");
        sb.AppendLine();
        sb.AppendLine("- 所有客户端在同一进程、同一端点、相同 SQL 下串行测量，互不重叠，避免相互干扰。");
        sb.AppendLine("- “建立连接并首次查询”包含协议握手与会话初始化，兼顾延迟握手（懒连接）的客户端。");
        sb.AppendLine("- “每次分配”取自 GC.GetTotalAllocatedBytes，仅统计托管堆分配，不含本机内存与网络缓冲区。");
        sb.AppendLine("- 并发场景每条任务使用独立连接，吞吐 = 总查询数 / 墙钟时间。");
        sb.AppendLine("- 多轮重复时，每个单元格取各轮均值居中一轮的数据，“轮间波动”=(最大均值-最小均值)/中位均值。");

        return sb.ToString();
    }

    private static String Escape(String text) => text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

    private static String FormatSpread(ReportRow row) => row.Runs > 1 ? $"{Statistics.Format(row.SpreadPercent)}%" : "-";

    /// <summary>生成控制台简表</summary>
    /// <param name="result">测试结果</param>
    public static String BuildConsole(BenchmarkResult result)
    {
        var sb = new StringBuilder();
        var collapsed = Statistics.Collapse(result.Results);
        foreach (var group in collapsed.GroupBy(e => e.Representative.Scenario))
        {
            sb.AppendLine($"[{group.Key}]");
            foreach (var item in group.OrderBy(e => e.Representative.MeanMs))
            {
                var row = item.Representative;
                var speed = group.Key.Contains("并发")
                    ? $"{Statistics.Format(row.Throughput)} 次/秒"
                    : $"{Statistics.Format(row.MeanMs)} ms (P95 {Statistics.Format(row.P95Ms)})";
                sb.AppendLine($"  {row.Client,-32} {speed,-28} 分配 {Statistics.Format(row.AllocKb)} KB {row.Error}");
            }
        }

        return sb.ToString();
    }
}
