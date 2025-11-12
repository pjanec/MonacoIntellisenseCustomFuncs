namespace ScribanLanguageServer.Core.Services;

/// <summary>
/// Service for safe file system operations with throttling and security
/// </summary>
public interface IFileSystemService
{
    /// <summary>
    /// Gets path suggestions for file picker autocomplete
    /// </summary>
    /// <param name="basePath">Base directory path</param>
    /// <param name="filter">Optional filter pattern (e.g., "*.txt")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of relative path suggestions</returns>
    Task<List<string>> GetPathSuggestionsAsync(
        string basePath,
        string? filter = null,
        CancellationToken cancellationToken = default);
}
