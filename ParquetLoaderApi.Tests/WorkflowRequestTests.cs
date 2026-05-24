using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using ParquetLoader.ParquetLoads.WorkflowParquetLoader;

namespace ParquetLoaderApi.Tests;

public sealed class WorkflowRequestTests
{
    [Fact]
    public void TryCreate_UsesDefaultsForEmptyQuery()
    {
        var created = WorkflowLoadRequest.TryCreate(
            Query(),
            out var request,
            out var error);

        Assert.True(created);
        Assert.Null(error);
        Assert.Null(request.ParquetFolder);
        Assert.Equal(100, request.Limit);
        Assert.Null(request.MonthOfYear);
        Assert.Empty(request.Statuses);
    }

    [Fact]
    public void TryCreate_ParsesAllSupportedQueryValues()
    {
        var created = WorkflowLoadRequest.TryCreate(
            Query(
                ("parquetFolder", "/tmp/workflow"),
                ("limit", "25"),
                ("monthOfYear", "202605"),
                ("minId", "10"),
                ("maxId", "99"),
                ("status", "Closed, Resolved"),
                ("status", "closed"),
                ("statuses", "Archived"),
                ("assignee", "assignee_001"),
                ("excludeAssignee", "blocked_user")),
            out var request,
            out var error);

        Assert.True(created);
        Assert.Null(error);
        Assert.Equal("/tmp/workflow", request.ParquetFolder);
        Assert.Equal(25, request.Limit);
        Assert.Equal(202605, request.MonthOfYear);
        Assert.Equal(10, request.MinId);
        Assert.Equal(99, request.MaxId);
        Assert.Equal(["Closed", "Resolved", "Archived"], request.Statuses);
        Assert.Equal("assignee_001", request.Assignee);
        Assert.Equal("blocked_user", request.ExcludeAssignee);
    }

    [Theory]
    [InlineData("limit", "abc", "limit must be an integer from 1 to 10000.")]
    [InlineData("limit", "10001", "limit must be an integer from 1 to 10000.")]
    [InlineData("monthOfYear", "2026-05", "monthOfYear must be an integer.")]
    [InlineData("minId", "x", "minId must be an integer.")]
    [InlineData("maxId", "x", "maxId must be an integer.")]
    public void TryCreate_ReturnsValidationErrorForInvalidNumbers(
        string key,
        string value,
        string expectedError)
    {
        var created = WorkflowLoadRequest.TryCreate(
            Query((key, value)),
            out _,
            out var error);

        Assert.False(created);
        Assert.Equal(expectedError, error);
    }

    [Fact]
    public void PredicateBuilder_ReturnsNullWhenNoFiltersAreProvided()
    {
        var request = new WorkflowLoadRequest(null, 100, null, null, null, [], null, null);

        var predicate = WorkflowPredicateBuilder.Build(request);

        Assert.Null(predicate);
    }

    [Fact]
    public void PredicateBuilder_CombinesAllFiltersWithAndSemantics()
    {
        var request = new WorkflowLoadRequest(
            null,
            100,
            202605,
            10,
            20,
            ["Closed", "Resolved"],
            "assignee_001",
            "blocked_user");

        var predicate = WorkflowPredicateBuilder.Build(request);

        Assert.NotNull(predicate);
        var filter = predicate.Compile();
        Assert.True(filter(Row(id: 12, status: "Closed", assignee: "assignee_001")));
        Assert.False(filter(Row(id: 9, status: "Closed", assignee: "assignee_001")));
        Assert.False(filter(Row(id: 21, status: "Closed", assignee: "assignee_001")));
        Assert.False(filter(Row(id: 12, status: "Open", assignee: "assignee_001")));
        Assert.False(filter(Row(id: 12, status: "Closed", assignee: "other")));
        Assert.False(filter(Row(id: 12, status: "Closed", assignee: "blocked_user")));
    }

    private static WorkflowDto Row(
        int id,
        string status,
        string assignee)
    {
        return new WorkflowDto
        {
            Id = id,
            MonthOfYear = 202605,
            Status = status,
            Assignee = assignee
        };
    }

    private static QueryCollection Query(params (string Key, string Value)[] values)
    {
        return new QueryCollection(
            values
                .GroupBy(value => value.Key)
                .ToDictionary(
                    group => group.Key,
                    group => new StringValues(group.Select(value => value.Value).ToArray())));
    }
}
