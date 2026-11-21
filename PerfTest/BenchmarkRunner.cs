using System.Diagnostics;

namespace PerfTest;

/// <summary>客户端描述，用工厂创建独立实例（并发场景需要多条连接）</summary>
/// <param name="Name">显示名称</param>
/// <param name="Category">客户端类别</param>
/// <param name="Create">实例工厂</param>
public sealed record RunnerDescriptor(String Name, String Category, Func<IRunner> Create);

/// <summary>客户端可用性状态</summary>
/// <param name="Name">显示名称</param>
/// <param name="Category">客户端类别</param>
/// <param name="Available">是否可用</param>
/// <param name="Checksum">扫描场景的数据校验和</param>
/// <param name="Note">备注或错误信息</param>
public sealed record ClientStatus(String Name, String Category, Boolean Available, Int64 Checksum, String? Note);

/// <summary>基准测试结果集</summary>
/// <param name="Results">场景结果</param>
/// <param name="Statuses">客户端状态</param>
public sealed record BenchmarkResult(List<ScenarioResult> Results, List<ClientStatus> Statuses);

/// <summary>场景执行器</summary>
public sealed class BenchmarkRunner(BenchmarkOptions options, Action<String> log)
{
    /// <summary>执行全部场景</summary>
    /// <param name="descriptors">客户端列表</param>
    public async Task<BenchmarkResult> RunAsync(IReadOnlyList<RunnerDescriptor> descriptors)
    {
        var results = new List<ScenarioResult>();
        var statuses = new List<ClientStatus>();

        for (var run = 1; run <= options.Repeat; run++)
        {
            if (options.Repeat > 1) log($"----- 第 {run}/{options.Repeat} 轮 -----");

            var current = await RunOnceAsync(descriptors).ConfigureAwait(false);
            results.AddRange(current.Results);

            foreach (var status in current.Statuses)
            {
                var index = statuses.FindIndex(e => e.Name == status.Name);
                if (index >= 0) statuses[index] = status;
                else statuses.Add(status);
            }
        }

        return new BenchmarkResult(results, statuses);
    }

    private async Task<BenchmarkResult> RunOnceAsync(IReadOnlyList<RunnerDescriptor> descriptors)
    {
        var results = new List<ScenarioResult>();
        var statuses = new List<ClientStatus>();

        foreach (var descriptor in descriptors)
        {
            log($"== {descriptor.Name} ==");

            IRunner probe;
            try
            {
                probe = descriptor.Create();
                probe.Open();
            }
            catch (Exception ex)
            {
                var reason = Simplify(ex);
                log($"  不可用：{reason}");
                statuses.Add(new ClientStatus(descriptor.Name, descriptor.Category, false, 0, reason));
                continue;
            }

            try
            {
                if (options.ProbeOnly)
                {
                    var value = probe.ExecuteScalar(ScalarSql);
                    log($"  探测成功：SELECT 1 => {value}");
                    statuses.Add(new ClientStatus(descriptor.Name, descriptor.Category, true, 0, "探测成功"));
                    continue;
                }

                for (var i = 0; i < options.Warmup; i++) probe.ExecuteScalar(ScalarSql);
                var rows = probe.ReadAll(ScanSql);
                var checksum = probe.Checksum;
                var expected = ExpectedChecksum;
                var note = rows == ActualRows && checksum == expected ? "数据一致" : $"行数 {rows}，校验和 {checksum}（期望 {expected}）";
                log($"  预热完成：扫描 {rows} 行，{note}");

                results.Add(MeasureConnect(descriptor));
                results.Add(Measure(descriptor, "标量查询 SELECT 1", options.ScalarIterations, probe, r => r.ExecuteScalar(ScalarSql)));
                results.Add(Measure(descriptor, $"聚合 {options.Rows} 行", options.AggregateIterations, probe, r => r.ExecuteScalar(AggregateSql)));
                results.Add(Measure(descriptor, $"扫描 {options.Rows} 行", options.ScanIterations, probe, r => r.ReadAll(ScanSql)));
                results.Add(await MeasureConcurrencyAsync(descriptor));

                statuses.Add(new ClientStatus(descriptor.Name, descriptor.Category, true, checksum, note));
            }
            catch (Exception ex)
            {
                var reason = Simplify(ex);
                log($"  失败：{reason}");
                statuses.Add(new ClientStatus(descriptor.Name, descriptor.Category, true, 0, reason));
            }
            finally
            {
                probe.Dispose();
            }
        }

        return new BenchmarkResult(results, statuses);
    }

    private String ScalarSql => "SELECT 1";

    private Int32 BaseRows => options.Rows <= 10000 ? options.Rows : 5000;

    private Int32 Factor => Math.Max(1, options.Rows / BaseRows);

    private Int32 ActualRows => BaseRows * Factor;

    private String ScanSql => Factor == 1
        ? $"SELECT x FROM UNNEST(sequence(1, {BaseRows})) AS t(x)"
        : $"SELECT x * k FROM UNNEST(sequence(1, {BaseRows})) AS t(x) CROSS JOIN UNNEST(sequence(1, {Factor})) AS u(k)";

    private String AggregateSql => $"SELECT count(*) FROM ({ScanSql}) AS s";

    private Int64 ExpectedChecksum
    {
        get
        {
            var baseSum = (Int64)BaseRows * (BaseRows + 1) / 2;
            var factorSum = (Int64)Factor * (Factor + 1) / 2;
            return baseSum * factorSum;
        }
    }

    private ScenarioResult MeasureConnect(RunnerDescriptor descriptor)
    {
        var samples = new List<Double>(options.ConnectIterations);
        var allocated = GC.GetTotalAllocatedBytes(true);
        var watch = new Stopwatch();
        String? error = null;

        for (var i = 0; i < options.ConnectIterations; i++)
        {
            var runner = descriptor.Create();
            watch.Restart();
            try
            {
                runner.Open();
                runner.ExecuteScalar(ScalarSql);
            }
            catch (Exception ex)
            {
                error = Simplify(ex);
                watch.Stop();
                runner.Dispose();
                break;
            }
            watch.Stop();
            runner.Close();
            runner.Dispose();
            samples.Add(watch.Elapsed.TotalMilliseconds);
        }

        allocated = GC.GetTotalAllocatedBytes(true) - allocated;
        var result = Statistics.Summarize(descriptor.Name, descriptor.Category, "建立连接并首次查询", samples, allocated, 0);
        return error == null ? result : result with { Error = error };
    }

    private ScenarioResult Measure(RunnerDescriptor descriptor, String scenario, Int32 iterations, IRunner runner, Action<IRunner> action)
    {
        var samples = new List<Double>(iterations);
        var allocated = GC.GetTotalAllocatedBytes(true);
        var watch = new Stopwatch();
        String? error = null;

        for (var i = 0; i < iterations; i++)
        {
            watch.Restart();
            try
            {
                action(runner);
            }
            catch (Exception ex)
            {
                error = Simplify(ex);
                watch.Stop();
                break;
            }
            watch.Stop();
            samples.Add(watch.Elapsed.TotalMilliseconds);
        }

        allocated = GC.GetTotalAllocatedBytes(true) - allocated;
        var result = Statistics.Summarize(descriptor.Name, descriptor.Category, scenario, samples, allocated, 0);
        return error == null ? result : result with { Error = error };
    }

    private async Task<ScenarioResult> MeasureConcurrencyAsync(RunnerDescriptor descriptor)
    {
        var scenario = $"{options.Concurrency} 并发 x {options.ConcurrencyIterations} 次";
        var watch = Stopwatch.StartNew();
        var errors = new List<String>();
        var allocated = GC.GetTotalAllocatedBytes(true);

        var workers = new Task[options.Concurrency];
        for (var i = 0; i < workers.Length; i++)
        {
            workers[i] = Task.Run(() =>
            {
                using var runner = descriptor.Create();
                try
                {
                    runner.Open();
                    for (var n = 0; n < options.ConcurrencyIterations; n++) runner.ExecuteScalar(AggregateSql);
                }
                catch (Exception ex)
                {
                    lock (errors) errors.Add(Simplify(ex));
                }
            });
        }

        await Task.WhenAll(workers).ConfigureAwait(false);
        watch.Stop();
        allocated = GC.GetTotalAllocatedBytes(true) - allocated;

        var total = options.Concurrency * options.ConcurrencyIterations;
        var throughput = watch.Elapsed.TotalSeconds > 0 ? total / watch.Elapsed.TotalSeconds : 0;
        var samples = new List<Double> { watch.Elapsed.TotalMilliseconds / total };
        var result = Statistics.Summarize(descriptor.Name, descriptor.Category, scenario, samples, allocated, throughput);
        return errors.Count == 0 ? result : result with { Error = errors[0] };
    }

    private static String Simplify(Exception ex)
    {
        var current = ex;
        while (current.InnerException != null) current = current.InnerException;
        return $"{current.GetType().Name}: {current.Message}".Replace("\r", " ").Replace("\n", " ");
    }
}
