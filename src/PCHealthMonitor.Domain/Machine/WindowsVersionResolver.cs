namespace PCHealthMonitor.Domain.Machine;

/// <summary>
/// Resolves the Windows product name. The registry value "ProductName" still reports
/// "Windows 10 ..." on Windows 11 machines, so the build number is authoritative:
/// build &gt;= 22000 is Windows 11, build &gt;= 10240 is Windows 10.
/// </summary>
public static class WindowsVersionResolver
{
    public const int FirstWindows11Build = 22000;
    public const int FirstWindows10Build = 10240;

    public static string? ResolveName(int? build, string? productName)
    {
        if (build is >= FirstWindows11Build)
        {
            return "Windows 11";
        }

        if (build is >= FirstWindows10Build)
        {
            return "Windows 10";
        }

        // Unknown or older build: use only the "Windows N" prefix of the product name, if any.
        return ExtractPrefix(productName);
    }

    /// <summary>
    /// Extracts the edition (e.g. "Pro", "Home Single Language") from the product name,
    /// falling back to the EditionID when the product name is missing.
    /// </summary>
    public static string? ResolveEdition(string? productName, string? editionId)
    {
        if (!string.IsNullOrWhiteSpace(productName))
        {
            var trimmed = productName.Trim();
            var prefix = ExtractPrefix(trimmed);
            var edition = (prefix is null ? trimmed : trimmed[prefix.Length..]).Trim();
            if (edition.Length > 0)
            {
                return edition;
            }
        }

        return string.IsNullOrWhiteSpace(editionId) ? null : editionId.Trim();
    }

    private static string? ExtractPrefix(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return null;
        }

        var tokens = productName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 2 && tokens[0].Equals("Windows", StringComparison.OrdinalIgnoreCase))
        {
            return $"{tokens[0]} {tokens[1]}";
        }

        return null;
    }
}
