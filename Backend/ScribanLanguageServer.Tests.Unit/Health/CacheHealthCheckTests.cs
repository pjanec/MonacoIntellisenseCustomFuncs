using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ScribanLanguageServer.Core.Health;
using ScribanLanguageServer.Core.Services;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Health;

[Trait("Stage", "B6")]
public class CacheHealthCheckTests
{
    private CacheHealthCheck CreateHealthCheck(IScribanParserService parser)
    {
        return new CacheHealthCheck(parser, NullLogger<CacheHealthCheck>.Instance);
    }

    [Fact]
    public async Task CheckHealthAsync_HighCacheHitRate_ReturnsHealthy()
    {
        // Arrange
        var stats = new CacheStatistics(
            TotalEntries: 10,
            TotalHits: 80,
            TotalMisses: 20,
            HitRate: 80.0);

        var parser = new Mock<IScribanParserService>();
        parser.Setup(p => p.GetCacheStatistics()).Returns(stats);

        var healthCheck = CreateHealthCheck(parser.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("10 entries");
        result.Description.Should().Contain("80");
        result.Data.Should().ContainKey("cache_entries");
        result.Data.Should().ContainKey("cache_hits");
        result.Data.Should().ContainKey("cache_misses");
        result.Data.Should().ContainKey("total_requests");
        result.Data.Should().ContainKey("hit_rate_percent");
        result.Data["cache_entries"].Should().Be(10);
        result.Data["cache_hits"].Should().Be(80);
        result.Data["cache_misses"].Should().Be(20);
        result.Data["total_requests"].Should().Be(100);
        result.Data["hit_rate_percent"].Should().Be(80.0);
    }

    [Fact]
    public async Task CheckHealthAsync_LowCacheHitRate_ReturnsDegraded()
    {
        // Arrange
        var stats = new CacheStatistics(
            TotalEntries: 5,
            TotalHits: 40,
            TotalMisses: 70,
            HitRate: 36.36); // Hit rate: ~36%

        var parser = new Mock<IScribanParserService>();
        parser.Setup(p => p.GetCacheStatistics()).Returns(stats);

        var healthCheck = CreateHealthCheck(parser.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("Cache hit rate is low");
        ((double)result.Data["hit_rate_percent"]).Should().BeLessThan(50);
    }

    [Fact]
    public async Task CheckHealthAsync_FewRequests_ReturnsHealthy()
    {
        // Arrange - even with low hit rate, if total requests < 100, should be healthy
        var stats = new CacheStatistics(
            TotalEntries: 2,
            TotalHits: 20,
            TotalMisses: 70,
            HitRate: 22.22); // Hit rate: ~22%, but total < 100

        var parser = new Mock<IScribanParserService>();
        parser.Setup(p => p.GetCacheStatistics()).Returns(stats);

        var healthCheck = CreateHealthCheck(parser.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_NoRequests_ReturnsHealthy()
    {
        // Arrange
        var stats = new CacheStatistics(
            TotalEntries: 0,
            TotalHits: 0,
            TotalMisses: 0,
            HitRate: 0);

        var parser = new Mock<IScribanParserService>();
        parser.Setup(p => p.GetCacheStatistics()).Returns(stats);

        var healthCheck = CreateHealthCheck(parser.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["hit_rate_percent"].Should().Be(0);
    }

    [Fact]
    public async Task CheckHealthAsync_ServiceThrowsException_ReturnsUnhealthy()
    {
        // Arrange
        var parser = new Mock<IScribanParserService>();
        parser.Setup(p => p.GetCacheStatistics()).Throws(new InvalidOperationException("Test error"));

        var healthCheck = CreateHealthCheck(parser.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Cache error");
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_NullParser_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new CacheHealthCheck(null!, NullLogger<CacheHealthCheck>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var parser = new Mock<IScribanParserService>();

        // Act
        var act = () => new CacheHealthCheck(parser.Object, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
