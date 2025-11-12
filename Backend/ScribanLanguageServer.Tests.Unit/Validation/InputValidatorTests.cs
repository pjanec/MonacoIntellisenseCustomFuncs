using FluentAssertions;
using ScribanLanguageServer.Core.Validation;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Validation;

[Trait("Stage", "B5")]
public class InputValidatorTests
{
    [Theory]
    [InlineData("file:///test.scriban")]
    [InlineData("untitled:Untitled-1")]
    [InlineData("inmemory://model/1")]
    public void ValidateDocumentUri_ValidUri_DoesNotThrow(string uri)
    {
        // Act
        var act = () => InputValidator.ValidateDocumentUri(uri);

        // Assert
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
        // Act
        var act = () => InputValidator.ValidateDocumentUri(uri);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateDocumentUri_TooLong_ThrowsArgumentException()
    {
        // Arrange
        var uri = "file:///" + new string('a', 3000);

        // Act
        var act = () => InputValidator.ValidateDocumentUri(uri);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*too long*");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 50)]
    [InlineData(99999, 9999)]
    public void ValidatePosition_ValidPosition_DoesNotThrow(int line, int character)
    {
        // Act
        var act = () => InputValidator.ValidatePosition(line, character);

        // Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(100001, 0)]
    [InlineData(0, 10001)]
    public void ValidatePosition_InvalidPosition_ThrowsArgumentException(int line, int character)
    {
        // Act
        var act = () => InputValidator.ValidatePosition(line, character);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateDocumentSize_EmptyDocument_DoesNotThrow()
    {
        // Act
        var act = () => InputValidator.ValidateDocumentSize(string.Empty);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateDocumentSize_NullDocument_DoesNotThrow()
    {
        // Act
        var act = () => InputValidator.ValidateDocumentSize(null!);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateDocumentSize_ValidSize_DoesNotThrow()
    {
        // Arrange
        var content = new string('a', 1000);

        // Act
        var act = () => InputValidator.ValidateDocumentSize(content);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateDocumentSize_TooLarge_ThrowsArgumentException()
    {
        // Arrange - 2MB document (exceeds 1MB limit)
        var content = new string('a', 2 * 1024 * 1024);

        // Act
        var act = () => InputValidator.ValidateDocumentSize(content);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*too large*");
    }

    [Fact]
    public void SanitizePath_PathTraversal_RemovesDangerousPatterns()
    {
        // Arrange
        var input = "../../../etc/passwd";

        // Act
        var result = InputValidator.SanitizePath(input);

        // Assert
        result.Should().NotContain("..");
        result.Should().Be("etc/passwd");
    }

    [Fact]
    public void SanitizePath_HomeDirectory_RemovesTilde()
    {
        // Arrange
        var input = "~/secrets/config";

        // Act
        var result = InputValidator.SanitizePath(input);

        // Assert
        result.Should().NotContain("~");
        result.Should().Be("secrets/config");
    }

    [Fact]
    public void SanitizePath_NormalizesSlashes()
    {
        // Arrange
        var input = @"folder\subfolder\file.txt";

        // Act
        var result = InputValidator.SanitizePath(input);

        // Assert
        result.Should().NotContain("\\");
        result.Should().Be("folder/subfolder/file.txt");
    }

    [Fact]
    public void SanitizePath_TrimsSlashes()
    {
        // Arrange
        var input = "/folder/file.txt/";

        // Act
        var result = InputValidator.SanitizePath(input);

        // Assert
        result.Should().Be("folder/file.txt");
    }

    [Fact]
    public void SanitizePath_NullOrWhitespace_ReturnsInput()
    {
        // Act & Assert
        InputValidator.SanitizePath(null!).Should().BeNull();
        InputValidator.SanitizePath("").Should().Be("");
        InputValidator.SanitizePath("   ").Should().Be("   ");
    }

    [Theory]
    [InlineData("valid_function")]
    [InlineData("_private")]
    [InlineData("func123")]
    [InlineData("MyFunction")]
    public void ValidateFunctionName_ValidName_DoesNotThrow(string name)
    {
        // Act
        var act = () => InputValidator.ValidateFunctionName(name);

        // Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123invalid")] // Starts with number
    [InlineData("invalid-name")] // Contains dash
    [InlineData("invalid.name")] // Contains dot
    [InlineData("invalid name")] // Contains space
    public void ValidateFunctionName_InvalidName_ThrowsArgumentException(string name)
    {
        // Act
        var act = () => InputValidator.ValidateFunctionName(name);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateFunctionName_TooLong_ThrowsArgumentException()
    {
        // Arrange
        var name = new string('a', 101);

        // Act
        var act = () => InputValidator.ValidateFunctionName(name);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*too long*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(20)]
    public void ValidateParameterIndex_ValidIndex_DoesNotThrow(int index)
    {
        // Act
        var act = () => InputValidator.ValidateParameterIndex(index);

        // Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    [InlineData(100)]
    public void ValidateParameterIndex_InvalidIndex_ThrowsArgumentException(int index)
    {
        // Act
        var act = () => InputValidator.ValidateParameterIndex(index);

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
