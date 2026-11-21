using Xunit;

// 诊断测试依赖全局 ActivityListener，关闭并行以避免捕获其它测试的跨度
[assembly: CollectionBehavior(DisableTestParallelization = true)]
