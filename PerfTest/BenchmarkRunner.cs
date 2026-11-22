using System.Diagnostics;
using System.Globalization;

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

                var columnRows = probe.ReadColumns(ColumnsSql);
                var columnNote = columnRows == ColumnRows && probe.Checksum == ColumnsChecksum ? "数据一致" : $"行数 {columnRows}，校验和 {probe.Checksum}（期望 {ColumnsChecksum}）";
                log($"  多列预热完成：{columnRows} 行，{columnNote}");

                var wideRows = probe.ReadWide(WideSql);
                var wideNote = wideRows == ColumnRows && probe.Checksum == WideChecksum ? "数据一致" : $"行数 {wideRows}，校验和 {probe.Checksum}（期望 {WideChecksum}）";
                log($"  宽表预热完成：{wideRows} 行，{wideNote}");

                results.Add(MeasureConnect(descriptor));
                results.Add(Measure(descriptor, "标量查询 SELECT 1", options.ScalarIterations, probe, r => r.ExecuteScalar(ScalarSql)));
                results.Add(Measure(descriptor, $"聚合 {options.Rows} 行", options.AggregateIterations, probe, r => r.ExecuteScalar(AggregateSql)));
                results.Add(Measure(descriptor, $"扫描 {options.Rows} 行", options.ScanIterations, probe, r => r.ReadAll(ScanSql)));
                results.Add(Measure(descriptor, $"多列类型 {ColumnRows} 行", options.ScanIterations, probe, r => r.ReadColumns(ColumnsSql)));
                results.Add(Measure(descriptor, $"宽表 {WideColumns} 列 {ColumnRows} 行", options.ScanIterations, probe, r => r.ReadWide(WideSql)));
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

    private const Int32 ColumnRows = 5000;

    private String ColumnsSql =>
        $"SELECT x, CAST(x AS varchar) || 'abcdefghij', CAST(x AS decimal(12,2)) / 4, date_add('day', x % 300, DATE '2020-01-01') FROM UNNEST(sequence(1, {ColumnRows})) AS t(x)";

    /// <summary>宽表列数</summary>
    private const Int32 WideColumns = 16;

    /// <summary>宽表：10 个整数 + 6 个字符串，单页 5000 行即 8 万个单元格，专门压单页多列路径</summary>
    /// <remarks>混合类型由“多列类型”场景覆盖；这里只用整数与字符串，避免各客户端对 decimal/date 的取值类型差异干扰对比。</remarks>
    private String WideSql =>
        "SELECT x, x * 2, x * 3, x * 4, x * 5, x * 6, x * 7, x * 8, x * 9, x * 10, " +
        "CAST(x AS varchar) || 'a', CAST(x AS varchar) || 'bb', CAST(x AS varchar) || 'ccc', " +
        "CAST(x AS varchar) || 'dddd', CAST(x AS varchar) || 'eeeee', CAST(x AS varchar) || 'ffffff' " +
        $"FROM UNNEST(sequence(1, {ColumnRows})) AS t(x)";

    /// <summary>宽表校验和：整数求和、字符串取长度</summary>
    private Int64 WideChecksum
    {
        get
        {
            var sum = 0L;
            for (var i = 1; i <= ColumnRows; i++)
            {
                // 10 个整数列：x * (1+2+..+10)
                sum += i * 55;
                // 6 个字符串列：位数 + 1..6 个填充字符
                sum += i.ToString(CultureInfo.InvariantCulture).Length * 6 + 21;
            }

            return sum;
        }
    }

    private Int64 ColumnsChecksum
    {
        get
        {
            var sum = 0L;
            for (var i = 1; i <= ColumnRows; i++)
            {
                sum += i;
                sum += i.ToString(CultureInfo.InvariantCulture).Length + 10;
                sum += i * 25;
                sum += 1 + i % 300;
            }

            return sum;
        }
    }

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
        String? error = null;

        for (var i = 0; i < options.Warmup; i++)
        {
            var warmup = descriptor.Create();
            try
            {
                warmup.Open();
                warmup.ExecuteScalar(ScalarSql);
            }
            catch
            {
                // 预热失败交给正式采样报告
            }
            finally
            {
                warmup.Dispose();
            }
        }

        Settle();

        var allocated = GC.GetTotalAllocatedBytes(true);
        var watch = new Stopwatch();

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
        String? error = null;

        for (var i = 0; i < options.Warmup; i++)
        {
            try
            {
                action(runner);
            }
            catch
            {
                // 预热失败交给正式采样报告
            }
        }

        Settle();

        var allocated = GC.GetTotalAllocatedBytes(true);
        var watch = new Stopwatch();

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

        var warmups = new Task[options.Concurrency];
        for (var i = 0; i < warmups.Length; i++)
        {
            warmups[i] = Task.Run(() =>
            {
                using var runner = descriptor.Create();
                try
                {
                    runner.Open();
                    runner.ExecuteScalar(AggregateSql);
                }
                catch
                {
                    // 预热失败交给正式采样报告
                }
            });
        }

        await Task.WhenAll(warmups).ConfigureAwait(false);

        Settle();

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

    /// <summary>采集前先做一次完整回收，避免上一个客户端的垃圾在本次测量期间触发回收</summary>
    private static void Settle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static String Simplify(Exception ex)
    {
        var current = ex;
        while (current.InnerException != null) current = current.InnerException;
        return $"{current.GetType().Name}: {current.Message}".Replace("\r", " ").Replace("\n", " ");
    }
}
