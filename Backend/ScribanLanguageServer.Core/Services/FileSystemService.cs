using Microsoft.Extensions.Logging;
using ScribanLanguageServer.Core.Validation;

namespace ScribanLanguageServer.Core.Services;

/// <summary>
/// Thread-safe file system service with throttling, timeout protection, and security measures
/// </summary>
public class FileSystemService : IFileSystemService
{
    private readonly ILogger<FileSystemService> _logger;
    private readonly SemaphoreSlim _throttle = new(5); // Max 5 concurrent operations
    private const int MaxItems = 10000;
    private const int TimeoutSeconds = 5;

    public FileSystemService(ILogger<FileSystemService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<string>> GetPathSuggestionsAsync(
        string basePath,
        string? filter = null,
        CancellationToken cancellationToken = default)
    {
        // Sanitize path to prevent directory traversal attacks
        basePath = InputValidator.SanitizePath(basePath);

        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Directory.GetCurrentDirectory();
        }

        // Validate path exists
        if (!Directory.Exists(basePath))
        {
            _logger.LogWarning("Directory not found: {Path}", basePath);
            return new List<string>();
        }

        // Check for dangerous paths - only allow access to specific roots
        var fullPath = Path.GetFullPath(basePath);
        var allowedRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Directory.GetCurrentDirectory()
        };

        if (!allowedRoots.Any(root => fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("Access denied to path outside allowed roots: {Path}", fullPath);
            return new List<string>();
        }

        await _throttle.WaitAsync(cancellationToken);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));

            return await Task.Run(() => GetPathsInternal(fullPath, filter, cts.Token), cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "GetPathSuggestions timed out after {Timeout}s for path: {Path}",
                TimeoutSeconds, basePath);
            throw new TimeoutException($"File system operation timed out after {TimeoutSeconds}s");
        }
        finally
        {
            _throttle.Release();
        }
    }

    private List<string> GetPathsInternal(
        string basePath,
        string? filter,
        CancellationToken cancellationToken)
    {
        var results = new List<string>();

        if (!Directory.Exists(basePath))
        {
            _logger.LogWarning("Directory not found: {Path}", basePath);
            return results;
        }

        try
        {
            var searchPattern = string.IsNullOrWhiteSpace(filter) ? "*" : filter;
            var entries = Directory.EnumerateFileSystemEntries(
                basePath,
                searchPattern,
                SearchOption.TopDirectoryOnly);

            int count = 0;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (++count > MaxItems)
                {
                    _logger.LogWarning(
                        "Path suggestions exceeded max items ({Max}) for: {Path}",
                        MaxItems, basePath);
                    break;
                }

                // Return relative paths
                var relativePath = Path.GetRelativePath(basePath, entry);
                results.Add(relativePath);
            }

            _logger.LogDebug(
                "Found {Count} path suggestions in {Path}",
                results.Count, basePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied to path: {Path}", basePath);
            // Return partial results
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "Directory not found: {Path}", basePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error enumerating path: {Path}", basePath);
            // Return partial results
        }

        return results;
    }

}
