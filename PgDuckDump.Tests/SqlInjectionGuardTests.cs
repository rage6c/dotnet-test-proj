using PgDuckDump.Utilities;
using Xunit;

namespace PgDuckDump.Tests;

public sealed class SqlInjectionGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("status = 'Closed' AND workflow_id > 10")]
    public void ValidateTrustedPredicate_AllowsBlankOrSinglePredicate(string? predicate)
    {
        SqlInjectionGuard.ValidateTrustedPredicate(predicate, "Dump:Tables:AdditionalWhere");
    }

    [Theory]
    [InlineData("status = 'Closed'; DROP TABLE workflow")]
    [InlineData("status = 'Closed' -- comment")]
    [InlineData("status = 'Closed' /* comment */")]
    [InlineData("DELETE FROM workflow")]
    [InlineData("copy workflow to 'x'")]
    public void ValidateTrustedPredicate_RejectsDangerousSql(string predicate)
    {
        Assert.Throws<ArgumentException>(() =>
            SqlInjectionGuard.ValidateTrustedPredicate(predicate, "Dump:Tables:AdditionalWhere"));
    }
}
