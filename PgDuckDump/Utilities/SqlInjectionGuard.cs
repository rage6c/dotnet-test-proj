using System.Text.RegularExpressions;

namespace PgDuckDump.Utilities;

public static partial class SqlInjectionGuard
{
    private static readonly string[] ForbiddenTokens = [";", "--", "/*", "*/"];

    public static void ValidateTrustedPredicate(string? predicate, string configurationPath)
    {
        if (string.IsNullOrWhiteSpace(predicate))
        {
            return;
        }

        foreach (var token in ForbiddenTokens)
        {
            if (predicate.Contains(token, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"{configurationPath} must be a single predicate and cannot contain SQL token {token}.",
                    configurationPath);
            }
        }

        var keywordMatch = ForbiddenKeywordRegex().Match(predicate);
        if (keywordMatch.Success)
        {
            throw new ArgumentException(
                $"{configurationPath} cannot contain SQL keyword {keywordMatch.Value.ToUpperInvariant()}.",
                configurationPath);
        }
    }

    [GeneratedRegex(@"\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|CREATE|ATTACH|COPY|CALL|PRAGMA)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenKeywordRegex();
}
