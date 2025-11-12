using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ScribanLanguageServer.Core.Services;

/// <summary>
/// Rate limiting service using token bucket algorithm
/// </summary>
public interface IRateLimitService
{
    bool TryAcquire(string connectionId);
    void RemoveConnection(string connectionId);
    RateLimitStats GetStats(string connectionId);
}

public class RateLimitService : IRateLimitService
{
    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new();
    private readonly ILogger<RateLimitService> _logger;
    private readonly int _maxTokens;
    private readonly TimeSpan _refillInterval;

    private class TokenBucket
    {
        private int _tokens;
        private DateTime _lastRefill;
        private readonly int _maxTokens;
        private readonly TimeSpan _refillInterval;
        private readonly object _lock = new();

        public TokenBucket(int maxTokens, TimeSpan refillInterval)
        {
            _maxTokens = maxTokens;
            _tokens = maxTokens;
            _refillInterval = refillInterval;
            _lastRefill = DateTime.UtcNow;
        }

        public bool TryConsume()
        {
            lock (_lock)
            {
                Refill();

                if (_tokens > 0)
                {
                    _tokens--;
                    return true;
                }

                return false;
            }
        }

        public int AvailableTokens
        {
            get
            {
                lock (_lock)
                {
                    Refill();
                    return _tokens;
                }
            }
        }

        private void Refill()
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastRefill;

            if (elapsed >= _refillInterval)
            {
                _tokens = _maxTokens;
                _lastRefill = now;
            }
        }
    }

    public RateLimitService(ILogger<RateLimitService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxTokens = 10; // 10 requests per interval
        _refillInterval = TimeSpan.FromSeconds(1); // 1 second
    }

    public bool TryAcquire(string connectionId)
    {
        var bucket = _buckets.GetOrAdd(
            connectionId,
            _ => new TokenBucket(_maxTokens, _refillInterval));

        var acquired = bucket.TryConsume();

        if (!acquired)
        {
            _logger.LogWarning(
                "Rate limit exceeded for connection {ConnectionId}",
                connectionId);
        }

        return acquired;
    }

    public void RemoveConnection(string connectionId)
    {
        _buckets.TryRemove(connectionId, out _);
        _logger.LogDebug("Removed rate limit bucket for {ConnectionId}", connectionId);
    }

    public RateLimitStats GetStats(string connectionId)
    {
        if (_buckets.TryGetValue(connectionId, out var bucket))
        {
            return new RateLimitStats
            {
                AvailableTokens = bucket.AvailableTokens,
                MaxTokens = _maxTokens
            };
        }

        return new RateLimitStats { AvailableTokens = _maxTokens, MaxTokens = _maxTokens };
    }
}

public class RateLimitStats
{
    public int AvailableTokens { get; set; }
    public int MaxTokens { get; set; }
}
