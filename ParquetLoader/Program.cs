using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParquetLoader.Configuration;
using ParquetLoader.ParquetLoads.WorkflowParquetLoader;

var builder = Host.CreateApplicationBuilder(
    new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = ResolveContentRoot()
    });

builder.Logging.ClearProviders();
builder.Logging
    .SetMinimumLevel(LogLevel.Debug)
    .AddSimpleConsole(options =>
    {
        options.SingleLine = false;
        options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    });

builder.Services.AddSingleton(provider =>
{
    var config = builder.Configuration
        .GetSection(ParquetLoaderConfig.SectionName)
        .Get<ParquetLoaderConfig>() ??
        throw new InvalidOperationException($"Configuration section {ParquetLoaderConfig.SectionName} is missing.");

    config.Normalize(builder.Environment.ContentRootPath);
    return config;
});
builder.Services.AddSingleton<WorkflowParquetLoader>();

using var host = builder.Build();

var logger = host.Services
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("ParquetLoader.Program");
var loader = host.Services.GetRequiredService<WorkflowParquetLoader>();
var rows = loader.Load(null, 10, dto => dto.MonthOfYear == 202605 && dto.Id > 2000);

var json = JsonSerializer.Serialize(
    rows,
    new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    });
logger.LogInformation("Test with dto => dto.MonthOfYear == 202605 && dto.Id > 2000");
logger.LogInformation("JSON output:{NewLine}{Json}", Environment.NewLine, json);

var statuses = new[] { "Closed", "Resolved" };

Expression<Func<WorkflowDto, bool>> predicate =
    row => row.MonthOfYear == 202605
           && ((IEnumerable<string>)statuses).Contains(row.Status)
           && row.Assignee != "blocked_user";

rows = loader.Load(null, 10, predicate);
json = JsonSerializer.Serialize(
    rows,
    new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    });
logger.LogInformation(
    "Test with dto => dto.MonthOfYear == 202605 && status in ('Closed', 'Resolved') and row.Assignee != \"blocked_user\"");
logger.LogInformation("JSON output:{NewLine}{Json}", Environment.NewLine, json);

static string ResolveContentRoot()
{
    var currentDirectoryProject = Path.Combine(Directory.GetCurrentDirectory(), "ParquetLoader.csproj");
    if (File.Exists(currentDirectoryProject))
    {
        return Directory.GetCurrentDirectory();
    }

    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        var projectFile = Path.Combine(directory.FullName, "ParquetLoader.csproj");
        if (File.Exists(projectFile))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return Directory.GetCurrentDirectory();
}
