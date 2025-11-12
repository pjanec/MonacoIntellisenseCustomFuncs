using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ScribanLanguageServer.Core.ApiSpec;
using ScribanLanguageServer.Core.Health;
using Xunit;
using ApiSpecClass = ScribanLanguageServer.Core.ApiSpec.ApiSpec;

namespace ScribanLanguageServer.Tests.Unit.Health;

[Trait("Stage", "B6")]
public class ApiSpecHealthCheckTests
{
    private ApiSpecHealthCheck CreateHealthCheck(IApiSpecService apiSpec)
    {
        return new ApiSpecHealthCheck(apiSpec, NullLogger<ApiSpecHealthCheck>.Instance);
    }

    [Fact]
    public async Task CheckHealthAsync_WithGlobalsLoaded_ReturnsHealthy()
    {
        // Arrange
        var globals = new List<GlobalEntry>
        {
            new() { Name = "func1", Type = "function", Hover = "Test function" },
            new() { Name = "func2", Type = "function", Hover = "Test function 2" },
            new() { Name = "obj1", Type = "object", Hover = "Test object" }
        };

        var spec = new ApiSpecClass { Globals = globals };

        var apiSpec = new Mock<IApiSpecService>();
        apiSpec.Setup(a => a.CurrentSpec).Returns(spec);

        var healthCheck = CreateHealthCheck(apiSpec.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("3 globals loaded");
        result.Description.Should().Contain("2 functions");
        result.Description.Should().Contain("1 object");
        result.Data.Should().ContainKey("total_globals");
        result.Data.Should().ContainKey("functions");
        result.Data.Should().ContainKey("objects");
        result.Data["total_globals"].Should().Be(3);
        result.Data["functions"].Should().Be(2);
        result.Data["objects"].Should().Be(1);
    }

    [Fact]
    public async Task CheckHealthAsync_NoGlobals_ReturnsUnhealthy()
    {
        // Arrange
        var spec = new ApiSpecClass { Globals = new List<GlobalEntry>() };
        var apiSpec = new Mock<IApiSpecService>();
        apiSpec.Setup(a => a.CurrentSpec).Returns(spec);

        var healthCheck = CreateHealthCheck(apiSpec.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("No globals loaded");
    }

    [Fact]
    public async Task CheckHealthAsync_NullSpec_ReturnsUnhealthy()
    {
        // Arrange
        var apiSpec = new Mock<IApiSpecService>();
        apiSpec.Setup(a => a.CurrentSpec).Returns((ApiSpecClass)null!);

        var healthCheck = CreateHealthCheck(apiSpec.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("No globals loaded");
    }

    [Fact]
    public async Task CheckHealthAsync_ServiceThrowsException_ReturnsUnhealthy()
    {
        // Arrange
        var apiSpec = new Mock<IApiSpecService>();
        apiSpec.Setup(a => a.CurrentSpec).Throws(new InvalidOperationException("Test error"));

        var healthCheck = CreateHealthCheck(apiSpec.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("ApiSpec error");
        result.Exception.Should().NotBeNull();
        result.Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_NullApiSpec_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new ApiSpecHealthCheck(null!, NullLogger<ApiSpecHealthCheck>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var apiSpec = new Mock<IApiSpecService>();

        // Act
        var act = () => new ApiSpecHealthCheck(apiSpec.Object, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
