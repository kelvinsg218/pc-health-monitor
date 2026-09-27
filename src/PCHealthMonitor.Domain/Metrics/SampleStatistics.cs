namespace PCHealthMonitor.Domain.Metrics;

/// <summary>Minimum / maximum (peak) / average over a set of percentage samples.</summary>
public sealed record SampleStatistics(double Minimum, double Maximum, double Average, int SampleCount)
{
    public double Peak => Maximum;

    /// <summary>
    /// Builds statistics from samples, ignoring NaN/infinite values.
    /// Returns null when there is no valid sample.
    /// </summary>
    public static SampleStatistics? FromSamples(IEnumerable<double> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var valid = samples.Where(double.IsFinite).ToList();
        if (valid.Count == 0)
        {
            return null;
        }

        return new SampleStatistics(valid.Min(), valid.Max(), valid.Average(), valid.Count);
    }
}
