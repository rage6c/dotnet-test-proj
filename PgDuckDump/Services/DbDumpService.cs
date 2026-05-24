using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PgDuckDump.Configuration;
using PgDuckDump.Utilities;

namespace PgDuckDump.Services;

public sealed class DbDumpService(
    IOptions<PostgresOptions> postgresOptions,
    IOptions<DuckDbOptions> duckDbOptions,
    IOptions<DumpOptions> dumpOptions,
    IDuckDbProcessor duckDbProcessor,
    TimeProvider timeProvider,
    ILogger<DbDumpService> logger)
{
    private readonly PostgresOptions _postgresOptions = postgresOptions.Value;
    private readonly DuckDbOptions _duckDbOptions = duckDbOptions.Value;
    private readonly DumpOptions _dumpOptions = dumpOptions.Value;

    public Task<int> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            ValidateConfiguration();

            var dumpMonth = DumpMonth.Resolve(_dumpOptions.Month, timeProvider);
            var outputRoot = Path.GetFullPath(_duckDbOptions.OutputRoot);

            Directory.CreateDirectory(outputRoot);
            duckDbProcessor.Initialize();

            logger.LogInformation("Output root: {OutputRoot}", outputRoot);
            logger.LogInformation(
                "Dumping month {MonthOfYear}: [{StartInclusive}, {EndExclusive})",
                dumpMonth.MonthOfYear,
                dumpMonth.StartInclusive,
                dumpMonth.EndExclusive);

            foreach (var table in _dumpOptions.Tables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DumpTable(table, dumpMonth, outputRoot);
            }

            logger.LogInformation("All configured dumps completed.");
            return Task.FromResult(0);
        }
        catch (InvalidConfigurationException ex)
        {
            logger.LogError(ex, "Invalid configuration: {Message}", ex.Message);
            return Task.FromResult(2);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Dump cancelled.");
            return Task.FromResult(1);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dump failed.");
            return Task.FromResult(1);
        }
    }

    private void DumpTable(TableDumpOptions table, DumpMonth dumpMonth, string outputRoot)
    {
        var partitionDirectory = Path.Combine(outputRoot, table.OutputName, $"monthOfYear={dumpMonth.MonthOfYear}");
        var outputFile = Path.Combine(partitionDirectory, "data.parquet");
        var selectSql = BuildMonthlySelect(table, dumpMonth);
        var expectedRowCount = _dumpOptions.EnableRowCount || _dumpOptions.DeleteSourceAfterWrite
            ? duckDbProcessor.CountPostgresQuery(selectSql)
            : (long?)null;

        logger.LogInformation(
            "Starting table {TableName} for partition monthOfYear={MonthOfYear}",
            table.Name,
            dumpMonth.MonthOfYear);

        PreparePartitionDirectory(partitionDirectory);

        duckDbProcessor.CopyPostgresQueryToParquet(
            selectSql,
            outputFile,
            new DuckDbCopyOptions(_duckDbOptions.Compression, _duckDbOptions.RowGroupSize));

        VerifyParquetOutput(partitionDirectory);

        long? deletedRowCount = null;
        if (_dumpOptions.DeleteSourceAfterWrite)
        {
            deletedRowCount = duckDbProcessor.DeletePostgresRows(BuildMonthlyDelete(table, dumpMonth));
            if (deletedRowCount >= 0 && expectedRowCount != deletedRowCount)
            {
                throw new InvalidOperationException(
                    $"Deleted row count mismatch for {table.Name}. Expected {expectedRowCount:N0}, deleted {deletedRowCount:N0}.");
            }
        }

        if (_dumpOptions.EnableRowCount || _dumpOptions.DeleteSourceAfterWrite)
        {
            logger.LogInformation(
                "Completed table {TableName}: {RowCount} exported row(s), {DeletedRowCount} deleted row(s), output {OutputPath}",
                table.Name,
                expectedRowCount,
                deletedRowCount,
                partitionDirectory);
            return;
        }

        logger.LogInformation(
            "Completed table {TableName}: output {OutputPath}",
            table.Name,
            partitionDirectory);
    }

    private string BuildMonthlySelect(TableDumpOptions table, DumpMonth dumpMonth)
    {
        return
            $"""
             SELECT *
             FROM {BuildAttachedTableName(table)}
             WHERE {BuildMonthlyWhereClause(table, dumpMonth)}
             """;
    }

    private string BuildMonthlyDelete(TableDumpOptions table, DumpMonth dumpMonth)
    {
        return
            $"""
             DELETE FROM {BuildAttachedTableName(table)}
             WHERE {BuildMonthlyWhereClause(table, dumpMonth)}
             """;
    }

    private void PreparePartitionDirectory(string partitionDirectory)
    {
        if (!Directory.Exists(partitionDirectory))
        {
            return;
        }

        if (!_dumpOptions.OverwritePartition)
        {
            throw new InvalidOperationException(
                $"Partition already exists: {partitionDirectory}. Set Dump:OverwritePartition to true to replace it.");
        }

        Directory.Delete(partitionDirectory, recursive: true);
    }

    private static void VerifyParquetOutput(string partitionDirectory)
    {
        if (!Directory.Exists(partitionDirectory))
        {
            throw new InvalidOperationException($"Parquet partition directory was not created: {partitionDirectory}");
        }

        var hasParquetFile = Directory
            .EnumerateFiles(partitionDirectory, "*.parquet", SearchOption.TopDirectoryOnly)
            .Any(file => new FileInfo(file).Length > 0);

        if (!hasParquetFile)
        {
            throw new InvalidOperationException($"No non-empty Parquet files were written to: {partitionDirectory}");
        }
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_postgresOptions.ConnectionString))
        {
            throw new InvalidConfigurationException("Postgres:ConnectionString is required.");
        }

        if (string.IsNullOrWhiteSpace(_postgresOptions.AttachAlias))
        {
            throw new InvalidConfigurationException("Postgres:AttachAlias is required.");
        }

        if (string.IsNullOrWhiteSpace(_duckDbOptions.OutputRoot))
        {
            throw new InvalidConfigurationException("DuckDb:OutputRoot is required.");
        }

        if (_duckDbOptions.Threads < 1)
        {
            throw new InvalidConfigurationException("DuckDb:Threads must be at least 1.");
        }

        if (_duckDbOptions.RowGroupSize < 1)
        {
            throw new InvalidConfigurationException("DuckDb:RowGroupSize must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(_duckDbOptions.Compression))
        {
            throw new InvalidConfigurationException("DuckDb:Compression is required.");
        }

        if (_dumpOptions.Tables.Count == 0)
        {
            throw new InvalidConfigurationException("Dump:Tables must contain at least one table.");
        }

        foreach (var table in _dumpOptions.Tables)
        {
            ValidateTable(table);
        }
    }

    private static void ValidateTable(TableDumpOptions table)
    {
        Require(table.Name, "Dump:Tables:Name");
        Require(table.SourceSchema, "Dump:Tables:SourceSchema");
        Require(table.SourceTable, "Dump:Tables:SourceTable");
        Require(table.OutputName, "Dump:Tables:OutputName");
        Require(table.DateColumn, "Dump:Tables:DateColumn");

        try
        {
            SqlIdentifier.ValidateSafePathSegment(table.OutputName, "Dump:Tables:OutputName");
            SqlInjectionGuard.ValidateTrustedPredicate(table.AdditionalWhere, "Dump:Tables:AdditionalWhere");
        }
        catch (ArgumentException ex)
        {
            throw new InvalidConfigurationException(ex.Message);
        }
    }

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidConfigurationException($"{name} is required.");
        }
    }

    private string BuildAttachedTableName(TableDumpOptions table)
    {
        return
            $"{SqlIdentifier.Quote(_postgresOptions.AttachAlias)}.{SqlIdentifier.Quote(table.SourceSchema)}.{SqlIdentifier.Quote(table.SourceTable)}";
    }

    private static string BuildMonthlyWhereClause(TableDumpOptions table, DumpMonth dumpMonth)
    {
        var filters = new List<string>
        {
            $"{SqlIdentifier.Quote(table.DateColumn)} >= TIMESTAMP {SqlIdentifier.Literal(FormatTimestamp(dumpMonth.StartInclusive))}",
            $"{SqlIdentifier.Quote(table.DateColumn)} < TIMESTAMP {SqlIdentifier.Literal(FormatTimestamp(dumpMonth.EndExclusive))}"
        };

        if (!string.IsNullOrWhiteSpace(table.AdditionalWhere))
        {
            filters.Add($"({table.AdditionalWhere})");
        }

        return string.Join($"{Environment.NewLine}  AND ", filters);
    }

    private static string FormatTimestamp(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss");
}
