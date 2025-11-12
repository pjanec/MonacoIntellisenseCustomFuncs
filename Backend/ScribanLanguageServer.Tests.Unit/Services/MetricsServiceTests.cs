using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ScribanLanguageServer.Core.Services;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Services;

[Trait("Stage", "B6")]
public class MetricsServiceTests
{
    private MetricsService CreateService()
    {
        return new MetricsService(NullLogger<MetricsService>.Instance);
    }

    [Fact]
    public void RecordRequest_Success_IncrementsCounters()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.RecordRequest("test_method", 100, true);

        // Assert
        var snapshot = service.GetSnapshot();
        snapshot.TotalRequests.Should().Be(1);
        snapshot.SuccessfulRequests.Should().Be(1);
        snapshot.FailedRequests.Should().Be(0);
        snapshot.SuccessRate.Should().Be(100.0);
    }

    [Fact]
    public void RecordRequest_Failure_IncrementsCounters()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.RecordRequest("test_method", 100, false);

        // Assert
        var snapshot = service.GetSnapshot();
        snapshot.TotalRequests.Should().Be(1);
        snapshot.SuccessfulRequests.Should().Be(0);
        snapshot.FailedRequests.Should().Be(1);
        snapshot.SuccessRate.Should().Be(0.0);
    }

    [Fact]
    public void RecordRequest_Mixed_CalculatesCorrectSuccessRate()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.RecordRequest("test1", 100, true);
        service.RecordRequest("test2", 100, true);
        service.RecordRequest("test3", 100, false);
        service.RecordRequest("test4", 100, true);

        // Assert
        var snapshot = service.GetSnapshot();
        snapshot.TotalRequests.Should().Be(4);
        snapshot.SuccessfulRequests.Should().Be(3);
        snapshot.FailedRequests.Should().Be(1);
        snapshot.SuccessRate.Should().Be(75.0);
    }

    [Fact]
    public void RecordCacheHit_IncrementsCounter()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.RecordCacheHit("parse");
        service.RecordCacheHit("parse");

        // Assert
        var snapshot = service.GetSnapshot();
        snapshot.CacheHits.Should().Be(2);
        snapshot.CacheMisses.Should().Be(0);
    }

    [Fact]
    public void RecordCacheMiss_IncrementsCounter()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.RecordCacheMiss("parse");

        // Assert
        var snapshot = service.GetSnapshot();
        snapshot.CacheHits.Should().Be(0);
        snapshot.CacheMisses.Should().Be(1);
    }

    [Fact]
    public void CacheHitRate_CalculatedCorrectly()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.RecordCacheHit("parse");
        service.RecordCacheHit("parse");
        service.RecordCacheHit("parse");
        service.RecordCacheMiss("parse");

        // Assert
        var snapshot = service.GetSnapshot();
        snapshot.CacheHits.Should().Be(3);
        snapshot.CacheMisses.Should().Be(1);
        snapshot.CacheHitRate.Should().Be(75.0);
    }

    [Fact]
    public void CacheHitRate_NoCacheActivity_ReturnsZero()
    {
        // Arrange
        var service = CreateService();

        // Act - no cache activity

        // Assert
        var snapshot = service.GetSnapshot();
        snapshot.CacheHitRate.Should().Be(0.0);
    }

    [Fact]
    public void RecordError_IncrementsCounters()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.RecordError("parsing", "SyntaxError");
        service.RecordError("parsing", "TimeoutException");
        service.RecordError("validation", "ArgumentException");

        // Assert
        var snapshot = service.GetSnapshot();
        snapshot.ErrorsByType.Should().ContainKey("parsing:SyntaxError");
        snapshot.ErrorsByType.Should().ContainKey("parsing:TimeoutException");
        snapshot.ErrorsByType.Should().ContainKey("validation:ArgumentException");
        snapshot.ErrorsByType["parsing:SyntaxError"].Should().Be(1);
        snapshot.ErrorsByType["parsing:TimeoutException"].Should().Be(1);
        snapshot.ErrorsByType["validation:ArgumentException"].Should().Be(1);
    }

    [Fact]
    public void RecordError_SameErrorMultipleTimes_IncrementsCount()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.RecordError("parsing", "SyntaxError");
        service.RecordError("parsing", "SyntaxError");
        service.RecordError("parsing", "SyntaxError");

        // Assert
        var snapshot = service.GetSnapshot();
        snapshot.ErrorsByType["parsing:SyntaxError"].Should().Be(3);
    }

    [Fact]
    public void RecordError_NullErrorType_UsesUnknown()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.RecordError("general", null);

        // Assert
        var snapshot = service.GetSnapshot();
        snapshot.ErrorsByType.Should().ContainKey("general:unknown");
        snapshot.ErrorsByType["general:unknown"].Should().Be(1);
    }

    [Fact]
    public void RecordDocumentSize_DoesNotThrow()
    {
        // Arrange
        var service = CreateService();

        // Act
        var act = () =>
        {
            service.RecordDocumentSize(1024);
            service.RecordDocumentSize(2048);
            service.RecordDocumentSize(512);
        };

        // Assert - just verify it doesn't throw
        act.Should().NotThrow();
    }

    [Fact]
    public void StartTimer_ReturnsDisposable()
    {
        // Arrange
        var service = CreateService();

        // Act
        var timer = service.StartTimer("test_operation");

        // Assert
        timer.Should().NotBeNull();
        timer.Should().BeAssignableTo<IDisposable>();
    }

    [Fact]
    public void StartTimer_DisposingTimer_RecordsRequest()
    {
        // Arrange
        var service = CreateService();

        // Act
        using (service.StartTimer("test_operation"))
        {
            // Simulate some work
            Thread.Sleep(10);
        }

        // Assert
        var snapshot = service.GetSnapshot();
        snapshot.TotalRequests.Should().Be(1);
        snapshot.SuccessfulRequests.Should().Be(1);
    }

    [Fact]
    public void StartTimer_MultipleTimers_RecordsAllRequests()
    {
        // Arrange
        var service = CreateService();

        // Act
        using (service.StartTimer("op1")) { }
        using (service.StartTimer("op2")) { }
        using (service.StartTimer("op3")) { }

        // Assert
        var snapshot = service.GetSnapshot();
        snapshot.TotalRequests.Should().Be(3);
        snapshot.SuccessfulRequests.Should().Be(3);
    }

    [Fact]
    public void GetSnapshot_InitialState_ReturnsZeros()
    {
        // Arrange
        var service = CreateService();

        // Act
        var snapshot = service.GetSnapshot();

        // Assert
        snapshot.TotalRequests.Should().Be(0);
        snapshot.SuccessfulRequests.Should().Be(0);
        snapshot.FailedRequests.Should().Be(0);
        snapshot.SuccessRate.Should().Be(0);
        snapshot.CacheHits.Should().Be(0);
        snapshot.CacheMisses.Should().Be(0);
        snapshot.CacheHitRate.Should().Be(0);
        snapshot.ErrorsByType.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentMetrics_ThreadSafe()
    {
        // Arrange
        var service = CreateService();
        var tasks = new List<Task>();

        // Act - simulate concurrent recording
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                service.RecordRequest("concurrent", 10, true);
                service.RecordCacheHit("concurrent");
                service.RecordCacheMiss("concurrent");
                service.RecordError("concurrent", "TestError");
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        var snapshot = service.GetSnapshot();
        snapshot.TotalRequests.Should().Be(10);
        snapshot.SuccessfulRequests.Should().Be(10);
        snapshot.CacheHits.Should().Be(10);
        snapshot.CacheMisses.Should().Be(10);
        snapshot.ErrorsByType["concurrent:TestError"].Should().Be(10);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new MetricsService(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
