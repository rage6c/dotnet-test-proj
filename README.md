# Dotnet Test Project

This repository contains a small .NET 10 data pipeline built around PostgreSQL, DuckDB, and Parquet.

The solution has two console applications and one API:

- `PgDuckDump`: dumps monthly slices of configured PostgreSQL tables into partitioned Parquet folders.
- `ParquetLoader`: reads partitioned Parquet folders back into strongly typed DTOs with DuckDB-backed filtering.
- `ParquetLoaderApi`: exposes REST endpoints over the `ParquetLoader` workflow dataset loader.

Both applications have xUnit test projects.

## Solution

The root solution file is:

```text
DotnetTestProj.slnx
```

Projects included in the solution:

```text
PgDuckDump/
  PgDuckDump.csproj

PgDuckDump.Tests/
  PgDuckDump.Tests.csproj

ParquetLoader/
  ParquetLoader.csproj

ParquetLoader.Tests/
  ParquetLoader.Tests.csproj

ParquetLoaderApi/
  ParquetLoaderApi.csproj

ParquetLoaderApi.Tests/
  ParquetLoaderApi.Tests.csproj
```

Build everything:

```bash
dotnet build DotnetTestProj.slnx
```

Run all tests:

```bash
dotnet test DotnetTestProj.slnx
```

## Data Flow

```text
PostgreSQL
  -> PgDuckDump
  -> parquet-output/{table}/monthOfYear=yyyyMM/data.parquet
  -> ParquetLoader
  -> JSON log output
```

`PgDuckDump` uses DuckDB's PostgreSQL extension to copy source rows directly from PostgreSQL into Parquet. It applies a monthly date filter and optional configured predicates, then writes Hive-style partition folders such as `monthOfYear=202605`.

`ParquetLoader` uses DuckDB `read_parquet(..., hive_partitioning = true)` to read those partition folders. It translates supported LINQ expression predicates into DuckDB SQL so rows are filtered while loading, not after all data is materialized in memory.

`ParquetLoaderApi` uses the same loader through dependency injection and exposes the workflow dataset over HTTP.

## PgDuckDump

Project documentation:

```text
PgDuckDump/README.md
PgDuckDump/postgres-duckdb-parquet-dump-spec.md
```

Run with the development profile:

```bash
DOTNET_ENVIRONMENT=Development dotnet run --project PgDuckDump
```

Configuration is loaded from:

```text
PgDuckDump/appsettings.json
PgDuckDump/appsettings.Development.json
```

Important configuration sections:

- `Postgres`: PostgreSQL connection and DuckDB attach alias.
- `DuckDb`: DuckDB database path, output root, memory, threads, row group size, and compression.
- `Dump`: target month, delete-after-write behavior, row count verification, and table dump definitions.

For each configured table, output is written to:

```text
{OutputRoot}/{OutputName}/monthOfYear=yyyyMM/data.parquet
```

## ParquetLoader

Project documentation:

```text
ParquetLoader/README.md
ParquetLoader/parquet-loader-spec.md
```

Run:

```bash
dotnet run --project ParquetLoader
```

Configuration is loaded from:

```text
ParquetLoader/appsettings.json
```

The `ParquetLoader` section defines `BaseParquetPath`. The default workflow dataset path is:

```text
{BaseParquetPath}/workflow
```

The current loader implementation includes:

- `ParquetLoads/ParquetLoader.cs`: shared abstract loader.
- `ParquetLoads/WorkflowParquetLoader/WorkflowDto.cs`: DTO for the `workflow` dataset.
- `ParquetLoads/WorkflowParquetLoader/WorkflowParquetLoader.cs`: workflow-specific loader and column mapping.

## ParquetLoaderApi

Run:

```bash
dotnet run --project ParquetLoaderApi
```

Configuration is loaded from:

```text
ParquetLoaderApi/appsettings.json
```

The API reuses the `ParquetLoader` section and resolves `BaseParquetPath` relative to the API project folder.

Endpoints:

```text
GET /health
GET /api/workflow
```

Workflow query parameters:

- `limit`: positive row limit, default `100`, max `10000`.
- `parquetFolder`: optional folder override. Omit it to use `{BaseParquetPath}/workflow`.
- `monthOfYear`: partition filter, for example `202605`.
- `minId` and `maxId`: inclusive `id` bounds.
- `status` or `statuses`: repeated or comma-separated status values.
- `assignee`: exact assignee filter.
- `excludeAssignee`: assignee value to exclude.

Example:

```bash
curl "http://localhost:5253/api/workflow?monthOfYear=202605&status=Closed&status=Resolved&excludeAssignee=blocked_user&limit=10"
```

## Coverage

Run PgDuckDump coverage:

```bash
dotnet test PgDuckDump.Tests/PgDuckDump.Tests.csproj \
  --settings PgDuckDump.Tests/PgDuckDump.Tests.runsettings \
  --collect:"XPlat Code Coverage" \
  --results-directory PgDuckDump.Tests/TestResults
```

Run ParquetLoader coverage:

```bash
dotnet test ParquetLoader.Tests/ParquetLoader.Tests.csproj \
  --settings ParquetLoader.Tests/ParquetLoader.Tests.runsettings \
  --collect:"XPlat Code Coverage" \
  --results-directory ParquetLoader.Tests/TestResults
```

Run ParquetLoaderApi coverage:

```bash
dotnet test ParquetLoaderApi.Tests/ParquetLoaderApi.Tests.csproj \
  --settings ParquetLoaderApi.Tests/ParquetLoaderApi.Tests.runsettings \
  --collect:"XPlat Code Coverage" \
  --results-directory ParquetLoaderApi.Tests/TestResults
```

Generate an HTML coverage report with `reportgenerator`:

```bash
pg_report=$(ls -t PgDuckDump.Tests/TestResults/*/coverage.cobertura.xml | head -1)
loader_report=$(ls -t ParquetLoader.Tests/TestResults/*/coverage.cobertura.xml | head -1)
api_report=$(ls -t ParquetLoaderApi.Tests/TestResults/*/coverage.cobertura.xml | head -1)

/Users/rage6c/.dotnet/tools/reportgenerator \
  -reports:"$pg_report;$loader_report;$api_report" \
  -targetdir:CoverageReportCurrent \
  "-reporttypes:Html;TextSummary;Cobertura"
```

The current coverage target is at least 90% line coverage for `ParquetLoader` and `ParquetLoaderApi`.

The combined coverage report is generated under:

```text
CoverageReportCurrent/index.html
```

## Notes

- Target framework is `net10.0`.
- DuckDB access uses `DuckDB.NET.Data.Full`.
- `Program.cs` files are intentionally thin console entry points.
- The project-level READMEs contain the operational details for each app.
