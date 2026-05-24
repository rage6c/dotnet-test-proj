# PgDuckDump

A .NET 10 console app that uses DuckDB's Postgres extension to dump configured Postgres tables to monthly Parquet partitions.

The app streams data through DuckDB from PostgreSQL to Parquet. It does not materialize source table rows in .NET memory.

## Configure

Edit `appsettings.Development.json` or create another environment-specific file such as `appsettings.Production.json`.

```json
{
  "Postgres": {
    "ConnectionString": "host=localhost port=5432 dbname=mydb user=postgres password=secret",
    "AttachAlias": "pg_source"
  },
  "DuckDb": {
    "DatabasePath": "./work/pg-duckdump.duckdb",
    "OutputRoot": "./parquet-output",
    "TempDirectory": "./work/tmp",
    "MemoryLimit": "1GB",
    "Threads": 4,
    "RowGroupSize": 122880,
    "Compression": "ZSTD"
  },
  "Dump": {
    "Month": "2026-05",
    "OverwritePartition": true,
    "DeleteSourceAfterWrite": false,
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

If `Dump:Month` is omitted, the app dumps the previous complete month.

Set `Dump:DeleteSourceAfterWrite` to `true` only when you want the app to delete matching source rows from Postgres after the Parquet file has been written and verified. This requires `DELETE` permission on the configured source tables.

`Dump:Tables:*:AdditionalWhere` is optional and is appended to the monthly date filter. It must be a single trusted predicate, for example:

```sql
status = 'Closed'
```

The app rejects dangerous SQL tokens and keywords such as `;`, comments, `DROP`, `DELETE`, `INSERT`, `UPDATE`, `COPY`, and `PRAGMA`.

## Run

```bash
DOTNET_ENVIRONMENT=Development dotnet run --project PgDuckDump
```

For table `test.workflow` and dump month May 2026, output is written to:

```text
parquet-output/
  workflow/
    monthOfYear=202605/
      data.parquet
```

The app copies data with DuckDB `COPY (...) TO ... (FORMAT PARQUET)` and does not load table rows into .NET memory.

## Runtime Flow

1. Load configuration from `appsettings.json`, `appsettings.{env}.json`, and environment variables.
2. Validate PostgreSQL, DuckDB, and table dump options.
3. Resolve the dump month.
4. Initialize DuckDB and attach PostgreSQL.
5. For each table, build a monthly `SELECT` using:
   - `DateColumn >= month start`
   - `DateColumn < next month start`
   - optional `AdditionalWhere`
6. Write the result to `{OutputRoot}/{OutputName}/monthOfYear=yyyyMM/data.parquet`.
7. Verify a non-empty parquet file was written.
8. If `DeleteSourceAfterWrite` is enabled, delete the same filtered rows from PostgreSQL.

## Project Structure

```text
PgDuckDump/
  Program.cs
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
    IDuckDbProcessor.cs
    DuckDbProcessor.cs
    DuckDbCopyOptions.cs
    SqlIdentifier.cs
    SqlInjectionGuard.cs

PgDuckDump.Tests/
  DbDumpServiceTests.cs
  DumpMonthTests.cs
  OptionsTests.cs
  SqlIdentifierTests.cs
  SqlInjectionGuardTests.cs
```

`DbDumpService` owns orchestration and depends on `IDuckDbProcessor`. `DuckDbProcessor` is the production DuckDB/PostgreSQL adapter.

## Tests

Run the unit tests:

```bash
dotnet test PgDuckDump.Tests/PgDuckDump.Tests.csproj
```

Run tests with coverage:

```bash
dotnet test PgDuckDump.Tests/PgDuckDump.Tests.csproj \
  --settings PgDuckDump.Tests/PgDuckDump.Tests.runsettings \
  --collect:"XPlat Code Coverage" \
  --results-directory PgDuckDump.Tests/TestResults
```

Generate the HTML coverage report:

```bash
/Users/rage6c/.dotnet/tools/reportgenerator \
  -reports:PgDuckDump.Tests/TestResults/*/coverage.cobertura.xml \
  -targetdir:PgDuckDump.Tests/CoverageReport \
  '-reporttypes:Html;TextSummary;Cobertura'
```

Latest coverage summary:

```text
Line coverage:   100%
Branch coverage: 98.5%
Method coverage: 100%
```

Coverage report:

```text
PgDuckDump.Tests/CoverageReport/index.html
```

`Program.cs` and `DuckDbProcessor.cs` are excluded from unit-test coverage. They are the console entry point and the external DuckDB/PostgreSQL adapter; orchestration is covered through `DbDumpService` with a fake `IDuckDbProcessor`.
