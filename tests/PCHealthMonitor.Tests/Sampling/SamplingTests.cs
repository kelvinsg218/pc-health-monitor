using PCHealthMonitor.Application.Sampling;
using PCHealthMonitor.Domain.Metrics;
using PCHealthMonitor.Tests.Fakes;

namespace PCHealthMonitor.Tests.Sampling;

public class SamplingTests
{
    [Fact]
    public void Statistics_compute_minimum_maximum_average_and_peak()
    {
        var stats = SampleStatistics.FromSamples([20, 40, 60, 80])!;

        Assert.Equal(20, stats.Minimum);
        Assert.Equal(80, stats.Maximum);
        Assert.Equal(80, stats.Peak);
        Assert.Equal(50, stats.Average);
        Assert.Equal(4, stats.SampleCount);
    }

    [Fact]
    public void Statistics_ignore_invalid_samples()
    {
        var stats = SampleStatistics.FromSamples([double.NaN, 10, double.PositiveInfinity, 30])!;

        Assert.Equal(2, stats.SampleCount);
        Assert.Equal(20, stats.Average);
    }

    [Fact]
    public void Statistics_are_null_without_valid_samples()
    {
        Assert.Null(SampleStatistics.FromSamples([]));
        Assert.Null(SampleStatistics.FromSamples([double.NaN]));
    }

    [Fact]
    public void Single_sample_has_equal_min_max_average()
    {
        var stats = SampleStatistics.FromSamples([42.5])!;

        Assert.Equal(42.5, stats.Minimum);
        Assert.Equal(42.5, stats.Maximum);
        Assert.Equal(42.5, stats.Average);
    }

    [Theory]
    [InlineData(5, 1, 5)]
    [InlineData(10, 2, 5)]
    [InlineData(1, 1, 1)]
    [InlineData(1, 5, 1)]
    [InlineData(5, 2, 3)]
    public void Sample_count_derives_from_duration_and_interval(double duration, double interval, int expected)
    {
        Assert.Equal(expected, CpuSampler.SampleCount(TimeSpan.FromSeconds(duration), TimeSpan.FromSeconds(interval)));
    }

    [Fact]
    public async Task Sampler_takes_the_expected_number_of_samples_without_real_waiting()
    {
        var collector = new FakeCpuCollector(20, 40, 60, 80);
        var sampler = new CpuSampler(collector);

        var metrics = await sampler.SampleAsync(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(1), null, CancellationToken.None);

        Assert.Equal(4, collector.Calls);
        Assert.Equal(50, metrics.Usage.Average);
        Assert.Equal(80, metrics.Usage.Peak);
        Assert.Equal(20, metrics.Usage.Minimum);
        Assert.Equal(8, metrics.Info.LogicalProcessorCount);
        Assert.Equal(TimeSpan.FromSeconds(1), metrics.SampleInterval);
    }

    [Fact]
    public async Task Sampler_reports_progress()
    {
        var reported = new List<int>();
        var sampler = new CpuSampler(new FakeCpuCollector(10, 20, 30));

        await sampler.SampleAsync(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), new InlineProgress<int>(reported.Add), CancellationToken.None);

        Assert.Equal([1, 2, 3], reported);
    }

    [Fact]
    public async Task Sampler_throws_when_no_valid_sample()
    {
        var sampler = new CpuSampler(new FakeCpuCollector(double.NaN));

        await Assert.ThrowsAsync<MetricUnavailableException>(() =>
            sampler.SampleAsync(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), null, CancellationToken.None));
    }

    [Fact]
    public async Task Sampler_honors_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var collector = new FakeCpuCollector(10) { OnMeasure = n => { if (n == 2) cts.Cancel(); } };
        var sampler = new CpuSampler(collector);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sampler.SampleAsync(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), null, cts.Token));
        Assert.Equal(2, collector.Calls);
    }

    [Fact]
    public void Cpu_usage_is_busy_time_over_total_time()
    {
        // kernel includes idle: total = 1000 + 1000 = 2000, idle = 500 → 75% busy
        var before = new CpuTimes(Idle: 1_000, Kernel: 2_000, User: 1_000);
        var after = new CpuTimes(Idle: 1_500, Kernel: 3_000, User: 2_000);

        Assert.Equal(75, CpuUsageCalculator.Calculate(before, after), precision: 6);
    }

    [Fact]
    public void Cpu_usage_is_zero_when_fully_idle_and_100_when_never_idle()
    {
        var start = new CpuTimes(0, 0, 0);

        Assert.Equal(0, CpuUsageCalculator.Calculate(start, new CpuTimes(1000, 1000, 0)));
        Assert.Equal(100, CpuUsageCalculator.Calculate(start, new CpuTimes(0, 500, 500)));
    }

    [Fact]
    public void Cpu_usage_is_invalid_for_empty_or_inconsistent_deltas()
    {
        var same = new CpuTimes(10, 20, 30);

        Assert.True(double.IsNaN(CpuUsageCalculator.Calculate(same, same)));
        Assert.True(double.IsNaN(CpuUsageCalculator.Calculate(new CpuTimes(100, 200, 300), new CpuTimes(50, 100, 100))));
    }

    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
