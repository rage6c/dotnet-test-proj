using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PgDuckDump.Configuration;
using PgDuckDump.Services;
using PgDuckDump.Utilities;
using Xunit;

namespace PgDuckDump.Tests;

public sealed class DbDumpServiceTests
{
    [Fact]
    public async Task RunAsync_ReturnsInvalidConfigurationWhenPostgresConnectionStringIsMissing()
    {
        var service = CreateService(postgresOptions: new PostgresOptions { ConnectionString = "" });

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task RunAsync_ReturnsInvalidConfigurationWhenAttachAliasIsMissing()
    {
        var service = CreateService(postgresOptions: new PostgresOptions { ConnectionString = "Host=localhost", AttachAlias = "" });

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task RunAsync_ReturnsInvalidConfigurationWhenOutputRootIsMissing()
    {
        var service = CreateService(duckDbOptions: new DuckDbOptions { OutputRoot = "" });

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task RunAsync_ReturnsInvalidConfigurationWhenThreadsIsInvalid()
    {
        var service = CreateService(duckDbOptions: new DuckDbOptions { OutputRoot = "out", Threads = 0 });

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task RunAsync_ReturnsInvalidConfigurationWhenRowGroupSizeIsInvalid()
    {
        var service = CreateService(duckDbOptions: new DuckDbOptions { OutputRoot = "out", RowGroupSize = 0 });

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task RunAsync_ReturnsInvalidConfigurationWhenCompressionIsMissing()
    {
        var service = CreateService(duckDbOptions: new DuckDbOptions { OutputRoot = "out", Compression = "" });

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task RunAsync_ReturnsInvalidConfigurationWhenTablesAreMissing()
    {
        var service = CreateService(dumpOptions: new DumpOptions { Tables = [] });

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task RunAsync_ReturnsInvalidConfigurationWhenTableNameIsMissing()
    {
        var service = CreateService(dumpOptions: new DumpOptions { Tables = [ValidTable(name: "")] });

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task RunAsync_ReturnsInvalidConfigurationWhenOutputNameIsUnsafe()
    {
        var service = CreateService(dumpOptions: new DumpOptions { Tables = [ValidTable(outputName: "../workflow")] });

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task RunAsync_ReturnsInvalidConfigurationWhenAdditionalWhereIsUnsafe()
    {
        var service = CreateService(dumpOptions: new DumpOptions { Tables = [ValidTable(additionalWhere: "status = 'Closed'; DROP TABLE workflow")] });

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task RunAsync_DumpsConfiguredTable()
    {
        using var outputRoot = TempDirectory.Create();
        var processor = new FakeDuckDbProcessor();
        var service = CreateService(
            duckDbOptions: new DuckDbOptions { OutputRoot = outputRoot.Path },
            processor: processor);

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(0, result);
        Assert.True(processor.Initialized);
        Assert.Single(processor.CopyCalls);
        Assert.Contains("SELECT *", processor.CopyCalls[0].SelectSql);
        Assert.Contains("FROM \"pg_source\".\"test\".\"workflow\"", processor.CopyCalls[0].SelectSql);
        Assert.EndsWith(Path.Combine("workflow", "monthOfYear=202605", "data.parquet"), processor.CopyCalls[0].OutputPath);
        Assert.Equal("ZSTD", processor.CopyCalls[0].CopyOptions.Compression);
    }

    [Fact]
    public async Task RunAsync_CountsRowsWhenEnabled()
    {
        using var outputRoot = TempDirectory.Create();
        var processor = new FakeDuckDbProcessor { CountResult = 7 };
        var service = CreateService(
            duckDbOptions: new DuckDbOptions { OutputRoot = outputRoot.Path },
            dumpOptions: ValidDumpOptions(enableRowCount: true),
            processor: processor);

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(0, result);
        Assert.Single(processor.CountQueries);
        Assert.Empty(processor.DeleteQueries);
    }

    [Fact]
    public async Task RunAsync_DeletesSourceRowsWhenConfigured()
    {
        using var outputRoot = TempDirectory.Create();
        var processor = new FakeDuckDbProcessor
        {
            CountResult = 3,
            DeleteResult = 3
        };
        var service = CreateService(
            duckDbOptions: new DuckDbOptions { OutputRoot = outputRoot.Path },
            dumpOptions: ValidDumpOptions(deleteSourceAfterWrite: true),
            processor: processor);

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(0, result);
        var deleteSql = Assert.Single(processor.DeleteQueries);
        Assert.Contains("DELETE FROM \"pg_source\".\"test\".\"workflow\"", deleteSql);
        Assert.Contains("(status = 'Closed')", deleteSql);
    }

    [Fact]
    public async Task RunAsync_ReturnsRuntimeFailureWhenDeletedRowCountDoesNotMatch()
    {
        using var outputRoot = TempDirectory.Create();
        var processor = new FakeDuckDbProcessor
        {
            CountResult = 3,
            DeleteResult = 2
        };
        var service = CreateService(
            duckDbOptions: new DuckDbOptions { OutputRoot = outputRoot.Path },
            dumpOptions: ValidDumpOptions(deleteSourceAfterWrite: true),
            processor: processor);

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task RunAsync_ReturnsRuntimeFailureWhenCopyFails()
    {
        using var outputRoot = TempDirectory.Create();
        var processor = new FakeDuckDbProcessor
        {
            CopyException = new InvalidOperationException("copy failed")
        };
        var service = CreateService(
            duckDbOptions: new DuckDbOptions { OutputRoot = outputRoot.Path },
            processor: processor);

        var result = await service.RunAsync(CancellationToken.None);

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task RunAsync_ReturnsCancelledWhenCancellationIsRequested()
    {
        using var outputRoot = TempDirectory.Create();
        var service = CreateService(duckDbOptions: new DuckDbOptions { OutputRoot = outputRoot.Path });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await service.RunAsync(cancellation.Token);

        Assert.Equal(1, result);
    }

    [Fact]
    public void BuildMonthlySelect_QuotesAttachedTableAndAddsFilters()
    {
        var service = CreateService();
        var month = DumpMonth.Resolve("202605", TimeProvider.System);

        var sql = InvokePrivate<string>(service, "BuildMonthlySelect", ValidTable(), month);

        Assert.Contains("SELECT *", sql);
        Assert.Contains("FROM \"pg_source\".\"test\".\"workflow\"", sql);
        Assert.Contains("\"updated_on\" >= TIMESTAMP '2026-05-01 00:00:00'", sql);
        Assert.Contains("\"updated_on\" < TIMESTAMP '2026-06-01 00:00:00'", sql);
        Assert.Contains("(status = 'Closed')", sql);
    }

    [Fact]
    public void BuildMonthlyDelete_QuotesAttachedTableAndAddsFilters()
    {
        var service = CreateService();
        var month = DumpMonth.Resolve("202605", TimeProvider.System);

        var sql = InvokePrivate<string>(service, "BuildMonthlyDelete", ValidTable(), month);

        Assert.Contains("DELETE FROM \"pg_source\".\"test\".\"workflow\"", sql);
        Assert.Contains("\"updated_on\" >= TIMESTAMP '2026-05-01 00:00:00'", sql);
        Assert.Contains("(status = 'Closed')", sql);
    }

    [Fact]
    public void PreparePartitionDirectory_DoesNothingWhenDirectoryDoesNotExist()
    {
        var service = CreateService();
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        InvokePrivate<object?>(service, "PreparePartitionDirectory", directory);

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void PreparePartitionDirectory_ThrowsWhenDirectoryExistsAndOverwriteIsDisabled()
    {
        using var directory = TempDirectory.Create();
        var service = CreateService(dumpOptions: ValidDumpOptions(overwritePartition: false));

        var exception = AssertInvocationThrows<InvalidOperationException>(() =>
            InvokePrivate<object?>(service, "PreparePartitionDirectory", directory.Path));

        Assert.Contains("Partition already exists", exception.Message);
    }

    [Fact]
    public void PreparePartitionDirectory_DeletesExistingDirectoryWhenOverwriteIsEnabled()
    {
        using var directory = TempDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "old.parquet"), "old");
        var service = CreateService(dumpOptions: ValidDumpOptions(overwritePartition: true));

        InvokePrivate<object?>(service, "PreparePartitionDirectory", directory.Path);

        Assert.False(Directory.Exists(directory.Path));
    }

    [Fact]
    public void VerifyParquetOutput_ThrowsWhenDirectoryIsMissing()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var exception = AssertInvocationThrows<InvalidOperationException>(() =>
            InvokePrivateStatic<object?>(typeof(DbDumpService), "VerifyParquetOutput", directory));

        Assert.Contains("was not created", exception.Message);
    }

    [Fact]
    public void VerifyParquetOutput_ThrowsWhenDirectoryHasNoNonEmptyParquetFile()
    {
        using var directory = TempDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "empty.parquet"), "");

        var exception = AssertInvocationThrows<InvalidOperationException>(() =>
            InvokePrivateStatic<object?>(typeof(DbDumpService), "VerifyParquetOutput", directory.Path));

        Assert.Contains("No non-empty Parquet files", exception.Message);
    }

    [Fact]
    public void VerifyParquetOutput_AcceptsNonEmptyParquetFile()
    {
        using var directory = TempDirectory.Create();
        File.WriteAllText(Path.Combine(directory.Path, "data.parquet"), "data");

        InvokePrivateStatic<object?>(typeof(DbDumpService), "VerifyParquetOutput", directory.Path);
    }

    private static DbDumpService CreateService(
        PostgresOptions? postgresOptions = null,
        DuckDbOptions? duckDbOptions = null,
        DumpOptions? dumpOptions = null,
        IDuckDbProcessor? processor = null)
    {
        postgresOptions ??= new PostgresOptions { ConnectionString = "Host=localhost", AttachAlias = "pg_source" };
        duckDbOptions ??= new DuckDbOptions { OutputRoot = "out" };
        dumpOptions ??= ValidDumpOptions();
        processor ??= new FakeDuckDbProcessor();

        return new DbDumpService(
            Options.Create(postgresOptions),
            Options.Create(duckDbOptions),
            Options.Create(dumpOptions),
            processor,
            TimeProvider.System,
            NullLogger<DbDumpService>.Instance);
    }

    private static DumpOptions ValidDumpOptions(
        bool overwritePartition = false,
        bool enableRowCount = false,
        bool deleteSourceAfterWrite = false)
    {
        return new DumpOptions
        {
            Month = "202605",
            OverwritePartition = overwritePartition,
            EnableRowCount = enableRowCount,
            DeleteSourceAfterWrite = deleteSourceAfterWrite,
            Tables = [ValidTable()]
        };
    }

    private static TableDumpOptions ValidTable(
        string name = "Workflow",
        string sourceSchema = "test",
        string sourceTable = "workflow",
        string outputName = "workflow",
        string dateColumn = "updated_on",
        string? additionalWhere = "status = 'Closed'")
    {
        return new TableDumpOptions
        {
            Name = name,
            SourceSchema = sourceSchema,
            SourceTable = sourceTable,
            OutputName = outputName,
            DateColumn = dateColumn,
            AdditionalWhere = additionalWhere
        };
    }

    private static T InvokePrivate<T>(object instance, string methodName, params object?[] args)
    {
        var method = instance
            .GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        return (T)method.Invoke(instance, args)!;
    }

    private static T InvokePrivateStatic<T>(Type type, string methodName, params object?[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        return (T)method.Invoke(null, args)!;
    }

    private static T AssertInvocationThrows<T>(Action action)
        where T : Exception
    {
        var exception = Assert.Throws<TargetInvocationException>(action);
        return Assert.IsType<T>(exception.InnerException);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class FakeDuckDbProcessor : IDuckDbProcessor
    {
        public bool Initialized { get; private set; }

        public long CountResult { get; init; } = 0;

        public long DeleteResult { get; init; } = 0;

        public Exception? CopyException { get; init; }

        public List<string> CountQueries { get; } = [];

        public List<string> DeleteQueries { get; } = [];

        public List<CopyCall> CopyCalls { get; } = [];

        public void Initialize()
        {
            Initialized = true;
        }

        public void CopyPostgresQueryToParquet(
            string selectSql,
            string outputPath,
            DuckDbCopyOptions copyOptions)
        {
            if (CopyException is not null)
            {
                throw CopyException;
            }

            CopyCalls.Add(new CopyCall(selectSql, outputPath, copyOptions));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, "parquet-bytes");
        }

        public long CountPostgresQuery(string selectSql)
        {
            CountQueries.Add(selectSql);
            return CountResult;
        }

        public long DeletePostgresRows(string deleteSql)
        {
            DeleteQueries.Add(deleteSql);
            return DeleteResult;
        }
    }

    private sealed record CopyCall(
        string SelectSql,
        string OutputPath,
        DuckDbCopyOptions CopyOptions);
}
