using System.Text.RegularExpressions;

namespace PgDuckDump.Utilities;

public static partial class SqlIdentifier
{
    public static string Quote(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("SQL identifier cannot be empty.", nameof(value));
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public static string Literal(string value) => "'" + value.Replace("'", "''") + "'";

    public static void ValidateSafePathSegment(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} cannot be empty.", name);
        }

        if (value is "." or "..")
        {
            throw new ArgumentException($"{name} cannot be a parent-directory segment. Value: {value}", name);
        }

        if (!SafePathSegmentRegex().IsMatch(value))
        {
            throw new ArgumentException(
                $"{name} can only contain letters, numbers, underscore, hyphen, and dot. Value: {value}",
                name);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]+$")]
    private static partial Regex SafePathSegmentRegex();
}
