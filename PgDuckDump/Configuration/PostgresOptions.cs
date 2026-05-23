namespace PgDuckDump.Configuration;

public sealed class PostgresOptions
{
    public required string ConnectionString { get; init; }

    public string AttachAlias { get; init; } = "pg_source";
}
