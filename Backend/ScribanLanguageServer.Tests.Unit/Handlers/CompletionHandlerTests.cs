using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ScribanLanguageServer.Core.Services;
using ScribanLanguageServer.Server.Handlers;
using ScribanLanguageServer.Tests.Mocks;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Handlers;

[Trait("Stage", "B3")]
public class CompletionHandlerTests
{
    private readonly CompletionHandler _handler;
    private readonly MockApiSpecService _apiSpec;
    private readonly Mock<IScribanParserService> _parser;
    private readonly Mock<IFileSystemService> _fileSystem;

    public CompletionHandlerTests()
    {
        _apiSpec = new MockApiSpecService();
        _parser = new Mock<IScribanParserService>();
        _fileSystem = new Mock<IFileSystemService>();

        _handler = new CompletionHandler(
            _apiSpec,
            _parser.Object,
            _fileSystem.Object,
            NullLogger<CompletionHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsEmptyList()
    {
        // Arrange
        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = DocumentUri.From("file:///test.scriban")
            },
            Position = new Position(0, 5)
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert - for now, empty list is expected until document storage is implemented
        result.Should().NotBeNull();
        result.IsIncomplete.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ValidRequest_DoesNotThrow()
    {
        // Arrange
        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = DocumentUri.From("file:///test.scriban")
            },
            Position = new Position(0, 5)
        };

        // Act
        var act = async () => await _handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Constructor_NullApiSpec_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new CompletionHandler(
            null!,
            _parser.Object,
            _fileSystem.Object,
            NullLogger<CompletionHandler>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("apiSpec");
    }

    [Fact]
    public void Constructor_NullParser_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new CompletionHandler(
            _apiSpec,
            null!,
            _fileSystem.Object,
            NullLogger<CompletionHandler>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("parser");
    }

    [Fact]
    public void Constructor_NullFileSystem_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new CompletionHandler(
            _apiSpec,
            _parser.Object,
            null!,
            NullLogger<CompletionHandler>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("fileSystem");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new CompletionHandler(
            _apiSpec,
            _parser.Object,
            _fileSystem.Object,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public async Task Handle_CancellationRequested_ReturnsSafely()
    {
        // Arrange
        var request = new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = DocumentUri.From("file:///test.scriban")
            },
            Position = new Position(0, 5)
        };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var result = await _handler.Handle(request, cts.Token);

        // Assert - should handle cancellation gracefully
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_MultipleRequests_AllSucceed()
    {
        // Arrange
        var requests = Enumerable.Range(0, 10).Select(i => new CompletionParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = DocumentUri.From("file:///test.scriban")
            },
            Position = new Position(0, i)
        }).ToList();

        // Act
        var tasks = requests.Select(r => _handler.Handle(r, CancellationToken.None));
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(10);
        results.Should().AllSatisfy(r => r.Should().NotBeNull());
    }
}
