using FluentAssertions;
using ScribanLanguageServer.Core.ApiSpec;
using ScribanLanguageServer.Tests.Mocks;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Mocks;

[Trait("Stage", "B1")]
public class MockApiSpecServiceTests
{
    [Fact]
    public void CreateWithDefaultSpec_SetsUpMockWithData()
    {
        // Act
        var mock = MockApiSpecService.CreateWithDefaultSpec();

        // Assert
        mock.IsLoaded.Should().BeTrue();
        mock.CurrentSpec.Should().NotBeNull();
        mock.CurrentSpec!.Globals.Should().NotBeEmpty();
    }

    [Fact]
    public void SetSpec_UpdatesCurrentSpec()
    {
        // Arrange
        var mock = new MockApiSpecService();
        var spec = new Core.ApiSpec.ApiSpec
        {
            Globals = new List<GlobalEntry>
            {
                new() { Name = "test", Type = "function", Hover = "Test" }
            }
        };

        // Act
        mock.SetSpec(spec);

        // Assert
        mock.CurrentSpec.Should().Be(spec);
        mock.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public void Clear_RemovesSpec()
    {
        // Arrange
        var mock = MockApiSpecService.CreateWithDefaultSpec();

        // Act
        mock.Clear();

        // Assert
        mock.CurrentSpec.Should().BeNull();
        mock.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_SimulatesSuccessfulLoad()
    {
        // Arrange
        var mock = new MockApiSpecService();

        // Act
        var result = await mock.LoadAsync("test.json");

        // Assert
        result.Success.Should().BeTrue();
        mock.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_EmptyPath_ReturnsFailure()
    {
        // Arrange
        var mock = new MockApiSpecService();

        // Act
        var result = await mock.LoadAsync("");

        // Assert
        result.Success.Should().BeFalse();
        mock.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public void GetGlobal_ReturnsExpectedEntry()
    {
        // Arrange
        var mock = MockApiSpecService.CreateWithDefaultSpec();

        // Act
        var global = mock.GetGlobal("copy_file");

        // Assert
        global.Should().NotBeNull();
        global!.Name.Should().Be("copy_file");
        global.Type.Should().Be("function");
    }

    [Fact]
    public void GetObjectMember_ReturnsExpectedEntry()
    {
        // Arrange
        var mock = MockApiSpecService.CreateWithDefaultSpec();

        // Act
        var member = mock.GetObjectMember("os", "execute");

        // Assert
        member.Should().NotBeNull();
        member!.Name.Should().Be("execute");
    }

    [Fact]
    public void GetGlobalFunctionNames_ReturnsAllFunctions()
    {
        // Arrange
        var mock = MockApiSpecService.CreateWithDefaultSpec();

        // Act
        var names = mock.GetGlobalFunctionNames();

        // Assert
        names.Should().Contain("copy_file");
        names.Should().Contain("set_mode");
        names.Should().NotContain("os"); // os is an object
    }

    [Fact]
    public void GetGlobalObjectNames_ReturnsAllObjects()
    {
        // Arrange
        var mock = MockApiSpecService.CreateWithDefaultSpec();

        // Act
        var names = mock.GetGlobalObjectNames();

        // Assert
        names.Should().Contain("os");
        names.Should().NotContain("copy_file"); // copy_file is a function
    }
}
