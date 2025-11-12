using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ScribanLanguageServer.Core.Services;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Services;

[Trait("Stage", "B5")]
public class RateLimitServiceTests
{
    private RateLimitService CreateService()
    {
        return new RateLimitService(NullLogger<RateLimitService>.Instance);
    }

    [Fact]
    public void TryAcquire_WithinLimit_ReturnsTrue()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert - should allow first 10 requests
        for (int i = 0; i < 10; i++)
        {
            service.TryAcquire("conn1").Should().BeTrue($"request {i + 1} should succeed");
        }
    }

    [Fact]
    public void TryAcquire_ExceedsLimit_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();

        // Act - exhaust tokens
        for (int i = 0; i < 10; i++)
        {
            service.TryAcquire("conn1");
        }

        // Assert - 11th request should fail
        service.TryAcquire("conn1").Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquire_AfterRefill_AllowsRequests()
    {
        // Arrange
        var service = CreateService();

        // Act - exhaust tokens
        for (int i = 0; i < 10; i++)
        {
            service.TryAcquire("conn1");
        }

        // Verify exhausted
        service.TryAcquire("conn1").Should().BeFalse();

        // Wait for refill (1 second + buffer)
        await Task.Delay(1100);

        // Assert - should allow requests again
        service.TryAcquire("conn1").Should().BeTrue();
    }

    [Fact]
    public void TryAcquire_DifferentConnections_IndependentLimits()
    {
        // Arrange
        var service = CreateService();

        // Act - exhaust conn1
        for (int i = 0; i < 10; i++)
        {
            service.TryAcquire("conn1");
        }

        // Assert - conn1 exhausted but conn2 should still work
        service.TryAcquire("conn1").Should().BeFalse();
        service.TryAcquire("conn2").Should().BeTrue();
    }

    [Fact]
    public void RemoveConnection_RemovesBucket()
    {
        // Arrange
        var service = CreateService();

        // Exhaust tokens
        for (int i = 0; i < 10; i++)
        {
            service.TryAcquire("conn1");
        }

        service.TryAcquire("conn1").Should().BeFalse();

        // Act - remove connection
        service.RemoveConnection("conn1");

        // Assert - new connection should get fresh bucket with full tokens
        service.TryAcquire("conn1").Should().BeTrue();
    }

    [Fact]
    public void GetStats_NewConnection_ReturnsMaxTokens()
    {
        // Arrange
        var service = CreateService();

        // Act
        var stats = service.GetStats("conn1");

        // Assert
        stats.AvailableTokens.Should().Be(10);
        stats.MaxTokens.Should().Be(10);
    }

    [Fact]
    public void GetStats_AfterConsumption_ReturnsRemainingTokens()
    {
        // Arrange
        var service = CreateService();

        // Consume 3 tokens
        service.TryAcquire("conn1");
        service.TryAcquire("conn1");
        service.TryAcquire("conn1");

        // Act
        var stats = service.GetStats("conn1");

        // Assert
        stats.AvailableTokens.Should().Be(7);
        stats.MaxTokens.Should().Be(10);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new RateLimitService(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task TryAcquire_ConcurrentRequests_ThreadSafe()
    {
        // Arrange
        var service = CreateService();
        var tasks = new List<Task<bool>>();

        // Act - make 20 concurrent requests from same connection
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(Task.Run(() => service.TryAcquire("conn1")));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - exactly 10 should succeed, 10 should fail
        results.Count(r => r).Should().Be(10, "only 10 tokens available");
        results.Count(r => !r).Should().Be(10, "10 requests should be rate limited");
    }
}
