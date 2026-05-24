namespace PgDuckDump.Utilities;

public interface IDuckDbProcessor
{
    void Initialize();

    void CopyPostgresQueryToParquet(
        string selectSql,
        string outputPath,
        DuckDbCopyOptions copyOptions);

    long CountPostgresQuery(string selectSql);

    long DeletePostgresRows(string deleteSql);
}
