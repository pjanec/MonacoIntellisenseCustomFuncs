using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace ScribanLanguageServer.Core.Services;

/// <summary>
/// Service for collecting and exposing metrics
/// </summary>
public interface IMetricsService
{
    void RecordRequest(string method, long durationMs, bool success);
    void RecordCacheHit(string operation);
    void RecordCacheMiss(string operation);
    void RecordError(string category, string? errorType = null);
    void RecordDocumentSize(int sizeBytes);
    IDisposable StartTimer(string operation);
    MetricsSnapshot GetSnapshot();
}

public class MetricsService : IMetricsService
{
    private readonly Meter _meter;
    private readonly Counter<long> _requestCounter;
    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;
    private readonly Counter<long> _errorCounter;
    private readonly Histogram<int> _documentSize;
    private readonly ILogger<MetricsService> _logger;

    // In-memory counters for snapshot
    private long _totalRequests;
    private long _successfulRequests;
    private long _failedRequests;
    private long _totalCacheHits;
    private long _totalCacheMisses;
    private readonly ConcurrentDictionary<string, long> _errorsByType = new();

    public MetricsService(ILogger<MetricsService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _meter = new Meter("ScribanLanguageServer", "1.0");

        _requestCounter = _meter.CreateCounter<long>(
            "requests_total",
            description: "Total number of requests");

        _requestDuration = _meter.CreateHistogram<double>(
            "request_duration_ms",
            unit: "ms",
            description: "Request duration in milliseconds");

        _cacheHits = _meter.CreateCounter<long>(
            "cache_hits_total",
            description: "Total cache hits");

        _cacheMisses = _meter.CreateCounter<long>(
            "cache_misses_total",
            description: "Total cache misses");

        _errorCounter = _meter.CreateCounter<long>(
            "errors_total",
            description: "Total errors");

        _documentSize = _meter.CreateHistogram<int>(
            "document_size_bytes",
            unit: "bytes",
            description: "Document size in bytes");
    }

    public void RecordRequest(string method, long durationMs, bool success)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("method", method),
            new("success", success)
        };

        _requestCounter.Add(1, tags);
        _requestDuration.Record(durationMs, tags);

        Interlocked.Increment(ref _totalRequests);
        if (success)
        {
            Interlocked.Increment(ref _successfulRequests);
        }
        else
        {
            Interlocked.Increment(ref _failedRequests);
        }

        _logger.LogDebug(
            "Request: {Method} completed in {Ms}ms (success: {Success})",
            method, durationMs, success);
    }

    public void RecordCacheHit(string operation)
    {
        _cacheHits.Add(1, new KeyValuePair<string, object?>("operation", operation));
        Interlocked.Increment(ref _totalCacheHits);
    }

    public void RecordCacheMiss(string operation)
    {
        _cacheMisses.Add(1, new KeyValuePair<string, object?>("operation", operation));
        Interlocked.Increment(ref _totalCacheMisses);
    }

    public void RecordError(string category, string? errorType = null)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("category", category),
            new("type", errorType ?? "unknown")
        };

        _errorCounter.Add(1, tags);

        var key = $"{category}:{errorType ?? "unknown"}";
        _errorsByType.AddOrUpdate(key, 1, (_, count) => count + 1);

        _logger.LogWarning("Error recorded: {Category} - {Type}", category, errorType);
    }

    public void RecordDocumentSize(int sizeBytes)
    {
        _documentSize.Record(sizeBytes);
    }

    public IDisposable StartTimer(string operation)
    {
        return new TimerScope(operation, this);
    }

    public MetricsSnapshot GetSnapshot()
    {
        var cacheTotal = _totalCacheHits + _totalCacheMisses;
        var cacheHitRate = cacheTotal > 0
            ? (double)_totalCacheHits / cacheTotal * 100
            : 0;

        return new MetricsSnapshot
        {
            TotalRequests = _totalRequests,
            SuccessfulRequests = _successfulRequests,
            FailedRequests = _failedRequests,
            SuccessRate = _totalRequests > 0
                ? (double)_successfulRequests / _totalRequests * 100
                : 0,
            CacheHits = _totalCacheHits,
            CacheMisses = _totalCacheMisses,
            CacheHitRate = cacheHitRate,
            ErrorsByType = _errorsByType.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };
    }

    private class TimerScope : IDisposable
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly string _operation;
        private readonly MetricsService _metrics;
        private bool _disposed;

        public TimerScope(string operation, MetricsService metrics)
        {
            _operation = operation;
            _metrics = metrics;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _sw.Stop();
            _metrics.RecordRequest(_operation, _sw.ElapsedMilliseconds, true);
        }
    }
}

public class MetricsSnapshot
{
    public long TotalRequests { get; set; }
    public long SuccessfulRequests { get; set; }
    public long FailedRequests { get; set; }
    public double SuccessRate { get; set; }
    public long CacheHits { get; set; }
    public long CacheMisses { get; set; }
    public double CacheHitRate { get; set; }
    public Dictionary<string, long> ErrorsByType { get; set; } = new();
}
