using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using ScribanLanguageServer.Core.Services;

namespace ScribanLanguageServer.Server.Health;

/// <summary>
/// Health check for SignalR hub connections
/// </summary>
public class SignalRHealthCheck : IHealthCheck
{
    private readonly IDocumentSessionService _sessions;
    private readonly ILogger<SignalRHealthCheck> _logger;

    public SignalRHealthCheck(
        IDocumentSessionService sessions,
        ILogger<SignalRHealthCheck> logger)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = _sessions.GetStatistics();

            var data = new Dictionary<string, object>
            {
                ["active_connections"] = stats.ActiveConnections,
                ["total_documents"] = stats.TotalDocuments,
                ["documents_per_connection"] = stats.ActiveConnections > 0
                    ? Math.Round((double)stats.TotalDocuments / stats.ActiveConnections, 2)
                    : 0
            };

            return Task.FromResult(
                HealthCheckResult.Healthy(
                    $"{stats.ActiveConnections} active connections, {stats.TotalDocuments} documents",
                    data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR health check failed");
            return Task.FromResult(
                HealthCheckResult.Unhealthy("SignalR error", ex));
        }
    }
}
