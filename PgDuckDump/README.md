# PgDuckDump

A .NET 10 console app that uses DuckDB's Postgres extension to dump configured Postgres tables to monthly Parquet partitions.

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
