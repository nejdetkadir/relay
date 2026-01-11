using Microsoft.Extensions.Logging;
using Relay.Models;
using Semver;

namespace Relay.Services;

/// <summary>
/// Service for semantic version parsing and comparison.
/// </summary>
public class VersionService : IVersionService
{
    private readonly ILogger<VersionService> _logger;

    // Common prefixes to strip from version tags (ordered by length descending to match longer prefixes first)
    private static readonly string[] VersionPrefixes = ["version-", "release-", "v", "V"];

    public VersionService(ILogger<VersionService> logger)
    {
        _logger = logger;
    }

    public bool TryParseVersion(string tag, out SemVersion? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(tag))
            return false;

        // Skip non-version tags
        if (tag.Equals("latest", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("stable", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("edge", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("dev", StringComparison.OrdinalIgnoreCase) ||
            tag.Equals("nightly", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Strip common prefixes
        var normalizedTag = tag;
        foreach (var prefix in VersionPrefixes)
        {
            if (normalizedTag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                normalizedTag = normalizedTag[prefix.Length..];
                break;
            }
        }

        // Try to parse as semver
        if (SemVersion.TryParse(normalizedTag, SemVersionStyles.Any, out version))
        {
            return true;
        }

        // Try parsing with additional flexibility (e.g., "1.25" -> "1.25.0")
        var parts = normalizedTag.Split('.', '-', '+');
        if (parts.Length >= 1 && int.TryParse(parts[0], out var major))
        {
            var minor = parts.Length >= 2 && int.TryParse(parts[1], out var m) ? m : 0;
            var patch = parts.Length >= 3 && int.TryParse(parts[2], out var p) ? p : 0;

            version = new SemVersion(major, minor, patch);
            return true;
        }

        return false;
    }

    public string? FindNewestVersion(string currentTag, IEnumerable<string> availableTags, UpdateStrategy strategy)
    {
        if (!TryParseVersion(currentTag, out var currentVersion) || currentVersion == null)
        {
            _logger.LogDebug("Current tag '{Tag}' is not a valid semantic version", currentTag);
            return null;
        }

        _logger.LogDebug("Current version: {Version}, Strategy: {Strategy}", currentVersion, strategy);

        SemVersion? newestVersion = null;
        string? newestTag = null;

        foreach (var tag in availableTags)
        {
            if (!TryParseVersion(tag, out var candidateVersion) || candidateVersion == null)
                continue;

            // Skip if not newer according to strategy
            if (!IsNewerVersion(currentVersion, candidateVersion, strategy))
                continue;

            // Check if this is the newest so far
            if (newestVersion == null || candidateVersion.ComparePrecedenceTo(newestVersion) > 0)
            {
                newestVersion = candidateVersion;
                newestTag = tag;
            }
        }

        if (newestTag != null)
        {
            _logger.LogDebug("Found newer version: {NewTag} ({NewVersion})", newestTag, newestVersion);
        }

        return newestTag;
    }

    public bool IsNewerVersion(SemVersion current, SemVersion candidate, UpdateStrategy strategy)
    {
        // Must be strictly newer
        if (candidate.ComparePrecedenceTo(current) <= 0)
            return false;

        return strategy switch
        {
            UpdateStrategy.Patch => candidate.Major == current.Major && candidate.Minor == current.Minor,
            UpdateStrategy.Minor => candidate.Major == current.Major,
            UpdateStrategy.Major => true, // Any newer version is acceptable
            UpdateStrategy.Digest => false, // Digest strategy doesn't use version comparison
            _ => false
        };
    }
}
