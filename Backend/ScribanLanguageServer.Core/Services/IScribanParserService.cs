using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Scriban.Syntax;

namespace ScribanLanguageServer.Core.Services;

/// <summary>
/// Service for parsing Scriban templates and managing AST cache
/// </summary>
public interface IScribanParserService
{
    /// <summary>
    /// Parses Scriban code and returns the AST
    /// </summary>
    /// <param name="code">The Scriban template code</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The parsed AST or null if parsing fails</returns>
    Task<ScriptPage?> ParseAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets diagnostics (syntax + semantic errors) for a document
    /// </summary>
    /// <param name="documentUri">The document URI</param>
    /// <param name="code">The document code</param>
    /// <param name="version">The document version for caching</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of diagnostics</returns>
    Task<List<Diagnostic>> GetDiagnosticsAsync(string documentUri, string code, int version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the AST node at a specific position
    /// </summary>
    /// <param name="ast">The AST to search</param>
    /// <param name="position">The LSP position</param>
    /// <returns>The node at the position or null</returns>
    ScriptNode? GetNodeAtPosition(ScriptPage ast, Position position);

    /// <summary>
    /// Invalidates the cached AST for a document
    /// </summary>
    /// <param name="documentUri">The document URI</param>
    void InvalidateCache(string documentUri);

    /// <summary>
    /// Gets cache statistics for monitoring
    /// </summary>
    /// <returns>Cache statistics</returns>
    CacheStatistics GetCacheStatistics();
}

/// <summary>
/// Cache statistics for monitoring parser performance
/// </summary>
/// <param name="TotalEntries">Total number of cached ASTs</param>
/// <param name="TotalHits">Total cache hits</param>
/// <param name="TotalMisses">Total cache misses</param>
/// <param name="HitRate">Cache hit rate (0-1)</param>
public record CacheStatistics(
    int TotalEntries,
    int TotalHits,
    int TotalMisses,
    double HitRate);
