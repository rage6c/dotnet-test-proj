using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using ParquetLoader.Configuration;
using ParquetLoader.ParquetLoads.WorkflowParquetLoader;
using Xunit;

namespace ParquetLoader.Tests;

public sealed class ParquetLoaderPredicateTranslationTests
{
    [Fact]
    public void TranslatePredicate_TranslatesAndComparison()
    {
        var loader = CreateLoader();

        var sql = loader.TranslateForTest(row => row.MonthOfYear == 202605 && row.Id > 2000);

        Assert.Equal("(\"monthOfYear\" = 202605 AND \"id\" > 2000)", sql);
    }

    [Fact]
    public void TranslatePredicate_TranslatesCollectionContainsToInClause()
    {
        var loader = CreateLoader();
        var statuses = new[] { "Closed", "Resolved" };

        var sql = loader.TranslateForTest(row =>
            row.MonthOfYear == 202605 &&
            ((IEnumerable<string>)statuses).Contains(row.Status) &&
            row.Assignee != "blocked_user");

        Assert.Equal(
            "((\"monthOfYear\" = 202605 AND \"status\" IN ('Closed', 'Resolved')) AND \"assignee\" <> 'blocked_user')",
            sql);
    }

    [Fact]
    public void TranslatePredicate_EscapesStringLiterals()
    {
        var loader = CreateLoader();

        var sql = loader.TranslateForTest(row => row.Assignee == "O'Brien");

        Assert.Equal("\"assignee\" = 'O''Brien'", sql);
    }

    [Fact]
    public void TranslatePredicate_TranslatesNullChecks()
    {
        var loader = CreateLoader();

        var sql = loader.TranslateForTest(row => row.Assignee != null);

        Assert.Equal("\"assignee\" IS NOT NULL", sql);
    }

    [Fact]
    public void TranslatePredicate_TranslatesOrAndNot()
    {
        var loader = CreateLoader();

        var sql = loader.TranslateForTest(row =>
            row.Status == "Closed" || !(row.Assignee == "blocked_user"));

        Assert.Equal("(\"status\" = 'Closed' OR NOT (\"assignee\" = 'blocked_user'))", sql);
    }

    [Fact]
    public void TranslatePredicate_TranslatesEmptyContainsToAlwaysFalse()
    {
        var loader = CreateLoader();
        var statuses = new List<string>();

        var sql = loader.TranslateForTest(row => statuses.Contains(row.Status!));

        Assert.Equal("1 = 0", sql);
    }

    [Fact]
    public void TranslatePredicate_TranslatesListContainsToInClause()
    {
        var loader = CreateLoader();
        var statuses = new List<string> { "Closed", "Resolved" };

        var sql = loader.TranslateForTest(row => statuses.Contains(row.Status!));

        Assert.Equal("\"status\" IN ('Closed', 'Resolved')", sql);
    }

    [Fact]
    public void TranslatePredicate_TranslatesDateTimeLiteral()
    {
        var loader = CreateLoader();
        var updatedAfter = new DateTime(2026, 5, 1, 2, 3, 4);

        var sql = loader.TranslateForTest(row => row.UpdatedOn >= updatedAfter);

        Assert.Equal("\"updated_on\" >= TIMESTAMP '2026-05-01 02:03:04'", sql);
    }

    [Fact]
    public void TranslatePredicate_TranslatesConstantOnLeftSide()
    {
        var loader = CreateLoader();

        var sql = loader.TranslateForTest(row => 2000 < row.Id);

        Assert.Equal("2000 < \"id\"", sql);
    }

    [Fact]
    public void TranslatePredicate_ThrowsForUnsupportedMethodCall()
    {
        var loader = CreateLoader();

        var exception = Assert.Throws<NotSupportedException>(() =>
            loader.TranslateForTest(row => row.Status!.StartsWith("Closed")));

        Assert.Contains("StartsWith", exception.Message);
    }

    [Fact]
    public void TranslatePredicate_ThrowsWhenComparisonDoesNotIncludeDtoProperty()
    {
        var loader = CreateLoader();

        var exception = Assert.Throws<NotSupportedException>(() =>
            loader.TranslateForTest(_ => 1 == 1));

        Assert.Contains("Unsupported predicate expression", exception.Message);
    }

    [Fact]
    public void Load_ThrowsWhenLimitIsLessThanOne()
    {
        var loader = CreateLoader();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            loader.Load("/tmp/parquet-output/workflow", 0));

        Assert.Equal("limit", exception.ParamName);
    }

    [Fact]
    public void Load_ThrowsWhenFolderDoesNotExist()
    {
        var loader = CreateLoader();
        var missingFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var exception = Assert.Throws<ArgumentException>(() =>
            loader.Load(missingFolder, 1));

        Assert.Contains("does not exist", exception.Message);
    }

    [Fact]
    public void Load_ThrowsWhenFolderHasNoParquetFiles()
    {
        var loader = CreateLoader();
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                loader.Load(folder, 1));

            Assert.Contains("No .parquet files", exception.Message);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
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
        : global::ParquetLoader.ParquetLoads.ParquetLoader<WorkflowDto>(config, logger)
    {
        public override string DatasetName => "workflow";

        protected override IReadOnlyDictionary<string, string> ColumnMap { get; } =
            new Dictionary<string, string>
            {
                [nameof(WorkflowDto.Id)] = "id",
                [nameof(WorkflowDto.WorkflowId)] = "workflow_id",
                [nameof(WorkflowDto.Status)] = "status",
                [nameof(WorkflowDto.CreatedOn)] = "created_on",
                [nameof(WorkflowDto.Assignee)] = "assignee",
                [nameof(WorkflowDto.UpdatedOn)] = "updated_on",
                [nameof(WorkflowDto.MonthOfYear)] = "monthOfYear"
            };

        public string TranslateForTest(Expression<Func<WorkflowDto, bool>> predicate)
        {
            return TranslatePredicate(predicate);
        }
    }
}
