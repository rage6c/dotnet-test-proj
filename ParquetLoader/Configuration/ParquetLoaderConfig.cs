namespace ParquetLoader.Configuration;

public sealed class ParquetLoaderConfig
{
    public const string SectionName = "ParquetLoader";

    public string BaseParquetPath { get; set; } = string.Empty;

    public void Normalize(string contentRootPath)
    {
        Validate();
        BaseParquetPath = Path.GetFullPath(BaseParquetPath, contentRootPath);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BaseParquetPath))
        {
            throw new InvalidOperationException($"Configuration section {SectionName} must define {nameof(BaseParquetPath)}.");
        }
    }
}
