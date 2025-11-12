using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Scriban;
using Scriban.Parsing;
using Scriban.Syntax;
using ScribanLanguageServer.Core.ApiSpec;

namespace ScribanLanguageServer.Core.Services;

/// <summary>
/// Thread-safe service for parsing Scriban templates with AST caching
/// </summary>
public partial class ScribanParserService : IScribanParserService, IDisposable
{
    private readonly IApiSpecService _apiSpecService;
    private readonly ILogger<ScribanParserService> _logger;
    private readonly ConcurrentDictionary<string, CachedAst> _astCache = new();
    private readonly Timer _cleanupTimer;

    private long _cacheHits = 0;
    private long _cacheMisses = 0;

    private class CachedAst
    {
        public int Version { get; set; }
        public ScriptPage Ast { get; set; } = null!;
        public List<Diagnostic> SyntaxErrors { get; set; } = new();
        public DateTime LastAccess { get; set; }
        public long ParseTimeMs { get; set; }
    }

    public ScribanParserService(
        IApiSpecService apiSpecService,
        ILogger<ScribanParserService> logger)
    {
        _apiSpecService = apiSpecService ?? throw new ArgumentNullException(nameof(apiSpecService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Cleanup stale cache entries every 5 minutes
        _cleanupTimer = new Timer(
            _ => EvictStaleEntries(),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
    }

    public async Task<ScriptPage?> ParseAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        // Calculate timeout based on code length
        // Base: 500ms, +1ms per 100 chars, max 10s
        var timeoutMs = Math.Min(10000, 500 + (code.Length / 100));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        try
        {
            return await Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                var template = Template.Parse(code);
                sw.Stop();

                _logger.LogDebug(
                    "Parsed {Size} bytes in {Ms}ms",
                    code.Length, sw.ElapsedMilliseconds);

                return template.Page;
            }, cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Parse timeout for document of size {Size} bytes (timeout: {Timeout}ms)",
                code.Length, timeoutMs);
            throw new TimeoutException($"Parsing timed out after {timeoutMs}ms");
        }
    }

    public async Task<List<Diagnostic>> GetDiagnosticsAsync(
        string documentUri,
        string code,
        int version,
        CancellationToken cancellationToken = default)
    {
        // Check cache
        if (_astCache.TryGetValue(documentUri, out var cached) &&
            cached.Version == version)
        {
            Interlocked.Increment(ref _cacheHits);
            cached.LastAccess = DateTime.UtcNow;

            _logger.LogDebug(
                "Diagnostics cache hit: {Uri} v{Version}",
                documentUri, version);

            // Return cached syntax errors + run semantic validation
            var allDiagnostics = new List<Diagnostic>(cached.SyntaxErrors);

            // Only run semantic validation if no syntax errors
            if (!cached.SyntaxErrors.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                var semanticErrors = await GetSemanticErrorsAsync(
                    cached.Ast, cancellationToken);
                allDiagnostics.AddRange(semanticErrors);
            }

            return allDiagnostics;
        }

        // Cache miss - parse and cache
        Interlocked.Increment(ref _cacheMisses);

        _logger.LogDebug(
            "Diagnostics cache miss: {Uri} v{Version}",
            documentUri, version);

        return await Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            var template = Template.Parse(code);
            sw.Stop();

            var syntaxErrors = template.Messages
                .Select(ConvertToLspDiagnostic)
                .ToList();

            // Cache the result
            _astCache[documentUri] = new CachedAst
            {
                Version = version,
                Ast = template.Page,
                SyntaxErrors = syntaxErrors,
                LastAccess = DateTime.UtcNow,
                ParseTimeMs = sw.ElapsedMilliseconds
            };

            _logger.LogInformation(
                "Parsed {Uri} v{Version} in {Ms}ms (size: {Size} chars)",
                documentUri, version, sw.ElapsedMilliseconds, code.Length);

            var allDiagnostics = new List<Diagnostic>(syntaxErrors);

            // Add semantic errors if no syntax errors
            if (!syntaxErrors.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                var semanticErrors = await GetSemanticErrorsAsync(
                    template.Page, cancellationToken);
                allDiagnostics.AddRange(semanticErrors);
            }

            return allDiagnostics;
        }, cancellationToken);
    }

    // GetSemanticErrorsAsync and GetNodeAtPosition are implemented in ScribanParserService_Semantic.cs partial class

    public void InvalidateCache(string documentUri)
    {
        if (_astCache.TryRemove(documentUri, out _))
        {
            _logger.LogDebug("Cache invalidated for {Uri}", documentUri);
        }
    }

    public CacheStatistics GetCacheStatistics()
    {
        var hits = Interlocked.Read(ref _cacheHits);
        var misses = Interlocked.Read(ref _cacheMisses);
        var total = hits + misses;
        var hitRate = total > 0 ? (double)hits / total : 0;

        return new CacheStatistics(
            _astCache.Count,
            (int)hits,
            (int)misses,
            hitRate);
    }

    private void EvictStaleEntries()
    {
        var threshold = DateTime.UtcNow.AddMinutes(-10);
        var evicted = 0;

        foreach (var key in _astCache.Keys.ToArray())
        {
            if (_astCache.TryGetValue(key, out var entry) &&
                entry.LastAccess < threshold)
            {
                if (_astCache.TryRemove(key, out _))
                {
                    evicted++;
                }
            }
        }

        if (evicted > 0)
        {
            _logger.LogInformation(
                "Evicted {Count} stale AST cache entries",
                evicted);
        }
    }

    private static Diagnostic ConvertToLspDiagnostic(LogMessage message)
    {
        var severity = message.Type switch
        {
            ParserMessageType.Error => DiagnosticSeverity.Error,
            ParserMessageType.Warning => DiagnosticSeverity.Warning,
            _ => DiagnosticSeverity.Information
        };

        var span = message.Span;
        var range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range
        {
            Start = new Position(span.Start.Line - 1, span.Start.Column - 1),
            End = new Position(span.End.Line - 1, span.End.Column - 1)
        };

        return new Diagnostic
        {
            Range = range,
            Severity = severity,
            Message = message.Message,
            Source = "scriban-parser"
        };
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}
