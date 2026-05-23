# PgDuckDump Functional Specification

## Goal

Build a .NET 10 console application that exports configured PostgreSQL table data to monthly Parquet partitions through DuckDB.

The application runs one month at a time. For each configured source table, it writes one Parquet dataset folder and one monthly partition folder:

```text
{OutputRoot}/
  {OutputName}/
    monthOfYear=yyyyMM/
      data.parquet
```

Example for `test.workflow` and May 2026:

```text
parquet-output/
  workflow/
    monthOfYear=202605/
      data.parquet
```

The application must use bounded memory. Data rows must flow through DuckDB from PostgreSQL to Parquet; the .NET process must not materialize full table data in memory.

`Program.cs` must remain a thin application entry point. Runtime behavior belongs in services and utilities.

## Core Requirements

- Target framework: .NET 10.
- Source database: PostgreSQL.
- Processing engine: DuckDB with the DuckDB PostgreSQL extension.
- Output format: Parquet.
- Partition key: `monthOfYear`.
- Partition value format: `yyyyMM`, for example `202605`.
- Dump window: one complete calendar month per run.
- Expected monthly volume: approximately 1 million rows per table partition.
- Configuration source: `appsettings.json`, `appsettings.{env}.json`, and environment variables.
- Default cleanup behavior: do not delete source rows unless explicitly enabled.
- Cleanup behavior: when `Dump:DeleteSourceAfterWrite` is `true`, delete exactly the exported PostgreSQL rows only after Parquet output is successfully written and verified.

## Runtime Flow

1. Resolve the environment name.
   - Prefer `DOTNET_ENVIRONMENT`.
   - Fall back to `ASPNETCORE_ENVIRONMENT`.
   - Fall back to `Development`.

2. Load configuration from:
   - `appsettings.json`
   - `appsettings.{env}.json`
   - environment variables

3. Validate configuration before opening DuckDB or modifying data.

4. Resolve the dump month.
   - If `Dump:Month` is set, parse it as `yyyy-MM` or `yyyyMM`.
   - If `Dump:Month` is omitted, use the previous complete month.
   - Derive:
     - `MonthOfYear`: `yyyyMM`
     - `StartInclusive`: first day of month at `00:00:00`
     - `EndExclusive`: first day of next month at `00:00:00`

5. Initialize DuckDB.
   - Open `DuckDb:DatabasePath`.
   - Apply `DuckDb:Threads`.
   - Apply `DuckDb:MemoryLimit`.
   - Apply `DuckDb:TempDirectory` when configured.
   - Install and load the DuckDB `postgres` extension.
   - Attach PostgreSQL using `Postgres:AttachAlias`.
   - Attach PostgreSQL with `READ_ONLY` when `Dump:DeleteSourceAfterWrite` is `false`.
   - Attach PostgreSQL without `READ_ONLY` when `Dump:DeleteSourceAfterWrite` is `true`.

6. For each configured table:
   - Build one monthly filtered `SELECT`.
   - Count matching rows when row counting or source cleanup is enabled.
   - Prepare the output partition directory.
   - Run DuckDB `COPY (...) TO ... (FORMAT PARQUET, ...)`.
   - Verify that at least one non-empty `.parquet` file exists in the partition directory.
   - If source cleanup is enabled, run a filtered `DELETE` using the same table and predicates.
   - If deleted row count is available, compare it with the pre-delete matching row count.
   - Log progress and summary.

7. Return an exit code:
   - `0`: all configured dumps succeeded.
   - `1`: runtime failure.
   - `2`: invalid configuration.

## Configuration

### Example `appsettings.json`

```json
{
  "Postgres": {
    "ConnectionString": "host=127.0.0.1 port=5433 dbname=postgres user=postgres password=postgres",
    "AttachAlias": "pg_source"
  },
  "DuckDb": {
    "DatabasePath": "./work/pg-duckdump.duckdb",
    "OutputRoot": "./parquet-output",
    "TempDirectory": "./work/tmp",
    "MemoryLimit": "1GB",
    "Threads": 1,
    "RowGroupSize": 122880,
    "Compression": "ZSTD"
  },
  "Dump": {
    "Month": "202605",
    "OverwritePartition": true,
    "DeleteSourceAfterWrite": true,
    "EnableRowCount": true,
    "Tables": [
      {
        "Name": "test.workflow",
        "SourceSchema": "test",
        "SourceTable": "workflow",
        "OutputName": "workflow",
        "DateColumn": "updated_on",
        "AdditionalWhere": "status = 'Closed'"
      }
    ]
  }
}
```

### Configuration Model

```csharp
public sealed class PostgresOptions
{
    public required string ConnectionString { get; init; }
    public string AttachAlias { get; init; } = "pg_source";
}

public sealed class DuckDbOptions
{
    public string DatabasePath { get; init; } = ":memory:";
    public required string OutputRoot { get; init; }
    public string? TempDirectory { get; init; }
    public string MemoryLimit { get; init; } = "1GB";
    public int Threads { get; init; } = 1;
    public int RowGroupSize { get; init; } = 122880;
    public string Compression { get; init; } = "ZSTD";
}

public sealed class DumpOptions
{
    public string? Month { get; init; }
    public bool OverwritePartition { get; init; }
    public bool DeleteSourceAfterWrite { get; init; }
    public bool EnableRowCount { get; init; }
    public IReadOnlyList<TableDumpOptions> Tables { get; init; } = [];
}

public sealed class TableDumpOptions
{
    public required string Name { get; init; }
    public required string SourceSchema { get; init; }
    public required string SourceTable { get; init; }
    public required string OutputName { get; init; }
    public required string DateColumn { get; init; }
    public string? AdditionalWhere { get; init; }
}
```

## Project Structure

```text
PgDuckDump/
  Program.cs
  appsettings.json
  appsettings.Development.json
  Configuration/
    PostgresOptions.cs
    DuckDbOptions.cs
    DumpOptions.cs
    TableDumpOptions.cs
  Services/
    DbDumpService.cs
    DumpMonth.cs
    InvalidConfigurationException.cs
  Utilities/
    DuckDbProcessor.cs
    DuckDbCopyOptions.cs
    SqlIdentifier.cs
    SqlInjectionGuard.cs
```

## Component Responsibilities

### `Program.cs`

`Program.cs` must only:

- Build the generic host.
- Resolve the environment name.
- Load configuration.
- Register options.
- Register services.
- Resolve `DbDumpService`.
- Invoke the dump workflow.
- Return the process exit code.

`Program.cs` must not:

- Build SQL.
- Open DuckDB connections directly.
- Enumerate tables.
- Contain dumping, filtering, cleanup, or partition logic.

### `DbDumpService`

`DbDumpService` owns dump orchestration:

- Validate dump configuration.
- Resolve the monthly window.
- Iterate configured tables.
- Build table-specific monthly `SELECT` statements.
- Build matching table-specific monthly `DELETE` statements when cleanup is enabled.
- Build output partition paths.
- Enforce `OverwritePartition`.
- Call `DuckDbProcessor` for count, copy, and delete operations.
- Verify Parquet output before cleanup.
- Compare exported and deleted row counts when both are available.
- Log progress and failures.

Suggested public surface:

```csharp
public sealed class DbDumpService
{
    public Task<int> RunAsync(CancellationToken cancellationToken);
}
```

### `DuckDbProcessor`

`DuckDbProcessor` owns direct DuckDB operations:

- Open and dispose the DuckDB connection.
- Configure DuckDB runtime settings.
- Install and load DuckDB extensions.
- Attach PostgreSQL.
- Execute SQL commands.
- Execute scalar queries.
- Run `COPY` from a PostgreSQL-attached query to Parquet.
- Run filtered PostgreSQL deletes when requested by `DbDumpService`.

Suggested public surface:

```csharp
public sealed class DuckDbProcessor : IDisposable, IAsyncDisposable
{
    public void Initialize();

    public void CopyPostgresQueryToParquet(
        string selectSql,
        string outputPath,
        DuckDbCopyOptions copyOptions);

    public long CountPostgresQuery(string selectSql);

    public long DeletePostgresRows(string deleteSql);
}
```

`DuckDbProcessor` must not return row collections for dump data.

### SQL Helpers

`SqlIdentifier` owns:

- SQL identifier quoting.
- SQL string literal escaping.
- safe filesystem path segment validation.

`SqlInjectionGuard` owns:

- validation of trusted operator predicates such as `AdditionalWhere`.

## Source Filtering

For every table, the source filter is composed from:

- required monthly date range on `DateColumn`
- optional table-specific `AdditionalWhere`

The monthly range is always:

```sql
"DateColumn" >= TIMESTAMP '{StartInclusive}'
AND "DateColumn" < TIMESTAMP '{EndExclusive}'
```

The start boundary is inclusive. The end boundary is exclusive.

### Workflow Example

Source table:

```sql
create table test.workflow
(
    id          serial
        constraint workflow_pk
            primary key,
    workflow_id integer,
    status      varchar(50),
    created_on  timestamp,
    assignee    varchar(50),
    updated_on  timestamp
);

alter table test.workflow
    owner to postgres;
```

Table configuration:

```json
{
  "Name": "test.workflow",
  "SourceSchema": "test",
  "SourceTable": "workflow",
  "OutputName": "workflow",
  "DateColumn": "updated_on",
  "AdditionalWhere": "status = 'Closed'"
}
```

For May 2026, the generated export query must be:

```sql
SELECT *
FROM "pg_source"."test"."workflow"
WHERE "updated_on" >= TIMESTAMP '2026-05-01 00:00:00'
  AND "updated_on" < TIMESTAMP '2026-06-01 00:00:00'
  AND (status = 'Closed')
```

The generated cleanup query must use the same table and predicates:

```sql
DELETE FROM "pg_source"."test"."workflow"
WHERE "updated_on" >= TIMESTAMP '2026-05-01 00:00:00'
  AND "updated_on" < TIMESTAMP '2026-06-01 00:00:00'
  AND (status = 'Closed')
```

Rules:

- `DateColumn` is required for every table.
- `AdditionalWhere` is optional.
- If `AdditionalWhere` is configured, it must be applied to both export and cleanup.
- `AdditionalWhere` must be wrapped in parentheses and appended with `AND`.
- `AdditionalWhere` is trusted operator-owned SQL predicate text. It must still pass the SQL injection checks below.

## Parquet Write Strategy

DuckDB must write the Parquet file with a `COPY` statement similar to:

```sql
COPY (
  SELECT *
  FROM "pg_source"."test"."workflow"
  WHERE "updated_on" >= TIMESTAMP '2026-05-01 00:00:00'
    AND "updated_on" < TIMESTAMP '2026-06-01 00:00:00'
    AND (status = 'Closed')
) TO './parquet-output/workflow/monthOfYear=202605/data.parquet'
  (FORMAT PARQUET, COMPRESSION 'ZSTD', ROW_GROUP_SIZE 122880);
```

The partition value is represented by the folder name `monthOfYear=yyyyMM`.

The `monthOfYear` value is not required as a physical Parquet column. If downstream consumers require the column inside the files, add a future configuration option such as `IncludePartitionColumn` and project it in the export query:

```sql
SELECT *, '202605' AS monthOfYear
```

## Source Cleanup Strategy

Source cleanup is controlled by `Dump:DeleteSourceAfterWrite`.

When `Dump:DeleteSourceAfterWrite` is `false`:

- PostgreSQL must be attached read-only.
- No source rows may be deleted.

When `Dump:DeleteSourceAfterWrite` is `true`:

- PostgreSQL must be attached with write capability.
- The PostgreSQL user must have `SELECT` and `DELETE` permissions on configured tables.
- Cleanup runs table by table after the Parquet `COPY` succeeds.
- Cleanup must verify that at least one non-empty `.parquet` file exists in the target partition directory before issuing `DELETE`.
- Cleanup must use the same table, date window, and `AdditionalWhere` predicate as the export query.
- Cleanup must run only for the table whose Parquet write just completed.
- Cleanup must not run after configuration validation failure, export failure, or output verification failure.
- Cleanup failure must fail the overall run.
- Deleted row count should be logged when available.
- If row count is available before deletion, the deleted count must match it. A mismatch is a runtime failure.

If large deletes cause lock or timeout issues, add a future configuration option for batched deletes. The initial behavior is one filtered `DELETE` statement per table/month.

Production cleanup credentials should be scoped to only the schemas and tables configured for dumping.

## SQL Injection Requirements

Configuration values are not end-user input, but the application must treat all configuration-derived SQL defensively.

Required protections:

- Quote all SQL identifiers from configuration with a shared helper:
  - `Postgres:AttachAlias`
  - `Dump:Tables:SourceSchema`
  - `Dump:Tables:SourceTable`
  - `Dump:Tables:DateColumn`
- Identifier quoting must escape embedded double quotes by doubling them.
- Create SQL string literals with a shared helper that escapes single quotes by doubling them.
- Generate month boundaries from parsed dates, not raw SQL from configuration.
- Parse numeric DuckDB settings, such as `Threads` and `RowGroupSize`, into numeric types before SQL generation.
- Pass filesystem paths used in SQL, such as `COPY TO` and `SET temp_directory`, as SQL string literals.
- Validate `OutputName` as a safe filesystem path segment. It must reject path separators, `.` and `..`, parent-directory traversal, and shell-like metacharacters.
- Generate delete SQL from the same validated identifiers and predicates as export SQL.
- Do not log raw SQL containing credentials, raw PostgreSQL connection strings, or secrets.

`AdditionalWhere` requirements:

- `AdditionalWhere` is trusted operator-owned predicate SQL only.
- `AdditionalWhere` must not come from users, HTTP requests, files dropped by untrusted parties, or other untrusted input.
- `AdditionalWhere` must be appended only inside parentheses after the generated monthly predicates.
- `AdditionalWhere` accepts predicate syntax only, not full statements.
- Validation must reject statement separators and SQL comment tokens:
  - semicolon: `;`
  - line comment: `--`
  - block comment start/end: `/*` and `*/`
- Validation must reject obvious mutation or DDL keywords case-insensitively:
  - `INSERT`
  - `UPDATE`
  - `DELETE`
  - `DROP`
  - `ALTER`
  - `TRUNCATE`
  - `CREATE`
  - `ATTACH`
  - `COPY`
  - `CALL`
  - `PRAGMA`

If richer filters are needed later, prefer a structured filter model over larger raw SQL fragments.

## Memory Constraints

The application must be designed for limited memory:

- Use DuckDB `COPY` directly from the PostgreSQL-attached query to Parquet.
- Do not read source rows through `DuckDBDataReader` except metadata or scalar counts.
- Configure `SET memory_limit = '{MemoryLimit}'`.
- Configure `SET temp_directory = '{TempDirectory}'` when provided.
- Configure `SET threads = {Threads}`.
- Keep .NET objects limited to configuration, table metadata, SQL strings, and summary results.
- Process one table/month copy operation at a time unless parallelism is explicitly introduced later.

## Error Handling

- Invalid configuration must fail before any dump starts.
- Missing output root must be created automatically.
- Existing partition behavior is controlled by `Dump:OverwritePartition`.
- If the partition exists and `OverwritePartition` is `false`, fail before writing.
- If the partition exists and `OverwritePartition` is `true`, delete the existing partition before writing.
- Source rows must not be deleted unless Parquet write and output verification both succeed.
- Per-table failures must be logged with table name and month.
- Default behavior is stop on first table failure.

Exit codes:

- `0`: all configured dumps succeeded.
- `1`: runtime failure.
- `2`: invalid configuration.

## Logging

Use `Microsoft.Extensions.Logging`.

Log at minimum:

- Environment name.
- Output root.
- Dump month.
- Table start.
- Table completion.
- Output partition path.
- Exported row count when available.
- Deleted row count when cleanup is enabled and count is available.
- Error details.

Do not log passwords, raw connection strings, or secret values.

## Testing Requirements

- Unit tests must cover identifier quoting for names containing double quotes.
- Unit tests must cover string literal escaping for values containing single quotes.
- Unit tests must verify unsafe `AdditionalWhere` rejection for `;`, `--`, `/*`, `*/`, and mutation/DDL keywords.
- Unit tests must verify generated export SQL uses quoted identifiers and generated timestamp literals.
- Unit tests must verify generated export SQL combines the monthly date filter and configured `AdditionalWhere`, for example `updated_on` plus `status = 'Closed'` for `test.workflow`.
- Unit tests must verify generated delete SQL targets the same quoted table and predicates as the export query.
- Unit tests must verify unsafe `OutputName` values such as `../tbl_1`, `a/b`, `a\b`, `.`, and `..` are rejected.
- Integration tests should cover one table/month export to `monthOfYear=yyyyMM/data.parquet`.
- Integration tests should cover cleanup disabled and cleanup enabled behavior.

## Acceptance Criteria

- Running with `DOTNET_ENVIRONMENT=Development` loads `appsettings.Development.json`.
- `Program.cs` contains only host/bootstrap logic.
- DuckDB setup and SQL execution live in `DuckDbProcessor`.
- Dump orchestration and filter construction live in `DbDumpService`.
- A table `test.workflow` dumped for May 2026 writes Parquet to:

```text
{OutputRoot}/workflow/monthOfYear=202605/data.parquet
```

- The `test.workflow` export filter uses:
  - `"updated_on" >= TIMESTAMP '2026-05-01 00:00:00'`
  - `"updated_on" < TIMESTAMP '2026-06-01 00:00:00'`
  - `(status = 'Closed')`
- SQL generation follows the SQL injection requirements.
- When `DeleteSourceAfterWrite` is `false`, no source rows are deleted and PostgreSQL export access remains read-only.
- When `DeleteSourceAfterWrite` is `true`, source rows are deleted only after successful Parquet write verification.
- The application can process approximately 1 million rows for a month without loading all records into .NET memory.
- `Dump:OverwritePartition` controls existing partition behavior.
- `dotnet build` succeeds.
