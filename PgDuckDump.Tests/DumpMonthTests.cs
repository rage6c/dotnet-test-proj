using PgDuckDump.Services;
using Xunit;

namespace PgDuckDump.Tests;

public sealed class DumpMonthTests
{
    [Theory]
    [InlineData("202605")]
    [InlineData("2026-05")]
    public void Resolve_ParsesConfiguredMonth(string value)
    {
        var month = DumpMonth.Resolve(value, new FakeTimeProvider(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal("202605", month.MonthOfYear);
        Assert.Equal(new DateTime(2026, 5, 1), month.StartInclusive);
        Assert.Equal(new DateTime(2026, 6, 1), month.EndExclusive);
    }

    [Fact]
    public void Resolve_UsesPreviousCompleteMonthWhenMonthIsNotConfigured()
    {
        var month = DumpMonth.Resolve(null, new FakeTimeProvider(new DateTimeOffset(2026, 5, 24, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal("202604", month.MonthOfYear);
        Assert.Equal(new DateTime(2026, 4, 1), month.StartInclusive);
        Assert.Equal(new DateTime(2026, 5, 1), month.EndExclusive);
    }

    [Fact]
    public void Resolve_ThrowsForInvalidConfiguredMonth()
    {
        var exception = Assert.Throws<InvalidConfigurationException>(() =>
            DumpMonth.Resolve("2026/05", TimeProvider.System));

        Assert.Contains("yyyy-MM or yyyyMM", exception.Message);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
