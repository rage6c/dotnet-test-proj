using PgDuckDump.Configuration;
using PgDuckDump.Services;
using PgDuckDump.Utilities;
using Xunit;

namespace PgDuckDump.Tests;

public sealed class OptionsTests
{
    [Fact]
    public void DuckDbOptions_UsesDefaults()
    {
        var options = new DuckDbOptions { OutputRoot = "out" };

        Assert.Equal(":memory:", options.DatabasePath);
        Assert.Equal("out", options.OutputRoot);
        Assert.Null(options.TempDirectory);
        Assert.Equal("1GB", options.MemoryLimit);
        Assert.Equal(1, options.Threads);
        Assert.Equal(122880, options.RowGroupSize);
        Assert.Equal("ZSTD", options.Compression);
    }

    [Fact]
    public void PostgresOptions_UsesDefaultAttachAlias()
    {
        var options = new PostgresOptions { ConnectionString = "Host=localhost" };

        Assert.Equal("Host=localhost", options.ConnectionString);
        Assert.Equal("pg_source", options.AttachAlias);
    }

    [Fact]
    public void DumpOptions_UsesDefaults()
    {
        var options = new DumpOptions();

        Assert.Null(options.Month);
        Assert.False(options.OverwritePartition);
        Assert.False(options.DeleteSourceAfterWrite);
        Assert.False(options.EnableRowCount);
        Assert.Empty(options.Tables);
    }

    [Fact]
    public void TableDumpOptions_StoresConfiguredValues()
    {
        var options = new TableDumpOptions
        {
            Name = "Workflow",
            SourceSchema = "test",
            SourceTable = "workflow",
            OutputName = "workflow",
            DateColumn = "updated_on",
            AdditionalWhere = "status = 'Closed'"
        };

        Assert.Equal("Workflow", options.Name);
        Assert.Equal("test", options.SourceSchema);
        Assert.Equal("workflow", options.SourceTable);
        Assert.Equal("workflow", options.OutputName);
        Assert.Equal("updated_on", options.DateColumn);
        Assert.Equal("status = 'Closed'", options.AdditionalWhere);
    }

    [Fact]
    public void DuckDbCopyOptions_StoresConfiguredValues()
    {
        var options = new DuckDbCopyOptions("ZSTD", 1000);

        Assert.Equal("ZSTD", options.Compression);
        Assert.Equal(1000, options.RowGroupSize);
    }

    [Fact]
    public void InvalidConfigurationException_StoresMessage()
    {
        var exception = new InvalidConfigurationException("bad config");

        Assert.Equal("bad config", exception.Message);
    }
}
