using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ScribanLanguageServer.Core.ApiSpec;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.ApiSpec;

[Trait("Stage", "B1")]
public class ApiSpecServiceTests : IDisposable
{
    private readonly Mock<ILogger<ApiSpecService>> _loggerMock;
    private readonly ApiSpecService _service;
    private readonly string _testFilePath;
    private readonly string _invalidJsonFilePath;
    private readonly string _invalidSpecFilePath;

    public ApiSpecServiceTests()
    {
        _loggerMock = new Mock<ILogger<ApiSpecService>>();
        _service = new ApiSpecService(_loggerMock.Object);

        // Create temp test files
        _testFilePath = Path.GetTempFileName();
        _invalidJsonFilePath = Path.GetTempFileName();
        _invalidSpecFilePath = Path.GetTempFileName();

        // Write valid test spec
        File.WriteAllText(_testFilePath, @"{
            ""globals"": [
                {
                    ""name"": ""copy_file"",
                    ""type"": ""function"",
                    ""hover"": ""Copies a file"",
                    ""parameters"": [
                        { ""name"": ""source"", ""type"": ""path"", ""picker"": ""file-picker"" },
                        { ""name"": ""dest"", ""type"": ""path"", ""picker"": ""file-picker"" }
                    ]
                },
                {
                    ""name"": ""os"",
                    ""type"": ""object"",
                    ""hover"": ""OS functions"",
                    ""members"": [
                        {
                            ""name"": ""execute"",
                            ""type"": ""function"",
                            ""hover"": ""Executes command"",
                            ""parameters"": [
                                { ""name"": ""cmd"", ""type"": ""string"", ""picker"": ""none"" }
                            ]
                        }
                    ]
                }
            ]
        }");

        // Write invalid JSON
        File.WriteAllText(_invalidJsonFilePath, "{ invalid json ][");

        // Write spec that fails validation (empty globals)
        File.WriteAllText(_invalidSpecFilePath, @"{
            ""globals"": []
        }");
    }

    public void Dispose()
    {
        // Clean up temp files
        if (File.Exists(_testFilePath)) File.Delete(_testFilePath);
        if (File.Exists(_invalidJsonFilePath)) File.Delete(_invalidJsonFilePath);
        if (File.Exists(_invalidSpecFilePath)) File.Delete(_invalidSpecFilePath);
    }

    [Fact]
    public async Task LoadAsync_ValidFile_ReturnsSuccess()
    {
        // Act
        var result = await _service.LoadAsync(_testFilePath);

        // Assert
        result.Success.Should().BeTrue();
        result.ErrorMessage.Should().BeNullOrEmpty();
        result.ValidationErrors.Should().BeEmpty();
        _service.IsLoaded.Should().BeTrue();
        _service.CurrentSpec.Should().NotBeNull();
        _service.CurrentSpec!.Globals.Should().HaveCount(2);
    }

    [Fact]
    public async Task LoadAsync_EmptyFilePath_ReturnsFailure()
    {
        // Act
        var result = await _service.LoadAsync("");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("cannot be empty");
        _service.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_NonExistentFile_ReturnsFailure()
    {
        // Act
        var result = await _service.LoadAsync("non-existent-file.json");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
        _service.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_InvalidJson_ReturnsFailure()
    {
        // Act
        var result = await _service.LoadAsync(_invalidJsonFilePath);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("JSON parsing error");
        _service.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_InvalidSpec_ReturnsFailureWithValidationErrors()
    {
        // Act
        var result = await _service.LoadAsync(_invalidSpecFilePath);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Validation failed");
        result.ValidationErrors.Should().NotBeEmpty();
        result.ValidationErrors.Should().Contain(e => e.Contains("at least one global entry"));
        _service.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task ReloadAsync_AfterSuccessfulLoad_ReturnsSuccess()
    {
        // Arrange
        await _service.LoadAsync(_testFilePath);

        // Act
        var result = await _service.ReloadAsync();

        // Assert
        result.Success.Should().BeTrue();
        _service.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task ReloadAsync_WithoutPriorLoad_ReturnsFailure()
    {
        // Act
        var result = await _service.ReloadAsync();

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No file path to reload");
    }

    [Fact]
    public async Task GetGlobal_ExistingFunction_ReturnsEntry()
    {
        // Arrange
        await _service.LoadAsync(_testFilePath);

        // Act
        var global = _service.GetGlobal("copy_file");

        // Assert
        global.Should().NotBeNull();
        global!.Name.Should().Be("copy_file");
        global.Type.Should().Be("function");
    }

    [Fact]
    public async Task GetGlobal_CaseInsensitive_ReturnsEntry()
    {
        // Arrange
        await _service.LoadAsync(_testFilePath);

        // Act
        var global = _service.GetGlobal("COPY_FILE");

        // Assert
        global.Should().NotBeNull();
        global!.Name.Should().Be("copy_file");
    }

    [Fact]
    public async Task GetGlobal_NonExistent_ReturnsNull()
    {
        // Arrange
        await _service.LoadAsync(_testFilePath);

        // Act
        var global = _service.GetGlobal("non_existent");

        // Assert
        global.Should().BeNull();
    }

    [Fact]
    public void GetGlobal_BeforeLoad_ReturnsNull()
    {
        // Act
        var global = _service.GetGlobal("copy_file");

        // Assert
        global.Should().BeNull();
    }

    [Fact]
    public async Task GetObjectMember_ExistingMember_ReturnsEntry()
    {
        // Arrange
        await _service.LoadAsync(_testFilePath);

        // Act
        var member = _service.GetObjectMember("os", "execute");

        // Assert
        member.Should().NotBeNull();
        member!.Name.Should().Be("execute");
        member.Type.Should().Be("function");
    }

    [Fact]
    public async Task GetObjectMember_CaseInsensitive_ReturnsEntry()
    {
        // Arrange
        await _service.LoadAsync(_testFilePath);

        // Act
        var member = _service.GetObjectMember("OS", "EXECUTE");

        // Assert
        member.Should().NotBeNull();
        member!.Name.Should().Be("execute");
    }

    [Fact]
    public async Task GetObjectMember_NonExistentObject_ReturnsNull()
    {
        // Arrange
        await _service.LoadAsync(_testFilePath);

        // Act
        var member = _service.GetObjectMember("non_existent", "execute");

        // Assert
        member.Should().BeNull();
    }

    [Fact]
    public async Task GetObjectMember_NonExistentMember_ReturnsNull()
    {
        // Arrange
        await _service.LoadAsync(_testFilePath);

        // Act
        var member = _service.GetObjectMember("os", "non_existent");

        // Assert
        member.Should().BeNull();
    }

    [Fact]
    public async Task GetGlobalFunctionNames_ReturnsAllFunctions()
    {
        // Arrange
        await _service.LoadAsync(_testFilePath);

        // Act
        var names = _service.GetGlobalFunctionNames();

        // Assert
        names.Should().HaveCount(1);
        names.Should().Contain("copy_file");
        names.Should().NotContain("os"); // os is an object, not a function
    }

    [Fact]
    public async Task GetGlobalObjectNames_ReturnsAllObjects()
    {
        // Arrange
        await _service.LoadAsync(_testFilePath);

        // Act
        var names = _service.GetGlobalObjectNames();

        // Assert
        names.Should().HaveCount(1);
        names.Should().Contain("os");
        names.Should().NotContain("copy_file"); // copy_file is a function, not an object
    }

    [Fact]
    public void GetGlobalFunctionNames_BeforeLoad_ReturnsEmptyList()
    {
        // Act
        var names = _service.GetGlobalFunctionNames();

        // Assert
        names.Should().BeEmpty();
    }

    [Fact]
    public void GetGlobalObjectNames_BeforeLoad_ReturnsEmptyList()
    {
        // Act
        var names = _service.GetGlobalObjectNames();

        // Assert
        names.Should().BeEmpty();
    }

    [Fact]
    public void IsLoaded_BeforeLoad_ReturnsFalse()
    {
        // Assert
        _service.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task IsLoaded_AfterSuccessfulLoad_ReturnsTrue()
    {
        // Arrange
        await _service.LoadAsync(_testFilePath);

        // Assert
        _service.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task IsLoaded_AfterFailedLoad_ReturnsFalse()
    {
        // Arrange
        await _service.LoadAsync("non-existent.json");

        // Assert
        _service.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public void CurrentSpec_BeforeLoad_ReturnsNull()
    {
        // Assert
        _service.CurrentSpec.Should().BeNull();
    }

    [Fact]
    public async Task CurrentSpec_AfterSuccessfulLoad_ReturnsSpec()
    {
        // Arrange
        await _service.LoadAsync(_testFilePath);

        // Assert
        _service.CurrentSpec.Should().NotBeNull();
        _service.CurrentSpec!.Globals.Should().HaveCount(2);
    }

    [Fact]
    public async Task LoadAsync_MultipleTimes_ReplacesSpec()
    {
        // Arrange
        await _service.LoadAsync(_testFilePath);
        var firstSpec = _service.CurrentSpec;

        // Act - Load again
        await _service.LoadAsync(_testFilePath);
        var secondSpec = _service.CurrentSpec;

        // Assert
        firstSpec.Should().NotBeNull();
        secondSpec.Should().NotBeNull();
        // Both should have same structure but be different instances
        secondSpec!.Globals.Should().HaveCount(2);
    }
}
