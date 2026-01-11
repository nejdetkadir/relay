using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Relay.Configuration;
using Relay.Services;
using Relay.Workers;
using Serilog;
using Serilog.Events;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting Relay - Docker Container Auto-Updater");

    var builder = Host.CreateApplicationBuilder(args);

    // Configure Serilog
    builder.Services.AddSerilog();

    // Bind configuration from environment variables
    builder.Services.Configure<RelayOptions>(options =>
    {
        // Read from environment variables with RELAY_ prefix
        var checkInterval = Environment.GetEnvironmentVariable("RELAY_CHECK_INTERVAL_SECONDS");
        if (int.TryParse(checkInterval, out var interval))
        {
            options.CheckIntervalSeconds = interval;
        }

        var labelKey = Environment.GetEnvironmentVariable("RELAY_LABEL_KEY");
        if (!string.IsNullOrWhiteSpace(labelKey))
        {
            options.LabelKey = labelKey;
        }

        var cleanupImages = Environment.GetEnvironmentVariable("RELAY_CLEANUP_OLD_IMAGES");
        if (bool.TryParse(cleanupImages, out var cleanup))
        {
            options.CleanupOldImages = cleanup;
        }

        var dockerHost = Environment.GetEnvironmentVariable("RELAY_DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(dockerHost))
        {
            options.DockerHost = dockerHost;
        }

        var timeout = Environment.GetEnvironmentVariable("RELAY_DOCKER_TIMEOUT_SECONDS");
        if (int.TryParse(timeout, out var timeoutValue))
        {
            options.DockerTimeoutSeconds = timeoutValue;
        }

        var checkOnStartup = Environment.GetEnvironmentVariable("RELAY_CHECK_ON_STARTUP");
        if (bool.TryParse(checkOnStartup, out var startup))
        {
            options.CheckOnStartup = startup;
        }

        var dockerConfigPath = Environment.GetEnvironmentVariable("RELAY_DOCKER_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(dockerConfigPath))
        {
            options.DockerConfigPath = dockerConfigPath;
        }
    });

    // Register HttpClient for registry API calls
    builder.Services.AddHttpClient<RegistryService>();

    // Register services
    builder.Services.AddSingleton<IDockerService, DockerService>();
    builder.Services.AddSingleton<IDockerConfigService, DockerConfigService>();
    builder.Services.AddSingleton<IVersionService, VersionService>();
    builder.Services.AddSingleton<IRegistryService>(sp => sp.GetRequiredService<RegistryService>());
    builder.Services.AddSingleton<IImageCheckerService, ImageCheckerService>();
    builder.Services.AddSingleton<IContainerUpdaterService, ContainerUpdaterService>();
    builder.Services.AddSingleton<IContainerMonitorService, ContainerMonitorService>();

    // Register the background worker
    builder.Services.AddHostedService<RelayWorker>();

    var host = builder.Build();

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Relay terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;
