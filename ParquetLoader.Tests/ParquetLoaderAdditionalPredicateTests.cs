using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using ParquetLoader.Configuration;
using Xunit;

namespace ParquetLoader.Tests;

public sealed class ParquetLoaderAdditionalPredicateTests
{
    [Fact]
    public void TranslatePredicate_TranslatesBoolLiteral()
    {
        var loader = CreateLoader();

        var sql = loader.TranslateForTest(row => row.IsActive == true);

        Assert.Equal("\"is_active\" = TRUE", sql);
    }

    [Fact]
    public void TranslatePredicate_TranslatesEnumLiteral()
    {
        var loader = CreateLoader();

        var sql = loader.TranslateForTest(row => row.Kind == TestKind.Done);

        Assert.Equal("\"kind\" = 2", sql);
    }

    [Fact]
    public void TranslatePredicate_TranslatesDateTimeOffsetAsUtcTimestamp()
    {
        var loader = CreateLoader();
        var cutoff = new DateTimeOffset(2026, 5, 1, 10, 30, 0, TimeSpan.FromHours(8));

        var sql = loader.TranslateForTest(row => row.UpdatedAt <= cutoff);

        Assert.Equal("\"updated_at\" <= TIMESTAMP '2026-05-01 02:30:00'", sql);
    }

    [Fact]
    public void TranslatePredicate_ThrowsWhenDtoPropertyHasNoColumnMapping()
    {
        var loader = CreateLoader();

        var exception = Assert.Throws<NotSupportedException>(() =>
            loader.TranslateForTest(row => row.Unmapped == "value"));

        Assert.Contains(nameof(TestDto.Unmapped), exception.Message);
    }

    private static TestParquetLoader CreateLoader()
    {
        return new TestParquetLoader(
            new ParquetLoaderConfig { BaseParquetPath = "/tmp/parquet-output" },
            NullLogger<TestParquetLoader>.Instance);
    }

    private sealed class TestParquetLoader(
        ParquetLoaderConfig config,
        NullLogger<TestParquetLoader> logger)
        : global::ParquetLoader.ParquetLoads.ParquetLoader<TestDto>(config, logger)
    {
        public override string DatasetName => "test";

        protected override IReadOnlyDictionary<string, string> ColumnMap { get; } =
            new Dictionary<string, string>
            {
                [nameof(TestDto.IsActive)] = "is_active",
                [nameof(TestDto.Kind)] = "kind",
                [nameof(TestDto.UpdatedAt)] = "updated_at"
            };

        public string TranslateForTest(Expression<Func<TestDto, bool>> predicate)
        {
            return TranslatePredicate(predicate);
        }
    }

    private sealed class TestDto
    {
        public bool IsActive { get; set; }

        public TestKind Kind { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public string? Unmapped { get; set; }
    }

    private enum TestKind
    {
        New = 1,
        Done = 2
    }
}
