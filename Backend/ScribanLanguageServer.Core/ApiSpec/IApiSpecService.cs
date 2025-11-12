namespace ScribanLanguageServer.Core.ApiSpec;

/// <summary>
/// Service for loading, validating, and managing the API specification
/// </summary>
public interface IApiSpecService
{
    /// <summary>
    /// Gets the currently loaded API specification
    /// </summary>
    ApiSpec? CurrentSpec { get; }

    /// <summary>
    /// Gets whether a valid spec is currently loaded
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Loads and validates the API specification from a file
    /// </summary>
    /// <param name="filePath">Path to the API spec JSON file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or validation errors</returns>
    Task<ApiSpecLoadResult> LoadAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads the API specification from the last loaded file path
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or validation errors</returns>
    Task<ApiSpecLoadResult> ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a global entry by name (case-insensitive)
    /// </summary>
    /// <param name="name">Name of the global entry</param>
    /// <returns>The global entry if found, null otherwise</returns>
    GlobalEntry? GetGlobal(string name);

    /// <summary>
    /// Gets a function member from an object global (case-insensitive)
    /// </summary>
    /// <param name="objectName">Name of the object global</param>
    /// <param name="memberName">Name of the member function</param>
    /// <returns>The function entry if found, null otherwise</returns>
    FunctionEntry? GetObjectMember(string objectName, string memberName);

    /// <summary>
    /// Gets all global function names
    /// </summary>
    /// <returns>List of function names</returns>
    IReadOnlyList<string> GetGlobalFunctionNames();

    /// <summary>
    /// Gets all global object names
    /// </summary>
    /// <returns>List of object names</returns>
    IReadOnlyList<string> GetGlobalObjectNames();
}

/// <summary>
/// Result of loading an API specification
/// </summary>
public class ApiSpecLoadResult
{
    /// <summary>
    /// Whether the load was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if load failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Validation errors from the spec validator
    /// </summary>
    public List<string> ValidationErrors { get; set; } = new();

    /// <summary>
    /// Creates a successful result
    /// </summary>
    public static ApiSpecLoadResult SuccessResult() => new() { Success = true };

    /// <summary>
    /// Creates a failed result with an error message
    /// </summary>
    public static ApiSpecLoadResult FailureResult(string errorMessage, List<string>? validationErrors = null)
    {
        return new ApiSpecLoadResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            ValidationErrors = validationErrors ?? new()
        };
    }
}
