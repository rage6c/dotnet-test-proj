using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PgDuckDump.Configuration;

namespace PgDuckDump.Utilities;

public sealed class DuckDbProcessor(
    IOptions<PostgresOptions> postgresOptions,
    IOptions<DuckDbOptions> duckDbOptions,
    IOptions<DumpOptions> dumpOptions,
    ILogger<DuckDbProcessor> logger) : IDuckDbProcessor, IDisposable, IAsyncDisposable
{
    private readonly PostgresOptions _postgresOptions = postgresOptions.Value;
    private readonly DuckDbOptions _duckDbOptions = duckDbOptions.Value;
    private readonly DumpOptions _dumpOptions = dumpOptions.Value;
    private DuckDBConnection? _connection;
    private bool _initialized;

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        EnsureParentDirectory(_duckDbOptions.DatabasePath);
        EnsureParentDirectory(_duckDbOptions.TempDirectory);

        _connection = new DuckDBConnection($"Data Source={_duckDbOptions.DatabasePath}");
        _connection.Open();

        Execute($"SET threads = {Math.Max(1, _duckDbOptions.Threads)};");
        Execute($"SET memory_limit = {SqlIdentifier.Literal(_duckDbOptions.MemoryLimit)};");

        if (!string.IsNullOrWhiteSpace(_duckDbOptions.TempDirectory))
        {
            Directory.CreateDirectory(_duckDbOptions.TempDirectory);
            Execute($"SET temp_directory = {SqlIdentifier.Literal(_duckDbOptions.TempDirectory)};");
        }

        Execute("INSTALL postgres;");
        Execute("LOAD postgres;");

        var attachMode = _dumpOptions.DeleteSourceAfterWrite ? string.Empty : ", READ_ONLY";
        Execute(
            $"ATTACH {SqlIdentifier.Literal(_postgresOptions.ConnectionString)} AS {SqlIdentifier.Quote(_postgresOptions.AttachAlias)} (TYPE postgres{attachMode});");

        _initialized = true;

        logger.LogInformation(
            "DuckDB initialized with database path {DatabasePath}, memory limit {MemoryLimit}, threads {Threads}",
            _duckDbOptions.DatabasePath,
            _duckDbOptions.MemoryLimit,
            _duckDbOptions.Threads);
    }

    public void CopyPostgresQueryToParquet(
        string selectSql,
        string outputPath,
        DuckDbCopyOptions copyOptions)
    {
        EnsureInitialized();

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        Execute(
            $"""
             COPY (
             {selectSql}
             ) TO {SqlIdentifier.Literal(outputPath)}
             (FORMAT PARQUET, COMPRESSION {SqlIdentifier.Literal(copyOptions.Compression.ToUpperInvariant())}, ROW_GROUP_SIZE {copyOptions.RowGroupSize});
             """);
    }

    public long CountPostgresQuery(string selectSql)
    {
        EnsureInitialized();
        return ExecuteScalar<long>(
            $"""
             SELECT count(*)
             FROM (
             {selectSql}
             ) AS dump_count
             """);
    }

    public long DeletePostgresRows(string deleteSql)
    {
        EnsureInitialized();
        return Execute(deleteSql);
    }

    public ValueTask DisposeAsync()
    {
        _connection?.Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }

    private int Execute(string sql)
    {
        using var command = CreateCommand(sql);
        return command.ExecuteNonQuery();
    }

    private T ExecuteScalar<T>(string sql)
    {
        using var command = CreateCommand(sql);
        var value = command.ExecuteScalar();

        if (value is null or DBNull)
        {
            throw new InvalidOperationException($"Query returned no value: {sql}");
        }

        return (T)Convert.ChangeType(value, typeof(T));
    }

    private DuckDBCommand CreateCommand(string sql)
    {
        EnsureConnectionOpen();
        var command = _connection!.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("DuckDB has not been initialized.");
        }
    }

    private void EnsureConnectionOpen()
    {
        if (_connection is null)
        {
            throw new InvalidOperationException("DuckDB connection has not been opened.");
        }
    }

    private static void EnsureParentDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == ":memory:")
        {
            return;
        }

        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }
    }
}
