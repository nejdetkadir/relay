using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Relay.Models;
using Relay.Services;
using Semver;
using Xunit;

namespace Relay.Tests.Services;

public class VersionServiceTests
{
    private readonly VersionService _sut;
    private readonly Mock<ILogger<VersionService>> _loggerMock;

    public VersionServiceTests()
    {
        _loggerMock = new Mock<ILogger<VersionService>>();
        _sut = new VersionService(_loggerMock.Object);
    }

    #region TryParseVersion Tests

    [Theory]
    [InlineData("1.0.0", 1, 0, 0)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("10.20.30", 10, 20, 30)]
    [InlineData("0.0.1", 0, 0, 1)]
    public void TryParseVersion_ValidSemver_ReturnsTrueWithCorrectVersion(string tag, int major, int minor, int patch)
    {
        // Act
        var result = _sut.TryParseVersion(tag, out var version);

        // Assert
        result.Should().BeTrue();
        version.Should().NotBeNull();
        version!.Major.Should().Be(major);
        version.Minor.Should().Be(minor);
        version.Patch.Should().Be(patch);
    }

    [Theory]
    [InlineData("v1.0.0", 1, 0, 0)]
    [InlineData("V2.3.4", 2, 3, 4)]
    [InlineData("version-1.2.3", 1, 2, 3)]
    [InlineData("release-5.0.0", 5, 0, 0)]
    public void TryParseVersion_VersionWithPrefix_StripsPrefix(string tag, int major, int minor, int patch)
    {
        // Act
        var result = _sut.TryParseVersion(tag, out var version);

        // Assert
        result.Should().BeTrue();
        version.Should().NotBeNull();
        version!.Major.Should().Be(major);
        version.Minor.Should().Be(minor);
        version.Patch.Should().Be(patch);
    }

    [Theory]
    [InlineData("1.25", 1, 25, 0)]
    [InlineData("3", 3, 0, 0)]
    [InlineData("7.2", 7, 2, 0)]
    public void TryParseVersion_PartialVersion_ParsesWithDefaults(string tag, int major, int minor, int patch)
    {
        // Act
        var result = _sut.TryParseVersion(tag, out var version);

        // Assert
        result.Should().BeTrue();
        version.Should().NotBeNull();
        version!.Major.Should().Be(major);
        version.Minor.Should().Be(minor);
        version.Patch.Should().Be(patch);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("stable")]
    [InlineData("edge")]
    [InlineData("dev")]
    [InlineData("nightly")]
    [InlineData("LATEST")]
    public void TryParseVersion_NonVersionTags_ReturnsFalse(string tag)
    {
        // Act
        var result = _sut.TryParseVersion(tag, out var version);

        // Assert
        result.Should().BeFalse();
        version.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParseVersion_EmptyOrNull_ReturnsFalse(string? tag)
    {
        // Act
        var result = _sut.TryParseVersion(tag!, out var version);

        // Assert
        result.Should().BeFalse();
        version.Should().BeNull();
    }

    [Theory]
    [InlineData("alpine")]
    [InlineData("bullseye")]
    [InlineData("bookworm")]
    public void TryParseVersion_NonNumericTags_ReturnsFalse(string tag)
    {
        // Act
        var result = _sut.TryParseVersion(tag, out var version);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region IsNewerVersion Tests

    [Theory]
    [InlineData(1, 0, 0, 1, 0, 1, UpdateStrategy.Patch, true)]  // 1.0.0 -> 1.0.1 (patch)
    [InlineData(1, 0, 0, 1, 0, 5, UpdateStrategy.Patch, true)]  // 1.0.0 -> 1.0.5 (patch)
    [InlineData(1, 0, 0, 1, 1, 0, UpdateStrategy.Patch, false)] // 1.0.0 -> 1.1.0 (minor, not allowed)
    [InlineData(1, 0, 0, 2, 0, 0, UpdateStrategy.Patch, false)] // 1.0.0 -> 2.0.0 (major, not allowed)
    public void IsNewerVersion_PatchStrategy_OnlyAllowsPatchUpdates(
        int curMajor, int curMinor, int curPatch,
        int newMajor, int newMinor, int newPatch,
        UpdateStrategy strategy, bool expected)
    {
        // Arrange
        var current = new SemVersion(curMajor, curMinor, curPatch);
        var candidate = new SemVersion(newMajor, newMinor, newPatch);

        // Act
        var result = _sut.IsNewerVersion(current, candidate, strategy);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 0, 0, 1, 0, 1, UpdateStrategy.Minor, true)]  // 1.0.0 -> 1.0.1 (patch allowed)
    [InlineData(1, 0, 0, 1, 1, 0, UpdateStrategy.Minor, true)]  // 1.0.0 -> 1.1.0 (minor allowed)
    [InlineData(1, 0, 0, 1, 5, 3, UpdateStrategy.Minor, true)]  // 1.0.0 -> 1.5.3 (minor allowed)
    [InlineData(1, 0, 0, 2, 0, 0, UpdateStrategy.Minor, false)] // 1.0.0 -> 2.0.0 (major, not allowed)
    public void IsNewerVersion_MinorStrategy_AllowsMinorAndPatchUpdates(
        int curMajor, int curMinor, int curPatch,
        int newMajor, int newMinor, int newPatch,
        UpdateStrategy strategy, bool expected)
    {
        // Arrange
        var current = new SemVersion(curMajor, curMinor, curPatch);
        var candidate = new SemVersion(newMajor, newMinor, newPatch);

        // Act
        var result = _sut.IsNewerVersion(current, candidate, strategy);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 0, 0, 1, 0, 1, UpdateStrategy.Major, true)]  // 1.0.0 -> 1.0.1 (patch)
    [InlineData(1, 0, 0, 1, 1, 0, UpdateStrategy.Major, true)]  // 1.0.0 -> 1.1.0 (minor)
    [InlineData(1, 0, 0, 2, 0, 0, UpdateStrategy.Major, true)]  // 1.0.0 -> 2.0.0 (major)
    [InlineData(1, 0, 0, 5, 3, 2, UpdateStrategy.Major, true)]  // 1.0.0 -> 5.3.2 (major)
    public void IsNewerVersion_MajorStrategy_AllowsAllNewerVersions(
        int curMajor, int curMinor, int curPatch,
        int newMajor, int newMinor, int newPatch,
        UpdateStrategy strategy, bool expected)
    {
        // Arrange
        var current = new SemVersion(curMajor, curMinor, curPatch);
        var candidate = new SemVersion(newMajor, newMinor, newPatch);

        // Act
        var result = _sut.IsNewerVersion(current, candidate, strategy);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void IsNewerVersion_DigestStrategy_AlwaysReturnsFalse()
    {
        // Arrange
        var current = new SemVersion(1, 0, 0);
        var candidate = new SemVersion(2, 0, 0);

        // Act
        var result = _sut.IsNewerVersion(current, candidate, UpdateStrategy.Digest);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(1, 0, 0, 1, 0, 0)]  // Same version
    [InlineData(2, 0, 0, 1, 0, 0)]  // Older version
    [InlineData(1, 5, 0, 1, 4, 0)]  // Older minor
    public void IsNewerVersion_SameOrOlderVersion_ReturnsFalse(
        int curMajor, int curMinor, int curPatch,
        int newMajor, int newMinor, int newPatch)
    {
        // Arrange
        var current = new SemVersion(curMajor, curMinor, curPatch);
        var candidate = new SemVersion(newMajor, newMinor, newPatch);

        // Act
        var result = _sut.IsNewerVersion(current, candidate, UpdateStrategy.Major);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region FindNewestVersion Tests

    [Fact]
    public void FindNewestVersion_WithNewerPatchVersions_ReturnsNewest()
    {
        // Arrange
        var currentTag = "1.0.0";
        var availableTags = new[] { "1.0.0", "1.0.1", "1.0.2", "1.0.3" };

        // Act
        var result = _sut.FindNewestVersion(currentTag, availableTags, UpdateStrategy.Patch);

        // Assert
        result.Should().Be("1.0.3");
    }

    [Fact]
    public void FindNewestVersion_WithNewerMinorVersions_ReturnsNewestMinor()
    {
        // Arrange
        var currentTag = "1.0.0";
        var availableTags = new[] { "1.0.0", "1.0.1", "1.1.0", "1.2.0", "2.0.0" };

        // Act
        var result = _sut.FindNewestVersion(currentTag, availableTags, UpdateStrategy.Minor);

        // Assert
        result.Should().Be("1.2.0");
    }

    [Fact]
    public void FindNewestVersion_WithMajorStrategy_ReturnsNewestOverall()
    {
        // Arrange
        var currentTag = "1.0.0";
        var availableTags = new[] { "1.0.0", "1.1.0", "2.0.0", "3.0.0" };

        // Act
        var result = _sut.FindNewestVersion(currentTag, availableTags, UpdateStrategy.Major);

        // Assert
        result.Should().Be("3.0.0");
    }

    [Fact]
    public void FindNewestVersion_NoNewerVersions_ReturnsNull()
    {
        // Arrange
        var currentTag = "2.0.0";
        var availableTags = new[] { "1.0.0", "1.5.0", "2.0.0" };

        // Act
        var result = _sut.FindNewestVersion(currentTag, availableTags, UpdateStrategy.Major);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void FindNewestVersion_InvalidCurrentTag_ReturnsNull()
    {
        // Arrange
        var currentTag = "latest";
        var availableTags = new[] { "1.0.0", "2.0.0" };

        // Act
        var result = _sut.FindNewestVersion(currentTag, availableTags, UpdateStrategy.Major);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void FindNewestVersion_MixedValidAndInvalidTags_FiltersInvalid()
    {
        // Arrange
        var currentTag = "1.0.0";
        var availableTags = new[] { "latest", "1.0.0", "1.1.0", "alpine", "1.2.0", "edge" };

        // Act
        var result = _sut.FindNewestVersion(currentTag, availableTags, UpdateStrategy.Minor);

        // Assert
        result.Should().Be("1.2.0");
    }

    [Fact]
    public void FindNewestVersion_WithPrefixedVersions_HandlesCorrectly()
    {
        // Arrange
        var currentTag = "v1.0.0";
        var availableTags = new[] { "v1.0.0", "v1.1.0", "v1.2.0" };

        // Act
        var result = _sut.FindNewestVersion(currentTag, availableTags, UpdateStrategy.Minor);

        // Assert
        result.Should().Be("v1.2.0");
    }

    #endregion
}
