using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Relay.Configuration;
using Relay.Models;

namespace Relay.Services;

/// <summary>
/// Service for reading Docker configuration and credentials from ~/.docker/config.json.
/// </summary>
public class DockerConfigService : IDockerConfigService
{
    private readonly RelayOptions _options;
    private readonly ILogger<DockerConfigService> _logger;
    private readonly object _lock = new();
    private DockerConfig? _config;
    private bool _configLoaded;

    // Common Docker Hub registry aliases
    private static readonly string[] DockerHubAliases =
    [
        "docker.io",
        "index.docker.io",
        "registry-1.docker.io",
        "https://index.docker.io/v1/",
        "https://index.docker.io/v2/"
    ];

    public DockerConfigService(IOptions<RelayOptions> options, ILogger<DockerConfigService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public DockerCredentials GetCredentials(string registry)
    {
        EnsureConfigLoaded();

        if (_config?.Auths == null || _config.Auths.Count == 0)
        {
            return DockerCredentials.None(registry);
        }

        // Try to find credentials for this registry
        var authEntry = FindAuthEntry(registry);
        if (authEntry == null)
        {
            _logger.LogDebug("No credentials found for registry {Registry}", registry);
            return DockerCredentials.None(registry);
        }

        // Parse the auth entry
        return ParseAuthEntry(registry, authEntry);
    }

    public bool HasCredentials(string registry)
    {
        return GetCredentials(registry).HasCredentials;
    }

    public void Reload()
    {
        lock (_lock)
        {
            _configLoaded = false;
            _config = null;
        }
        EnsureConfigLoaded();
    }

    private void EnsureConfigLoaded()
    {
        if (_configLoaded) return;

        lock (_lock)
        {
            if (_configLoaded) return;

            _config = LoadConfig();
            _configLoaded = true;
        }
    }

    private DockerConfig? LoadConfig()
    {
        var configPath = GetConfigPath();

        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
        {
            _logger.LogDebug("Docker config file not found at {Path}", configPath ?? "(not configured)");
            return null;
        }

        try
        {
            _logger.LogInformation("Loading Docker config from {Path}", configPath);
            var json = File.ReadAllText(configPath);
            
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            var config = JsonSerializer.Deserialize<DockerConfig>(json, options);

            if (config?.Auths != null)
            {
                _logger.LogInformation("Found {Count} registry credentials in Docker config", config.Auths.Count);
                foreach (var registry in config.Auths.Keys)
                {
                    _logger.LogDebug("  - {Registry}", registry);
                }
            }

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Docker config from {Path}", configPath);
            return null;
        }
    }

    private string? GetConfigPath()
    {
        // Priority order:
        // 1. Configured path in options
        // 2. DOCKER_CONFIG environment variable
        // 3. Default paths

        if (!string.IsNullOrEmpty(_options.DockerConfigPath))
        {
            return _options.DockerConfigPath;
        }

        var dockerConfigEnv = Environment.GetEnvironmentVariable("DOCKER_CONFIG");
        if (!string.IsNullOrEmpty(dockerConfigEnv))
        {
            var envPath = Path.Combine(dockerConfigEnv, "config.json");
            if (File.Exists(envPath))
            {
                return envPath;
            }
        }

        // Default paths to check
        var defaultPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker", "config.json"),
            "/root/.docker/config.json",
            "/home/.docker/config.json"
        };

        foreach (var path in defaultPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private DockerAuthEntry? FindAuthEntry(string registry)
    {
        if (_config?.Auths == null) return null;

        // Normalize registry name
        var normalizedRegistry = NormalizeRegistry(registry);

        // Direct match
        if (_config.Auths.TryGetValue(registry, out var entry))
        {
            return entry;
        }

        // Try normalized name
        if (_config.Auths.TryGetValue(normalizedRegistry, out entry))
        {
            return entry;
        }

        // For Docker Hub, try all known aliases
        if (IsDockerHub(registry))
        {
            foreach (var alias in DockerHubAliases)
            {
                if (_config.Auths.TryGetValue(alias, out entry))
                {
                    return entry;
                }
            }
        }

        // Try with https:// prefix
        if (_config.Auths.TryGetValue($"https://{registry}", out entry))
        {
            return entry;
        }

        // Try with /v1/ and /v2/ suffixes
        if (_config.Auths.TryGetValue($"https://{registry}/v1/", out entry))
        {
            return entry;
        }
        if (_config.Auths.TryGetValue($"https://{registry}/v2/", out entry))
        {
            return entry;
        }

        return null;
    }

    private DockerCredentials ParseAuthEntry(string registry, DockerAuthEntry authEntry)
    {
        // If we have an identity token, use it as the password with empty username
        if (!string.IsNullOrEmpty(authEntry.IdentityToken))
        {
            return DockerCredentials.Create(registry, "", authEntry.IdentityToken);
        }

        // If we have a registry token, use it
        if (!string.IsNullOrEmpty(authEntry.RegistryToken))
        {
            return DockerCredentials.Create(registry, "", authEntry.RegistryToken);
        }

        // If username and password are stored directly
        if (!string.IsNullOrEmpty(authEntry.Username) && !string.IsNullOrEmpty(authEntry.Password))
        {
            return DockerCredentials.Create(registry, authEntry.Username, authEntry.Password);
        }

        // Parse the base64 encoded auth string
        if (!string.IsNullOrEmpty(authEntry.Auth))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authEntry.Auth));
                var colonIndex = decoded.IndexOf(':');
                
                if (colonIndex > 0)
                {
                    var username = decoded[..colonIndex];
                    var password = decoded[(colonIndex + 1)..];
                    return DockerCredentials.Create(registry, username, password);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decode auth for registry {Registry}", registry);
            }
        }

        return DockerCredentials.None(registry);
    }

    private static string NormalizeRegistry(string registry)
    {
        // Remove protocol prefix if present
        if (registry.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            registry = registry[8..];
        }
        else if (registry.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            registry = registry[7..];
        }

        // Remove trailing slashes and paths
        var slashIndex = registry.IndexOf('/');
        if (slashIndex > 0 && !registry.Contains('.'))
        {
            // This might be a path, not a registry
        }
        else if (slashIndex > 0)
        {
            registry = registry[..slashIndex];
        }

        return registry.TrimEnd('/');
    }

    private static bool IsDockerHub(string registry)
    {
        var normalized = NormalizeRegistry(registry);
        return string.IsNullOrEmpty(normalized) ||
               normalized.Equals("docker.io", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("index.docker.io", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("registry-1.docker.io", StringComparison.OrdinalIgnoreCase);
    }
}
