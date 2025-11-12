using FluentAssertions;
using ScribanLanguageServer.Core.ApiSpec;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.ApiSpec;

[Trait("Stage", "B1")]
public class ApiSpecValidatorTests
{
    [Fact]
    public void Validate_EmptyGlobals_ReturnsError()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec { Globals = new List<GlobalEntry>() };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("at least one global entry"));
    }

    [Fact]
    public void Validate_NullSpec_ReturnsError()
    {
        // Act
        var result = ApiSpecValidator.Validate(null!);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_DuplicateGlobalNames_ReturnsError()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new() { Name = "test", Type = "function", Hover = "Test" },
                new() { Name = "test", Type = "object", Hover = "Test", Members = new List<FunctionEntry> { new() { Name = "foo", Type = "function", Hover = "Test" } } }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Duplicate global names"));
    }

    [Fact]
    public void Validate_DuplicateGlobalNames_CaseInsensitive()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new() { Name = "Test", Type = "function", Hover = "Test", Parameters = new() },
                new() { Name = "test", Type = "function", Hover = "Test", Parameters = new() }
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
        var spec = new Core.ApiSpec.ApiSpec
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
    public void Validate_EnumListWithWrongType_ReturnsWarning()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
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
                            Type = "string", // Should be "constant"
                            Picker = "enum-list",
                            Options = new List<string> { "FAST", "SLOW" }
                        }
                    }
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeTrue(); // Changed: type mismatch is warning, not error
        result.Warnings.Should().Contain(w => w.Contains("typically uses type 'constant'"));
    }

    [Fact]
    public void Validate_FilePickerWithWrongType_ReturnsWarning()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new()
                {
                    Name = "copy_file",
                    Type = "function",
                    Hover = "Copies",
                    Parameters = new List<ParameterEntry>
                    {
                        new()
                        {
                            Name = "source",
                            Type = "string", // Should be "path"
                            Picker = "file-picker"
                        }
                    }
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeTrue(); // Changed: type mismatch is warning, not error
        result.Warnings.Should().Contain(w => w.Contains("typically uses type 'path'"));
    }

    [Fact]
    public void Validate_MacrosWithWrongType_ReturnsError()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new()
                {
                    Name = "log",
                    Type = "function",
                    Hover = "Logs",
                    Parameters = new List<ParameterEntry>
                    {
                        new()
                        {
                            Name = "message",
                            Type = "number", // Should be "string" for macros
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
                    Name = "os",
                    Type = "object",
                    Hover = "OS functions"
                    // Missing Members!
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("must have at least one member"));
    }

    [Fact]
    public void Validate_ObjectWithDuplicateMembers_ReturnsError()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new()
                {
                    Name = "os",
                    Type = "object",
                    Hover = "OS functions",
                    Members = new List<FunctionEntry>
                    {
                        new() { Name = "execute", Type = "function", Hover = "Test", Parameters = new() },
                        new() { Name = "execute", Type = "function", Hover = "Test", Parameters = new() }
                    }
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Duplicate member names"));
    }

    [Fact]
    public void Validate_ValidSpec_ReturnsSuccess()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
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
    [InlineData("file-picker", "path", true, false)]
    [InlineData("file-picker", "string", true, true)] // Changed: returns warning, not error
    [InlineData("enum-list", "constant", true, false)]
    [InlineData("enum-list", "string", true, true)] // Changed: returns warning, not error
    [InlineData("none", "string", true, false)]
    [InlineData("none", "number", true, false)]
    public void Validate_PickerTypeCombination_ValidatesCorrectly(
        string picker,
        string type,
        bool shouldBeValid,
        bool shouldHaveWarnings)
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

        var spec = new Core.ApiSpec.ApiSpec
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
        result.IsValid.Should().Be(shouldBeValid);
        if (shouldHaveWarnings)
        {
            result.Warnings.Should().NotBeEmpty();
        }
    }
}

// ============================================================================
// Stage B1.5 Enhancement Tests
// ============================================================================

[Trait("Stage", "B1.5")]
public class ApiSpecValidatorEnhancementTests
{
    [Fact]
    public void Validate_ReservedKeywordAsGlobalName_ReturnsError()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new() { Name = "for", Type = "function", Hover = "Test", Parameters = new() },
                new() { Name = "if", Type = "function", Hover = "Test", Parameters = new() },
                new() { Name = "while", Type = "function", Hover = "Test", Parameters = new() }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Reserved Scriban keywords"));
        result.Errors.Should().Contain(e => e.Contains("for") && e.Contains("if") && e.Contains("while"));
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
                    Hover = "", // Empty hover
                    Parameters = new()
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.Warnings.Should().Contain(w => w.Contains("Hover documentation is empty"));
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
                    Hover = "Test",
                    Parameters = new List<ParameterEntry>
                    {
                        new() { Name = "param1", Type = "invalid_type", Picker = "none" }
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
                    Hover = "Test",
                    Parameters = new List<ParameterEntry>
                    {
                        new() { Name = "param1", Type = "string", Picker = "invalid-picker" }
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
    public void Validate_EnumListWithWrongType_ReturnsWarning()
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
                    Hover = "Test",
                    Parameters = new List<ParameterEntry>
                    {
                        new()
                        {
                            Name = "mode",
                            Type = "string",  // Should be constant
                            Picker = "enum-list",
                            Options = new List<string> { "A", "B" }
                        }
                    }
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.Warnings.Should().Contain(w => w.Contains("typically uses type 'constant'"));
    }

    [Fact]
    public void Validate_FilePickerWithWrongType_ReturnsWarning()
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
                    Hover = "Test",
                    Parameters = new List<ParameterEntry>
                    {
                        new()
                        {
                            Name = "file",
                            Type = "string",  // Should be path
                            Picker = "file-picker"
                        }
                    }
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.Warnings.Should().Contain(w => w.Contains("typically uses type 'path'"));
    }

    [Fact]
    public void Validate_OptionsWithoutEnumList_ReturnsWarning()
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
                    Hover = "Test",
                    Parameters = new List<ParameterEntry>
                    {
                        new()
                        {
                            Name = "param1",
                            Type = "string",
                            Picker = "none", // Not enum-list
                            Options = new List<string> { "A", "B" } // But has options
                        }
                    }
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.Warnings.Should().Contain(w => w.Contains("Options defined but picker is 'none'"));
    }

    [Fact]
    public void Validate_DuplicateParameterNames_ReturnsError()
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
                    Hover = "Test",
                    Parameters = new List<ParameterEntry>
                    {
                        new() { Name = "param1", Type = "string", Picker = "none" },
                        new() { Name = "param1", Type = "number", Picker = "none" } // Duplicate
                    }
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Duplicate parameter names"));
    }

    [Fact]
    public void Validate_InvalidGlobalType_ReturnsError()
    {
        // Arrange
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new()
                {
                    Name = "test",
                    Type = "invalid_type", // Should be "object" or "function"
                    Hover = "Test"
                }
            }
        };

        // Act
        var result = ApiSpecValidator.Validate(spec);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("must be 'object' or 'function'"));
    }

    [Fact]
    public void Validate_AllValidTypes_Succeeds()
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
                    Hover = "Test all types",
                    Parameters = new List<ParameterEntry>
                    {
                        new() { Name = "path_param", Type = "path", Picker = "none" },
                        new() { Name = "const_param", Type = "constant", Picker = "none" },
                        new() { Name = "string_param", Type = "string", Picker = "none" },
                        new() { Name = "number_param", Type = "number", Picker = "none" },
                        new() { Name = "boolean_param", Type = "boolean", Picker = "none" },
                        new() { Name = "any_param", Type = "any", Picker = "none" }
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
}
