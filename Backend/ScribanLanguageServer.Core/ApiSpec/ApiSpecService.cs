using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ScribanLanguageServer.Core.ApiSpec;

/// <summary>
/// Service for loading, validating, and managing the API specification
/// </summary>
public class ApiSpecService : IApiSpecService
{
    private readonly ILogger<ApiSpecService> _logger;
    private readonly object _lock = new();
    private ApiSpec? _currentSpec;
    private string? _lastFilePath;

    public ApiSpecService(ILogger<ApiSpecService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ApiSpec? CurrentSpec
    {
        get
        {
            lock (_lock)
            {
                return _currentSpec;
            }
        }
    }

    public bool IsLoaded
    {
        get
        {
            lock (_lock)
            {
                return _currentSpec != null;
            }
        }
    }

    public async Task<ApiSpecLoadResult> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return ApiSpecLoadResult.FailureResult("File path cannot be empty");
        }

        if (!File.Exists(filePath))
        {
            return ApiSpecLoadResult.FailureResult($"File not found: {filePath}");
        }

        try
        {
            _logger.LogInformation("Loading API spec from: {FilePath}", filePath);

            // Read file content
            string jsonContent = await File.ReadAllTextAsync(filePath, cancellationToken);

            // Parse JSON
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            var spec = JsonSerializer.Deserialize<ApiSpec>(jsonContent, options);

            if (spec == null)
            {
                return ApiSpecLoadResult.FailureResult("Failed to parse JSON: result was null");
            }

            // Validate the spec
            var validationResult = ApiSpecValidator.Validate(spec);
            if (!validationResult.IsValid)
            {
                _logger.LogError("API spec validation failed with {ErrorCount} errors", validationResult.Errors.Count);
                foreach (var error in validationResult.Errors)
                {
                    _logger.LogError("  - {Error}", error);
                }
                return ApiSpecLoadResult.FailureResult("Validation failed", validationResult.Errors);
            }

            // Log warnings if any
            if (validationResult.Warnings.Any())
            {
                _logger.LogWarning("API spec loaded with {WarningCount} warnings", validationResult.Warnings.Count);
                foreach (var warning in validationResult.Warnings)
                {
                    _logger.LogWarning("  - {Warning}", warning);
                }
            }

            // Store the spec
            lock (_lock)
            {
                _currentSpec = spec;
                _lastFilePath = filePath;
            }

            _logger.LogInformation("Successfully loaded API spec with {Count} globals", spec.Globals.Count);
            return ApiSpecLoadResult.SuccessResult();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parsing error while loading API spec");
            return ApiSpecLoadResult.FailureResult($"JSON parsing error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading API spec from {FilePath}", filePath);
            return ApiSpecLoadResult.FailureResult($"Error loading file: {ex.Message}");
        }
    }

    public async Task<ApiSpecLoadResult> ReloadAsync(CancellationToken cancellationToken = default)
    {
        string? filePath;
        lock (_lock)
        {
            filePath = _lastFilePath;
        }

        if (string.IsNullOrEmpty(filePath))
        {
            return ApiSpecLoadResult.FailureResult("No file path to reload from. Load a file first.");
        }

        _logger.LogInformation("Reloading API spec from: {FilePath}", filePath);
        return await LoadAsync(filePath, cancellationToken);
    }

    public GlobalEntry? GetGlobal(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        lock (_lock)
        {
            return _currentSpec?.Globals
                .FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public FunctionEntry? GetObjectMember(string objectName, string memberName)
    {
        if (string.IsNullOrWhiteSpace(objectName) || string.IsNullOrWhiteSpace(memberName))
        {
            return null;
        }

        lock (_lock)
        {
            var objectEntry = _currentSpec?.Globals
                .FirstOrDefault(g => g.Type == "object" &&
                                   string.Equals(g.Name, objectName, StringComparison.OrdinalIgnoreCase));

            return objectEntry?.Members?
                .FirstOrDefault(m => string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<string> GetGlobalFunctionNames()
    {
        lock (_lock)
        {
            return _currentSpec?.Globals
                .Where(g => g.Type == "function")
                .Select(g => g.Name)
                .ToList() ?? new List<string>();
        }
    }

    public IReadOnlyList<string> GetGlobalObjectNames()
    {
        lock (_lock)
        {
            return _currentSpec?.Globals
                .Where(g => g.Type == "object")
                .Select(g => g.Name)
                .ToList() ?? new List<string>();
        }
    }
}
