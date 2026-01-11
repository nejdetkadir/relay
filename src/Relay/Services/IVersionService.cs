using Relay.Models;
using Semver;

namespace Relay.Services;

/// <summary>
/// Service interface for semantic version parsing and comparison.
/// </summary>
public interface IVersionService
{
    /// <summary>
    /// Attempts to parse a semantic version from an image tag.
    /// </summary>
    /// <param name="tag">The image tag to parse.</param>
    /// <param name="version">The parsed version if successful.</param>
    /// <returns>True if the tag is a valid semantic version.</returns>
    bool TryParseVersion(string tag, out SemVersion? version);

    /// <summary>
    /// Finds the newest version from a list of tags that matches the update strategy.
    /// </summary>
    /// <param name="currentTag">The current image tag.</param>
    /// <param name="availableTags">List of available tags from the registry.</param>
    /// <param name="strategy">The update strategy to apply.</param>
    /// <returns>The newest matching tag, or null if no update is available.</returns>
    string? FindNewestVersion(string currentTag, IEnumerable<string> availableTags, UpdateStrategy strategy);

    /// <summary>
    /// Checks if a version is newer than another according to the update strategy.
    /// </summary>
    /// <param name="current">The current version.</param>
    /// <param name="candidate">The candidate version to compare.</param>
    /// <param name="strategy">The update strategy.</param>
    /// <returns>True if candidate is newer and matches the strategy constraints.</returns>
    bool IsNewerVersion(SemVersion current, SemVersion candidate, UpdateStrategy strategy);
}
