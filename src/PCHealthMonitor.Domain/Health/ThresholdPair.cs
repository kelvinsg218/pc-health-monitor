namespace PCHealthMonitor.Domain.Health;

/// <summary>
/// A warning/critical threshold pair. Classification is inclusive:
/// value &gt;= Critical → CRITICAL, value &gt;= Warning → WARNING, otherwise OK.
/// A missing or non-finite value is UNKNOWN.
/// </summary>
public sealed record ThresholdPair(double Warning, double Critical)
{
    public HealthStatus Classify(double? value)
    {
        if (value is null || !double.IsFinite(value.Value))
        {
            return HealthStatus.Unknown;
        }

        if (value.Value >= Critical)
        {
            return HealthStatus.Critical;
        }

        return value.Value >= Warning ? HealthStatus.Warning : HealthStatus.Ok;
    }

    /// <summary>Returns validation errors for a threshold pair constrained to (min, max].</summary>
    public IEnumerable<string> Validate(string name, double min, double max)
    {
        if (!double.IsFinite(Warning) || Warning <= min || Warning > max)
        {
            yield return $"{name}: warning threshold must be greater than {min} and at most {max} (was {Warning}).";
        }

        if (!double.IsFinite(Critical) || Critical <= min || Critical > max)
        {
            yield return $"{name}: critical threshold must be greater than {min} and at most {max} (was {Critical}).";
        }

        if (Warning > Critical)
        {
            yield return $"{name}: warning threshold ({Warning}) cannot be greater than critical threshold ({Critical}).";
        }
    }
}
