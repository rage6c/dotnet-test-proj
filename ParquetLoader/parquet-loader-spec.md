# ParquetLoader Functional Specification

## Goal

Build a .NET 10 console application that loads rows from a partitioned Parquet dataset folder into strongly typed DTO objects.

Example dataset layout:

```text
parquet-output/
  workflow/
    monthOfYear=202605/
      data.parquet
```

The loader must:

- read `*.parquet` files recursively from a dataset folder
- discover Hive-style partition keys such as `monthOfYear`
- translate one optional LINQ expression predicate into a DuckDB `WHERE` clause
- filter rows in DuckDB while reading Parquet
- limit returned rows in DuckDB while reading Parquet
- map matching rows to `List<TDto>`
- write logs through `Microsoft.Extensions.Logging`
- log DuckDB SQL at `Debug` level
- log the final DTO list as JSON at `Information` level

The loader must not load all rows and then filter in memory.

## Core Requirements

- Target framework: .NET 10.
- Input format: Parquet dataset folder.
- Partition format: Hive-style folders, for example `monthOfYear=202605`.
- Query engine: DuckDB.
- Configuration file: `appsettings.json`.
- Configuration class: `ParquetLoaderConfig`.
- Logging: `Microsoft.Extensions.Logging`.
- Public abstraction: `abstract class ParquetLoader<T>`.
- For each Parquet dataset:
  - create one `{TableName}Dto`
  - create one `{TableName}ParquetLoader`
- Logged output: DuckDB SQL and JSON array of filtered DTO objects.

## Configuration

`appsettings.json` must provide the base Parquet folder:

```json
{
  "ParquetLoader": {
    "BaseParquetPath": "../PgDuckDump/parquet-output"
  }
}
```

`Program.cs` must bind `ParquetLoaderConfig` from:

```csharp
builder.Configuration.GetSection("ParquetLoader")
```

`ParquetLoaderConfig` must validate the bound configuration.

The base loader uses `ParquetLoaderConfig` and exposes `BaseParquetPath` as a protected property.

When `Load` receives `null` for `parquetFolder`, the base loader uses protected `DefaultFolder`, which defaults to:

```csharp
Path.Combine(BaseParquetPath, DatasetName)
```

## Runtime Flow

1. Create a host builder with `Host.CreateApplicationBuilder`.
2. Bind `ParquetLoaderConfig` from `builder.Configuration.GetSection("ParquetLoader")`.
3. Register `ParquetLoaderConfig` and `WorkflowParquetLoader` in dependency injection.
4. Build the host and resolve `WorkflowParquetLoader` from `host.Services`.
5. Call `Load(null, 10, dto => dto.MonthOfYear == 202605 && dto.Id > 2000)`.
6. Resolve `null` to the default workflow folder: `Path.Combine(BaseParquetPath, DatasetName)`.
7. Translate the LINQ expression predicate into a DuckDB `WHERE` clause.
8. Execute a DuckDB `read_parquet` query with Hive partitioning, the translated filter, and `LIMIT 10`.
9. Map matching rows to `List<WorkflowDto>`.
10. Log DuckDB SQL at `Debug` level before query execution.
11. Serialize DTOs as indented camelCase JSON.
12. Log JSON through `ILogger` at `Information` level.

## Project Structure

```text
ParquetLoader/
  Program.cs
  appsettings.json
  Configuration/
    ParquetLoaderConfig.cs
  parquet-loader-spec.md
  ParquetLoads/
    ParquetLoader.cs
    WorkflowParquetLoader/
      WorkflowDto.cs
      WorkflowParquetLoader.cs
```

## Loader Contract

`ParquetLoader<T>` is the shared base class for table-specific loaders.

The base class owns:

- public `Load` API
- predicate translation orchestration
- shared Parquet loading flow
- DuckDB `WHERE` clause handoff
- DuckDB `LIMIT` handoff

Concrete loaders own:

- dataset name
- DTO-to-Parquet column mapping metadata
- DTO mapping metadata needed by the shared loader

### Base Class Shape

Method bodies are abridged here. The actual shared implementation lives in `ParquetLoads/ParquetLoader.cs`.

```csharp
using System.Linq.Expressions;
using Microsoft.Extensions.Logging;

public abstract class ParquetLoader<T>
{
    public abstract string DatasetName { get; }

    protected ParquetLoaderConfig Config { get; }

    protected string BaseParquetPath => Config.BaseParquetPath;

    protected virtual string DefaultFolder => Path.Combine(BaseParquetPath, DatasetName);

    protected abstract IReadOnlyDictionary<string, string> ColumnMap { get; }

    protected ParquetLoader(
        ParquetLoaderConfig config,
        ILogger logger)
    {
        // Store config and logger for shared loader behavior.
    }

    public List<T> Load(
        string? parquetFolder,
        int limit,
        Expression<Func<T, bool>>? predicate = null)
    {
        // Validate limit is greater than zero.
        var whereClause = predicate is null
            ? null
            : TranslatePredicate(predicate);

        return LoadData(parquetFolder ?? DefaultFolder, whereClause, limit);
    }

    protected List<T> LoadData(
        string parquetFolder,
        string? whereClause,
        int limit)
    {
        // Shared implementation validates the folder, builds DuckDB SQL,
        // logs DuckDB SQL, executes read_parquet, and maps rows to T.
    }

    protected string TranslatePredicate(
        Expression<Func<T, bool>> predicate)
    {
        // Shared implementation translates supported predicate expressions
        // to DuckDB SQL using ColumnMap.
    }
}
```

Rules:

- `LoadData` and `TranslatePredicate` are non-abstract shared methods implemented by the base class.
- `Load` accepts zero or one predicate.
- `Load` accepts an optional Parquet folder override. When omitted, it uses protected `DefaultFolder`.
- `Program.cs` must bind `ParquetLoaderConfig` from `builder.Configuration.GetSection("ParquetLoader")`.
- `BaseParquetPath` must come from `ParquetLoaderConfig.BaseParquetPath`.
- Protected `DefaultFolder` must default to `Path.Combine(BaseParquetPath, DatasetName)`.
- `Load` requires a positive `limit` value.
- `LoadData` must pass the positive `limit` into DuckDB SQL as `LIMIT`.
- Filtering must happen in DuckDB before DTO materialization.
- Limiting must happen in DuckDB before DTO materialization.
- `Load` returns `List<T>`, not a data reader.
- The base class must not call `Console.*`.
- The base class must log DuckDB SQL through `ILogger`.

## DTO Requirements

Each dataset must have a DTO matching the Parquet row schema and partition columns.

Example:

```csharp
public sealed class WorkflowDto
{
    public int? Id { get; set; }
    public int? WorkflowId { get; set; }
    public string? Status { get; set; }
    public DateTime? CreatedOn { get; set; }
    public string? Assignee { get; set; }
    public DateTime? UpdatedOn { get; set; }
    public long? MonthOfYear { get; set; }
}
```

Mapping rules:

- Mapping must be explicit.
- Snake case Parquet columns map to PascalCase DTO properties:
  - `id` -> `Id`
  - `workflow_id` -> `WorkflowId`
  - `status` -> `Status`
  - `created_on` -> `CreatedOn`
  - `assignee` -> `Assignee`
  - `updated_on` -> `UpdatedOn`
  - `monthOfYear` -> `MonthOfYear`
- Nullable Parquet values must map to nullable DTO properties.
- Partition columns discovered from folder names must be included in the DTO when present.

## Concrete Loader Requirements

For each Parquet dataset folder, implement one concrete loader.

Example:

```csharp
public sealed class WorkflowParquetLoader : ParquetLoader<WorkflowDto>
{
    public WorkflowParquetLoader(
        ParquetLoaderConfig config,
        ILogger<WorkflowParquetLoader> logger)
        : base(config, logger)
    {
    }

    public override string DatasetName => "workflow";

    protected override IReadOnlyDictionary<string, string> ColumnMap { get; } =
        new Dictionary<string, string>
        {
            [nameof(WorkflowDto.Id)] = "id",
            [nameof(WorkflowDto.WorkflowId)] = "workflow_id",
            [nameof(WorkflowDto.Status)] = "status",
            [nameof(WorkflowDto.CreatedOn)] = "created_on",
            [nameof(WorkflowDto.Assignee)] = "assignee",
            [nameof(WorkflowDto.UpdatedOn)] = "updated_on",
            [nameof(WorkflowDto.MonthOfYear)] = "monthOfYear"
        };
}
```

Concrete loader rules:

- It must provide `DatasetName`.
- It must not override `DefaultFolder` unless the dataset needs non-standard folder resolution.
- It must provide DTO property to Parquet column mapping.
- It must not parse command-line arguments.
- It must not call `Console.*`.
- It should rely on base `ParquetLoader<T>` methods for shared loading and predicate translation.

## Predicate Filtering

Filtering is supplied as one optional LINQ expression predicate:

```csharp
Expression<Func<WorkflowDto, bool>>? predicate
```

Example:

```csharp
var loader = host.Services.GetRequiredService<WorkflowParquetLoader>();

List<WorkflowDto> rows = loader.Load(
    null,
    10,
    dto => dto.MonthOfYear == 202605 && dto.Id > 2000);
```

Rules:

- The predicate is optional.
- The predicate must be translated to DuckDB SQL.
- The translated SQL must be applied in the DuckDB query before DTO mapping.
- Unsupported expressions must fail before executing SQL.

## Supported Predicate Expressions

The translator must support:

- Boolean `&&`.
- Boolean `||`.
- Unary logical negation `!`.
- Grouped expressions, preserving C# logical precedence.
- Equality and inequality:
  - `==`
  - `!=`
- Numeric comparisons:
  - `>`
  - `>=`
  - `<`
  - `<=`
- String equality and inequality.
- DTO property access on either side of a comparison.
- Constant values.
- Captured local variables.
- Collection membership with captured arrays or lists:
  - `values.Contains(row.Property)`
  - `row => statuses.Contains(row.Status)`

Unsupported expressions:

- Method calls other than supported collection `Contains`.
- String instance `Contains`, for example `row.Status.Contains("Closed")`.
- `StartsWith`, `EndsWith`, `ToLower`, `ToUpper`, or similar string methods.
- Arbitrary computed expressions.
- Nested object access.
- SQL fragments supplied as strings.

## Predicate Translation

The loader must translate `Expression<Func<T, bool>>` into a DuckDB SQL predicate.

Example:

```csharp
Expression<Func<WorkflowDto, bool>> predicate =
    dto => dto.MonthOfYear == 202605 && dto.Id > 2000;
```

Translated SQL:

```sql
"monthOfYear" = 202605
AND "id" > 2000
```

Translation rules:

- Use `ColumnMap` to translate DTO property names to Parquet column names.
- Use `ColumnMap` consistently for predicate translation and row mapping.
- Quote all translated column identifiers.
- Escape all translated string literals.
- Emit numeric values as numeric literals.
- Emit date/time values as DuckDB timestamp literals.
- Translate null checks to `IS NULL` or `IS NOT NULL`.
- Translate `values.Contains(row.Property)` to `"column" IN (...)`.
- `values` may be a captured array, `List<T>`, or other enumerable of scalar constants.
- Empty `values` collections must translate to a predicate that is always false, for example `1 = 0`.
- Translate `!values.Contains(row.Property)` to `NOT ("column" IN (...))`.
- Wrap binary boolean expressions in parentheses when required to preserve C# expression precedence.
- Translate unary `!` to `NOT (...)`.

## DuckDB Reading Strategy

Use DuckDB `read_parquet` with recursive glob and Hive partitioning:

```sql
SELECT *
FROM read_parquet('./PgDuckDump/parquet-output/workflow/**/*.parquet', hive_partitioning = true)
WHERE "monthOfYear" = 202605
  AND "id" > 2000
LIMIT 10;
```

Rules:

- Validate the folder exists before loading.
- Validate at least one `.parquet` file exists before loading.
- Validate the row limit is greater than zero.
- Escape the folder path before inserting it into SQL.
- Append the translated `WHERE` clause before executing the query.
- Append `LIMIT` before executing the query.
- Keep DuckDB-specific query execution outside `Program.cs`.

## Console Application Behavior

The console app is a minimal runner over the workflow loader.

Current behavior:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging
    .SetMinimumLevel(LogLevel.Debug)
    .AddSimpleConsole();

builder.Services.AddSingleton(provider =>
{
    var config = builder.Configuration
        .GetSection(ParquetLoaderConfig.SectionName)
        .Get<ParquetLoaderConfig>() ??
        throw new InvalidOperationException($"Configuration section {ParquetLoaderConfig.SectionName} is missing.");

    config.Normalize(builder.Environment.ContentRootPath);
    return config;
});
builder.Services.AddSingleton<WorkflowParquetLoader>();

using var host = builder.Build();

var logger = host.Services
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("ParquetLoader.Program");
var loader = host.Services.GetRequiredService<WorkflowParquetLoader>();
var rows = loader.Load(null, 10, dto => dto.MonthOfYear == 202605 && dto.Id > 2000);

var json = JsonSerializer.Serialize(
    rows,
    new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    });
logger.LogInformation("Test with dto => dto.MonthOfYear == 202605 && dto.Id > 2000");
logger.LogInformation("JSON output:{NewLine}{Json}", Environment.NewLine, json);

var statuses = new[] { "Closed", "Resolved" };

Expression<Func<WorkflowDto, bool>> predicate =
    row => row.MonthOfYear == 202605
           && ((IEnumerable<string>)statuses).Contains(row.Status)
           && row.Assignee != "blocked_user";

rows = loader.Load(null, 10, predicate);
json = JsonSerializer.Serialize(
    rows,
    new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    });
logger.LogInformation(
    "Test with dto => dto.MonthOfYear == 202605 && status in ('Closed', 'Resolved') and row.Assignee != \"blocked_user\"");
logger.LogInformation("JSON output:{NewLine}{Json}", Environment.NewLine, json);
```

Run command:

```bash
dotnet run --project ParquetLoader
```

## Logging Requirements

- Use `Microsoft.Extensions.Logging`.
- Do not use direct `Console.*` calls for application logging.
- Configure console logging from `Program.cs`.
- Log DuckDB SQL at `Debug` level.
- Log test descriptions and JSON output at `Information` level.
- JSON log payload must be a JSON array.
- Use `System.Text.Json`.
- Use camelCase JSON property names.
- Use indented JSON for log readability.
- The current runner does not implement custom error handling; unhandled exceptions are written by the .NET runtime.

Example output:

```json
[
  {
    "id": 1,
    "workflowId": 1001,
    "status": "Closed",
    "createdOn": "2026-05-01T00:00:00",
    "assignee": "assignee_001",
    "updatedOn": "2026-05-02T00:00:00",
    "monthOfYear": 202605
  }
]
```

## Error Handling

- The current `Program.cs` does not parse command-line arguments.
- The current `Program.cs` does not define custom exit codes.
- Missing folders, folders with no `.parquet` files, missing configuration, invalid limits, unsupported predicates, mapping failures, and DuckDB failures are allowed to throw exceptions.
- A future CLI runner may catch these exceptions and map them to explicit exit codes.

## Acceptance Criteria

- `ParquetLoader<T>` exists as an abstract base class.
- `LoadData` and `TranslatePredicate` are non-abstract shared methods in `ParquetLoader<T>`.
- `WorkflowDto` exists for the `workflow` dataset.
- `WorkflowParquetLoader` exists and inherits `ParquetLoader<WorkflowDto>`.
- `ParquetLoaderConfig` exists and is bound from `builder.Configuration.GetSection("ParquetLoader")`.
- `ParquetLoader<T>` gets `BaseParquetPath` from `ParquetLoaderConfig`.
- `DefaultFolder` is protected and defaults to `Path.Combine(BaseParquetPath, DatasetName)`.
- `WorkflowParquetLoader` provides `DatasetName` and `ColumnMap`.
- `WorkflowParquetLoader` reads partition keys such as `monthOfYear`.
- The loader returns `List<WorkflowDto>`.
- The loader requires a positive row limit and applies it in DuckDB SQL.
- The loader accepts one optional LINQ expression predicate.
- The loader translates the predicate to DuckDB SQL and filters rows while loading.
- The loader supports `&&`, `||`, `!`, grouped expressions, comparisons, null checks, and collection `Contains`.
- The loader does not load all rows and then apply the predicate in memory.
- The console app logs filtered DTOs as JSON through `ILogger`.
- `dotnet build` succeeds.
