namespace PgDuckDump.Utilities;

public sealed record DuckDbCopyOptions(string Compression, int RowGroupSize);
