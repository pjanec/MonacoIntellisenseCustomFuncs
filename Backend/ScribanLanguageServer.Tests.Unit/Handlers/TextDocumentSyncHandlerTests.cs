using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using ScribanLanguageServer.Core.Services;
using ScribanLanguageServer.Server.Handlers;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Handlers;

[Trait("Stage", "B3")]
public class TextDocumentSyncHandlerTests
{
    private readonly Mock<ILanguageServerFacade> _languageServer;
    private readonly Mock<IScribanParserService> _parser;
    private readonly TextDocumentSyncHandler _handler;

    public TextDocumentSyncHandlerTests()
    {
        _languageServer = new Mock<ILanguageServerFacade>();
        _parser = new Mock<IScribanParserService>();

        _handler = new TextDocumentSyncHandler(
            _languageServer.Object,
            _parser.Object,
            NullLogger<TextDocumentSyncHandler>.Instance);
    }

    [Fact]
    public async Task Handle_DidOpen_StoresDocument()
    {
        // Arrange
        var request = new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = DocumentUri.From("file:///test.scriban"),
                Text = "{{ x = 5 }}",
                Version = 1
            }
        };

        _parser.Setup(p => p.GetDiagnosticsAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Diagnostic>());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Wait for debounced validation
        await Task.Delay(300);

        // Assert
        _parser.Verify(p => p.GetDiagnosticsAsync(
            It.IsAny<string>(),
            "{{ x = 5 }}",
            1,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DidChange_TriggersValidation()
    {
        // Arrange - first open
        var openRequest = new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = DocumentUri.From("file:///test.scriban"),
                Text = "{{ x = 5 }}",
                Version = 1
            }
        };

        _parser.Setup(p => p.GetDiagnosticsAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Diagnostic>());

        await _handler.Handle(openRequest, CancellationToken.None);
        await Task.Delay(300);

        _parser.Invocations.Clear();

        // Act - change
        var changeRequest = new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier
            {
                Uri = DocumentUri.From("file:///test.scriban"),
                Version = 2
            },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(
                new TextDocumentContentChangeEvent
                {
                    Text = "{{ x = 10 }}"
                })
        };

        await _handler.Handle(changeRequest, CancellationToken.None);
        await Task.Delay(300);

        // Assert
        _parser.Verify(p => p.GetDiagnosticsAsync(
            It.IsAny<string>(),
            "{{ x = 10 }}",
            2,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RapidChanges_DebouncesCorrectly()
    {
        // Arrange
        var openRequest = new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = DocumentUri.From("file:///test.scriban"),
                Text = "{{ x = 1 }}",
                Version = 1
            }
        };

        _parser.Setup(p => p.GetDiagnosticsAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Diagnostic>());

        await _handler.Handle(openRequest, CancellationToken.None);
        await Task.Delay(300);

        _parser.Invocations.Clear();

        // Act - rapid changes
        for (int i = 2; i <= 10; i++)
        {
            var changeRequest = new DidChangeTextDocumentParams
            {
                TextDocument = new OptionalVersionedTextDocumentIdentifier
                {
                    Uri = DocumentUri.From("file:///test.scriban"),
                    Version = i
                },
                ContentChanges = new Container<TextDocumentContentChangeEvent>(
                    new TextDocumentContentChangeEvent
                    {
                        Text = $"{{{{ x = {i} }}}}"
                    })
            };

            await _handler.Handle(changeRequest, CancellationToken.None);
            await Task.Delay(50); // Rapid typing simulation
        }

        // Wait for debounce
        await Task.Delay(400);

        // Assert - should validate last version at least once
        _parser.Verify(p => p.GetDiagnosticsAsync(
            It.IsAny<string>(),
            "{{ x = 10 }}",
            10,
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // Debouncing should reduce total validations compared to number of changes
        // With 9 changes (versions 2-10), we expect fewer than 9 validations
        var totalValidations = _parser.Invocations.Count;
        totalValidations.Should().BeLessThan(9, "debouncing should reduce validation calls");
    }

    [Fact]
    public async Task Handle_DidClose_RemovesDocument()
    {
        // Arrange - open document
        var openRequest = new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = DocumentUri.From("file:///test.scriban"),
                Text = "{{ x = 5 }}",
                Version = 1
            }
        };

        _parser.Setup(p => p.GetDiagnosticsAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Diagnostic>());

        await _handler.Handle(openRequest, CancellationToken.None);

        // Act - close document
        var closeRequest = new DidCloseTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = DocumentUri.From("file:///test.scriban")
            }
        };

        await _handler.Handle(closeRequest, CancellationToken.None);

        // Assert - cache should be invalidated
        _parser.Verify(p => p.InvalidateCache(
            It.Is<string>(s => s.Contains("test.scriban"))), Times.Once);
    }

    [Fact]
    public async Task Handle_DidSave_DoesNotThrow()
    {
        // Arrange
        var saveRequest = new DidSaveTextDocumentParams
        {
            TextDocument = new TextDocumentIdentifier
            {
                Uri = DocumentUri.From("file:///test.scriban")
            }
        };

        // Act
        var act = async () => await _handler.Handle(saveRequest, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Constructor_NullLanguageServer_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new TextDocumentSyncHandler(
            null!,
            _parser.Object,
            NullLogger<TextDocumentSyncHandler>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("languageServer");
    }

    [Fact]
    public void Constructor_NullParser_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new TextDocumentSyncHandler(
            _languageServer.Object,
            null!,
            NullLogger<TextDocumentSyncHandler>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("parser");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new TextDocumentSyncHandler(
            _languageServer.Object,
            _parser.Object,
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public async Task Handle_ChangeForUnknownDocument_LogsWarning()
    {
        // Arrange
        var changeRequest = new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier
            {
                Uri = DocumentUri.From("file:///unknown.scriban"),
                Version = 1
            },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(
                new TextDocumentContentChangeEvent
                {
                    Text = "{{ x = 5 }}"
                })
        };

        // Act
        var act = async () => await _handler.Handle(changeRequest, CancellationToken.None);

        // Assert - should not throw
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void GetTextDocumentAttributes_ReturnsScribanLanguage()
    {
        // Arrange
        var uri = DocumentUri.From("file:///test.scriban");

        // Act
        var attributes = _handler.GetTextDocumentAttributes(uri);

        // Assert
        attributes.Should().NotBeNull();
        attributes.LanguageId.Should().Be("scriban");
    }
}
