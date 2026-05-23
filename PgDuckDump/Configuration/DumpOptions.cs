namespace PgDuckDump.Configuration;

public sealed class DumpOptions
{
    public string? Month { get; init; }

    public bool OverwritePartition { get; init; }

    public bool DeleteSourceAfterWrite { get; init; }

    public bool EnableRowCount { get; init; }

    public IReadOnlyList<TableDumpOptions> Tables { get; init; } = [];
}
