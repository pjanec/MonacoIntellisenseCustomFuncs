using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ScribanLanguageServer.Core.Services;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Services;

[Trait("Stage", "B5")]
public class TimeoutServiceTests
{
    private TimeoutService CreateService(TimeoutConfiguration? config = null)
    {
        config ??= new TimeoutConfiguration();
        return new TimeoutService(config, NullLogger<TimeoutService>.Instance);
    }

    [Fact]
    public async Task CreateTimeout_CancelsAfterDuration()
    {
        // Arrange
        var service = CreateService();

        // Act
        using var cts = service.CreateTimeout(TimeSpan.FromMilliseconds(100));

        // Assert - should not be cancelled immediately
        cts.Token.IsCancellationRequested.Should().BeFalse();

        // Wait for timeout
        await Task.Delay(150);

        // Should be cancelled now
        cts.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task CreateTimeoutForOperation_UsesCorrectTimeout()
    {
        // Arrange
        var config = new TimeoutConfiguration { ParsingTimeoutSeconds = 1 };
        var service = new TimeoutService(config, NullLogger<TimeoutService>.Instance);

        // Act
        using var cts = service.CreateTimeoutForOperation("parsing");

        // Assert - should not cancel immediately
        cts.Token.IsCancellationRequested.Should().BeFalse();

        // Should cancel after timeout
        await Task.Delay(1100);
        cts.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void CreateTimeoutForOperation_UnknownOperation_UsesGlobalTimeout()
    {
        // Arrange
        var config = new TimeoutConfiguration { GlobalRequestTimeoutSeconds = 30 };
        var service = new TimeoutService(config, NullLogger<TimeoutService>.Instance);

        // Act
        using var cts = service.CreateTimeoutForOperation("unknown");

        // Assert - should not be cancelled immediately (30 second timeout)
        cts.Token.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void CreateTimeout_WithLinkedToken_RespectsBothTokens()
    {
        // Arrange
        var service = CreateService();
        using var externalCts = new CancellationTokenSource();

        // Act
        using var cts = service.CreateTimeout(TimeSpan.FromSeconds(10), externalCts.Token);

        // Assert - not cancelled yet
        cts.Token.IsCancellationRequested.Should().BeFalse();

        // Cancel external token
        externalCts.Cancel();

        // Linked token should be cancelled too
        cts.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Theory]
    [InlineData("parsing")]
    [InlineData("filesystem")]
    [InlineData("validation")]
    [InlineData("signalr")]
    public void CreateTimeoutForOperation_ValidOperationType_CreatesTimeout(string operationType)
    {
        // Arrange
        var service = CreateService();

        // Act
        using var cts = service.CreateTimeoutForOperation(operationType);

        // Assert
        cts.Should().NotBeNull();
        cts.Token.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void Constructor_NullConfig_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new TimeoutService(null!, NullLogger<TimeoutService>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new TimeoutService(new TimeoutConfiguration(), null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
