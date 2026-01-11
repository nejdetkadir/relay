namespace Relay.Models;

/// <summary>
/// Represents Docker registry credentials.
/// </summary>
public class DockerCredentials
{
    /// <summary>
    /// The registry hostname (e.g., "ghcr.io", "registry.example.com").
    /// </summary>
    public required string Registry { get; init; }

    /// <summary>
    /// The username for authentication.
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// The password or token for authentication.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Whether credentials are available for this registry.
    /// </summary>
    public bool HasCredentials => !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password);

    /// <summary>
    /// Creates a credential object indicating no credentials are available.
    /// </summary>
    public static DockerCredentials None(string registry) => new()
    {
        Registry = registry,
        Username = null,
        Password = null
    };

    /// <summary>
    /// Creates a credential object with username and password.
    /// </summary>
    public static DockerCredentials Create(string registry, string username, string password) => new()
    {
        Registry = registry,
        Username = username,
        Password = password
    };
}

/// <summary>
/// Represents the Docker config.json file structure.
/// </summary>
public class DockerConfig
{
    /// <summary>
    /// Authentication entries per registry.
    /// </summary>
    public Dictionary<string, DockerAuthEntry>? Auths { get; set; }

    /// <summary>
    /// Credential helpers for specific registries.
    /// </summary>
    public Dictionary<string, string>? CredHelpers { get; set; }

    /// <summary>
    /// Default credential store.
    /// </summary>
    public string? CredsStore { get; set; }
}

/// <summary>
/// Represents an authentication entry in Docker config.
/// </summary>
public class DockerAuthEntry
{
    /// <summary>
    /// Base64 encoded "username:password" string.
    /// </summary>
    public string? Auth { get; set; }

    /// <summary>
    /// Username (if stored separately).
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password (if stored separately).
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Email associated with the account.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Identity token for token-based auth.
    /// </summary>
    public string? IdentityToken { get; set; }

    /// <summary>
    /// Registry token.
    /// </summary>
    public string? RegistryToken { get; set; }
}
