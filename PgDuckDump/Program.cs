using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PgDuckDump.Configuration;
using PgDuckDump.Services;
using PgDuckDump.Utilities;

var settings = new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    EnvironmentName = ResolveEnvironmentName()
};

var builder = Host.CreateApplicationBuilder(settings);

builder.Services.Configure<PostgresOptions>(builder.Configuration.GetSection("Postgres"));
builder.Services.Configure<DuckDbOptions>(builder.Configuration.GetSection("DuckDb"));
builder.Services.Configure<DumpOptions>(builder.Configuration.GetSection("Dump"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DuckDbProcessor>();
builder.Services.AddSingleton<DbDumpService>();

using var host = builder.Build();
var service = host.Services.GetRequiredService<DbDumpService>();
return await service.RunAsync(CancellationToken.None);

static string ResolveEnvironmentName()
{
    return Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
           ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
           ?? "Development";
}
