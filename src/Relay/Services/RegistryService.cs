using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Relay.Models;

namespace Relay.Services;

/// <summary>
/// Service for querying Docker registries for available image tags.
/// Supports Docker Hub, GitHub Container Registry, and other OCI-compliant registries.
/// Reads credentials from Docker config file for private registry authentication.
/// </summary>
public partial class RegistryService : IRegistryService
{
    private readonly HttpClient _httpClient;
    private readonly IDockerConfigService _configService;
    private readonly ILogger<RegistryService> _logger;

    // Docker Hub API endpoints
    private const string DockerHubAuthUrl = "https://auth.docker.io/token";
    private const string DockerHubRegistryUrl = "https://registry-1.docker.io/v2";

    public RegistryService(
        HttpClient httpClient,
        IDockerConfigService configService,
        ILogger<RegistryService> logger)
    {
        _httpClient = httpClient;
        _configService = configService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> GetTagsAsync(string imageName, CancellationToken cancellationToken = default)
    {
        try
        {
            var (registry, repository) = ParseImageName(imageName);

            _logger.LogDebug("Fetching tags for {Repository} from {Registry}", repository, registry);

            if (IsDockerHub(registry))
            {
                return await GetDockerHubTagsAsync(repository, cancellationToken);
            }
            else
            {
                return await GetPrivateRegistryTagsAsync(registry, repository, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch tags for image {ImageName}", imageName);
            return [];
        }
    }

    private async Task<IReadOnlyList<string>> GetDockerHubTagsAsync(string repository, CancellationToken cancellationToken)
    {
        // Get credentials if available
        var credentials = _configService.GetCredentials("docker.io");

        // Get authentication token for Docker Hub
        var token = await GetDockerHubTokenAsync(repository, credentials, cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("Failed to get Docker Hub authentication token for {Repository}", repository);
            return [];
        }

        // Fetch tags from Docker Hub registry
        var allTags = new List<string>();
        var url = $"{DockerHubRegistryUrl}/{repository}/tags/list";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to fetch tags from Docker Hub: {StatusCode}", response.StatusCode);
            return [];
        }

        var tagsResponse = await response.Content.ReadFromJsonAsync<TagsListResponse>(cancellationToken: cancellationToken);
        if (tagsResponse?.Tags != null)
        {
            allTags.AddRange(tagsResponse.Tags);
        }

        _logger.LogDebug("Found {Count} tags for {Repository}", allTags.Count, repository);
        return allTags;
    }

    private async Task<string?> GetDockerHubTokenAsync(string repository, DockerCredentials credentials, CancellationToken cancellationToken)
    {
        try
        {
            var scope = $"repository:{repository}:pull";
            var url = $"{DockerHubAuthUrl}?service=registry.docker.io&scope={Uri.EscapeDataString(scope)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            // Add Basic auth if we have credentials
            if (credentials.HasCredentials)
            {
                var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);
                _logger.LogDebug("Using credentials for Docker Hub authentication");
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Docker Hub token request failed: {StatusCode}", response.StatusCode);
                return null;
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
            return tokenResponse?.Token ?? tokenResponse?.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get Docker Hub token for {Repository}", repository);
            return null;
        }
    }

    private async Task<IReadOnlyList<string>> GetPrivateRegistryTagsAsync(string registry, string repository, CancellationToken cancellationToken)
    {
        var credentials = _configService.GetCredentials(registry);
        var baseUrl = $"https://{registry}/v2";
        var tagsUrl = $"{baseUrl}/{repository}/tags/list";

        try
        {
            // First, try without authentication to see if it's public
            using var initialRequest = new HttpRequestMessage(HttpMethod.Get, tagsUrl);
            var initialResponse = await _httpClient.SendAsync(initialRequest, cancellationToken);

            if (initialResponse.IsSuccessStatusCode)
            {
                var tagsResponse = await initialResponse.Content.ReadFromJsonAsync<TagsListResponse>(cancellationToken: cancellationToken);
                _logger.LogDebug("Found {Count} tags for {Repository} (public)", tagsResponse?.Tags?.Count ?? 0, repository);
                return tagsResponse?.Tags ?? [];
            }

            // If 401, we need to authenticate
            if (initialResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                return await AuthenticateAndFetchTagsAsync(registry, repository, initialResponse, credentials, cancellationToken);
            }

            _logger.LogWarning("Failed to fetch tags from {Registry}: {StatusCode}", registry, initialResponse.StatusCode);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch tags from {Registry}/{Repository}", registry, repository);
            return [];
        }
    }

    private async Task<IReadOnlyList<string>> AuthenticateAndFetchTagsAsync(
        string registry,
        string repository,
        HttpResponseMessage initialResponse,
        DockerCredentials credentials,
        CancellationToken cancellationToken)
    {
        // Parse WWW-Authenticate header to get auth details
        var wwwAuth = initialResponse.Headers.WwwAuthenticate.FirstOrDefault();
        
        if (wwwAuth == null)
        {
            _logger.LogWarning("No WWW-Authenticate header in 401 response from {Registry}", registry);
            
            // Try Basic auth directly if we have credentials
            if (credentials.HasCredentials)
            {
                return await FetchTagsWithBasicAuthAsync(registry, repository, credentials, cancellationToken);
            }
            return [];
        }

        _logger.LogDebug("WWW-Authenticate: {Scheme} {Parameter}", wwwAuth.Scheme, wwwAuth.Parameter);

        if (wwwAuth.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return await FetchTagsWithBearerAuthAsync(registry, repository, wwwAuth.Parameter, credentials, cancellationToken);
        }
        else if (wwwAuth.Scheme.Equals("Basic", StringComparison.OrdinalIgnoreCase))
        {
            if (credentials.HasCredentials)
            {
                return await FetchTagsWithBasicAuthAsync(registry, repository, credentials, cancellationToken);
            }
            _logger.LogWarning("Basic auth required but no credentials available for {Registry}", registry);
            return [];
        }

        _logger.LogWarning("Unsupported auth scheme {Scheme} for {Registry}", wwwAuth.Scheme, registry);
        return [];
    }

    private async Task<IReadOnlyList<string>> FetchTagsWithBasicAuthAsync(
        string registry,
        string repository,
        DockerCredentials credentials,
        CancellationToken cancellationToken)
    {
        var url = $"https://{registry}/v2/{repository}/tags/list";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to fetch tags from {Registry} with Basic auth: {StatusCode}", registry, response.StatusCode);
            return [];
        }

        var tagsResponse = await response.Content.ReadFromJsonAsync<TagsListResponse>(cancellationToken: cancellationToken);
        _logger.LogDebug("Found {Count} tags for {Repository}", tagsResponse?.Tags?.Count ?? 0, repository);
        return tagsResponse?.Tags ?? [];
    }

    private async Task<IReadOnlyList<string>> FetchTagsWithBearerAuthAsync(
        string registry,
        string repository,
        string? wwwAuthParameter,
        DockerCredentials credentials,
        CancellationToken cancellationToken)
    {
        // Parse Bearer auth parameters: realm="...",service="...",scope="..."
        var authParams = ParseWwwAuthenticateParams(wwwAuthParameter);

        if (!authParams.TryGetValue("realm", out var realm))
        {
            _logger.LogWarning("No realm in Bearer WWW-Authenticate for {Registry}", registry);
            return [];
        }

        // Build token request URL
        var tokenUrl = new StringBuilder(realm);
        var hasQuery = realm.Contains('?');
        
        if (authParams.TryGetValue("service", out var service))
        {
            tokenUrl.Append(hasQuery ? '&' : '?');
            tokenUrl.Append($"service={Uri.EscapeDataString(service)}");
            hasQuery = true;
        }

        if (authParams.TryGetValue("scope", out var scope))
        {
            tokenUrl.Append(hasQuery ? '&' : '?');
            tokenUrl.Append($"scope={Uri.EscapeDataString(scope)}");
        }
        else
        {
            // Default scope for listing tags
            tokenUrl.Append(hasQuery ? '&' : '?');
            tokenUrl.Append($"scope={Uri.EscapeDataString($"repository:{repository}:pull")}");
        }

        // Request token
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, tokenUrl.ToString());
        
        if (credentials.HasCredentials)
        {
            var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}"));
            tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);
            _logger.LogDebug("Using credentials for {Registry} token request", registry);
        }

        var tokenResponse = await _httpClient.SendAsync(tokenRequest, cancellationToken);

        if (!tokenResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Token request failed for {Registry}: {StatusCode}", registry, tokenResponse.StatusCode);
            return [];
        }

        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
        var accessToken = token?.Token ?? token?.AccessToken;

        if (string.IsNullOrEmpty(accessToken))
        {
            _logger.LogWarning("Empty token received from {Registry}", registry);
            return [];
        }

        // Fetch tags with the token
        var tagsUrl = $"https://{registry}/v2/{repository}/tags/list";
        using var tagsRequest = new HttpRequestMessage(HttpMethod.Get, tagsUrl);
        tagsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var tagsResponse = await _httpClient.SendAsync(tagsRequest, cancellationToken);

        if (!tagsResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to fetch tags from {Registry} with Bearer auth: {StatusCode}", registry, tagsResponse.StatusCode);
            return [];
        }

        var tags = await tagsResponse.Content.ReadFromJsonAsync<TagsListResponse>(cancellationToken: cancellationToken);
        _logger.LogDebug("Found {Count} tags for {Repository}", tags?.Tags?.Count ?? 0, repository);
        return tags?.Tags ?? [];
    }

    private Dictionary<string, string> ParseWwwAuthenticateParams(string? parameter)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        if (string.IsNullOrEmpty(parameter))
            return result;

        // Match key="value" or key=value patterns
        var matches = WwwAuthParamRegex().Matches(parameter);
        
        foreach (Match match in matches)
        {
            var key = match.Groups[1].Value;
            var value = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
            result[key] = value;
        }

        return result;
    }

    [GeneratedRegex(@"(\w+)=""([^""]*)""|(\w+)=([^\s,]+)")]
    private static partial Regex WwwAuthParamRegex();

    private static (string registry, string repository) ParseImageName(string imageName)
    {
        // Remove tag if present
        var imageWithoutTag = imageName.Split(':')[0];

        // Check if image has a registry prefix
        var parts = imageWithoutTag.Split('/');

        if (parts.Length == 1)
        {
            // Official Docker Hub image (e.g., "nginx" -> "library/nginx")
            return ("docker.io", $"library/{parts[0]}");
        }
        else if (parts.Length == 2)
        {
            // Could be Docker Hub user image or registry/image
            if (parts[0].Contains('.') || parts[0].Contains(':'))
            {
                // Has a dot or colon - likely a registry (e.g., "ghcr.io/repo")
                return (parts[0], parts[1]);
            }
            else
            {
                // Docker Hub user image (e.g., "myuser/myimage")
                return ("docker.io", imageWithoutTag);
            }
        }
        else
        {
            // Full path with registry (e.g., "ghcr.io/owner/repo" or "registry.example.com/path/to/image")
            var registry = parts[0];
            var repository = string.Join('/', parts[1..]);
            return (registry, repository);
        }
    }

    private static bool IsDockerHub(string registry)
    {
        return registry.Equals("docker.io", StringComparison.OrdinalIgnoreCase) ||
               registry.Equals("registry-1.docker.io", StringComparison.OrdinalIgnoreCase) ||
               registry.Equals("index.docker.io", StringComparison.OrdinalIgnoreCase);
    }

    private class TokenResponse
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }

    private class TagsListResponse
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; set; }
    }
}
