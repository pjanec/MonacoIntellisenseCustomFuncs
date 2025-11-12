using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ScribanLanguageServer.Core.Services;
using ScribanLanguageServer.Server.Handlers;
using ScribanLanguageServer.Tests.Mocks;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Handlers;

[Trait("Stage", "B3")]
public class HoverHandlerTests
{
    private readonly HoverHandler _handler;
    private readonly MockApiSpecService _apiSpec;
    private readonly Mock<IScribanParserService> _parser;

    public HoverHandlerTests()
    {
        _apiSpec = new MockApiSpecService();
        _parser = new Mock<IScribanParserService>();

        _handler = new HoverHandler(
            _apiSpec,
            _parser.Object,
            NullLogger<HoverHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsNull()
    {
        // Arrange
        var request = new HoverParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = DocumentUri.From("file:///test.scriban")
            },
            Position = new Position(0, 5)
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert - for now, null is expected until document storage is implemented
        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ValidRequest_DoesNotThrow()
    {
        // Arrange
        var request = new HoverParams
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
        var act = () => new HoverHandler(
            null!,
            _parser.Object,
            NullLogger<HoverHandler>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("apiSpec");
    }

    [Fact]
    public void Constructor_NullParser_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new HoverHandler(
            _apiSpec,
            null!,
            NullLogger<HoverHandler>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("parser");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new HoverHandler(
            _apiSpec,
            _parser.Object,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public async Task Handle_CancellationRequested_ReturnsSafely()
    {
        // Arrange
        var request = new HoverParams
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
        result.Should().BeNull();
    }
}
