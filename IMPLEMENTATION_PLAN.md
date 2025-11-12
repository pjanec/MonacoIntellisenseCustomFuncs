# Scriban Language Server - Implementation Plan

**Version:** 1.0
**Date:** 2025-11-11
**Strategy:** Independent development with mocked counterparts, staged integration

---

## Table of Contents

1. [Overview](#overview)
2. [Development Strategy](#development-strategy)
3. [Tooling & Setup](#tooling--setup)
4. [Implementation Stages](#implementation-stages)
5. [Stage Details](#stage-details)
6. [Testing Strategy](#testing-strategy)
7. [Integration Checkpoints](#integration-checkpoints)
8. [Success Metrics](#success-metrics)

---

## Overview

### Core Principle
**Develop backend and frontend independently** using comprehensive mocks, integrate at defined checkpoints when both sides have proven stability.

### Architecture Improvements Applied
Based on the design evaluation, we'll implement:
- ✅ AST caching from the start
- ✅ Debounced validation
- ✅ Document session management
- ✅ Proper timeout handling
- ✅ Input validation
- ✅ ApiSpec validation
- ✅ Comprehensive error handling
- ✅ Full test coverage at each stage

### Total Duration Estimate
- **Backend Development:** 8-10 weeks
- **Frontend Development:** 6-8 weeks (parallel)
- **Integration:** 2-3 weeks
- **Total (with parallel work):** ~12-14 weeks

---

## Development Strategy

### Independent Development Approach

```
┌─────────────────────────────────────────────────────────────┐
│                    STAGE-BASED DEVELOPMENT                   │
└─────────────────────────────────────────────────────────────┘

BACKEND (C#)                          FRONTEND (TypeScript)
    │                                        │
    ├── Stage 1: Foundation                 ├── Stage 1: Foundation
    │   (Mock LSP responses)                │   (Mock SignalR)
    │                                        │
    ├── Stage 2: Core Logic                 ├── Stage 2: Editor Setup
    │   (Unit tests only)                   │   (Mock server responses)
    │                                        │
    ├── Stage 3: Handlers                   ├── Stage 3: LSP Client
    │   (Mocked services)                   │   (Mock adapter)
    │                                        │
    ├── Stage 4: Communication              ├── Stage 4: Custom UI
    │   (Integration tests)                 │   (Component tests)
    │                                        │
    └──────────────┬────────────────────────┘
                   │
         INTEGRATION CHECKPOINT #1
         (Smoke tests, basic flow)
                   │
    ┌──────────────┴────────────────────────┐
    │         STAGE 5: INTEGRATION          │
    │      (Full end-to-end testing)        │
    └───────────────────────────────────────┘
```

### Mocking Strategy

#### Backend Mocks (for independent testing)
- **Mock Monaco Client:** Simulates LSP requests/responses
- **Mock SignalR Client:** Simulates hub method calls
- **Mock File System:** In-memory file system for testing
- **Mock ApiSpec:** Simplified test specifications

#### Frontend Mocks (for independent testing)
- **Mock SignalR Connection:** Returns canned responses
- **Mock LSP Server:** Simulates server notifications/responses
- **Mock File Service:** Returns fake file lists
- **Mock Hub Methods:** Simulates CheckTrigger, GetPathSuggestions

---

## Tooling & Setup

### Backend Stack
```bash
# Required tools
dotnet --version  # 8.0 or later
git --version

# Project structure
Backend/
├── ScribanLanguageServer.Core/          # Core logic (no dependencies)
├── ScribanLanguageServer.Server/        # LSP server host
├── ScribanLanguageServer.Tests.Unit/    # Fast unit tests
├── ScribanLanguageServer.Tests.Integration/  # Integration tests
└── ScribanLanguageServer.Tests.Mocks/   # Mock implementations
```

### Frontend Stack
```bash
# Required tools
node --version    # 18.x or later
npm --version     # 9.x or later

# Project structure
Frontend/
├── src/
│   ├── services/           # SignalR, LSP adapter
│   ├── hooks/              # React hooks
│   ├── components/         # UI components
│   ├── mocks/              # Mock services
│   └── __tests__/          # Tests
├── package.json
└── vitest.config.ts
```

### Testing Frameworks

**Backend:**
- xUnit (unit tests)
- Moq (mocking)
- FluentAssertions (assertions)
- Microsoft.AspNetCore.TestHost (integration)
- BenchmarkDotNet (performance)

**Frontend:**
- Vitest (test runner)
- React Testing Library (component tests)
- MSW (Mock Service Worker for network)
- Playwright (E2E tests - later stages)

---

## Implementation Stages

### Overview Table

| Stage | Component | Focus | Duration | Dependencies | Integration |
|-------|-----------|-------|----------|--------------|-------------|
| **B1** | Backend | Foundation & Validation | 1 week | None | None |
| **B2** | Backend | Core Services | 2 weeks | B1 | None |
| **B3** | Backend | LSP Handlers | 2 weeks | B2 | None |
| **B4** | Backend | SignalR & Communication | 1.5 weeks | B3 | None |
| **F1** | Frontend | Foundation & Mocks | 1 week | None | None |
| **F2** | Frontend | Editor Integration | 1.5 weeks | F1 | None |
| **F3** | Frontend | LSP Client Setup | 2 weeks | F2 | None |
| **F4** | Frontend | Custom UI Components | 1.5 weeks | F3 | None |
| **I1** | Integration | Basic Flow Integration | 1 week | B4, F4 | Checkpoint #1 |
| **B5** | Backend | Advanced Features | 2 weeks | I1 | None |
| **F5** | Frontend | Advanced Features | 2 weeks | I1 | None |
| **I2** | Integration | Full Integration | 1 week | B5, F5 | Checkpoint #2 |
| **P1** | Polish | Performance & Hardening | 2 weeks | I2 | Final |

---

## Stage Details

## STAGE B1: Backend Foundation & Validation

**Duration:** 1 week
**Developer:** Backend engineer
**Parallel Work:** Can start F1 simultaneously

### Objectives
1. Set up backend project structure
2. Implement ApiSpec loading and validation
3. Create mock infrastructure for testing
4. Establish logging and configuration

### Tasks

#### Task B1.1: Project Setup (Day 1)
```bash
# Create solution and projects
dotnet new sln -n ScribanLanguageServer
dotnet new classlib -n ScribanLanguageServer.Core
dotnet new web -n ScribanLanguageServer.Server
dotnet new xunit -n ScribanLanguageServer.Tests.Unit
dotnet new xunit -n ScribanLanguageServer.Tests.Integration
dotnet new classlib -n ScribanLanguageServer.Tests.Mocks

# Add projects to solution
dotnet sln add **/*.csproj

# Add NuGet packages
cd ScribanLanguageServer.Server
dotnet add package OmniSharp.Extensions.LanguageServer
dotnet add package Microsoft.AspNetCore.SignalR
dotnet add package Scriban
dotnet add package Serilog.AspNetCore

cd ../ScribanLanguageServer.Tests.Unit
dotnet add package xUnit
dotnet add package Moq
dotnet add package FluentAssertions
dotnet add package Microsoft.Extensions.Logging.Abstractions
```

**Success Criteria:**
- ✅ All projects build successfully
- ✅ Basic program runs and exits cleanly
- ✅ Unit test project discovers and runs sample test

---

#### Task B1.2: ApiSpec Models & Schema (Day 1-2)

**File:** `Core/ApiSpec/ApiSpecModels.cs`
```csharp
using System.ComponentModel.DataAnnotations;

namespace ScribanLanguageServer.Core.ApiSpec;

public class ApiSpec
{
    [Required]
    public List<GlobalEntry> Globals { get; set; } = new();
}

public class GlobalEntry
{
    [Required, MinLength(1)]
    public string Name { get; set; } = string.Empty;

    [Required, RegularExpression("^(object|function)$")]
    public string Type { get; set; } = string.Empty;

    [Required]
    public string Hover { get; set; } = string.Empty;

    public List<FunctionEntry>? Members { get; set; }
    public List<ParameterEntry>? Parameters { get; set; }
}

public class FunctionEntry
{
    [Required, MinLength(1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Type { get; set; } = "function";

    [Required]
    public string Hover { get; set; } = string.Empty;

    public List<ParameterEntry> Parameters { get; set; } = new();
}

public class ParameterEntry
{
    [Required, MinLength(1)]
    public string Name { get; set; } = string.Empty;

    [Required, RegularExpression("^(path|constant|string|number|boolean|any)$")]
    public string Type { get; set; } = string.Empty;

    [Required, RegularExpression("^(file-picker|enum-list|none)$")]
    public string Picker { get; set; } = "none";

    public List<string>? Options { get; set; }
    public List<string>? Macros { get; set; }
}
```

**File:** `Core/ApiSpec/ApiSpecValidator.cs`
```csharp
namespace ScribanLanguageServer.Core.ApiSpec;

public static class ApiSpecValidator
{
    public static ValidationResult Validate(ApiSpec spec)
    {
        var errors = new List<string>();

        if (spec?.Globals == null || !spec.Globals.Any())
        {
            errors.Add("ApiSpec must contain at least one global entry");
            return new ValidationResult { IsValid = false, Errors = errors };
        }

        // Check for duplicate names
        var duplicates = spec.Globals
            .GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Any())
        {
            errors.Add($"Duplicate global names found: {string.Join(", ", duplicates)}");
        }

        // Validate each global
        foreach (var global in spec.Globals)
        {
            ValidateGlobalEntry(global, errors);
        }

        return new ValidationResult
        {
            IsValid = !errors.Any(),
            Errors = errors
        };
    }

    private static void ValidateGlobalEntry(GlobalEntry entry, List<string> errors)
    {
        var context = $"Global '{entry.Name}'";

        if (entry.Type == "object")
        {
            if (entry.Members == null || !entry.Members.Any())
            {
                errors.Add($"{context}: Objects must have at least one member");
            }
            else
            {
                var memberDuplicates = entry.Members
                    .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                if (memberDuplicates.Any())
                {
                    errors.Add($"{context}: Duplicate members: {string.Join(", ", memberDuplicates)}");
                }

                foreach (var member in entry.Members)
                {
                    ValidateFunction(member.Parameters, $"{context}.{member.Name}", errors);
                }
            }
        }
        else if (entry.Type == "function")
        {
            ValidateFunction(entry.Parameters, context, errors);
        }
    }

    private static void ValidateFunction(List<ParameterEntry>? parameters, string context, List<string> errors)
    {
        if (parameters == null) return;

        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];
            var paramContext = $"{context}.param[{i}]({param.Name})";

            // Enum-list validation
            if (param.Picker == "enum-list")
            {
                if (param.Options == null || !param.Options.Any())
                {
                    errors.Add($"{paramContext}: Picker 'enum-list' requires non-empty 'options' array");
                }

                if (param.Type != "constant")
                {
                    errors.Add($"{paramContext}: Picker 'enum-list' should have type 'constant' (found '{param.Type}')");
                }
            }

            // File-picker validation
            if (param.Picker == "file-picker" && param.Type != "path")
            {
                errors.Add($"{paramContext}: Picker 'file-picker' should have type 'path' (found '{param.Type}')");
            }

            // Macros validation
            if (param.Macros != null && param.Macros.Any() && param.Type != "string")
            {
                errors.Add($"{paramContext}: Macros only valid for type 'string' (found '{param.Type}')");
            }
        }
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}
```

**Tests:** `Tests.Unit/ApiSpec/ApiSpecValidatorTests.cs`
```csharp
using FluentAssertions;
using ScribanLanguageServer.Core.ApiSpec;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.ApiSpec;

public class ApiSpecValidatorTests
{
    [Fact]
    public void Validate_EmptyGlobals_ReturnsError()
    {
        // Arrange
        var spec = new ApiSpec { Globals = new List<GlobalEntry>() };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("at least one global"));
    }

    [Fact]
    public void Validate_DuplicateGlobalNames_ReturnsError()
    {
        // Arrange
        var spec = new ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new() { Name = "test", Type = "function", Hover = "Test" },
                new() { Name = "test", Type = "object", Hover = "Test" }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Duplicate global names"));
    }

    [Fact]
    public void Validate_EnumListWithoutOptions_ReturnsError()
    {
        // Arrange
        var spec = new ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
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
        result.Errors.Should().Contain(e => e.Contains("requires non-empty 'options'"));
    }

    [Fact]
    public void Validate_ValidSpec_ReturnsSuccess()
    {
        // Arrange
        var spec = new ApiSpec
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
                        new() { Name = "destination", Type = "path", Picker = "file-picker" }
                    }
                },
                new()
                {
                    Name = "os",
                    Type = "object",
                    Hover = "OS functions",
                    Members = new List<FunctionEntry>
                    {
                        new()
                        {
                            Name = "execute",
                            Type = "function",
                            Hover = "Executes command",
                            Parameters = new List<ParameterEntry>
                            {
                                new() { Name = "command", Type = "string", Picker = "none" }
                            }
                        }
                    }
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("file-picker", "path", true)]
    [InlineData("file-picker", "string", false)]
    [InlineData("enum-list", "constant", true)]
    [InlineData("enum-list", "string", false)]
    public void Validate_PickerTypeCombination_ValidatesCorrectly(
        string picker,
        string type,
        bool shouldBeValid)
    {
        // Arrange
        var param = new ParameterEntry
        {
            Name = "test",
            Type = type,
            Picker = picker
        };

        if (picker == "enum-list")
        {
            param.Options = new List<string> { "OPTION1", "OPTION2" };
        }

        var spec = new ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new()
                {
                    Name = "test_func",
                    Type = "function",
                    Hover = "Test",
                    Parameters = new List<ParameterEntry> { param }
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        if (shouldBeValid)
        {
            result.IsValid.Should().BeTrue();
        }
        else
        {
            result.IsValid.Should().BeFalse();
        }
    }
}
```

**Success Criteria:**
- ✅ All validator tests pass (100% coverage)
- ✅ Run: `dotnet test --filter "FullyQualifiedName~ApiSpecValidator"`
- ✅ Should see: "Passed! - Total: 6, Passed: 6, Failed: 0"

---

#### Task B1.3: ApiSpec Service Implementation (Day 2-3)

**File:** `Core/ApiSpec/ApiSpecService.cs`
```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace ScribanLanguageServer.Core.ApiSpec;

public interface IApiSpecService
{
    ApiSpec GetSpec();
    GlobalEntry? GetGlobal(string name);
    FunctionEntry? GetFunction(string globalName, string memberName);
    GlobalEntry? GetGlobalFunction(string name);
    List<GlobalEntry> GetAllGlobals();
}

public class ApiSpecService : IApiSpecService
{
    private readonly ILogger<ApiSpecService> _logger;
    private readonly ApiSpec _spec;
    private readonly Dictionary<string, GlobalEntry> _globalIndex;
    private readonly Dictionary<string, FunctionEntry> _memberIndex;

    public ApiSpecService(
        IConfiguration configuration,
        ILogger<ApiSpecService> logger)
    {
        _logger = logger;

        var apiSpecPath = configuration["ApiSpec:Path"] ?? "ApiSpec.json";

        _logger.LogInformation("Loading ApiSpec from: {Path}", apiSpecPath);

        if (!File.Exists(apiSpecPath))
        {
            throw new FileNotFoundException(
                $"ApiSpec.json not found at: {apiSpecPath}. " +
                $"Current directory: {Directory.GetCurrentDirectory()}");
        }

        try
        {
            var json = File.ReadAllText(apiSpecPath);
            _spec = JsonConvert.DeserializeObject<ApiSpec>(json)
                ?? throw new InvalidOperationException("ApiSpec deserialized to null");

            // Validate
            var validationResult = ApiSpecValidator.Validate(_spec);
            if (!validationResult.IsValid)
            {
                var errorMsg = "ApiSpec validation failed:\n" +
                    string.Join("\n", validationResult.Errors.Select(e => $"  - {e}"));
                _logger.LogError("{ErrorMsg}", errorMsg);
                throw new InvalidOperationException(errorMsg);
            }

            // Build indexes for fast lookup
            _globalIndex = _spec.Globals.ToDictionary(
                g => g.Name,
                StringComparer.OrdinalIgnoreCase);

            _memberIndex = _spec.Globals
                .Where(g => g.Type == "object" && g.Members != null)
                .SelectMany(g => g.Members!.Select(m => new
                {
                    Key = $"{g.Name}.{m.Name}",
                    Member = m
                }))
                .ToDictionary(
                    x => x.Key,
                    x => x.Member,
                    StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation(
                "ApiSpec loaded: {GlobalCount} globals, {FunctionCount} functions, {MemberCount} members",
                _spec.Globals.Count,
                _spec.Globals.Count(g => g.Type == "function"),
                _memberIndex.Count);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse ApiSpec.json");
            throw new InvalidOperationException(
                "ApiSpec.json contains invalid JSON", ex);
        }
    }

    public ApiSpec GetSpec() => _spec;

    public GlobalEntry? GetGlobal(string name)
    {
        _globalIndex.TryGetValue(name, out var global);
        return global;
    }

    public FunctionEntry? GetFunction(string globalName, string memberName)
    {
        var key = $"{globalName}.{memberName}";
        _memberIndex.TryGetValue(key, out var member);
        return member;
    }

    public GlobalEntry? GetGlobalFunction(string name)
    {
        if (_globalIndex.TryGetValue(name, out var global) &&
            global.Type == "function")
        {
            return global;
        }
        return null;
    }

    public List<GlobalEntry> GetAllGlobals() => _spec.Globals;
}
```

**Create Test ApiSpec:** `Tests.Unit/TestData/test-apispec.json`
```json
{
  "globals": [
    {
      "name": "copy_file",
      "type": "function",
      "hover": "Copies a file from source to destination.\n\n**Example:**\n`copy_file(\"src.txt\", \"dest.txt\")`",
      "parameters": [
        {
          "name": "source",
          "type": "path",
          "picker": "file-picker"
        },
        {
          "name": "destination",
          "type": "path",
          "picker": "file-picker"
        }
      ]
    },
    {
      "name": "set_mode",
      "type": "function",
      "hover": "Sets the operational mode",
      "parameters": [
        {
          "name": "mode",
          "type": "constant",
          "picker": "enum-list",
          "options": ["MODE_FAST", "MODE_SLOW", "MODE_SAFE"]
        }
      ]
    },
    {
      "name": "os",
      "type": "object",
      "hover": "Operating system functions",
      "members": [
        {
          "name": "execute",
          "type": "function",
          "hover": "Executes a shell command",
          "parameters": [
            {
              "name": "command",
              "type": "string",
              "picker": "none"
            }
          ]
        }
      ]
    }
  ]
}
```

**Tests:** `Tests.Unit/ApiSpec/ApiSpecServiceTests.cs`
```csharp
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ScribanLanguageServer.Core.ApiSpec;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.ApiSpec;

public class ApiSpecServiceTests
{
    private readonly IConfiguration _configuration;

    public ApiSpecServiceTests()
    {
        var basePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "TestData");

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["ApiSpec:Path"] = Path.Combine(basePath, "test-apispec.json")
            }!)
            .Build();
    }

    [Fact]
    public void Constructor_ValidApiSpec_LoadsSuccessfully()
    {
        // Act
        var service = new ApiSpecService(_configuration, NullLogger<ApiSpecService>.Instance);

        // Assert
        service.Should().NotBeNull();
        var globals = service.GetAllGlobals();
        globals.Should().HaveCount(3);
    }

    [Fact]
    public void Constructor_MissingFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var badConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["ApiSpec:Path"] = "nonexistent.json"
            }!)
            .Build();

        // Act & Assert
        var act = () => new ApiSpecService(badConfig, NullLogger<ApiSpecService>.Instance);
        act.Should().Throw<FileNotFoundException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public void GetGlobal_ExistingName_ReturnsGlobal()
    {
        // Arrange
        var service = new ApiSpecService(_configuration, NullLogger<ApiSpecService>.Instance);

        // Act
        var result = service.GetGlobal("copy_file");

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("copy_file");
        result.Type.Should().Be("function");
    }

    [Fact]
    public void GetGlobal_CaseInsensitive_ReturnsGlobal()
    {
        // Arrange
        var service = new ApiSpecService(_configuration, NullLogger<ApiSpecService>.Instance);

        // Act
        var result = service.GetGlobal("COPY_FILE");

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("copy_file");
    }

    [Fact]
    public void GetGlobal_NonExistent_ReturnsNull()
    {
        // Arrange
        var service = new ApiSpecService(_configuration, NullLogger<ApiSpecService>.Instance);

        // Act
        var result = service.GetGlobal("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetFunction_ExistingMember_ReturnsFunction()
    {
        // Arrange
        var service = new ApiSpecService(_configuration, NullLogger<ApiSpecService>.Instance);

        // Act
        var result = service.GetFunction("os", "execute");

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("execute");
        result.Parameters.Should().HaveCount(1);
    }

    [Fact]
    public void GetGlobalFunction_ExistingFunction_ReturnsGlobal()
    {
        // Arrange
        var service = new ApiSpecService(_configuration, NullLogger<ApiSpecService>.Instance);

        // Act
        var result = service.GetGlobalFunction("copy_file");

        // Assert
        result.Should().NotBeNull();
        result!.Parameters.Should().HaveCount(2);
    }

    [Fact]
    public void GetGlobalFunction_ObjectType_ReturnsNull()
    {
        // Arrange
        var service = new ApiSpecService(_configuration, NullLogger<ApiSpecService>.Instance);

        // Act
        var result = service.GetGlobalFunction("os");

        // Assert
        result.Should().BeNull(); // os is an object, not a function
    }
}
```

**Success Criteria:**
- ✅ All tests pass: `dotnet test --filter "FullyQualifiedName~ApiSpecService"`
- ✅ Test coverage > 90%
- ✅ Service loads test ApiSpec successfully
- ✅ Throws clear errors for invalid/missing files

---

#### Task B1.4: Mock Infrastructure (Day 3)

**File:** `Tests.Mocks/MockApiSpecService.cs`
```csharp
using ScribanLanguageServer.Core.ApiSpec;

namespace ScribanLanguageServer.Tests.Mocks;

public class MockApiSpecService : IApiSpecService
{
    private readonly ApiSpec _spec;
    private readonly Dictionary<string, GlobalEntry> _index;

    public MockApiSpecService()
    {
        _spec = CreateDefaultSpec();
        _index = _spec.Globals.ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);
    }

    public MockApiSpecService(ApiSpec customSpec)
    {
        _spec = customSpec;
        _index = _spec.Globals.ToDictionary(g => g.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static ApiSpec CreateDefaultSpec()
    {
        return new ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new()
                {
                    Name = "test_func",
                    Type = "function",
                    Hover = "Test function",
                    Parameters = new List<ParameterEntry>
                    {
                        new() { Name = "param1", Type = "string", Picker = "none" }
                    }
                }
            }
        };
    }

    public ApiSpec GetSpec() => _spec;
    public GlobalEntry? GetGlobal(string name) => _index.GetValueOrDefault(name);
    public FunctionEntry? GetFunction(string globalName, string memberName) => null;
    public GlobalEntry? GetGlobalFunction(string name) => GetGlobal(name);
    public List<GlobalEntry> GetAllGlobals() => _spec.Globals;
}
```

**File:** `Tests.Mocks/MockFileSystem.cs`
```csharp
namespace ScribanLanguageServer.Tests.Mocks;

public interface IFileSystem
{
    bool FileExists(string path);
    string ReadAllText(string path);
    IEnumerable<string> EnumerateFiles(string path, string pattern);
}

public class MockFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new();
    private readonly Dictionary<string, List<string>> _directories = new();

    public void AddFile(string path, string content)
    {
        _files[path] = content;
    }

    public void AddDirectory(string path, List<string> files)
    {
        _directories[path] = files;
    }

    public bool FileExists(string path) => _files.ContainsKey(path);

    public string ReadAllText(string path)
    {
        if (!_files.TryGetValue(path, out var content))
        {
            throw new FileNotFoundException($"File not found: {path}");
        }
        return content;
    }

    public IEnumerable<string> EnumerateFiles(string path, string pattern)
    {
        if (!_directories.TryGetValue(path, out var files))
        {
            return Enumerable.Empty<string>();
        }
        return files;
    }
}
```

**Success Criteria:**
- ✅ Mock services compile and are usable
- ✅ Other test projects can reference Mocks project
- ✅ Mocks provide predictable, controllable behavior

---

### Stage B1: Acceptance Criteria

**Run all stage tests:**
```bash
cd Backend
dotnet test --filter "Stage=B1" --verbosity normal
```

**Expected Output:**
```
Test Run Successful.
Total tests: 12
     Passed: 12
     Failed: 0
    Skipped: 0
 Total time: 2.5s
```

**Verification Checklist:**
- [ ] All projects build without warnings
- [ ] ApiSpec validator has 100% test coverage
- [ ] ApiSpec service loads valid specs correctly
- [ ] ApiSpec service rejects invalid specs with clear errors
- [ ] Mock infrastructure is usable by other test projects
- [ ] Configuration loads from appsettings correctly
- [ ] Logging outputs at appropriate levels

**Deliverables:**
- ✅ Working project structure
- ✅ ApiSpec loading and validation (tested)
- ✅ Mock infrastructure for future stages
- ✅ Test data files
- ✅ Documentation in README.md

**Next Stage:** Can proceed to B2 (Core Services)

---

## STAGE B2: Backend Core Services

**Duration:** 2 weeks
**Developer:** Backend engineer
**Dependencies:** B1 complete
**Parallel Work:** F1, F2 can run in parallel

### Objectives
1. Implement ScribanParserService with AST caching
2. Implement FileSystemService with timeouts
3. Implement DocumentSessionService
4. Create comprehensive unit tests for all services

### Tasks

#### Task B2.1: Document Session Management (Day 1-2)

**File:** `Core/Services/IDocumentSessionService.cs`
```csharp
namespace ScribanLanguageServer.Core.Services;

public interface IDocumentSessionService
{
    void RegisterDocument(string connectionId, string documentUri);
    void UnregisterDocument(string connectionId, string documentUri);
    bool ValidateAccess(string connectionId, string documentUri);
    IEnumerable<string> GetDocumentsForConnection(string connectionId);
    void CleanupConnection(string connectionId);
    int GetTotalDocuments();
    int GetTotalConnections();
}
```

**File:** `Core/Services/DocumentSessionService.cs`
```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ScribanLanguageServer.Core.Services;

public class DocumentSessionService : IDocumentSessionService
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _connectionToDocuments = new();
    private readonly ConcurrentDictionary<string, string> _documentToConnection = new();
    private readonly ILogger<DocumentSessionService> _logger;
    private readonly object _lock = new();

    public DocumentSessionService(ILogger<DocumentSessionService> logger)
    {
        _logger = logger;
    }

    public void RegisterDocument(string connectionId, string documentUri)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
            throw new ArgumentNullException(nameof(connectionId));
        if (string.IsNullOrWhiteSpace(documentUri))
            throw new ArgumentNullException(nameof(documentUri));

        lock (_lock)
        {
            // Add to connection's document list
            if (!_connectionToDocuments.TryGetValue(connectionId, out var docs))
            {
                docs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _connectionToDocuments[connectionId] = docs;
            }
            docs.Add(documentUri);

            // Map document to connection
            _documentToConnection[documentUri] = connectionId;

            _logger.LogDebug(
                "Document registered: {Uri} -> Connection {ConnectionId}",
                documentUri, connectionId);
        }
    }

    public void UnregisterDocument(string connectionId, string documentUri)
    {
        lock (_lock)
        {
            if (_connectionToDocuments.TryGetValue(connectionId, out var docs))
            {
                docs.Remove(documentUri);
            }

            _documentToConnection.TryRemove(documentUri, out _);

            _logger.LogDebug(
                "Document unregistered: {Uri} from Connection {ConnectionId}",
                documentUri, connectionId);
        }
    }

    public bool ValidateAccess(string connectionId, string documentUri)
    {
        if (_documentToConnection.TryGetValue(documentUri, out var ownerConnectionId))
        {
            var hasAccess = ownerConnectionId.Equals(connectionId, StringComparison.Ordinal);

            if (!hasAccess)
            {
                _logger.LogWarning(
                    "Access denied: Connection {ConnectionId} attempted to access {Uri} (owned by {Owner})",
                    connectionId, documentUri, ownerConnectionId);
            }

            return hasAccess;
        }

        // Document not registered - allow access (will be registered on first use)
        return true;
    }

    public IEnumerable<string> GetDocumentsForConnection(string connectionId)
    {
        if (_connectionToDocuments.TryGetValue(connectionId, out var docs))
        {
            return docs.ToList();
        }
        return Enumerable.Empty<string>();
    }

    public void CleanupConnection(string connectionId)
    {
        lock (_lock)
        {
            if (_connectionToDocuments.TryRemove(connectionId, out var docs))
            {
                foreach (var doc in docs)
                {
                    _documentToConnection.TryRemove(doc, out _);
                }

                _logger.LogInformation(
                    "Connection cleanup: {ConnectionId} had {Count} documents",
                    connectionId, docs.Count);
            }
        }
    }

    public int GetTotalDocuments() => _documentToConnection.Count;
    public int GetTotalConnections() => _connectionToDocuments.Count;
}
```

**Tests:** `Tests.Unit/Services/DocumentSessionServiceTests.cs`
```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ScribanLanguageServer.Core.Services;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Services;

public class DocumentSessionServiceTests
{
    private readonly DocumentSessionService _service;

    public DocumentSessionServiceTests()
    {
        _service = new DocumentSessionService(
            NullLogger<DocumentSessionService>.Instance);
    }

    [Fact]
    public void RegisterDocument_NewDocument_Succeeds()
    {
        // Act
        _service.RegisterDocument("conn1", "file:///test.scriban");

        // Assert
        _service.GetTotalDocuments().Should().Be(1);
        _service.GetTotalConnections().Should().Be(1);
    }

    [Fact]
    public void RegisterDocument_SameConnectionMultipleDocs_Succeeds()
    {
        // Act
        _service.RegisterDocument("conn1", "file:///test1.scriban");
        _service.RegisterDocument("conn1", "file:///test2.scriban");

        // Assert
        var docs = _service.GetDocumentsForConnection("conn1");
        docs.Should().HaveCount(2);
    }

    [Fact]
    public void ValidateAccess_OwnerConnection_ReturnsTrue()
    {
        // Arrange
        _service.RegisterDocument("conn1", "file:///test.scriban");

        // Act
        var hasAccess = _service.ValidateAccess("conn1", "file:///test.scriban");

        // Assert
        hasAccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateAccess_DifferentConnection_ReturnsFalse()
    {
        // Arrange
        _service.RegisterDocument("conn1", "file:///test.scriban");

        // Act
        var hasAccess = _service.ValidateAccess("conn2", "file:///test.scriban");

        // Assert
        hasAccess.Should().BeFalse();
    }

    [Fact]
    public void ValidateAccess_UnregisteredDocument_ReturnsTrue()
    {
        // Act - unregistered documents allow access
        var hasAccess = _service.ValidateAccess("conn1", "file:///new.scriban");

        // Assert
        hasAccess.Should().BeTrue();
    }

    [Fact]
    public void CleanupConnection_RemovesAllDocuments()
    {
        // Arrange
        _service.RegisterDocument("conn1", "file:///test1.scriban");
        _service.RegisterDocument("conn1", "file:///test2.scriban");

        // Act
        _service.CleanupConnection("conn1");

        // Assert
        _service.GetTotalDocuments().Should().Be(0);
        _service.GetTotalConnections().Should().Be(0);
    }

    [Fact]
    public void UnregisterDocument_RemovesDocument_()
    {
        // Arrange
        _service.RegisterDocument("conn1", "file:///test.scriban");

        // Act
        _service.UnregisterDocument("conn1", "file:///test.scriban");

        // Assert
        _service.GetTotalDocuments().Should().Be(0);
    }

    [Fact]
    public void RegisterDocument_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            _service.RegisterDocument($"conn{i % 10}", $"file:///test{i}.scriban");
        })).ToArray();

        // Act
        Task.WaitAll(tasks);

        // Assert
        _service.GetTotalDocuments().Should().Be(100);
        _service.GetTotalConnections().Should().Be(10);
    }
}
```

**Success Criteria:**
- ✅ All tests pass
- ✅ Thread-safety test passes
- ✅ Service properly tracks document ownership

---

#### Task B2.2: Scriban Parser Service - Part 1: Core Parsing (Day 3-5)

**File:** `Core/Services/IScribanParserService.cs`
```csharp
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Scriban.Syntax;

namespace ScribanLanguageServer.Core.Services;

public interface IScribanParserService
{
    Task<ScriptPage?> ParseAsync(string code, CancellationToken cancellationToken = default);
    Task<List<Diagnostic>> GetDiagnosticsAsync(string documentUri, string code, int version, CancellationToken cancellationToken = default);
    ScriptNode? GetNodeAtPosition(ScriptPage ast, Position position);
    void InvalidateCache(string documentUri);
    CacheStatistics GetCacheStatistics();
}

public record CacheStatistics(
    int TotalEntries,
    int TotalHits,
    int TotalMisses,
    double HitRate);
```

**File:** `Core/Services/ScribanParserService.cs`
```csharp
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Scriban;
using Scriban.Parsing;
using Scriban.Syntax;
using ScribanLanguageServer.Core.ApiSpec;

namespace ScribanLanguageServer.Core.Services;

public class ScribanParserService : IScribanParserService, IDisposable
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
        _apiSpecService = apiSpecService;
        _logger = logger;

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

        return await Task.Run(() =>
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
                var semanticErrors = GetSemanticErrorsAsync(
                    template.Page, cancellationToken).Result;
                allDiagnostics.AddRange(semanticErrors);
            }

            return allDiagnostics;
        }, cancellationToken);
    }

    private async Task<List<Diagnostic>> GetSemanticErrorsAsync(
        ScriptPage ast,
        CancellationToken cancellationToken)
    {
        // Will implement in Task B2.3
        await Task.CompletedTask;
        return new List<Diagnostic>();
    }

    public ScriptNode? GetNodeAtPosition(ScriptPage ast, Position position)
    {
        // Will implement in Task B2.3
        return null;
    }

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
        var range = new Range
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
```

**Tests:** `Tests.Unit/Services/ScribanParserServiceTests.cs`
```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ScribanLanguageServer.Core.Services;
using ScribanLanguageServer.Tests.Mocks;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Services;

public class ScribanParserServiceTests
{
    private readonly ScribanParserService _service;

    public ScribanParserServiceTests()
    {
        _service = new ScribanParserService(
            new MockApiSpecService(),
            NullLogger<ScribanParserService>.Instance);
    }

    [Fact]
    public async Task ParseAsync_ValidCode_ReturnsAst()
    {
        // Arrange
        var code = "{{ x = 5 }}";

        // Act
        var result = await _service.ParseAsync(code);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ParseAsync_InvalidCode_ReturnsAstWithErrors()
    {
        // Arrange
        var code = "{{ for item in list }}";  // Missing 'end'

        // Act
        var result = await _service.ParseAsync(code);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDiagnosticsAsync_ValidCode_ReturnsNoDiagnostics()
    {
        // Arrange
        var code = "{{ x = 5 }}";

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDiagnosticsAsync_InvalidCode_ReturnsDiagnostics()
    {
        // Arrange
        var code = "{{ for item in list }}";  // Missing 'end'

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        diagnostics.Should().NotBeEmpty();
        diagnostics.Should().Contain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_SameVersionTwice_UsesCacheSecondTime()
    {
        // Arrange
        var code = "{{ x = 5 }}";
        var uri = "file:///test.scriban";

        // Act
        await _service.GetDiagnosticsAsync(uri, code, 1);
        var stats1 = _service.GetCacheStatistics();

        await _service.GetDiagnosticsAsync(uri, code, 1);
        var stats2 = _service.GetCacheStatistics();

        // Assert
        stats1.TotalMisses.Should().Be(1);
        stats1.TotalHits.Should().Be(0);

        stats2.TotalMisses.Should().Be(1); // Still 1
        stats2.TotalHits.Should().Be(1);   // Now 1
    }

    [Fact]
    public async Task GetDiagnosticsAsync_DifferentVersion_ReParses()
    {
        // Arrange
        var uri = "file:///test.scriban";

        // Act
        await _service.GetDiagnosticsAsync(uri, "{{ x = 5 }}", 1);
        await _service.GetDiagnosticsAsync(uri, "{{ x = 10 }}", 2);

        var stats = _service.GetCacheStatistics();

        // Assert
        stats.TotalMisses.Should().Be(2); // Both were cache misses
    }

    [Fact]
    public async Task InvalidateCache_RemovesCachedEntry()
    {
        // Arrange
        var uri = "file:///test.scriban";
        await _service.GetDiagnosticsAsync(uri, "{{ x = 5 }}", 1);

        // Act
        _service.InvalidateCache(uri);

        // Request again - should be cache miss
        await _service.GetDiagnosticsAsync(uri, "{{ x = 5 }}", 1);
        var stats = _service.GetCacheStatistics();

        // Assert
        stats.TotalMisses.Should().Be(2); // Original + after invalidation
        stats.TotalHits.Should().Be(0);
    }

    [Fact]
    public async Task GetCacheStatistics_CalculatesHitRateCorrectly()
    {
        // Arrange
        var uri1 = "file:///test1.scriban";
        var uri2 = "file:///test2.scriban";

        // Act
        await _service.GetDiagnosticsAsync(uri1, "{{ x = 5 }}", 1);  // Miss
        await _service.GetDiagnosticsAsync(uri1, "{{ x = 5 }}", 1);  // Hit
        await _service.GetDiagnosticsAsync(uri1, "{{ x = 5 }}", 1);  // Hit
        await _service.GetDiagnosticsAsync(uri2, "{{ y = 10 }}", 1); // Miss

        var stats = _service.GetCacheStatistics();

        // Assert
        stats.TotalHits.Should().Be(2);
        stats.TotalMisses.Should().Be(2);
        stats.HitRate.Should().BeApproximately(0.5, 0.01);
        stats.TotalEntries.Should().Be(2);
    }

    [Fact]
    public async Task ParseAsync_HugeDocument_TimesOut()
    {
        // Arrange - create a very large document
        var code = string.Concat(Enumerable.Repeat("{{ x = 1 }}\n", 100000));

        // Act & Assert
        var act = () => _service.ParseAsync(code);

        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*timed out*");
    }
}
```

**Success Criteria:**
- ✅ All tests pass (11 tests)
- ✅ Cache hit rate > 50% in repeated access test
- ✅ Timeout test completes in reasonable time
- ✅ No memory leaks in cache

---

*Due to length constraints, I'll continue with a summary of remaining stages. Would you like me to continue with the complete detailed implementation for stages B2.3-B2.4, B3, B4, and then the frontend stages F1-F4, followed by integration stages?*

The document structure would be:
- **Full detail** for each stage like above
- **Exact file contents** for implementation
- **Complete test suites** with assertions
- **Success criteria** for each task
- **Integration checkpoints** with smoke tests

Should I continue with the complete implementation plan?
