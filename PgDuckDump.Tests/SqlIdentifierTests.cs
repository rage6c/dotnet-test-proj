using PgDuckDump.Utilities;
using Xunit;

namespace PgDuckDump.Tests;

public sealed class SqlIdentifierTests
{
    [Fact]
    public void Quote_EscapesDoubleQuotes()
    {
        Assert.Equal("\"schema\"\"name\"", SqlIdentifier.Quote("schema\"name"));
    }

    [Fact]
    public void Quote_ThrowsForEmptyValue()
    {
        Assert.Throws<ArgumentException>(() => SqlIdentifier.Quote(" "));
    }

    [Fact]
    public void Literal_EscapesSingleQuotes()
    {
        Assert.Equal("'O''Brien'", SqlIdentifier.Literal("O'Brien"));
    }

    [Theory]
    [InlineData("workflow")]
    [InlineData("workflow-202605")]
    [InlineData("workflow_202605")]
    [InlineData("workflow.v1")]
    public void ValidateSafePathSegment_AllowsSafeValues(string value)
    {
        SqlIdentifier.ValidateSafePathSegment(value, "OutputName");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../workflow")]
    [InlineData("workflow/month")]
    public void ValidateSafePathSegment_RejectsUnsafeValues(string value)
    {
        Assert.Throws<ArgumentException>(() =>
            SqlIdentifier.ValidateSafePathSegment(value, "OutputName"));
    }
}
