using System.Net;
using System.Net.Http.Json;
using DuckDB.NET.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ParquetLoader.ParquetLoads.WorkflowParquetLoader;

namespace ParquetLoaderApi.Tests;

public sealed class WorkflowEndpointTests
{
    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        await using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<HealthResponse>("/health");

        Assert.NotNull(response);
        Assert.Equal("Healthy", response.Status);
    }

    [Fact]
    public async Task Workflow_ReturnsBadRequestForInvalidLimit()
    {
        await using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/workflow?limit=0");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("limit must be an integer from 1 to 10000", body);
    }

    [Fact]
    public async Task Workflow_ReturnsBadRequestForMissingParquetFolder()
    {
        await using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/workflow?monthOfYear=202605&limit=10");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Parquet folder does not exist", body);
    }

    [Fact]
    public async Task Workflow_AppliesQueryFiltersAndReturnsDtos()
    {
        using var dataset = TestParquetDataset.Create();
        await using var factory = new TestApiFactory(dataset.BasePath);
        using var client = factory.CreateClient();

        var rows = await client.GetFromJsonAsync<List<WorkflowDto>>(
            "/api/workflow?monthOfYear=202605&minId=2&status=Closed&status=Resolved&excludeAssignee=blocked_user&limit=10");

        Assert.NotNull(rows);
        var row = Assert.Single(rows);
        Assert.Equal(2, row.Id);
        Assert.Equal(1002, row.WorkflowId);
        Assert.Equal("Resolved", row.Status);
        Assert.Equal("assignee_002", row.Assignee);
        Assert.Equal(202605, row.MonthOfYear);
    }

    private sealed record HealthResponse(string Status);

    private sealed class TestApiFactory(string? baseParquetPath = null) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(configurationBuilder =>
            {
                configurationBuilder.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ParquetLoader:BaseParquetPath"] = baseParquetPath ?? MissingBasePath()
                    });
            });

            builder.ConfigureServices(services =>
            {
                services.ConfigureHttpJsonOptions(options =>
                {
                    options.SerializerOptions.PropertyNameCaseInsensitive = true;
                });
            });

            return base.CreateHost(builder);
        }

        private static string MissingBasePath()
        {
            return Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}");
        }
    }

    private sealed class TestParquetDataset : IDisposable
    {
        private TestParquetDataset(string basePath)
        {
            BasePath = basePath;
        }

        public string BasePath { get; }

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
                         (3::INTEGER, 1003::INTEGER, 'Closed'::VARCHAR, TIMESTAMP '2026-01-03 00:00:00', 'blocked_user'::VARCHAR, TIMESTAMP '2026-05-03 03:00:00'),
                         (4::INTEGER, 1004::INTEGER, 'Open'::VARCHAR, TIMESTAMP '2026-01-04 00:00:00', 'assignee_004'::VARCHAR, TIMESTAMP '2026-05-04 04:00:00')
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
