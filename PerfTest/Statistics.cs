using System.Globalization;

namespace PerfTest;

/// <summary>单个场景的统计结果</summary>
/// <param name="Client">客户端名称</param>
/// <param name="Category">客户端类别</param>
/// <param name="Scenario">场景名称</param>
/// <param name="Samples">采样次数</param>
/// <param name="MeanMs">平均耗时（毫秒）</param>
/// <param name="P50Ms">中位数耗时</param>
/// <param name="P95Ms">P95 耗时</param>
/// <param name="MinMs">最小耗时</param>
/// <param name="AllocKb">每次调用分配的托管内存（KB）</param>
/// <param name="Throughput">吞吐量（次/秒），仅并发场景有效</param>
/// <param name="Error">错误信息，成功为空</param>
public sealed record ScenarioResult(
    String Client,
    String Category,
    String Scenario,
    Int32 Samples,
    Double MeanMs,
    Double P50Ms,
    Double P95Ms,
    Double MinMs,
    Double AllocKb,
    Double Throughput,
    String? Error);

/// <summary>跨多轮聚合后的报告行</summary>
/// <param name="Representative">代表轮次（各轮均值的中位）</param>
/// <param name="SpreadPercent">轮间波动百分比</param>
/// <param name="Runs">参与聚合的轮数</param>
public sealed record ReportRow(ScenarioResult Representative, Double SpreadPercent, Int32 Runs);

/// <summary>统计辅助</summary>
public static class Statistics
{
    /// <summary>按客户端与场景合并多轮结果，取均值居中的一轮作为代表</summary>
    /// <param name="results">多轮场景结果</param>
    public static List<ReportRow> Collapse(IEnumerable<ScenarioResult> results)
    {
        var rows = new List<ReportRow>();
        foreach (var group in results.GroupBy(e => (e.Client, e.Scenario)))
        {
            var ordered = group.OrderBy(e => e.MeanMs).ToList();
            var pick = ordered[ordered.Count / 2];
            var spread = ordered.Count > 1 && pick.MeanMs > 0 ? (ordered[^1].MeanMs - ordered[0].MeanMs) / pick.MeanMs * 100 : 0;
            rows.Add(new ReportRow(pick, spread, ordered.Count));
        }

        return rows;
    }
    /// <summary>对采样结果做汇总</summary>
    /// <param name="client">客户端名称</param>
    /// <param name="category">客户端类别</param>
    /// <param name="scenario">场景名称</param>
    /// <param name="samples">采样耗时（毫秒）</param>
    /// <param name="allocBytes">总分配字节数</param>
    /// <param name="throughput">吞吐量</param>
    public static ScenarioResult Summarize(String client, String category, String scenario, IReadOnlyList<Double> samples, Int64 allocBytes, Double throughput)
    {
        if (samples.Count == 0) return new ScenarioResult(client, category, scenario, 0, 0, 0, 0, 0, 0, throughput, "无有效采样");

        var sorted = samples.OrderBy(e => e).ToArray();
        return new ScenarioResult(
            client,
            category,
            scenario,
            sorted.Length,
            sorted.Average(),
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            sorted[0],
            allocBytes / 1024d / sorted.Length,
            throughput,
            null);
    }

    /// <summary>线性插值求分位数</summary>
    /// <param name="sorted">已排序样本</param>
    /// <param name="percentile">分位，0~1</param>
    public static Double Percentile(Double[] sorted, Double percentile)
    {
        if (sorted.Length == 0) return 0;
        if (sorted.Length == 1) return sorted[0];

        var position = (sorted.Length - 1) * percentile;
        var lower = (Int32)Math.Floor(position);
        var upper = (Int32)Math.Ceiling(position);
        if (lower == upper) return sorted[lower];

        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    /// <summary>格式化为两位小数</summary>
    /// <param name="value">数值</param>
    public static String Format(Double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
