using Microsoft.Extensions.Logging.Abstractions;
using ParquetLoader.Configuration;
using ParquetLoader.ParquetLoads.WorkflowParquetLoader;
using Xunit;

namespace ParquetLoader.Tests;

public sealed class WorkflowParquetLoaderTests
{
    [Fact]
    public void DatasetName_IsWorkflow()
    {
        var loader = new WorkflowParquetLoader(
            new ParquetLoaderConfig { BaseParquetPath = "/tmp/parquet-output" },
            NullLogger<WorkflowParquetLoader>.Instance);

        Assert.Equal("workflow", loader.DatasetName);
    }
}
