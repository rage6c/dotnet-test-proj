using System.Globalization;

namespace PgDuckDump.Services;

public sealed record DumpMonth(string MonthOfYear, DateTime StartInclusive, DateTime EndExclusive)
{
    public static DumpMonth Resolve(string? configuredMonth, TimeProvider timeProvider)
    {
        var start = string.IsNullOrWhiteSpace(configuredMonth)
            ? ResolvePreviousCompleteMonth(timeProvider)
            : ResolveConfiguredMonth(configuredMonth);

        return new DumpMonth(start.ToString("yyyyMM", CultureInfo.InvariantCulture), start, start.AddMonths(1));
    }

    private static DateTime ResolvePreviousCompleteMonth(TimeProvider timeProvider)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        var currentMonth = new DateTime(today.Year, today.Month, 1);
        return currentMonth.AddMonths(-1);
    }

    private static DateTime ResolveConfiguredMonth(string configuredMonth)
    {
        var acceptedFormats = new[] { "yyyy-MM", "yyyyMM" };
        if (!DateTime.TryParseExact(
                configuredMonth,
                acceptedFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            throw new InvalidConfigurationException(
                $"Dump:Month must use yyyy-MM or yyyyMM format. Value: {configuredMonth}");
        }

        return new DateTime(parsed.Year, parsed.Month, 1);
    }
}
