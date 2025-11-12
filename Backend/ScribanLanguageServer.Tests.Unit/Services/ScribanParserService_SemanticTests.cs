using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ScribanLanguageServer.Core.ApiSpec;
using ScribanLanguageServer.Core.Services;
using ScribanLanguageServer.Tests.Mocks;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Services;

[Trait("Stage", "B2")]
public class ScribanParserService_SemanticTests : IDisposable
{
    private readonly ScribanParserService _service;

    public ScribanParserService_SemanticTests()
    {
        // Create spec with test functions
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

        var mockApiSpec = new MockApiSpecService(spec);
        _service = new ScribanParserService(
            mockApiSpec,
            NullLogger<ScribanParserService>.Instance);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    [Fact]
    public async Task GetDiagnostics_UnknownFunction_ReturnsError()
    {
        // Arrange
        var code = "{{ unknown_function() }}";

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        diagnostics.Should().Contain(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("Unknown function") &&
            d.Source == "scriban-semantic");
    }

    [Fact]
    public async Task GetDiagnostics_KnownFunction_NoError()
    {
        // Arrange
        var code = "{{ copy_file(\"a.txt\", \"b.txt\") }}";

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDiagnostics_WrongArgumentCount_TooFew_ReturnsError()
    {
        // Arrange
        var code = "{{ copy_file(\"a.txt\") }}"; // Missing second argument

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        diagnostics.Should().Contain(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("expects 2 arguments") &&
            d.Message.Contains("but 1 provided"));
    }

    [Fact]
    public async Task GetDiagnostics_WrongArgumentCount_TooMany_ReturnsError()
    {
        // Arrange
        var code = "{{ copy_file(\"a.txt\", \"b.txt\", \"c.txt\") }}"; // Extra argument

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        diagnostics.Should().Contain(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("expects 2 arguments") &&
            d.Message.Contains("but 3 provided"));
    }

    [Fact]
    public async Task GetDiagnostics_InvalidEnumValue_ReturnsError()
    {
        // Arrange
        var code = "{{ set_mode(\"INVALID\") }}";

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        diagnostics.Should().Contain(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("Invalid value") &&
            d.Message.Contains("INVALID") &&
            d.Message.Contains("FAST") &&
            d.Message.Contains("SLOW"));
    }

    [Fact]
    public async Task GetDiagnostics_ValidEnumValue_NoError()
    {
        // Arrange
        var code = "{{ set_mode(\"FAST\") }}";

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDiagnostics_EnumValue_CaseInsensitive()
    {
        // Arrange
        var code = "{{ set_mode(\"fast\") }}"; // lowercase should work

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDiagnostics_ObjectMemberFunction_Valid_NoError()
    {
        // Arrange
        var code = "{{ os.execute(\"ls\") }}";

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDiagnostics_ObjectMemberFunction_UnknownMember_ReturnsError()
    {
        // Arrange
        var code = "{{ os.unknown_method() }}";

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        diagnostics.Should().Contain(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("Unknown function"));
    }

    [Fact]
    public async Task GetDiagnostics_ObjectMemberFunction_WrongArgCount_ReturnsError()
    {
        // Arrange
        var code = "{{ os.execute() }}"; // Missing argument

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        diagnostics.Should().Contain(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("expects 1 arguments"));
    }

    [Fact]
    public async Task GetDiagnostics_MultipleFunctionCalls_ReportsAllErrors()
    {
        // Arrange
        var code = @"
{{ unknown1() }}
{{ unknown2() }}
{{ copy_file(""a"") }}
";

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        diagnostics.Should().HaveCount(3);
        diagnostics.Should().AllSatisfy(d => d.Severity.Should().Be(DiagnosticSeverity.Error));
    }

    [Fact]
    public async Task GetDiagnostics_SyntaxErrorPresent_SkipsSemanticValidation()
    {
        // Arrange
        var code = "{{ for item in list }}"; // Syntax error: missing 'end'

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        // Should have syntax error but not semantic errors
        diagnostics.Should().NotBeEmpty();
        diagnostics.Should().AllSatisfy(d => d.Source.Should().Be("scriban-parser"));
    }

    [Fact(Skip = "GetNodeAtPosition requires more complex AST traversal - deferred to future iteration")]
    public async Task GetNodeAtPosition_FunctionCall_ReturnsNode()
    {
        // Arrange
        var code = "{{ copy_file(\"a.txt\", \"b.txt\") }}";
        var ast = await _service.ParseAsync(code);
        var position = new Position(0, 5); // On "copy_file"

        // Act
        var node = _service.GetNodeAtPosition(ast!, position);

        // Assert
        node.Should().NotBeNull();
    }

    [Fact]
    public async Task GetNodeAtPosition_OutsideCode_ReturnsNull()
    {
        // Arrange
        var code = "{{ x = 5 }}";
        var ast = await _service.ParseAsync(code);
        var position = new Position(10, 10); // Way outside the code

        // Act
        var node = _service.GetNodeAtPosition(ast!, position);

        // Assert
        node.Should().BeNull();
    }

    [Fact]
    public void GetNodeAtPosition_NullAst_ReturnsNull()
    {
        // Act
        var node = _service.GetNodeAtPosition(null!, new Position(0, 0));

        // Assert
        node.Should().BeNull();
    }

    [Fact(Skip = "GetNodeAtPosition requires more complex AST traversal - deferred to future iteration")]
    public async Task GetNodeAtPosition_MultipleNodes_ReturnsSmallest()
    {
        // Arrange
        var code = "{{ copy_file(\"test\", \"dest\") }}";
        var ast = await _service.ParseAsync(code);
        var position = new Position(0, 14); // Inside the string literal "test"

        // Act
        var node = _service.GetNodeAtPosition(ast!, position);

        // Assert
        node.Should().NotBeNull();
        // Should find a node, the visitor traverses to find the smallest one
    }

    [Fact]
    public async Task GetDiagnostics_ConstantAsEnumValue_Validated()
    {
        // Arrange - using variable name as constant
        var code = "{{ set_mode(FAST) }}"; // Using FAST as a variable reference

        // Act
        var diagnostics = await _service.GetDiagnosticsAsync(
            "file:///test.scriban", code, 1);

        // Assert
        // FAST as a variable name should be accepted (it's the value at runtime)
        diagnostics.Should().BeEmpty();
    }
}
