using ParquetLoader.Configuration;
using Xunit;

namespace ParquetLoader.Tests;

public sealed class ParquetLoaderConfigTests
{
    [Fact]
    public void Normalize_ResolvesBaseParquetPathAgainstContentRoot()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        try
        {
            var config = new ParquetLoaderConfig
            {
                BaseParquetPath = "parquet-output"
            };

            config.Normalize(contentRoot);

            Assert.Equal(Path.Combine(contentRoot, "parquet-output"), config.BaseParquetPath);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void Normalize_KeepsAbsoluteBaseParquetPath()
    {
        var absolutePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var config = new ParquetLoaderConfig
        {
            BaseParquetPath = absolutePath
        };

        config.Normalize(Path.GetTempPath());

        Assert.Equal(Path.GetFullPath(absolutePath), config.BaseParquetPath);
    }

    [Fact]
    public void Validate_ThrowsWhenBaseParquetPathIsMissing()
    {
        var config = new ParquetLoaderConfig();

        var exception = Assert.Throws<InvalidOperationException>(config.Validate);

        Assert.Contains(nameof(ParquetLoaderConfig.BaseParquetPath), exception.Message);
    }
}
