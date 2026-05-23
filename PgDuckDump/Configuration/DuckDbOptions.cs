namespace PgDuckDump.Configuration;

public sealed class DuckDbOptions
{
    public string DatabasePath { get; init; } = ":memory:";

    public required string OutputRoot { get; init; }

    public string? TempDirectory { get; init; }

    public string MemoryLimit { get; init; } = "1GB";

    public int Threads { get; init; } = 1;

    public int RowGroupSize { get; init; } = 122880;

    public string Compression { get; init; } = "ZSTD";
}
