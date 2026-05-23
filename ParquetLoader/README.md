# ParquetLoader

A .NET 10 console app that loads rows from a partitioned Parquet dataset folder into strongly typed DTOs and writes JSON through `Microsoft.Extensions.Logging`.

It uses DuckDB `read_parquet(..., hive_partitioning = true)` so partition folders such as `monthOfYear=202605` are available as query columns.

The default dataset folder is resolved from `appsettings.json`:

```json
{
  "ParquetLoader": {
    "BaseParquetPath": "../PgDuckDump/parquet-output"
  }
}
```

The `ParquetLoaderConfig` class loads and validates the `ParquetLoader` section.

For `workflow`, the default folder is `Path.Combine(BaseParquetPath, "workflow")`.

## Run

```bash
dotnet run --project ParquetLoader
```

The current `Program.cs` is a minimal runner:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(provider =>
    builder.Configuration
        .GetSection(ParquetLoaderConfig.SectionName)
        .Get<ParquetLoaderConfig>()!);
builder.Services.AddSingleton<WorkflowParquetLoader>();

using var host = builder.Build();

var loader = host.Services.GetRequiredService<WorkflowParquetLoader>();
var rows = loader.Load(null, 10, dto => dto.MonthOfYear == 202605 && dto.Id > 2000);
```

Passing `null` uses the default folder from `appsettings.json`: `Path.Combine(BaseParquetPath, "workflow")`.

The command logs the DuckDB SQL at `Debug` level and logs JSON output at `Information` level.

## Filters

Filtering is supplied directly as one LINQ expression predicate in `Program.cs`:

```csharp
dto => dto.MonthOfYear == 202605 && dto.Id > 2000
```

The predicate is translated into a DuckDB `WHERE` clause before rows are loaded. The `limit` argument is applied as DuckDB `LIMIT`.

The current runner:

```sql
"monthOfYear" = 202605
AND "id" > 2000
LIMIT 10
```

## Loader Structure

The reusable base loader is:

```text
ParquetLoads/ParquetLoader.cs
```

The workflow-specific loader and DTO are:

```text
ParquetLoads/WorkflowParquetLoader/WorkflowDto.cs
ParquetLoads/WorkflowParquetLoader/WorkflowParquetLoader.cs
```

`WorkflowDto` maps the Parquet schema:

```text
id           INTEGER
workflow_id  INTEGER
status       VARCHAR
created_on   TIMESTAMP
assignee     VARCHAR
updated_on   TIMESTAMP
monthOfYear  BIGINT
```

## Notes

- Filtering is pushed down to DuckDB; the loader does not load all rows and then filter in memory.
- JSON uses camelCase property names.
- `workflow` is the only dataset implemented right now.
