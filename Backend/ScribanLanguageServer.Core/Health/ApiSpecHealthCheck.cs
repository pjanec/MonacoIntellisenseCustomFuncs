using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using ScribanLanguageServer.Core.ApiSpec;

namespace ScribanLanguageServer.Core.Health;

/// <summary>
/// Health check for ApiSpec service
/// </summary>
public class ApiSpecHealthCheck : IHealthCheck
{
    private readonly IApiSpecService _apiSpec;
    private readonly ILogger<ApiSpecHealthCheck> _logger;

    public ApiSpecHealthCheck(IApiSpecService apiSpec, ILogger<ApiSpecHealthCheck> logger)
    {
        _apiSpec = apiSpec ?? throw new ArgumentNullException(nameof(apiSpec));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var spec = _apiSpec.CurrentSpec;

            if (spec == null || spec.Globals == null || !spec.Globals.Any())
            {
                return Task.FromResult(
                    HealthCheckResult.Unhealthy("No globals loaded from ApiSpec"));
            }

            var functionCount = spec.Globals.Count(g => g.Type == "function");
            var objectCount = spec.Globals.Count(g => g.Type == "object");

            var data = new Dictionary<string, object>
            {
                ["total_globals"] = spec.Globals.Count,
                ["functions"] = functionCount,
                ["objects"] = objectCount
            };

            return Task.FromResult(
                HealthCheckResult.Healthy(
                    $"{spec.Globals.Count} globals loaded ({functionCount} functions, {objectCount} objects)",
                    data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApiSpec health check failed");
            return Task.FromResult(
                HealthCheckResult.Unhealthy("ApiSpec error", ex));
        }
    }
}
