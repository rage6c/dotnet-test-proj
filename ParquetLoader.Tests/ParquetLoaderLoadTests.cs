using DuckDB.NET.Data;
using Microsoft.Extensions.Logging.Abstractions;
using ParquetLoader.Configuration;
using ParquetLoader.ParquetLoads.WorkflowParquetLoader;
using Xunit;

namespace ParquetLoader.Tests;

public sealed class ParquetLoaderLoadTests
{
    [Fact]
    public void Load_ReadsParquetRowsWithHivePartitionAndMapsDtoValues()
    {
        using var dataset = TestParquetDataset.Create();
        var loader = CreateLoader(dataset.BasePath);

        var rows = loader.Load(null, 10, row => row.MonthOfYear == 202605 && row.Id > 1);

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].Id);
        Assert.Equal(1002, rows[0].WorkflowId);
        Assert.Equal("Resolved", rows[0].Status);
        Assert.Equal("assignee_002", rows[0].Assignee);
        Assert.Equal(new DateTime(2026, 5, 2, 2, 0, 0), rows[0].UpdatedOn);
        Assert.Equal(202605, rows[0].MonthOfYear);

        Assert.Equal(3, rows[1].Id);
        Assert.Null(rows[1].Assignee);
        Assert.Equal(202605, rows[1].MonthOfYear);
    }

    [Fact]
    public void Load_AppliesLimitInDuckDbQuery()
    {
        using var dataset = TestParquetDataset.Create();
        var loader = CreateLoader(dataset.BasePath);

        var rows = loader.Load(null, 1, row => row.MonthOfYear == 202605);

        Assert.Single(rows);
        Assert.Equal(1, rows[0].Id);
    }

    [Fact]
    public void Load_CanUseExplicitParquetFolderOverride()
    {
        using var dataset = TestParquetDataset.Create();
        var loader = CreateLoader("/tmp/not-used");

        var rows = loader.Load(dataset.WorkflowPath, 10, row => row.Id == 2);

        var row = Assert.Single(rows);
        Assert.Equal("Resolved", row.Status);
    }

    private static TestParquetLoader CreateLoader(string basePath)
    {
        return new TestParquetLoader(
            new ParquetLoaderConfig { BaseParquetPath = basePath },
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
    }

    private sealed class TestParquetDataset : IDisposable
    {
        private TestParquetDataset(string basePath)
        {
            BasePath = basePath;
            WorkflowPath = Path.Combine(basePath, "workflow");
        }

        public string BasePath { get; }

        public string WorkflowPath { get; }

        public static TestParquetDataset Create()
        {
            var basePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var partitionPath = Path.Combine(basePath, "workflow", "monthOfYear=202605");
            Directory.CreateDirectory(partitionPath);

            var parquetPath = Path.Combine(partitionPath, "data.parquet");
            using var connection = new DuckDBConnection("Data Source=:memory:");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                 CREATE TABLE workflow AS
                 SELECT *
                 FROM (
                     VALUES
                         (1::INTEGER, 1001::INTEGER, 'Closed'::VARCHAR, TIMESTAMP '2026-01-01 00:00:00', 'assignee_001'::VARCHAR, TIMESTAMP '2026-05-01 01:00:00'),
                         (2::INTEGER, 1002::INTEGER, 'Resolved'::VARCHAR, TIMESTAMP '2026-01-02 00:00:00', 'assignee_002'::VARCHAR, TIMESTAMP '2026-05-02 02:00:00'),
                         (3::INTEGER, 1003::INTEGER, 'Closed'::VARCHAR, TIMESTAMP '2026-01-03 00:00:00', NULL::VARCHAR, TIMESTAMP '2026-05-03 03:00:00')
                 ) AS rows(id, workflow_id, status, created_on, assignee, updated_on);

                 COPY workflow TO '{parquetPath.Replace("'", "''")}' (FORMAT PARQUET);
                 """;
            command.ExecuteNonQuery();

            return new TestParquetDataset(basePath);
        }

        public void Dispose()
        {
            if (Directory.Exists(BasePath))
            {
                Directory.Delete(BasePath, recursive: true);
            }
        }
    }
}
