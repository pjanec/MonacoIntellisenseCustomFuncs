# Implementation Plan - Part 3: Production Readiness

**Document Version:** 1.0
**Date:** 2025-11-11
**Status:** Ready for Implementation
**Dependencies:** Requires completion of Stages B1, B2, B3

---

## Overview

This document extends the implementation plan with critical production-readiness features identified in the design evaluation. These stages focus on:
- Enhanced validation and error handling
- Performance optimizations
- Security hardening
- Monitoring and observability
- Testing infrastructure

---

## STAGE B1.5: ApiSpec Validation Enhancement (Retrofit)

**Duration:** 1 week
**Dependencies:** B1 complete
**Priority:** P0 - Critical

### Objectives
1. Add comprehensive validation for ApiSpec.json
2. Prevent server crashes from malformed metadata
3. Provide clear error messages on startup
4. Support JSON schema validation

### Task B1.5.1: ApiSpec Validator Implementation (Day 1-2)

**File:** `Core/ApiSpec/ApiSpecValidator.cs`

```csharp
using ScribanLanguageServer.Core.ApiSpec;

namespace ScribanLanguageServer.Core.ApiSpec;

/// <summary>
/// Validates ApiSpec.json for correctness and consistency
/// </summary>
public static class ApiSpecValidator
{
    public static ValidationResult Validate(ApiSpec spec)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (spec?.Globals == null)
        {
            errors.Add("ApiSpec must have a 'globals' array");
            return new ValidationResult { IsValid = false, Errors = errors };
        }

        // Check for duplicate global names
        var duplicateGlobals = spec.Globals
            .GroupBy(g => g.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateGlobals.Any())
        {
            errors.Add($"Duplicate global names: {string.Join(", ", duplicateGlobals)}");
        }

        // Validate each global entry
        foreach (var global in spec.Globals)
        {
            ValidateGlobalEntry(global, errors, warnings);
        }

        // Check for reserved names
        var reservedNames = new[] { "for", "if", "end", "else", "while", "func", "ret" };
        var conflicts = spec.Globals
            .Where(g => reservedNames.Contains(g.Name, StringComparer.OrdinalIgnoreCase))
            .Select(g => g.Name)
            .ToList();

        if (conflicts.Any())
        {
            errors.Add($"Reserved Scriban keywords used as global names: {string.Join(", ", conflicts)}");
        }

        return new ValidationResult
        {
            IsValid = !errors.Any(),
            Errors = errors,
            Warnings = warnings
        };
    }

    private static void ValidateGlobalEntry(
        GlobalEntry entry,
        List<string> errors,
        List<string> warnings)
    {
        var context = $"Global '{entry.Name}'";

        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            errors.Add($"{context}: Name cannot be empty");
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.Type))
        {
            errors.Add($"{context}: Type is required");
        }
        else if (entry.Type != "object" && entry.Type != "function")
        {
            errors.Add($"{context}: Type must be 'object' or 'function', got '{entry.Type}'");
        }

        if (string.IsNullOrWhiteSpace(entry.Hover))
        {
            warnings.Add($"{context}: Hover documentation is empty");
        }

        // Type-specific validation
        if (entry.Type == "object")
        {
            ValidateObjectEntry(entry, context, errors, warnings);
        }
        else if (entry.Type == "function")
        {
            ValidateFunctionEntry(entry, context, errors, warnings);
        }
    }

    private static void ValidateObjectEntry(
        GlobalEntry entry,
        string context,
        List<string> errors,
        List<string> warnings)
    {
        if (entry.Members == null || !entry.Members.Any())
        {
            errors.Add($"{context}: Objects must have at least one member");
            return;
        }

        // Check for duplicate member names
        var duplicateMembers = entry.Members
            .GroupBy(m => m.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateMembers.Any())
        {
            errors.Add($"{context}: Duplicate member names: {string.Join(", ", duplicateMembers)}");
        }

        // Validate each member
        foreach (var member in entry.Members)
        {
            ValidateFunctionEntry(member, $"{context}.{member.Name}", errors, warnings);
        }
    }

    private static void ValidateFunctionEntry(
        dynamic entry,
        string context,
        List<string> errors,
        List<string> warnings)
    {
        if (entry.Parameters == null)
        {
            errors.Add($"{context}: Parameters array is required (use empty array if no parameters)");
            return;
        }

        // Check for duplicate parameter names
        var duplicateParams = entry.Parameters
            .GroupBy(p => p.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateParams.Any())
        {
            errors.Add($"{context}: Duplicate parameter names: {string.Join(", ", duplicateParams)}");
        }

        // Validate each parameter
        for (int i = 0; i < entry.Parameters.Count; i++)
        {
            var param = entry.Parameters[i];
            ValidateParameter(param, $"{context}.param[{i}]({param.Name})", errors, warnings);
        }
    }

    private static void ValidateParameter(
        ParameterEntry param,
        string context,
        List<string> errors,
        List<string> warnings)
    {
        // Validate type
        var validTypes = new[] { "path", "constant", "string", "number", "boolean", "any" };
        if (!validTypes.Contains(param.Type))
        {
            errors.Add($"{context}: Invalid type '{param.Type}'. Must be one of: {string.Join(", ", validTypes)}");
        }

        // Validate picker
        var validPickers = new[] { "file-picker", "enum-list", "none" };
        if (!validPickers.Contains(param.Picker))
        {
            errors.Add($"{context}: Invalid picker '{param.Picker}'. Must be one of: {string.Join(", ", validPickers)}");
        }

        // Picker-specific validation
        if (param.Picker == "enum-list")
        {
            if (param.Options == null || !param.Options.Any())
            {
                errors.Add($"{context}: Picker 'enum-list' requires non-empty 'options' array");
            }

            if (param.Type != "constant")
            {
                warnings.Add($"{context}: Picker 'enum-list' typically uses type 'constant', found '{param.Type}'");
            }
        }

        if (param.Picker == "file-picker")
        {
            if (param.Type != "path")
            {
                warnings.Add($"{context}: Picker 'file-picker' typically uses type 'path', found '{param.Type}'");
            }
        }

        // Validate macros
        if (param.Macros != null && param.Macros.Any())
        {
            if (param.Type != "string")
            {
                errors.Add($"{context}: Macros are only valid for type 'string', found '{param.Type}'");
            }
        }

        // Warn about unused options
        if (param.Options != null && param.Options.Any() && param.Picker != "enum-list")
        {
            warnings.Add($"{context}: Options defined but picker is '{param.Picker}' (not 'enum-list')");
        }
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
```

**Tests:** `Tests.Unit/ApiSpec/ApiSpecValidatorTests.cs`

```csharp
using FluentAssertions;
using ScribanLanguageServer.Core.ApiSpec;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.ApiSpec;

[Trait("Stage", "B1.5")]
public class ApiSpecValidatorTests
{
    [Fact]
    public void Validate_ValidSpec_ReturnsSuccess()
    {
        // Arrange
        var spec = CreateValidSpec();

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_DuplicateGlobalNames_ReturnsError()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new() { Name = "duplicate", Type = "function", Hover = "test", Parameters = new() },
                new() { Name = "duplicate", Type = "object", Hover = "test", Members = new() }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Duplicate global names: duplicate"));
    }

    [Fact]
    public void Validate_ReservedKeywordUsed_ReturnsError()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new() { Name = "for", Type = "function", Hover = "test", Parameters = new() }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Reserved Scriban keywords"));
    }

    [Fact]
    public void Validate_EnumListWithoutOptions_ReturnsError()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new()
                {
                    Name = "test_func",
                    Type = "function",
                    Hover = "test",
                    Parameters = new List<ParameterEntry>
                    {
                        new()
                        {
                            Name = "mode",
                            Type = "constant",
                            Picker = "enum-list"
                            // Missing Options!
                        }
                    }
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("enum-list' requires non-empty 'options' array"));
    }

    [Fact]
    public void Validate_MacrosOnNonStringParameter_ReturnsError()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new()
                {
                    Name = "test_func",
                    Type = "function",
                    Hover = "test",
                    Parameters = new List<ParameterEntry>
                    {
                        new()
                        {
                            Name = "value",
                            Type = "number",
                            Picker = "none",
                            Macros = new List<string> { "TIMESTAMP" }
                        }
                    }
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Macros are only valid for type 'string'"));
    }

    [Fact]
    public void Validate_ObjectWithoutMembers_ReturnsError()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new()
                {
                    Name = "empty_obj",
                    Type = "object",
                    Hover = "test",
                    Members = new List<FunctionEntry>() // Empty!
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Objects must have at least one member"));
    }

    [Fact]
    public void Validate_DuplicateMemberNames_ReturnsError()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new()
                {
                    Name = "obj",
                    Type = "object",
                    Hover = "test",
                    Members = new List<FunctionEntry>
                    {
                        new() { Name = "method", Type = "function", Hover = "test", Parameters = new() },
                        new() { Name = "method", Type = "function", Hover = "test", Parameters = new() }
                    }
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Duplicate member names: method"));
    }

    [Fact]
    public void Validate_InvalidParameterType_ReturnsError()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new()
                {
                    Name = "test_func",
                    Type = "function",
                    Hover = "test",
                    Parameters = new List<ParameterEntry>
                    {
                        new()
                        {
                            Name = "value",
                            Type = "invalid_type",
                            Picker = "none"
                        }
                    }
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid type 'invalid_type'"));
    }

    [Fact]
    public void Validate_InvalidPickerType_ReturnsError()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new()
                {
                    Name = "test_func",
                    Type = "function",
                    Hover = "test",
                    Parameters = new List<ParameterEntry>
                    {
                        new()
                        {
                            Name = "value",
                            Type = "string",
                            Picker = "invalid-picker"
                        }
                    }
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid picker 'invalid-picker'"));
    }

    [Fact]
    public void Validate_EmptyHover_ReturnsWarning()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new()
                {
                    Name = "test_func",
                    Type = "function",
                    Hover = "", // Empty!
                    Parameters = new()
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeTrue(); // Still valid, just a warning
        result.Warnings.Should().Contain(w => w.Contains("Hover documentation is empty"));
    }

    [Fact]
    public void Validate_FilePickerWithNonPathType_ReturnsWarning()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new()
                {
                    Name = "test_func",
                    Type = "function",
                    Hover = "test",
                    Parameters = new List<ParameterEntry>
                    {
                        new()
                        {
                            Name = "file",
                            Type = "string", // Should be 'path'
                            Picker = "file-picker"
                        }
                    }
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Contains("typically uses type 'path'"));
    }

    private Core.ApiSpec.ApiSpec CreateValidSpec()
    {
        return new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new()
                {
                    Name = "copy_file",
                    Type = "function",
                    Hover = "Copies a file",
                    Parameters = new List<ParameterEntry>
                    {
                        new() { Name = "source", Type = "path", Picker = "file-picker" },
                        new() { Name = "dest", Type = "path", Picker = "file-picker" }
                    }
                },
                new()
                {
                    Name = "set_mode",
                    Type = "function",
                    Hover = "Sets mode",
                    Parameters = new List<ParameterEntry>
                    {
                        new()
                        {
                            Name = "mode",
                            Type = "constant",
                            Picker = "enum-list",
                            Options = new List<string> { "FAST", "SLOW" }
                        }
                    }
                }
            }
        };
    }
}
```

---

### Task B1.5.2: Update ApiSpecService with Validation (Day 2-3)

**Update:** `Core/ApiSpec/ApiSpecService.cs`

```csharp
public class ApiSpecService : IApiSpecService
{
    private readonly ILogger<ApiSpecService> _logger;
    private readonly Core.ApiSpec.ApiSpec _currentSpec;

    public ApiSpecService(
        IConfiguration configuration,
        ILogger<ApiSpecService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var apiSpecPath = configuration["ApiSpec:Path"] ?? "ApiSpec.json";

        _logger.LogInformation("Loading ApiSpec from: {Path}", apiSpecPath);

        // 1. Check file exists
        if (!File.Exists(apiSpecPath))
        {
            var errorMsg = $"ApiSpec.json not found at path: {apiSpecPath}. " +
                          $"Current directory: {Directory.GetCurrentDirectory()}";
            _logger.LogCritical(errorMsg);
            throw new FileNotFoundException(errorMsg);
        }

        try
        {
            // 2. Read and deserialize
            var json = File.ReadAllText(apiSpecPath);
            _logger.LogDebug("ApiSpec file size: {Size} bytes", json.Length);

            var spec = JsonConvert.DeserializeObject<Core.ApiSpec.ApiSpec>(json);

            if (spec == null)
            {
                throw new InvalidOperationException("Failed to deserialize ApiSpec.json - result was null");
            }

            // 3. Run validation
            _logger.LogInformation("Validating ApiSpec...");
            var validationResult = ApiSpecValidator.Validate(spec);

            // Log warnings
            foreach (var warning in validationResult.Warnings)
            {
                _logger.LogWarning("ApiSpec validation warning: {Warning}", warning);
            }

            // Fail on errors
            if (!validationResult.IsValid)
            {
                var errorMessage = "ApiSpec.json validation failed:\n" +
                    string.Join("\n", validationResult.Errors.Select(e => $"  - {e}"));

                _logger.LogCritical(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            _currentSpec = spec;

            // 4. Log successful load
            var functionCount = _currentSpec.Globals.Count(g => g.Type == "function");
            var objectCount = _currentSpec.Globals.Count(g => g.Type == "object");

            _logger.LogInformation(
                "ApiSpec loaded successfully: {GlobalCount} globals ({FunctionCount} functions, {ObjectCount} objects)",
                _currentSpec.Globals.Count,
                functionCount,
                objectCount);

            // Log each global for debugging
            foreach (var global in _currentSpec.Globals)
            {
                if (global.Type == "function")
                {
                    _logger.LogDebug(
                        "  Function: {Name} ({ParamCount} parameters)",
                        global.Name,
                        global.Parameters?.Count ?? 0);
                }
                else if (global.Type == "object")
                {
                    _logger.LogDebug(
                        "  Object: {Name} ({MemberCount} members)",
                        global.Name,
                        global.Members?.Count ?? 0);
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogCritical(ex, "Failed to parse ApiSpec.json - invalid JSON syntax");
            throw new InvalidOperationException(
                "ApiSpec.json contains invalid JSON. Please check the file format.", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException && ex is not FileNotFoundException)
        {
            _logger.LogCritical(ex, "Unexpected error loading ApiSpec.json");
            throw new InvalidOperationException(
                "Failed to load ApiSpec.json due to unexpected error.", ex);
        }
    }

    // ... rest of existing methods ...
}
```

**Tests:** `Tests.Unit/ApiSpec/ApiSpecServiceTests.cs` (add these tests)

```csharp
[Fact]
public void Constructor_MissingFile_ThrowsFileNotFoundException()
{
    // Arrange
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string>
        {
            ["ApiSpec:Path"] = "nonexistent.json"
        })
        .Build();

    // Act & Assert
    var act = () => new ApiSpecService(config, NullLogger<ApiSpecService>.Instance);

    act.Should().Throw<FileNotFoundException>()
        .WithMessage("*not found at path*");
}

[Fact]
public void Constructor_InvalidJson_ThrowsInvalidOperationException()
{
    // Arrange
    var tempFile = Path.GetTempFileName();
    File.WriteAllText(tempFile, "{ invalid json }");

    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string>
        {
            ["ApiSpec:Path"] = tempFile
        })
        .Build();

    try
    {
        // Act & Assert
        var act = () => new ApiSpecService(config, NullLogger<ApiSpecService>.Instance);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*invalid JSON*");
    }
    finally
    {
        File.Delete(tempFile);
    }
}

[Fact]
public void Constructor_ValidationErrors_ThrowsInvalidOperationException()
{
    // Arrange
    var tempFile = Path.GetTempFileName();
    var invalidSpec = new
    {
        globals = new[]
        {
            new
            {
                name = "duplicate",
                type = "function",
                hover = "test",
                parameters = new object[] { }
            },
            new
            {
                name = "duplicate", // Duplicate!
                type = "function",
                hover = "test",
                parameters = new object[] { }
            }
        }
    };

    File.WriteAllText(tempFile, JsonConvert.SerializeObject(invalidSpec));

    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string>
        {
            ["ApiSpec:Path"] = tempFile
        })
        .Build();

    try
    {
        // Act & Assert
        var act = () => new ApiSpecService(config, NullLogger<ApiSpecService>.Instance);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*validation failed*");
    }
    finally
    {
        File.Delete(tempFile);
    }
}
```

---

### Stage B1.5: Acceptance Criteria

**Run tests:**
```bash
dotnet test --filter "Stage=B1.5"
```

**Expected:**
```
Total tests: 12
     Passed: 12
```

**Deliverables:**
- ✅ Comprehensive ApiSpec validation
- ✅ Clear error messages on startup
- ✅ Warnings for best practices
- ✅ 100% test coverage for validator

---

## STAGE B4: SignalR Hub & Custom Communication

**Duration:** 1 week
**Dependencies:** B1, B2, B3 complete
**Priority:** P1 - High

### Objectives
1. Implement ScribanHub with custom methods
2. Add document session management to Hub
3. Implement CheckTrigger workflow
4. Add GetPathSuggestions data endpoint
5. Add proper error handling and logging

### Task B4.1: ScribanHub Core Implementation (Day 1-2)

**File:** `Server/Hubs/ScribanHub.cs`

```csharp
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ScribanLanguageServer.Core.Services;

namespace ScribanLanguageServer.Server.Hubs;

/// <summary>
/// SignalR Hub for custom Scriban language server communication
/// </summary>
public class ScribanHub : Hub<IScribanClient>
{
    private readonly IDocumentSessionService _sessionService;
    private readonly IScribanParserService _parserService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IApiSpecService _apiSpecService;
    private readonly ILogger<ScribanHub> _logger;

    public ScribanHub(
        IDocumentSessionService sessionService,
        IScribanParserService parserService,
        IFileSystemService fileSystemService,
        IApiSpecService apiSpecService,
        ILogger<ScribanHub> logger)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _parserService = parserService ?? throw new ArgumentNullException(nameof(parserService));
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
        _apiSpecService = apiSpecService ?? throw new ArgumentNullException(nameof(apiSpecService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Called when client connects
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when client disconnects
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation(
            "Client disconnected: {ConnectionId}, reason: {Reason}",
            Context.ConnectionId,
            exception?.Message ?? "normal");

        // Cleanup all documents for this connection
        _sessionService.CleanupConnection(Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Checks if a trigger character should open a custom picker
    /// </summary>
    public async Task CheckTrigger(TriggerContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        _logger.LogDebug(
            "CheckTrigger: event={Event}, char={Char}, position={Position}",
            context.Event,
            context.Char,
            context.Position);

        try
        {
            // Validate document access
            if (!_sessionService.ValidateAccess(Context.ConnectionId, context.DocumentUri))
            {
                _logger.LogWarning(
                    "CheckTrigger rejected: Connection {ConnectionId} doesn't own {Uri}",
                    Context.ConnectionId,
                    context.DocumentUri);
                throw new UnauthorizedAccessException("Access denied to document");
            }

            // Get parameter context at cursor position
            // Note: In full implementation, we'd need access to TextDocumentStore
            // For now, this is a simplified version

            var paramContext = await _parserService.GetParameterContextAsync(
                context.DocumentUri,
                context.Code,
                context.Position,
                CancellationToken.None);

            if (paramContext == null)
            {
                // Not in a function call context
                _logger.LogDebug("CheckTrigger: No parameter context found");
                return;
            }

            // Check picker type from API spec
            if (paramContext.ParameterSpec?.Picker == "file-picker")
            {
                // Send OpenPicker command to client
                var data = new OpenPickerData
                {
                    PickerType = "file-picker",
                    FunctionName = paramContext.FunctionName,
                    ParameterIndex = paramContext.ParameterIndex,
                    CurrentValue = paramContext.CurrentValue
                };

                await Clients.Caller.OpenPicker(data);

                _logger.LogInformation(
                    "Sent OpenPicker command: function={Function}, param={Index}",
                    paramContext.FunctionName,
                    paramContext.ParameterIndex);
            }
            else if (paramContext.ParameterSpec?.Picker == "enum-list")
            {
                // Do nothing - standard LSP completion handles this
                _logger.LogDebug("CheckTrigger: enum-list param, handled by LSP completion");
            }
        }
        catch (UnauthorizedAccessException)
        {
            throw; // Re-throw to client
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CheckTrigger failed for {Uri}", context.DocumentUri);
            throw new HubException("Failed to process trigger");
        }
    }

    /// <summary>
    /// Gets path suggestions for file picker
    /// </summary>
    public async Task<List<string>> GetPathSuggestions(
        string functionName,
        int parameterIndex,
        string? basePath = null)
    {
        _logger.LogDebug(
            "GetPathSuggestions: function={Function}, param={Index}, base={Base}",
            functionName,
            parameterIndex,
            basePath);

        try
        {
            // Use a timeout for file system operations
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var suggestions = await _fileSystemService.GetPathSuggestionsAsync(
                basePath ?? Directory.GetCurrentDirectory(),
                filter: null,
                cancellationToken: cts.Token);

            _logger.LogInformation(
                "Returning {Count} path suggestions for {Function}",
                suggestions.Count,
                functionName);

            return suggestions;
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "GetPathSuggestions timed out");
            throw new HubException("File system operation timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetPathSuggestions failed");
            throw new HubException("Failed to get path suggestions");
        }
    }
}

/// <summary>
/// Client interface for strongly-typed hub calls
/// </summary>
public interface IScribanClient
{
    Task OpenPicker(OpenPickerData data);
}

/// <summary>
/// Context sent from client on trigger character
/// </summary>
public class TriggerContext
{
    public string Event { get; set; } = string.Empty; // "char" or "hotkey"
    public string? Char { get; set; } // "(", ",", or null
    public string DocumentUri { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty; // Full document text
    public Position Position { get; set; } = null!;
}

/// <summary>
/// Data sent to client to open picker
/// </summary>
public class OpenPickerData
{
    public string PickerType { get; set; } = string.Empty; // "file-picker"
    public string FunctionName { get; set; } = string.Empty;
    public int ParameterIndex { get; set; }
    public string? CurrentValue { get; set; }
}
```

**Update:** Add GetParameterContextAsync to ScribanParserService (if not already present):

```csharp
// In ScribanParserService.cs
public async Task<ParameterContext?> GetParameterContextAsync(
    string documentUri,
    string code,
    Position position,
    CancellationToken cancellationToken)
{
    return await Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Parse code
        var ast = ParseAsync(code, cancellationToken).Result;
        if (ast == null) return null;

        // Find node at position
        var node = GetNodeAtPosition(ast, position);
        if (node == null) return null;

        // Get parameter context
        return BuildParameterContext(node, position);

    }, cancellationToken);
}
```

---

### Task B4.2: Hub Integration Tests (Day 3)

**File:** `Tests.Unit/Hubs/ScribanHubTests.cs`

```csharp
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ScribanLanguageServer.Core.Services;
using ScribanLanguageServer.Server.Hubs;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Hubs;

[Trait("Stage", "B4")]
public class ScribanHubTests
{
    private readonly Mock<IDocumentSessionService> _sessionService;
    private readonly Mock<IScribanParserService> _parserService;
    private readonly Mock<IFileSystemService> _fileSystemService;
    private readonly Mock<IApiSpecService> _apiSpecService;
    private readonly Mock<IHubCallerClients<IScribanClient>> _clients;
    private readonly Mock<IScribanClient> _caller;
    private readonly Mock<HubCallerContext> _context;
    private readonly ScribanHub _hub;

    public ScribanHubTests()
    {
        _sessionService = new Mock<IDocumentSessionService>();
        _parserService = new Mock<IScribanParserService>();
        _fileSystemService = new Mock<IFileSystemService>();
        _apiSpecService = new Mock<IApiSpecService>();
        _clients = new Mock<IHubCallerClients<IScribanClient>>();
        _caller = new Mock<IScribanClient>();
        _context = new Mock<HubCallerContext>();

        _hub = new ScribanHub(
            _sessionService.Object,
            _parserService.Object,
            _fileSystemService.Object,
            _apiSpecService.Object,
            NullLogger<ScribanHub>.Instance)
        {
            Clients = _clients.Object,
            Context = _context.Object
        };

        // Setup default mocks
        _clients.Setup(c => c.Caller).Returns(_caller.Object);
        _context.Setup(c => c.ConnectionId).Returns("test-connection-id");
    }

    [Fact]
    public async Task CheckTrigger_FilePickerContext_SendsOpenPicker()
    {
        // Arrange
        var triggerContext = new TriggerContext
        {
            Event = "char",
            Char = "(",
            DocumentUri = "file:///test.scriban",
            Code = "{{ copy_file(",
            Position = new Position(0, 13)
        };

        var paramContext = new ParameterContext
        {
            FunctionName = "copy_file",
            ParameterIndex = 0,
            ParameterSpec = new ParameterEntry
            {
                Name = "source",
                Type = "path",
                Picker = "file-picker"
            },
            CurrentValue = null
        };

        _sessionService
            .Setup(s => s.ValidateAccess("test-connection-id", triggerContext.DocumentUri))
            .Returns(true);

        _parserService
            .Setup(p => p.GetParameterContextAsync(
                triggerContext.DocumentUri,
                triggerContext.Code,
                triggerContext.Position,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(paramContext);

        // Act
        await _hub.CheckTrigger(triggerContext);

        // Assert
        _caller.Verify(c => c.OpenPicker(It.Is<OpenPickerData>(d =>
            d.PickerType == "file-picker" &&
            d.FunctionName == "copy_file" &&
            d.ParameterIndex == 0
        )), Times.Once);
    }

    [Fact]
    public async Task CheckTrigger_EnumListContext_DoesNotSendOpenPicker()
    {
        // Arrange
        var triggerContext = new TriggerContext
        {
            Event = "char",
            Char = "(",
            DocumentUri = "file:///test.scriban",
            Code = "{{ set_mode(",
            Position = new Position(0, 11)
        };

        var paramContext = new ParameterContext
        {
            FunctionName = "set_mode",
            ParameterIndex = 0,
            ParameterSpec = new ParameterEntry
            {
                Name = "mode",
                Type = "constant",
                Picker = "enum-list",
                Options = new List<string> { "FAST", "SLOW" }
            }
        };

        _sessionService
            .Setup(s => s.ValidateAccess(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(true);

        _parserService
            .Setup(p => p.GetParameterContextAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Position>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(paramContext);

        // Act
        await _hub.CheckTrigger(triggerContext);

        // Assert - Should NOT send OpenPicker for enum-list
        _caller.Verify(c => c.OpenPicker(It.IsAny<OpenPickerData>()), Times.Never);
    }

    [Fact]
    public async Task CheckTrigger_UnauthorizedAccess_ThrowsException()
    {
        // Arrange
        var triggerContext = new TriggerContext
        {
            Event = "char",
            Char = "(",
            DocumentUri = "file:///test.scriban",
            Code = "{{ copy_file(",
            Position = new Position(0, 13)
        };

        _sessionService
            .Setup(s => s.ValidateAccess(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(false); // Access denied!

        // Act & Assert
        var act = async () => await _hub.CheckTrigger(triggerContext);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task GetPathSuggestions_ValidRequest_ReturnsList()
    {
        // Arrange
        var expected = new List<string> { "file1.txt", "file2.txt", "dir/" };

        _fileSystemService
            .Setup(f => f.GetPathSuggestionsAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _hub.GetPathSuggestions("copy_file", 0, null);

        // Assert
        result.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task GetPathSuggestions_Timeout_ThrowsHubException()
    {
        // Arrange
        _fileSystemService
            .Setup(f => f.GetPathSuggestionsAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException());

        // Act & Assert
        var act = async () => await _hub.GetPathSuggestions("copy_file", 0, null);

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*timed out*");
    }

    [Fact]
    public async Task OnDisconnectedAsync_CleansUpConnection()
    {
        // Act
        await _hub.OnDisconnectedAsync(null);

        // Assert
        _sessionService.Verify(
            s => s.CleanupConnection("test-connection-id"),
            Times.Once);
    }
}
```

---

### Stage B4: Acceptance Criteria

**Run tests:**
```bash
dotnet test --filter "Stage=B4"
```

**Expected:**
```
Total tests: 7
     Passed: 7
```

**Verification Checklist:**
- [ ] ScribanHub implements OnConnected/OnDisconnected
- [ ] CheckTrigger validates document access
- [ ] CheckTrigger sends OpenPicker for file-picker
- [ ] CheckTrigger does nothing for enum-list
- [ ] GetPathSuggestions returns file list
- [ ] Timeouts handled gracefully
- [ ] Unauthorized access blocked

**Deliverables:**
- ✅ Full SignalR Hub implementation
- ✅ Custom trigger workflow
- ✅ Path suggestions endpoint
- ✅ Proper error handling
- ✅ 100% test coverage

---

## STAGE B5: Production Hardening (Week 5-6)

**Duration:** 1.5 weeks
**Dependencies:** B1, B2, B3, B4 complete
**Priority:** P1 - Before Production

### Objectives
1. Add comprehensive timeout protection
2. Implement rate limiting
3. Add input validation everywhere
4. Secure file system operations
5. Prevent resource exhaustion

### Task B5.1: Timeout Infrastructure (Day 1-2)

**File:** `Core/Services/TimeoutConfiguration.cs`

```csharp
namespace ScribanLanguageServer.Core.Services;

/// <summary>
/// Configuration for operation timeouts
/// </summary>
public class TimeoutConfiguration
{
    public int GlobalRequestTimeoutSeconds { get; set; } = 30;
    public int ParsingTimeoutSeconds { get; set; } = 10;
    public int FileSystemTimeoutSeconds { get; set; } = 5;
    public int ValidationTimeoutSeconds { get; set; } = 5;
    public int SignalRMethodTimeoutSeconds { get; set; } = 10;
}
```

**File:** `Core/Services/ITimeoutService.cs`

```csharp
namespace ScribanLanguageServer.Core.Services;

/// <summary>
/// Service for creating timeout-aware cancellation tokens
/// </summary>
public interface ITimeoutService
{
    CancellationTokenSource CreateTimeout(TimeSpan timeout, CancellationToken linkedToken = default);
    CancellationTokenSource CreateTimeoutForOperation(string operationType, CancellationToken linkedToken = default);
}

public class TimeoutService : ITimeoutService
{
    private readonly TimeoutConfiguration _config;
    private readonly ILogger<TimeoutService> _logger;

    public TimeoutService(TimeoutConfiguration config, ILogger<TimeoutService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public CancellationTokenSource CreateTimeout(TimeSpan timeout, CancellationToken linkedToken = default)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);
        cts.CancelAfter(timeout);
        return cts;
    }

    public CancellationTokenSource CreateTimeoutForOperation(string operationType, CancellationToken linkedToken = default)
    {
        var timeout = operationType switch
        {
            "parsing" => TimeSpan.FromSeconds(_config.ParsingTimeoutSeconds),
            "filesystem" => TimeSpan.FromSeconds(_config.FileSystemTimeoutSeconds),
            "validation" => TimeSpan.FromSeconds(_config.ValidationTimeoutSeconds),
            "signalr" => TimeSpan.FromSeconds(_config.SignalRMethodTimeoutSeconds),
            _ => TimeSpan.FromSeconds(_config.GlobalRequestTimeoutSeconds)
        };

        _logger.LogDebug("Creating timeout for {Operation}: {Timeout}s", operationType, timeout.TotalSeconds);
        return CreateTimeout(timeout, linkedToken);
    }
}
```

**Update:** `Core/Services/ScribanParserService.cs`

```csharp
// Add timeout to ParseAsync
public async Task<ScriptPage?> ParseAsync(
    string code,
    CancellationToken cancellationToken = default)
{
    // Dynamic timeout based on document size
    var baseTimeout = 500; // ms
    var perCharTimeout = 0.01; // ms per char
    var maxTimeout = 10000; // 10 seconds max

    var timeout = Math.Min(
        maxTimeout,
        (int)(baseTimeout + code.Length * perCharTimeout));

    using var cts = _timeoutService.CreateTimeout(
        TimeSpan.FromMilliseconds(timeout),
        cancellationToken);

    try
    {
        return await Task.Run(() =>
        {
            var template = Template.Parse(code);
            cts.Token.ThrowIfCancellationRequested();
            return template.Page;
        }, cts.Token);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        _logger.LogWarning("Parsing timed out for document of {Size} bytes", code.Length);
        throw new TimeoutException($"Parsing exceeded timeout of {timeout}ms");
    }
}
```

**Tests:** `Tests.Unit/Services/TimeoutServiceTests.cs`

```csharp
[Trait("Stage", "B5")]
public class TimeoutServiceTests
{
    [Fact]
    public async Task CreateTimeout_CancelsAfterDuration()
    {
        var service = CreateService();
        using var cts = service.CreateTimeout(TimeSpan.FromMilliseconds(100));

        await Task.Delay(150);

        cts.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task CreateTimeoutForOperation_UseCorrectTimeout()
    {
        var config = new TimeoutConfiguration { ParsingTimeoutSeconds = 1 };
        var service = new TimeoutService(config, NullLogger<TimeoutService>.Instance);

        using var cts = service.CreateTimeoutForOperation("parsing");

        // Should not cancel immediately
        cts.Token.IsCancellationRequested.Should().BeFalse();

        // Should cancel after timeout
        await Task.Delay(1100);
        cts.Token.IsCancellationRequested.Should().BeTrue();
    }
}
```

---

### Task B5.2: Rate Limiting Service (Day 2-3)

**File:** `Core/Services/IRateLimitService.cs`

```csharp
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
        _logger = logger;
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
```

**Update:** `Server/ScribanHub.cs`

```csharp
public class ScribanHub : Hub<IScribanClient>
{
    private readonly IRateLimitService _rateLimit;

    public async Task CheckTrigger(TriggerContext context)
    {
        // Apply rate limiting
        if (!_rateLimit.TryAcquire(Context.ConnectionId))
        {
            throw new HubException("Rate limit exceeded: maximum 10 requests per second");
        }

        // ... rest of method
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _rateLimit.RemoveConnection(Context.ConnectionId);
        // ... rest of cleanup
    }
}
```

**Tests:** `Tests.Unit/Services/RateLimitServiceTests.cs`

```csharp
[Trait("Stage", "B5")]
public class RateLimitServiceTests
{
    [Fact]
    public void TryAcquire_WithinLimit_ReturnsTrue()
    {
        var service = CreateService();

        // Should allow first 10 requests
        for (int i = 0; i < 10; i++)
        {
            service.TryAcquire("conn1").Should().BeTrue();
        }
    }

    [Fact]
    public void TryAcquire_ExceedsLimit_ReturnsFalse()
    {
        var service = CreateService();

        // Exhaust tokens
        for (int i = 0; i < 10; i++)
        {
            service.TryAcquire("conn1");
        }

        // 11th request should fail
        service.TryAcquire("conn1").Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquire_AfterRefill_AllowsRequests()
    {
        var service = CreateService();

        // Exhaust tokens
        for (int i = 0; i < 10; i++)
        {
            service.TryAcquire("conn1");
        }

        // Wait for refill
        await Task.Delay(1100);

        // Should allow requests again
        service.TryAcquire("conn1").Should().BeTrue();
    }

    [Fact]
    public void TryAcquire_DifferentConnections_IndependentLimits()
    {
        var service = CreateService();

        // Exhaust conn1
        for (int i = 0; i < 10; i++)
        {
            service.TryAcquire("conn1");
        }

        // conn2 should still work
        service.TryAcquire("conn2").Should().BeTrue();
    }
}
```

---

### Task B5.3: Input Validation (Day 3-4)

**File:** `Core/Validation/InputValidator.cs`

```csharp
namespace ScribanLanguageServer.Core.Validation;

/// <summary>
/// Validates user input to prevent injection attacks and resource exhaustion
/// </summary>
public static class InputValidator
{
    private const int MaxDocumentUriLength = 2048;
    private const int MaxLineLength = 10000;
    private const int MaxDocumentSize = 1024 * 1024; // 1MB
    private const int MaxLineNumber = 100000;

    public static void ValidateDocumentUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("Document URI cannot be empty", nameof(uri));
        }

        if (uri.Length > MaxDocumentUriLength)
        {
            throw new ArgumentException(
                $"Document URI too long (max {MaxDocumentUriLength} chars)",
                nameof(uri));
        }

        // Must be a valid URI
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
        {
            throw new ArgumentException("Invalid document URI format", nameof(uri));
        }

        // Only allow specific schemes
        var allowedSchemes = new[] { "file", "untitled", "inmemory" };
        if (!allowedSchemes.Contains(parsedUri.Scheme.ToLowerInvariant()))
        {
            throw new ArgumentException(
                $"URI scheme '{parsedUri.Scheme}' not allowed. Allowed schemes: {string.Join(", ", allowedSchemes)}",
                nameof(uri));
        }
    }

    public static void ValidatePosition(int line, int character)
    {
        if (line < 0 || line > MaxLineNumber)
        {
            throw new ArgumentException(
                $"Invalid line number: {line}. Must be between 0 and {MaxLineNumber}",
                nameof(line));
        }

        if (character < 0 || character > MaxLineLength)
        {
            throw new ArgumentException(
                $"Invalid character position: {character}. Must be between 0 and {MaxLineLength}",
                nameof(character));
        }
    }

    public static void ValidateDocumentSize(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return; // Empty documents are valid
        }

        if (content.Length > MaxDocumentSize)
        {
            throw new ArgumentException(
                $"Document too large: {content.Length} bytes. Maximum allowed: {MaxDocumentSize} bytes",
                nameof(content));
        }
    }

    public static string SanitizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        // Remove dangerous path traversal patterns
        var sanitized = path
            .Replace("..", string.Empty)
            .Replace("~", string.Empty);

        // Normalize path separators
        sanitized = sanitized.Replace('\\', '/');

        // Remove leading/trailing slashes
        sanitized = sanitized.Trim('/');

        return sanitized;
    }

    public static void ValidateFunctionName(string functionName)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            throw new ArgumentException("Function name cannot be empty", nameof(functionName));
        }

        if (functionName.Length > 100)
        {
            throw new ArgumentException("Function name too long", nameof(functionName));
        }

        // Only allow alphanumeric and underscore
        if (!System.Text.RegularExpressions.Regex.IsMatch(functionName, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
        {
            throw new ArgumentException(
                "Function name must start with letter or underscore and contain only alphanumeric characters and underscores",
                nameof(functionName));
        }
    }

    public static void ValidateParameterIndex(int parameterIndex)
    {
        if (parameterIndex < 0 || parameterIndex > 20)
        {
            throw new ArgumentException(
                $"Invalid parameter index: {parameterIndex}. Must be between 0 and 20",
                nameof(parameterIndex));
        }
    }
}
```

**Update:** `Server/ScribanHub.cs`

```csharp
public async Task CheckTrigger(TriggerContext context)
{
    // Rate limiting
    if (!_rateLimit.TryAcquire(Context.ConnectionId))
    {
        throw new HubException("Rate limit exceeded");
    }

    // Input validation
    try
    {
        InputValidator.ValidateDocumentUri(context.DocumentUri);
        InputValidator.ValidatePosition(context.Position.Line, context.Position.Character);

        if (!string.IsNullOrEmpty(context.CurrentLine))
        {
            if (context.CurrentLine.Length > 10000)
            {
                throw new ArgumentException("Line too long");
            }
        }
    }
    catch (ArgumentException ex)
    {
        _logger.LogWarning(ex, "Invalid input in CheckTrigger");
        throw new HubException($"Invalid input: {ex.Message}");
    }

    // Validate access
    if (!_sessionService.ValidateAccess(Context.ConnectionId, context.DocumentUri))
    {
        _logger.LogWarning(
            "Unauthorized access attempt: {ConnectionId} -> {Uri}",
            Context.ConnectionId,
            context.DocumentUri);
        throw new HubException("Access denied to document");
    }

    // ... rest of method
}

public async Task<List<string>> GetPathSuggestions(
    string functionName,
    int parameterIndex,
    string? currentValue)
{
    // Rate limiting
    if (!_rateLimit.TryAcquire(Context.ConnectionId))
    {
        throw new HubException("Rate limit exceeded");
    }

    // Input validation
    try
    {
        InputValidator.ValidateFunctionName(functionName);
        InputValidator.ValidateParameterIndex(parameterIndex);
    }
    catch (ArgumentException ex)
    {
        _logger.LogWarning(ex, "Invalid input in GetPathSuggestions");
        throw new HubException($"Invalid input: {ex.Message}");
    }

    // ... rest of method
}
```

**Tests:** `Tests.Unit/Validation/InputValidatorTests.cs`

```csharp
[Trait("Stage", "B5")]
public class InputValidatorTests
{
    [Theory]
    [InlineData("file:///test.scriban")]
    [InlineData("untitled:Untitled-1")]
    [InlineData("inmemory://model/1")]
    public void ValidateDocumentUri_ValidUri_DoesNotThrow(string uri)
    {
        var act = () => InputValidator.ValidateDocumentUri(uri);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-uri")]
    [InlineData("http://example.com/script.scriban")] // HTTP not allowed
    public void ValidateDocumentUri_InvalidUri_ThrowsArgumentException(string uri)
    {
        var act = () => InputValidator.ValidateDocumentUri(uri);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 50)]
    [InlineData(99999, 9999)]
    public void ValidatePosition_ValidPosition_DoesNotThrow(int line, int character)
    {
        var act = () => InputValidator.ValidatePosition(line, character);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(100001, 0)]
    [InlineData(0, 10001)]
    public void ValidatePosition_InvalidPosition_ThrowsArgumentException(int line, int character)
    {
        var act = () => InputValidator.ValidatePosition(line, character);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SanitizePath_PathTraversal_RemovesDangerousPatterns()
    {
        var input = "../../../etc/passwd";
        var result = InputValidator.SanitizePath(input);

        result.Should().NotContain("..");
        result.Should().Be("etc/passwd");
    }

    [Theory]
    [InlineData("valid_function")]
    [InlineData("_private")]
    [InlineData("func123")]
    public void ValidateFunctionName_ValidName_DoesNotThrow(string name)
    {
        var act = () => InputValidator.ValidateFunctionName(name);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("123invalid")] // Starts with number
    [InlineData("invalid-name")] // Contains dash
    [InlineData("invalid.name")] // Contains dot
    public void ValidateFunctionName_InvalidName_ThrowsArgumentException(string name)
    {
        var act = () => InputValidator.ValidateFunctionName(name);
        act.Should().Throw<ArgumentException>();
    }
}
```

---

### Task B5.4: Secure FileSystemService (Day 4-5)

**Update:** `Core/Services/FileSystemService.cs`

```csharp
public async Task<List<string>> GetPathSuggestionsAsync(
    string basePath,
    string? searchPattern,
    CancellationToken cancellationToken = default)
{
    // Create timeout
    using var cts = _timeoutService.CreateTimeoutForOperation("filesystem", cancellationToken);

    // Throttle concurrent operations
    await _throttle.WaitAsync(cts.Token);

    try
    {
        // Sanitize base path
        basePath = InputValidator.SanitizePath(basePath);

        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Directory.GetCurrentDirectory();
        }

        // Validate path exists and is accessible
        if (!Directory.Exists(basePath))
        {
            _logger.LogWarning("Directory not found: {Path}", basePath);
            return new List<string>();
        }

        // Check for dangerous paths
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

        return await Task.Run(() =>
        {
            var results = new List<string>();
            var itemCount = 0;
            const int maxItems = 10000;

            try
            {
                var entries = Directory.EnumerateFileSystemEntries(
                    fullPath,
                    searchPattern ?? "*",
                    SearchOption.TopDirectoryOnly);

                foreach (var entry in entries)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    if (++itemCount > maxItems)
                    {
                        _logger.LogWarning("Path suggestions exceeded max items ({Max})", maxItems);
                        break;
                    }

                    results.Add(Path.GetFileName(entry));
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Access denied to path: {Path}", fullPath);
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogWarning(ex, "Directory not found: {Path}", fullPath);
            }

            return results;
        }, cts.Token);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        _logger.LogWarning("GetPathSuggestions timed out for path: {Path}", basePath);
        throw new TimeoutException("File system operation timed out");
    }
    finally
    {
        _throttle.Release();
    }
}
```

---

### Stage B5: Acceptance Criteria

**Run tests:**
```bash
dotnet test --filter "Stage=B5"
```

**Expected:**
```
Total tests: 20+
     Passed: 20+
```

**Verification Checklist:**
- [ ] Timeouts configured for all operations
- [ ] Rate limiting works (10 req/sec)
- [ ] Input validation on all endpoints
- [ ] Path traversal prevention working
- [ ] Maximum document size enforced
- [ ] Invalid URIs rejected
- [ ] FileSystemService secured

**Deliverables:**
- ✅ Comprehensive timeout infrastructure
- ✅ Token bucket rate limiting
- ✅ Full input validation
- ✅ Secured file system operations
- ✅ Protection against resource exhaustion

---

## STAGE B6: Monitoring & Observability (Week 7)

**Duration:** 1 week
**Dependencies:** B1-B5 complete
**Priority:** P1 - Before Production

### Objectives
1. Add health checks for server components
2. Implement metrics collection
3. Add request/response tracking
4. Monitor cache performance
5. Track error rates

### Task B6.1: Health Checks (Day 1)

**File:** `Core/Health/ApiSpecHealthCheck.cs`

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ScribanLanguageServer.Core.Health;

/// <summary>
/// Health check for ApiSpec service
/// </summary>
public class ApiSpecHealthCheck : IHealthCheck
{
    private readonly IApiSpecService _apiSpec;
    private readonly ILogger<ApiSpecHealthCheck> _logger;

    public ApiSpecHealthCheck(IApiSpecService apiSpec, ILogger<ApiSpecHealthCheck> logger)
    {
        _apiSpec = apiSpec;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var globals = _apiSpec.GetGlobals();

            if (globals == null || !globals.Any())
            {
                return Task.FromResult(
                    HealthCheckResult.Unhealthy("No globals loaded from ApiSpec"));
            }

            var functionCount = globals.Count(g => g.Type == "function");
            var objectCount = globals.Count(g => g.Type == "object");

            var data = new Dictionary<string, object>
            {
                ["total_globals"] = globals.Count(),
                ["functions"] = functionCount,
                ["objects"] = objectCount
            };

            return Task.FromResult(
                HealthCheckResult.Healthy(
                    $"{globals.Count()} globals loaded ({functionCount} functions, {objectCount} objects)",
                    data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApiSpec health check failed");
            return Task.FromResult(
                HealthCheckResult.Unhealthy("ApiSpec error", ex));
        }
    }
}
```

**File:** `Core/Health/CacheHealthCheck.cs`

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;

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
        _parser = parser;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = _parser.GetCacheStatistics();

            var hitRate = stats.TotalRequests > 0
                ? (double)stats.CacheHits / stats.TotalRequests * 100
                : 0;

            var data = new Dictionary<string, object>
            {
                ["cache_entries"] = stats.CachedDocuments,
                ["cache_hits"] = stats.CacheHits,
                ["cache_misses"] = stats.CacheMisses,
                ["total_requests"] = stats.TotalRequests,
                ["hit_rate_percent"] = Math.Round(hitRate, 2)
            };

            // Warn if hit rate is too low
            if (stats.TotalRequests > 100 && hitRate < 50)
            {
                return Task.FromResult(
                    HealthCheckResult.Degraded(
                        $"Cache hit rate is low: {hitRate:F2}%",
                        data: data));
            }

            return Task.FromResult(
                HealthCheckResult.Healthy(
                    $"Cache: {stats.CachedDocuments} entries, {hitRate:F2}% hit rate",
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
```

**File:** `Server/Health/SignalRHealthCheck.cs`

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ScribanLanguageServer.Server.Health;

/// <summary>
/// Health check for SignalR hub connections
/// </summary>
public class SignalRHealthCheck : IHealthCheck
{
    private readonly IDocumentSessionService _sessions;
    private readonly ILogger<SignalRHealthCheck> _logger;

    public SignalRHealthCheck(
        IDocumentSessionService sessions,
        ILogger<SignalRHealthCheck> logger)
    {
        _sessions = sessions;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stats = _sessions.GetStatistics();

            var data = new Dictionary<string, object>
            {
                ["active_connections"] = stats.ActiveConnections,
                ["total_documents"] = stats.TotalDocuments,
                ["documents_per_connection"] = stats.ActiveConnections > 0
                    ? Math.Round((double)stats.TotalDocuments / stats.ActiveConnections, 2)
                    : 0
            };

            return Task.FromResult(
                HealthCheckResult.Healthy(
                    $"{stats.ActiveConnections} active connections, {stats.TotalDocuments} documents",
                    data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR health check failed");
            return Task.FromResult(
                HealthCheckResult.Unhealthy("SignalR error", ex));
        }
    }
}
```

**Update:** `Server/Program.cs`

```csharp
// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<ApiSpecHealthCheck>("apispec", tags: new[] { "ready" })
    .AddCheck<CacheHealthCheck>("cache", tags: new[] { "ready" })
    .AddCheck<SignalRHealthCheck>("signalr", tags: new[] { "ready" });

// Map health check endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                data = e.Value.Data,
                duration = e.Value.Duration.TotalMilliseconds
            }),
            totalDuration = report.TotalDuration.TotalMilliseconds
        }, new JsonSerializerOptions { WriteIndented = true });

        await context.Response.WriteAsync(result);
    }
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false // Just checks if server is running
});
```

---

### Task B6.2: Metrics Service (Day 2-3)

**File:** `Core/Services/IMetricsService.cs`

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

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
        _logger = logger;
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
```

**Add Metrics Endpoint:** `Server/Controllers/MetricsController.cs`

```csharp
using Microsoft.AspNetCore.Mvc;

namespace ScribanLanguageServer.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly IMetricsService _metrics;
    private readonly IScribanParserService _parser;

    public MetricsController(IMetricsService metrics, IScribanParserService parser)
    {
        _metrics = metrics;
        _parser = parser;
    }

    [HttpGet]
    public ActionResult<object> GetMetrics()
    {
        var metricsSnapshot = _metrics.GetSnapshot();
        var cacheStats = _parser.GetCacheStatistics();

        return Ok(new
        {
            requests = new
            {
                total = metricsSnapshot.TotalRequests,
                successful = metricsSnapshot.SuccessfulRequests,
                failed = metricsSnapshot.FailedRequests,
                success_rate_percent = Math.Round(metricsSnapshot.SuccessRate, 2)
            },
            cache = new
            {
                hits = metricsSnapshot.CacheHits,
                misses = metricsSnapshot.CacheMisses,
                hit_rate_percent = Math.Round(metricsSnapshot.CacheHitRate, 2),
                entries = cacheStats.CachedDocuments
            },
            errors = metricsSnapshot.ErrorsByType
        });
    }
}
```

---

### Task B6.3: Integrate Metrics into Handlers (Day 4)

**Update:** `Server/Handlers/CompletionHandler.cs`

```csharp
public class CompletionHandler : CompletionHandlerBase
{
    private readonly IMetricsService _metrics;

    public override async Task<CompletionList> Handle(
        CompletionParams request,
        CancellationToken cancellationToken)
    {
        using var timer = _metrics.StartTimer("completion");

        try
        {
            // ... existing logic
            return result;
        }
        catch (Exception ex)
        {
            _metrics.RecordError("completion", ex.GetType().Name);
            _logger.LogError(ex, "Completion failed");
            return new CompletionList();
        }
    }
}
```

**Update:** `Server/Handlers/HoverHandler.cs`

```csharp
public class HoverHandler : HoverHandlerBase
{
    private readonly IMetricsService _metrics;

    public override async Task<Hover?> Handle(
        HoverParams request,
        CancellationToken cancellationToken)
    {
        using var timer = _metrics.StartTimer("hover");

        try
        {
            // ... existing logic
            return result;
        }
        catch (Exception ex)
        {
            _metrics.RecordError("hover", ex.GetType().Name);
            _logger.LogError(ex, "Hover failed");
            return null;
        }
    }
}
```

**Update:** `Core/Services/ScribanParserService.cs`

```csharp
public async Task<ScriptPage?> ParseAsync(
    string code,
    CancellationToken cancellationToken = default)
{
    var uri = "..."; // Get from context
    var version = 0; // Get from context

    // Check cache
    if (_astCache.TryGetValue(uri, out var cached) && cached.Version == version)
    {
        _metrics.RecordCacheHit("parse");
        cached.LastAccess = DateTime.UtcNow;
        return cached.Ast;
    }

    _metrics.RecordCacheMiss("parse");

    using var timer = _metrics.StartTimer("parse");

    try
    {
        var ast = await ParseWithTimeoutAsync(code, cancellationToken);
        _metrics.RecordDocumentSize(code.Length);
        return ast;
    }
    catch (TimeoutException)
    {
        _metrics.RecordError("parse", "timeout");
        throw;
    }
    catch (Exception ex)
    {
        _metrics.RecordError("parse", ex.GetType().Name);
        throw;
    }
}
```

---

### Task B6.4: Logging Enhancements (Day 5)

**File:** `Core/Logging/LoggingExtensions.cs`

```csharp
using Microsoft.Extensions.Logging;

namespace ScribanLanguageServer.Core.Logging;

/// <summary>
/// High-performance logging using LoggerMessage pattern
/// </summary>
public static partial class LoggingExtensions
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Document opened: {Uri} (version: {Version}, size: {Size} bytes)")]
    public static partial void LogDocumentOpened(
        this ILogger logger,
        string uri,
        int version,
        int size);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Debug,
        Message = "Cache hit for {Uri} v{Version}")]
    public static partial void LogCacheHit(
        this ILogger logger,
        string uri,
        int version);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Debug,
        Message = "Cache miss for {Uri} v{Version}, parsing...")]
    public static partial void LogCacheMiss(
        this ILogger logger,
        string uri,
        int version);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "Rate limit exceeded for connection {ConnectionId}")]
    public static partial void LogRateLimitExceeded(
        this ILogger logger,
        string connectionId);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "Unauthorized access: {ConnectionId} tried to access {Uri}")]
    public static partial void LogUnauthorizedAccess(
        this ILogger logger,
        string connectionId,
        string uri);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Error,
        Message = "Operation {Operation} timed out after {TimeoutMs}ms")]
    public static partial void LogOperationTimeout(
        this ILogger logger,
        string operation,
        int timeoutMs);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Error,
        Message = "Parsing failed for document {Uri}: {ErrorMessage}")]
    public static partial void LogParsingError(
        this ILogger logger,
        string uri,
        string errorMessage,
        Exception exception);
}
```

**Update appsettings.json:**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.AspNetCore.SignalR": "Information",
      "ScribanLanguageServer": "Debug",
      "ScribanLanguageServer.Core.Services.ScribanParserService": "Information"
    }
  },
  "Serilog": {
    "Using": [ "Serilog.Sinks.Console", "Serilog.Sinks.File" ],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning",
        "ScribanLanguageServer": "Debug"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/scriban-language-server-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7,
          "outputTemplate": "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
        }
      }
    ],
    "Enrich": [ "FromLogContext", "WithMachineName", "WithThreadId" ]
  }
}
```

---

### Stage B6: Acceptance Criteria

**Run tests:**
```bash
dotnet test --filter "Stage=B6"
```

**Expected:**
```
Total tests: 10+
     Passed: 10+
```

**Verification Checklist:**
- [ ] Health checks return proper status
- [ ] /health endpoint returns JSON with all checks
- [ ] /health/ready validates all components
- [ ] /health/live responds quickly
- [ ] Metrics track requests correctly
- [ ] Metrics track cache hit/miss
- [ ] Metrics endpoint returns valid data
- [ ] Errors are categorized properly
- [ ] Logging includes structured data
- [ ] Log files rotate daily

**Deliverables:**
- ✅ Comprehensive health checks
- ✅ System.Diagnostics.Metrics integration
- ✅ Metrics API endpoint
- ✅ High-performance logging
- ✅ Structured logging with Serilog
- ✅ Request/response tracking

---

## Implementation Summary & Roadmap

### Overall Timeline

**Total Duration:** 9-10 weeks (including Stages B1-B6)

| Stage | Duration | Priority | Status |
|-------|----------|----------|---------|
| B1 | 1 week | P0 | ✅ COMPLETE |
| B1.5 | 1 week | P0 | 📋 Ready |
| B2 | 2 weeks | P0 | ✅ COMPLETE |
| B3 | 2 weeks | P0 | ✅ COMPLETE |
| B4 | 1.5 weeks | P1 | 📋 Ready |
| B5 | 1.5 weeks | P1 | 📋 Ready |
| B6 | 1 week | P1 | 📋 Ready |

### Stage Dependencies

```
B1 (Foundation) ─────┬─→ B2 (Core Services) ─→ B3 (LSP Handlers) ─→ B4 (SignalR)
                     │
                     └─→ B1.5 (Validation) ────────────────────────────┘
                                                                        │
                                                                        ↓
                     B5 (Production Hardening) ←────────────────────────
                                ↓
                     B6 (Monitoring & Observability)
```

### Test Coverage Goals

| Stage | Target Tests | Critical Areas |
|-------|-------------|----------------|
| B1 | 51 tests | ApiSpec validation, model serialization |
| B1.5 | 10+ tests | Duplicate detection, type validation, reserved words |
| B2 | 64 tests | Parser caching, semantic analysis, file system |
| B3 | 24 tests | LSP protocol handlers, debouncing |
| B4 | 7+ tests | SignalR hub, access control, triggers |
| B5 | 20+ tests | Timeouts, rate limiting, input validation |
| B6 | 10+ tests | Health checks, metrics collection |
| **Total** | **186+** | **High coverage across all components** |

### Critical Production Requirements

#### Before Production Deployment:
1. **Security (P0)**
   - [ ] Input validation on all endpoints
   - [ ] Rate limiting (10 req/sec per connection)
   - [ ] Path traversal prevention
   - [ ] Maximum document size enforced (1MB)
   - [ ] URI scheme validation
   - [ ] Document access control

2. **Performance (P0)**
   - [ ] AST caching implemented (3-5x speedup)
   - [ ] Debounced validation (250ms)
   - [ ] Operation timeouts configured
   - [ ] File system operations throttled
   - [ ] Cache hit rate > 80%

3. **Reliability (P0)**
   - [ ] Health checks for all components
   - [ ] Graceful error handling
   - [ ] Connection cleanup on disconnect
   - [ ] Proper cancellation token handling
   - [ ] Memory leak prevention

4. **Observability (P1)**
   - [ ] Metrics collection enabled
   - [ ] Structured logging configured
   - [ ] Error tracking by category
   - [ ] Request/response timing
   - [ ] Cache performance metrics

### Configuration Template

**Complete appsettings.json for Production:**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.AspNetCore.SignalR": "Information",
      "ScribanLanguageServer": "Debug"
    }
  },

  "ApiSpec": {
    "Path": "ApiSpec.json",
    "ValidateOnStartup": true,
    "ReloadOnChange": false
  },

  "Cors": {
    "AllowedOrigins": ["http://localhost:3000", "https://yourdomain.com"],
    "AllowedHeaders": ["Content-Type", "Authorization"],
    "AllowedMethods": ["GET", "POST"]
  },

  "Timeouts": {
    "GlobalRequestTimeoutSeconds": 30,
    "ParsingTimeoutSeconds": 10,
    "FileSystemTimeoutSeconds": 5,
    "ValidationTimeoutSeconds": 5,
    "SignalRMethodTimeoutSeconds": 10
  },

  "Limits": {
    "MaxDocumentSizeBytes": 1048576,
    "MaxFileListSize": 10000,
    "MaxConcurrentFileOperations": 5,
    "MaxLineLength": 10000,
    "MaxLineNumber": 100000
  },

  "Cache": {
    "AstCacheMaxEntries": 1000,
    "AstCacheExpirationMinutes": 10,
    "EnableCaching": true
  },

  "RateLimiting": {
    "RequestsPerSecond": 10,
    "BurstSize": 20,
    "RefillIntervalSeconds": 1
  },

  "FileSystem": {
    "AllowedRoots": [
      "UserProfile",
      "MyDocuments",
      "CurrentDirectory"
    ],
    "MaxConcurrentOperations": 5,
    "TimeoutSeconds": 5
  },

  "Serilog": {
    "Using": ["Serilog.Sinks.Console", "Serilog.Sinks.File"],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning",
        "ScribanLanguageServer": "Debug"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/scriban-language-server-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7,
          "outputTemplate": "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
        }
      }
    ],
    "Enrich": ["FromLogContext", "WithMachineName", "WithThreadId"]
  }
}
```

### Performance Targets

| Metric | Target | Current | Priority |
|--------|--------|---------|----------|
| Completion latency (p95) | < 100ms | TBD | High |
| Hover latency (p95) | < 50ms | TBD | High |
| Diagnostics latency (p95) | < 300ms | TBD | High |
| Cache hit rate | > 80% | TBD | Medium |
| Parse time (10KB doc) | < 50ms | TBD | Medium |
| Memory per document | < 5MB | TBD | Low |

### Deployment Checklist

#### Pre-Deployment:
1. **Code Quality**
   - [ ] All tests passing (186+ tests)
   - [ ] No compiler warnings
   - [ ] Code review completed
   - [ ] Security audit passed

2. **Configuration**
   - [ ] appsettings.Production.json configured
   - [ ] CORS origins set correctly
   - [ ] Logging configured for production
   - [ ] Metrics collection enabled

3. **Testing**
   - [ ] Unit tests passing
   - [ ] Integration tests passing
   - [ ] Load tests completed (100 concurrent users)
   - [ ] Security tests passed

4. **Documentation**
   - [ ] API documentation updated
   - [ ] Deployment guide created
   - [ ] Configuration guide created
   - [ ] Troubleshooting guide created

#### Post-Deployment:
1. **Monitoring**
   - [ ] Health check endpoints accessible
   - [ ] Metrics endpoint configured
   - [ ] Log aggregation working
   - [ ] Alerting configured

2. **Validation**
   - [ ] Smoke tests passing
   - [ ] Performance within targets
   - [ ] No errors in logs
   - [ ] Health checks green

### Quick Start for Implementation

**Week 1-2: Retrofit B1.5 (Validation)**
```bash
# 1. Implement validator
cd Backend/ScribanLanguageServer.Core/ApiSpec
# Create ApiSpecValidator.cs

# 2. Add tests
cd ../../Tests.Unit/ApiSpec
# Create ApiSpecValidatorTests.cs

# 3. Run tests
dotnet test --filter "Stage=B1.5"

# 4. Update ApiSpecService to use validator
# Edit Core/ApiSpec/ApiSpecService.cs
```

**Week 3-4: Implement B4 (SignalR Hub)**
```bash
# 1. Create hub
cd Backend/ScribanLanguageServer.Server
# Create ScribanHub.cs, IScribanClient.cs

# 2. Add tests
cd ../Tests.Unit/Handlers
# Create ScribanHubTests.cs

# 3. Run tests
dotnet test --filter "Stage=B4"

# 4. Update Program.cs with SignalR configuration
```

**Week 5-6: Implement B5 (Production Hardening)**
```bash
# 1. Add timeout service
cd Backend/ScribanLanguageServer.Core/Services
# Create TimeoutService.cs, RateLimitService.cs

# 2. Add input validation
cd ../Validation
# Create InputValidator.cs

# 3. Update all services to use timeout/validation
# Edit ScribanParserService.cs, FileSystemService.cs

# 4. Run tests
dotnet test --filter "Stage=B5"
```

**Week 7: Implement B6 (Monitoring)**
```bash
# 1. Add health checks
cd Backend/ScribanLanguageServer.Core/Health
# Create health check classes

# 2. Add metrics service
cd ../Services
# Create MetricsService.cs

# 3. Integrate metrics into handlers
# Update all handler classes

# 4. Run tests
dotnet test --filter "Stage=B6"
```

### Final Verification

**Run All Tests:**
```bash
dotnet test
```

**Expected Output:**
```
Total tests: 186+
     Passed: 186+
     Failed: 0
  Skipped: 3
```

**Check Health:**
```bash
curl http://localhost:5000/health
```

**Check Metrics:**
```bash
curl http://localhost:5000/api/metrics
```

---

## Next Steps

1. **Review this plan** with the team
2. **Prioritize stages** based on immediate needs
3. **Begin with B1.5** (ApiSpec validation) as it's a critical retrofit
4. **Progress sequentially** through B4, B5, B6
5. **Run tests continuously** to maintain quality
6. **Update PROGRESS.md** after each stage completion

---

**End of Implementation Plan - Part 3**

This plan addresses the critical production-readiness findings from the design evaluation and provides a clear path to a robust, secure, and observable language server.
