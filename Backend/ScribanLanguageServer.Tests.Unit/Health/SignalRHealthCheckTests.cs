using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ScribanLanguageServer.Core.Services;
using ScribanLanguageServer.Server.Health;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Health;

[Trait("Stage", "B6")]
public class SignalRHealthCheckTests
{
    private SignalRHealthCheck CreateHealthCheck(IDocumentSessionService sessions)
    {
        return new SignalRHealthCheck(sessions, NullLogger<SignalRHealthCheck>.Instance);
    }

    [Fact]
    public async Task CheckHealthAsync_WithConnections_ReturnsHealthy()
    {
        // Arrange
        var stats = new SessionStatistics
        {
            ActiveConnections = 5,
            TotalDocuments = 15
        };

        var sessions = new Mock<IDocumentSessionService>();
        sessions.Setup(s => s.GetStatistics()).Returns(stats);

        var healthCheck = CreateHealthCheck(sessions.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("5 active connections");
        result.Description.Should().Contain("15 documents");
        result.Data.Should().ContainKey("active_connections");
        result.Data.Should().ContainKey("total_documents");
        result.Data.Should().ContainKey("documents_per_connection");
        result.Data["active_connections"].Should().Be(5);
        result.Data["total_documents"].Should().Be(15);
        result.Data["documents_per_connection"].Should().Be(3.0);
    }

    [Fact]
    public async Task CheckHealthAsync_NoConnections_ReturnsHealthy()
    {
        // Arrange
        var stats = new SessionStatistics
        {
            ActiveConnections = 0,
            TotalDocuments = 0
        };

        var sessions = new Mock<IDocumentSessionService>();
        sessions.Setup(s => s.GetStatistics()).Returns(stats);

        var healthCheck = CreateHealthCheck(sessions.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("0 active connections");
        result.Data["documents_per_connection"].Should().Be(0);
    }

    [Fact]
    public async Task CheckHealthAsync_ManyDocumentsPerConnection_ReturnsHealthy()
    {
        // Arrange
        var stats = new SessionStatistics
        {
            ActiveConnections = 2,
            TotalDocuments = 10
        };

        var sessions = new Mock<IDocumentSessionService>();
        sessions.Setup(s => s.GetStatistics()).Returns(stats);

        var healthCheck = CreateHealthCheck(sessions.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["documents_per_connection"].Should().Be(5.0);
    }

    [Fact]
    public async Task CheckHealthAsync_ServiceThrowsException_ReturnsUnhealthy()
    {
        // Arrange
        var sessions = new Mock<IDocumentSessionService>();
        sessions.Setup(s => s.GetStatistics()).Throws(new InvalidOperationException("Test error"));

        var healthCheck = CreateHealthCheck(sessions.Object);

        // Act
        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("SignalR error");
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_NullSessions_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new SignalRHealthCheck(null!, NullLogger<SignalRHealthCheck>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var sessions = new Mock<IDocumentSessionService>();

        // Act
        var act = () => new SignalRHealthCheck(sessions.Object, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
