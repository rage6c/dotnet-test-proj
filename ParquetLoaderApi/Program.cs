using System.Linq.Expressions;
using Microsoft.AspNetCore.Mvc;
using ParquetLoader.Configuration;
using ParquetLoader.ParquetLoads.WorkflowParquetLoader;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
    .WithName("Health");

app.MapGet("/api/workflow", (
        HttpRequest request,
        WorkflowParquetLoader loader,
        ILoggerFactory loggerFactory) =>
    {
        var logger = loggerFactory.CreateLogger("ParquetLoaderApi.Workflow");

        if (!WorkflowLoadRequest.TryCreate(request.Query, out var loadRequest, out var error))
        {
            return Results.BadRequest(new { error });
        }

        try
        {
            var predicate = WorkflowPredicateBuilder.Build(loadRequest);
            var rows = loader.Load(loadRequest.ParquetFolder, loadRequest.Limit, predicate);

            logger.LogInformation(
                "Loaded {Count} workflow rows with limit {Limit}.",
                rows.Count,
                loadRequest.Limit);

            return Results.Ok(rows);
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or NotSupportedException)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to load workflow parquet rows.");
            return Results.Problem("Failed to load workflow parquet rows.");
        }
    })
    .WithName("LoadWorkflow")
    .Produces<List<WorkflowDto>>()
    .Produces(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

app.Run();

public partial class Program;

internal sealed record WorkflowLoadRequest(
    string? ParquetFolder,
    int Limit,
    long? MonthOfYear,
    int? MinId,
    int? MaxId,
    string[] Statuses,
    string? Assignee,
    string? ExcludeAssignee)
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 10_000;

    public static bool TryCreate(
        IQueryCollection query,
        out WorkflowLoadRequest request,
        out string? error)
    {
        request = new WorkflowLoadRequest(null, DefaultLimit, null, null, null, [], null, null);
        error = null;

        if (!TryGetPositiveInt(query, "limit", DefaultLimit, MaxLimit, out var limit, out error) ||
            !TryGetOptionalLong(query, "monthOfYear", out var monthOfYear, out error) ||
            !TryGetOptionalInt(query, "minId", out var minId, out error) ||
            !TryGetOptionalInt(query, "maxId", out var maxId, out error))
        {
            return false;
        }

        request = new WorkflowLoadRequest(
            GetSingleValue(query, "parquetFolder"),
            limit,
            monthOfYear,
            minId,
            maxId,
            GetStringValues(query, "status", "statuses"),
            GetSingleValue(query, "assignee"),
            GetSingleValue(query, "excludeAssignee"));

        return true;
    }

    private static bool TryGetPositiveInt(
        IQueryCollection query,
        string key,
        int defaultValue,
        int maxValue,
        out int value,
        out string? error)
    {
        value = defaultValue;
        error = null;
        var rawValue = GetSingleValue(query, key);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        if (!int.TryParse(rawValue, out value) || value < 1 || value > maxValue)
        {
            error = $"{key} must be an integer from 1 to {maxValue}.";
            return false;
        }

        return true;
    }

    private static bool TryGetOptionalInt(
        IQueryCollection query,
        string key,
        out int? value,
        out string? error)
    {
        value = null;
        error = null;
        var rawValue = GetSingleValue(query, key);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        if (!int.TryParse(rawValue, out var parsedValue))
        {
            error = $"{key} must be an integer.";
            return false;
        }

        value = parsedValue;
        return true;
    }

    private static bool TryGetOptionalLong(
        IQueryCollection query,
        string key,
        out long? value,
        out string? error)
    {
        value = null;
        error = null;
        var rawValue = GetSingleValue(query, key);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        if (!long.TryParse(rawValue, out var parsedValue))
        {
            error = $"{key} must be an integer.";
            return false;
        }

        value = parsedValue;
        return true;
    }

    private static string? GetSingleValue(
        IQueryCollection query,
        string key)
    {
        return query.TryGetValue(key, out var values)
            ? values.FirstOrDefault()
            : null;
    }

    private static string[] GetStringValues(
        IQueryCollection query,
        params string[] keys)
    {
        return keys
            .Where(query.ContainsKey)
            .SelectMany(key => query[key])
            .SelectMany(value => value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

internal static class WorkflowPredicateBuilder
{
    public static Expression<Func<WorkflowDto, bool>>? Build(WorkflowLoadRequest request)
    {
        var row = Expression.Parameter(typeof(WorkflowDto), "row");
        Expression? body = null;

        AddEquals(ref body, row, nameof(WorkflowDto.MonthOfYear), request.MonthOfYear);
        AddGreaterThanOrEqual(ref body, row, nameof(WorkflowDto.Id), request.MinId);
        AddLessThanOrEqual(ref body, row, nameof(WorkflowDto.Id), request.MaxId);
        AddContains(ref body, row, nameof(WorkflowDto.Status), request.Statuses);
        AddEquals(ref body, row, nameof(WorkflowDto.Assignee), request.Assignee);
        AddNotEquals(ref body, row, nameof(WorkflowDto.Assignee), request.ExcludeAssignee);

        return body is null
            ? null
            : Expression.Lambda<Func<WorkflowDto, bool>>(body, row);
    }

    private static void AddEquals<TValue>(
        ref Expression? body,
        ParameterExpression row,
        string propertyName,
        TValue? value)
        where TValue : struct
    {
        if (value is null)
        {
            return;
        }

        var property = Expression.Property(row, propertyName);
        var constant = Expression.Constant(value, property.Type);
        AddAnd(ref body, Expression.Equal(property, constant));
    }

    private static void AddEquals(
        ref Expression? body,
        ParameterExpression row,
        string propertyName,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var property = Expression.Property(row, propertyName);
        var constant = Expression.Constant(value, property.Type);
        AddAnd(ref body, Expression.Equal(property, constant));
    }

    private static void AddNotEquals(
        ref Expression? body,
        ParameterExpression row,
        string propertyName,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var property = Expression.Property(row, propertyName);
        var constant = Expression.Constant(value, property.Type);
        AddAnd(ref body, Expression.NotEqual(property, constant));
    }

    private static void AddGreaterThanOrEqual<TValue>(
        ref Expression? body,
        ParameterExpression row,
        string propertyName,
        TValue? value)
        where TValue : struct
    {
        if (value is null)
        {
            return;
        }

        var property = Expression.Property(row, propertyName);
        var constant = Expression.Constant(value, property.Type);
        AddAnd(ref body, Expression.GreaterThanOrEqual(property, constant));
    }

    private static void AddLessThanOrEqual<TValue>(
        ref Expression? body,
        ParameterExpression row,
        string propertyName,
        TValue? value)
        where TValue : struct
    {
        if (value is null)
        {
            return;
        }

        var property = Expression.Property(row, propertyName);
        var constant = Expression.Constant(value, property.Type);
        AddAnd(ref body, Expression.LessThanOrEqual(property, constant));
    }

    private static void AddContains(
        ref Expression? body,
        ParameterExpression row,
        string propertyName,
        string[] values)
    {
        if (values.Length == 0)
        {
            return;
        }

        var property = Expression.Property(row, propertyName);
        var contains = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [typeof(string)],
            Expression.Constant(values),
            property);
        AddAnd(ref body, contains);
    }

    private static void AddAnd(
        ref Expression? body,
        Expression expression)
    {
        body = body is null
            ? expression
            : Expression.AndAlso(body, expression);
    }
}
