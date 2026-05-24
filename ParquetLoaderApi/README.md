# ParquetLoaderApi

`ParquetLoaderApi` is a .NET 10 ASP.NET Core API that exposes REST endpoints over the `ParquetLoader` workflow Parquet reader.

The API reads partitioned Parquet data through DuckDB and returns JSON DTOs.

## Configuration

The API uses the `ParquetLoader` configuration section:

```json
{
  "ParquetLoader": {
    "BaseParquetPath": "/base-parquet-path"
  }
}
```

`BaseParquetPath` is the root folder that contains dataset folders such as:

```text
/base-parquet-path/
  workflow/
    monthOfYear=202605/
      data.parquet
```

The workflow endpoint reads from:

```text
{BaseParquetPath}/workflow
```

unless `parquetFolder` is passed as a query parameter.

## Local Run

From the repository root:

```bash
dotnet run --project ParquetLoaderApi
```

Default local URL:

```text
http://localhost:5253
```

Health check:

```bash
curl http://localhost:5253/health
```

## Endpoints

```text
GET /health
GET /api/workflow
```

`GET /health` returns:

```json
{
  "status": "Healthy"
}
```

`GET /api/workflow` returns a JSON array of workflow DTOs.

Example:

```bash
curl "http://localhost:5253/api/workflow?monthOfYear=202605&status=Closed&status=Resolved&excludeAssignee=blocked_user&limit=10"
```

Supported workflow query parameters:

- `limit`: positive row limit. Default is `100`; max is `10000`.
- `parquetFolder`: optional full folder override.
- `monthOfYear`: partition filter, for example `202605`.
- `minId`: inclusive minimum workflow row id.
- `maxId`: inclusive maximum workflow row id.
- `status`: repeated or comma-separated status values.
- `statuses`: repeated or comma-separated status values.
- `assignee`: exact assignee value.
- `excludeAssignee`: assignee value to exclude.

Status examples:

```bash
curl "http://localhost:5253/api/workflow?status=Closed&status=Resolved"
curl "http://localhost:5253/api/workflow?statuses=Closed,Resolved"
```

The API builds a LINQ expression from query parameters and passes it to `WorkflowParquetLoader`. The loader translates the expression into DuckDB SQL, so filtering and limiting happen while reading Parquet.

## Container Files

This folder contains:

```text
Dockerfile
entrypoint.sh
podman-compose.yaml
```

The container is configured with:

```text
ASPNETCORE_URLS=http://+:8080
ParquetLoader__BaseParquetPath=/base-parquet-path
LOG_FILE=/logs/dotnet-test-proj.log
```

Container paths:

- `/base-parquet-path`: mounted Parquet data root.
- `/logs`: mounted log volume.
- `/logs/dotnet-test-proj.log`: application log file.

## Podman

From the repository root:

```bash
podman-compose -f ParquetLoaderApi/podman-compose.yaml up --build
```

or, when using Podman's compose wrapper:

```bash
podman compose -f ParquetLoaderApi/podman-compose.yaml up --build
```

The compose file maps:

```text
host port 5253 -> container port 8080
```

It mounts the local Parquet output folder to `/base-parquet-path` and a named volume to `/logs`.

Check the API:

```bash
curl http://localhost:5253/health
```

Read logs from inside the container:

```bash
podman exec parquet-loader-api tail -f /logs/dotnet-test-proj.log
```

## Build Image Manually

From the repository root:

```bash
podman build \
  -f ParquetLoaderApi/Dockerfile \
  -t dotnet-test-proj/parquet-loader-api:latest .
```

Run the image manually:

```bash
podman run --rm \
  -p 5253:8080 \
  -e ParquetLoader__BaseParquetPath=/base-parquet-path \
  -e LOG_FILE=/logs/dotnet-test-proj.log \
  -v "$(pwd)/PgDuckDump/parquet-output:/base-parquet-path:ro" \
  -v parquet-loader-api-logs:/logs \
  dotnet-test-proj/parquet-loader-api:latest
```

## Tests

Run API tests:

```bash
dotnet test ParquetLoaderApi.Tests/ParquetLoaderApi.Tests.csproj
```

Run API coverage:

```bash
dotnet test ParquetLoaderApi.Tests/ParquetLoaderApi.Tests.csproj \
  --settings ParquetLoaderApi.Tests/ParquetLoaderApi.Tests.runsettings \
  --collect:"XPlat Code Coverage" \
  --results-directory ParquetLoaderApi.Tests/TestResults
```
