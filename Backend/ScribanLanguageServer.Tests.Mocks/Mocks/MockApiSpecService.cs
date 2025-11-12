using ScribanLanguageServer.Core.ApiSpec;

namespace ScribanLanguageServer.Tests.Mocks;

/// <summary>
/// Mock implementation of IApiSpecService for testing purposes.
/// Allows setting up test specs in-memory without requiring file I/O.
/// </summary>
public class MockApiSpecService : IApiSpecService
{
    private ApiSpec? _currentSpec;
    private string? _lastFilePath;

    public MockApiSpecService()
    {
    }

    public MockApiSpecService(ApiSpec spec)
    {
        _currentSpec = spec ?? throw new ArgumentNullException(nameof(spec));
    }

    public ApiSpec? CurrentSpec => _currentSpec;

    public bool IsLoaded => _currentSpec != null;

    /// <summary>
    /// Sets the current spec directly (useful for testing)
    /// </summary>
    public void SetSpec(ApiSpec spec)
    {
        _currentSpec = spec ?? throw new ArgumentNullException(nameof(spec));
    }

    /// <summary>
    /// Clears the current spec
    /// </summary>
    public void Clear()
    {
        _currentSpec = null;
        _lastFilePath = null;
    }

    public Task<ApiSpecLoadResult> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Task.FromResult(ApiSpecLoadResult.FailureResult("File path cannot be empty"));
        }

        // In mock mode, we simulate successful load
        // Real implementation would read from file
        _lastFilePath = filePath;

        // Create a default test spec if none is set
        if (_currentSpec == null)
        {
            _currentSpec = CreateDefaultTestSpec();
        }

        return Task.FromResult(ApiSpecLoadResult.SuccessResult());
    }

    public Task<ApiSpecLoadResult> ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_lastFilePath))
        {
            return Task.FromResult(ApiSpecLoadResult.FailureResult("No file path to reload from"));
        }

        return LoadAsync(_lastFilePath, cancellationToken);
    }

    public GlobalEntry? GetGlobal(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || _currentSpec == null)
        {
            return null;
        }

        return _currentSpec.Globals
            .FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public FunctionEntry? GetObjectMember(string objectName, string memberName)
    {
        if (string.IsNullOrWhiteSpace(objectName) || string.IsNullOrWhiteSpace(memberName) || _currentSpec == null)
        {
            return null;
        }

        var objectEntry = _currentSpec.Globals
            .FirstOrDefault(g => g.Type == "object" &&
                               string.Equals(g.Name, objectName, StringComparison.OrdinalIgnoreCase));

        return objectEntry?.Members?
            .FirstOrDefault(m => string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> GetGlobalFunctionNames()
    {
        return _currentSpec?.Globals
            .Where(g => g.Type == "function")
            .Select(g => g.Name)
            .ToList() ?? new List<string>();
    }

    public IReadOnlyList<string> GetGlobalObjectNames()
    {
        return _currentSpec?.Globals
            .Where(g => g.Type == "object")
            .Select(g => g.Name)
            .ToList() ?? new List<string>();
    }

    /// <summary>
    /// Creates a default test API spec for testing purposes
    /// </summary>
    private static ApiSpec CreateDefaultTestSpec()
    {
        return new ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new()
                {
                    Name = "copy_file",
                    Type = "function",
                    Hover = "Copies a file from source to destination",
                    Parameters = new List<ParameterEntry>
                    {
                        new() { Name = "source", Type = "path", Picker = "file-picker" },
                        new() { Name = "destination", Type = "path", Picker = "file-picker" }
                    }
                },
                new()
                {
                    Name = "set_mode",
                    Type = "function",
                    Hover = "Sets the operational mode",
                    Parameters = new List<ParameterEntry>
                    {
                        new()
                        {
                            Name = "mode",
                            Type = "constant",
                            Picker = "enum-list",
                            Options = new List<string> { "MODE_FAST", "MODE_SLOW", "MODE_SAFE" }
                        }
                    }
                },
                new()
                {
                    Name = "os",
                    Type = "object",
                    Hover = "Operating system functions",
                    Members = new List<FunctionEntry>
                    {
                        new()
                        {
                            Name = "execute",
                            Type = "function",
                            Hover = "Executes a shell command",
                            Parameters = new List<ParameterEntry>
                            {
                                new() { Name = "command", Type = "string", Picker = "none" }
                            }
                        },
                        new()
                        {
                            Name = "getenv",
                            Type = "function",
                            Hover = "Gets an environment variable",
                            Parameters = new List<ParameterEntry>
                            {
                                new() { Name = "varname", Type = "string", Picker = "none" }
                            }
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// Creates a mock service with the default test spec pre-loaded
    /// </summary>
    public static MockApiSpecService CreateWithDefaultSpec()
    {
        var mock = new MockApiSpecService();
        mock.SetSpec(CreateDefaultTestSpec());
        return mock;
    }
}
