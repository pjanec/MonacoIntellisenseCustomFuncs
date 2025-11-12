using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using ScribanLanguageServer.Core.Services;

namespace ScribanLanguageServer.Core.Health;

/// <summary>
/// Health check for parser cache
/// </summary>
public class CacheHealthCheck : IHealthCheck
{
    private readonly IScribanParserService _parser;
    private readonly ILogger<CacheHealthCheck> _logger;

    public CacheHealthCheck(IScribanParserService parser, ILogger<CacheHealthCheck> logger)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = _parser.GetCacheStatistics();

            var totalRequests = stats.TotalHits + stats.TotalMisses;
            var hitRate = totalRequests > 0
                ? (double)stats.TotalHits / totalRequests * 100
                : 0;

            var data = new Dictionary<string, object>
            {
                ["cache_entries"] = stats.TotalEntries,
                ["cache_hits"] = stats.TotalHits,
                ["cache_misses"] = stats.TotalMisses,
                ["total_requests"] = totalRequests,
                ["hit_rate_percent"] = Math.Round(hitRate, 2)
            };

            // Warn if hit rate is too low
            if (totalRequests > 100 && hitRate < 50)
            {
                return Task.FromResult(
                    HealthCheckResult.Degraded(
                        $"Cache hit rate is low: {hitRate:F2}%",
                        data: data));
            }

            return Task.FromResult(
                HealthCheckResult.Healthy(
                    $"Cache: {stats.TotalEntries} entries, {hitRate:F2}% hit rate",
                    data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache health check failed");
            return Task.FromResult(
                HealthCheckResult.Unhealthy("Cache error", ex));
        }
    }
}
