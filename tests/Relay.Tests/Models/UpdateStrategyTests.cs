using FluentAssertions;
using Relay.Models;
using Xunit;

namespace Relay.Tests.Models;

public class UpdateStrategyTests
{
    #region Parse Tests

    [Theory]
    [InlineData("digest", UpdateStrategy.Digest)]
    [InlineData("DIGEST", UpdateStrategy.Digest)]
    [InlineData("Digest", UpdateStrategy.Digest)]
    [InlineData("patch", UpdateStrategy.Patch)]
    [InlineData("PATCH", UpdateStrategy.Patch)]
    [InlineData("minor", UpdateStrategy.Minor)]
    [InlineData("MINOR", UpdateStrategy.Minor)]
    [InlineData("major", UpdateStrategy.Major)]
    [InlineData("MAJOR", UpdateStrategy.Major)]
    public void Parse_ValidStrategyStrings_ReturnsCorrectStrategy(string input, UpdateStrategy expected)
    {
        // Act
        var result = UpdateStrategyExtensions.Parse(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_NullOrEmpty_ReturnsDigest(string? input)
    {
        // Act
        var result = UpdateStrategyExtensions.Parse(input);

        // Assert
        result.Should().Be(UpdateStrategy.Digest);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("unknown")]
    [InlineData("semver")]
    [InlineData("auto")]
    public void Parse_InvalidStrings_ReturnsDigest(string input)
    {
        // Act
        var result = UpdateStrategyExtensions.Parse(input);

        // Assert
        result.Should().Be(UpdateStrategy.Digest);
    }

    #endregion

    #region FromLabels Tests

    [Theory]
    [InlineData("digest", UpdateStrategy.Digest)]
    [InlineData("patch", UpdateStrategy.Patch)]
    [InlineData("minor", UpdateStrategy.Minor)]
    [InlineData("major", UpdateStrategy.Major)]
    public void FromLabels_WithValidLabel_ReturnsCorrectStrategy(string labelValue, UpdateStrategy expected)
    {
        // Arrange
        var labels = new Dictionary<string, string>
        {
            [UpdateStrategyExtensions.LabelKey] = labelValue
        };

        // Act
        var result = UpdateStrategyExtensions.FromLabels(labels);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void FromLabels_NullLabels_ReturnsDigest()
    {
        // Act
        var result = UpdateStrategyExtensions.FromLabels(null);

        // Assert
        result.Should().Be(UpdateStrategy.Digest);
    }

    [Fact]
    public void FromLabels_EmptyLabels_ReturnsDigest()
    {
        // Arrange
        var labels = new Dictionary<string, string>();

        // Act
        var result = UpdateStrategyExtensions.FromLabels(labels);

        // Assert
        result.Should().Be(UpdateStrategy.Digest);
    }

    [Fact]
    public void FromLabels_MissingLabel_ReturnsDigest()
    {
        // Arrange
        var labels = new Dictionary<string, string>
        {
            ["some.other.label"] = "value"
        };

        // Act
        var result = UpdateStrategyExtensions.FromLabels(labels);

        // Assert
        result.Should().Be(UpdateStrategy.Digest);
    }

    #endregion

    #region RequiresRegistryQuery Tests

    [Theory]
    [InlineData(UpdateStrategy.Digest, false)]
    [InlineData(UpdateStrategy.Patch, true)]
    [InlineData(UpdateStrategy.Minor, true)]
    [InlineData(UpdateStrategy.Major, true)]
    public void RequiresRegistryQuery_ReturnsCorrectValue(UpdateStrategy strategy, bool expected)
    {
        // Act
        var result = strategy.RequiresRegistryQuery();

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region GetHealthCheckTimeout Tests

    [Fact]
    public void GetHealthCheckTimeout_NullLabels_ReturnsDefault()
    {
        // Arrange
        var defaultTimeout = 60;

        // Act
        var result = UpdateStrategyExtensions.GetHealthCheckTimeout(null, defaultTimeout);

        // Assert
        result.Should().Be(60);
    }

    [Fact]
    public void GetHealthCheckTimeout_NoLabel_ReturnsDefault()
    {
        // Arrange
        var labels = new Dictionary<string, string>();
        var defaultTimeout = 60;

        // Act
        var result = UpdateStrategyExtensions.GetHealthCheckTimeout(labels, defaultTimeout);

        // Assert
        result.Should().Be(60);
    }

    [Theory]
    [InlineData("30", 60, 30)]
    [InlineData("120", 60, 120)]
    [InlineData("300", 60, 300)]
    public void GetHealthCheckTimeout_ValidLabel_ReturnsLabelValue(string labelValue, int defaultTimeout, int expected)
    {
        // Arrange
        var labels = new Dictionary<string, string>
        {
            [UpdateStrategyExtensions.HealthCheckTimeoutLabelKey] = labelValue
        };

        // Act
        var result = UpdateStrategyExtensions.GetHealthCheckTimeout(labels, defaultTimeout);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-100")]
    public void GetHealthCheckTimeout_ZeroOrNegative_ReturnsDefault(string labelValue)
    {
        // Arrange
        var labels = new Dictionary<string, string>
        {
            [UpdateStrategyExtensions.HealthCheckTimeoutLabelKey] = labelValue
        };
        var defaultTimeout = 60;

        // Act
        var result = UpdateStrategyExtensions.GetHealthCheckTimeout(labels, defaultTimeout);

        // Assert
        result.Should().Be(60);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("abc")]
    [InlineData("1.5")]
    public void GetHealthCheckTimeout_InvalidLabel_ReturnsDefault(string labelValue)
    {
        // Arrange
        var labels = new Dictionary<string, string>
        {
            [UpdateStrategyExtensions.HealthCheckTimeoutLabelKey] = labelValue
        };
        var defaultTimeout = 60;

        // Act
        var result = UpdateStrategyExtensions.GetHealthCheckTimeout(labels, defaultTimeout);

        // Assert
        result.Should().Be(60);
    }

    #endregion

    #region Label Key Constants Tests

    [Fact]
    public void LabelKey_HasCorrectValue()
    {
        UpdateStrategyExtensions.LabelKey.Should().Be("relay.update");
    }

    [Fact]
    public void HealthCheckTimeoutLabelKey_HasCorrectValue()
    {
        UpdateStrategyExtensions.HealthCheckTimeoutLabelKey.Should().Be("relay.healthcheck.timeout");
    }

    #endregion
}
