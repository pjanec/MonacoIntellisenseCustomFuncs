using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ScribanLanguageServer.Core.ApiSpec;
using ScribanLanguageServer.Core.Services;
using ScribanLanguageServer.Server.Hubs;
using Xunit;

namespace ScribanLanguageServer.Tests.Unit.Hubs;

[Trait("Stage", "B4")]
public class ScribanHubTests
{
    private readonly Mock<IApiSpecService> _apiSpec;
    private readonly Mock<IScribanParserService> _parser;
    private readonly Mock<IFileSystemService> _fileSystem;
    private readonly Mock<IDocumentSessionService> _sessionService;
    private readonly Mock<IRateLimitService> _rateLimit;
    private readonly Mock<IHubCallerClients<IScribanClient>> _clients;
    private readonly Mock<IScribanClient> _caller;
    private readonly Mock<HubCallerContext> _context;
    private readonly ScribanHub _hub;

    public ScribanHubTests()
    {
        _apiSpec = new Mock<IApiSpecService>();
        _parser = new Mock<IScribanParserService>();
        _fileSystem = new Mock<IFileSystemService>();
        _sessionService = new Mock<IDocumentSessionService>();
        _rateLimit = new Mock<IRateLimitService>();
        _clients = new Mock<IHubCallerClients<IScribanClient>>();
        _caller = new Mock<IScribanClient>();
        _context = new Mock<HubCallerContext>();

        // Setup context
        _context.Setup(c => c.ConnectionId).Returns("test-connection-id");

        // Setup clients to return our mock caller
        _clients.Setup(c => c.Caller).Returns(_caller.Object);

        // Setup rate limiting to allow requests by default
        _rateLimit.Setup(r => r.TryAcquire(It.IsAny<string>())).Returns(true);

        _hub = new ScribanHub(
            _apiSpec.Object,
            _parser.Object,
            _fileSystem.Object,
            _sessionService.Object,
            _rateLimit.Object,
            NullLogger<ScribanHub>.Instance)
        {
            Clients = _clients.Object,
            Context = _context.Object
        };
    }

    [Fact]
    public async Task OnConnectedAsync_LogsConnection()
    {
        // Act
        await _hub.OnConnectedAsync();

        // Assert - just verify it doesn't throw
        _context.Verify(c => c.ConnectionId, Times.AtLeastOnce);
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
        _rateLimit.Verify(
            r => r.RemoveConnection("test-connection-id"),
            Times.Once);
    }

    [Fact]
    public async Task RegisterDocument_ValidUri_RegistersSuccessfully()
    {
        // Arrange
        var documentUri = "file:///test.scriban";

        // Act
        await _hub.RegisterDocument(documentUri);

        // Assert
        _sessionService.Verify(
            s => s.RegisterDocument("test-connection-id", documentUri),
            Times.Once);
    }

    [Fact]
    public async Task CheckTrigger_UnauthorizedAccess_ThrowsHubException()
    {
        // Arrange
        var context = new TriggerContext
        {
            DocumentUri = "file:///test.scriban",
            Code = "{{ copy_file( ",
            Position = new Position(0, 14),
            CurrentLine = "{{ copy_file( "
        };

        _sessionService
            .Setup(s => s.ValidateAccess("test-connection-id", context.DocumentUri))
            .Returns(false);

        // Act & Assert
        var act = async () => await _hub.CheckTrigger(context);

        await act.Should().ThrowAsync<HubException>()
            .WithMessage("Access denied to document");
    }

    [Fact]
    public async Task CheckTrigger_FilePickerParameter_SendsOpenPicker()
    {
        // Arrange
        var context = new TriggerContext
        {
            DocumentUri = "file:///test.scriban",
            Code = "{{ copy_file( ",
            Position = new Position(0, 14),
            CurrentLine = "{{ copy_file( "
        };

        _sessionService
            .Setup(s => s.ValidateAccess("test-connection-id", context.DocumentUri))
            .Returns(true);

        var paramEntry = new ParameterEntry
        {
            Name = "source",
            Type = "path",
            Picker = "file-picker"
        };

        _apiSpec
            .Setup(a => a.GetGlobal("copy_file"))
            .Returns(new GlobalEntry
            {
                Name = "copy_file",
                Type = "function",
                Hover = "Copies file",
                Parameters = new List<ParameterEntry> { paramEntry }
            });

        _parser
            .Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Scriban.Syntax.ScriptPage?)null); // Will use simple regex parsing

        // Act
        await _hub.CheckTrigger(context);

        // Assert
        _caller.Verify(
            c => c.OpenPicker(It.Is<OpenPickerData>(d =>
                d.PickerType == "file-picker" &&
                d.FunctionName == "copy_file" &&
                d.ParameterIndex == 0)),
            Times.Once);
    }

    [Fact]
    public async Task CheckTrigger_EnumListParameter_SendsOpenPickerWithOptions()
    {
        // Arrange
        var context = new TriggerContext
        {
            DocumentUri = "file:///test.scriban",
            Code = "{{ set_mode( ",
            Position = new Position(0, 13),
            CurrentLine = "{{ set_mode( "
        };

        _sessionService
            .Setup(s => s.ValidateAccess("test-connection-id", context.DocumentUri))
            .Returns(true);

        var paramEntry = new ParameterEntry
        {
            Name = "mode",
            Type = "constant",
            Picker = "enum-list",
            Options = new List<string> { "FAST", "SLOW", "MEDIUM" }
        };

        _apiSpec
            .Setup(a => a.GetGlobal("set_mode"))
            .Returns(new GlobalEntry
            {
                Name = "set_mode",
                Type = "function",
                Hover = "Sets mode",
                Parameters = new List<ParameterEntry> { paramEntry }
            });

        _parser
            .Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Scriban.Syntax.ScriptPage?)null);

        // Act
        await _hub.CheckTrigger(context);

        // Assert
        _caller.Verify(
            c => c.OpenPicker(It.Is<OpenPickerData>(d =>
                d.PickerType == "enum-list" &&
                d.FunctionName == "set_mode" &&
                d.Options != null &&
                d.Options.Count == 3)),
            Times.Once);
    }

    [Fact]
    public async Task CheckTrigger_NonePickerParameter_DoesNotSendOpenPicker()
    {
        // Arrange
        var context = new TriggerContext
        {
            DocumentUri = "file:///test.scriban",
            Code = "{{ log( ",
            Position = new Position(0, 8),
            CurrentLine = "{{ log( "
        };

        _sessionService
            .Setup(s => s.ValidateAccess("test-connection-id", context.DocumentUri))
            .Returns(true);

        var paramEntry = new ParameterEntry
        {
            Name = "message",
            Type = "string",
            Picker = "none"
        };

        _apiSpec
            .Setup(a => a.GetGlobal("log"))
            .Returns(new GlobalEntry
            {
                Name = "log",
                Type = "function",
                Hover = "Logs message",
                Parameters = new List<ParameterEntry> { paramEntry }
            });

        _parser
            .Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Scriban.Syntax.ScriptPage?)null);

        // Act
        await _hub.CheckTrigger(context);

        // Assert
        _caller.Verify(
            c => c.OpenPicker(It.IsAny<OpenPickerData>()),
            Times.Never);
    }

    [Fact]
    public async Task GetPathSuggestions_ValidFunction_ReturnsPathList()
    {
        // Arrange
        var expected = new List<string> { "file1.txt", "file2.txt", "folder/" };

        _apiSpec
            .Setup(a => a.GetGlobal("copy_file"))
            .Returns(new GlobalEntry
            {
                Name = "copy_file",
                Type = "function",
                Hover = "Copies file",
                Parameters = new List<ParameterEntry>
                {
                    new()
                    {
                        Name = "source",
                        Type = "path",
                        Picker = "file-picker"
                    }
                }
            });

        _fileSystem
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
    public async Task GetPathSuggestions_InvalidFunction_ReturnsEmptyList()
    {
        // Arrange
        _apiSpec
            .Setup(a => a.GetGlobal("unknown_function"))
            .Returns((GlobalEntry?)null);

        // Act
        var result = await _hub.GetPathSuggestions("unknown_function", 0, null);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPathSuggestions_NonFilePickerParameter_ReturnsEmptyList()
    {
        // Arrange
        _apiSpec
            .Setup(a => a.GetGlobal("log"))
            .Returns(new GlobalEntry
            {
                Name = "log",
                Type = "function",
                Hover = "Logs message",
                Parameters = new List<ParameterEntry>
                {
                    new()
                    {
                        Name = "message",
                        Type = "string",
                        Picker = "none" // Not a file-picker
                    }
                }
            });

        // Act
        var result = await _hub.GetPathSuggestions("log", 0, null);

        // Assert
        result.Should().BeEmpty();
    }
}
