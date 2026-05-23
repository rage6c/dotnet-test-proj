namespace PgDuckDump.Configuration;

public sealed class TableDumpOptions
{
    public required string Name { get; init; }

    public required string SourceSchema { get; init; }

    public required string SourceTable { get; init; }

    public required string OutputName { get; init; }

    public required string DateColumn { get; init; }

    public string? AdditionalWhere { get; init; }
}
