using System.Collections.Concurrent;
using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using ScribanLanguageServer.Core.Services;

namespace ScribanLanguageServer.Server.Handlers;

/// <summary>
/// Handles document synchronization and triggers diagnostics with debouncing
/// </summary>
public class TextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly ILanguageServerFacade _languageServer;
    private readonly IScribanParserService _parser;
    private readonly ILogger<TextDocumentSyncHandler> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _validationTokens = new();
    private readonly ConcurrentDictionary<string, DocumentState> _documents = new();

    private class DocumentState
    {
        public string Uri { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public int Version { get; set; }
    }

    public TextDocumentSyncHandler(
        ILanguageServerFacade languageServer,
        IScribanParserService parser,
        ILogger<TextDocumentSyncHandler> logger)
    {
        _languageServer = languageServer ?? throw new ArgumentNullException(nameof(languageServer));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
    {
        return new TextDocumentAttributes(uri, "scriban");
    }

    public override Task<Unit> Handle(
        DidOpenTextDocumentParams request,
        CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToString();

        _documents[uri] = new DocumentState
        {
            Uri = uri,
            Text = request.TextDocument.Text,
            Version = request.TextDocument.Version ?? 0
        };

        _logger.LogInformation("Document opened: {Uri}", uri);

        // Trigger validation
        _ = ValidateDocumentAsync(uri, cancellationToken);

        return Unit.Task;
    }

    public override Task<Unit> Handle(
        DidChangeTextDocumentParams request,
        CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToString();

        if (!_documents.TryGetValue(uri, out var doc))
        {
            _logger.LogWarning("Change for unknown document: {Uri}", uri);
            return Unit.Task;
        }

        // Apply changes
        foreach (var change in request.ContentChanges)
        {
            if (change.Range == null)
            {
                // Full document update
                doc.Text = change.Text;
            }
            else
            {
                // Incremental update
                doc.Text = ApplyChange(doc.Text, change);
            }
        }

        doc.Version = request.TextDocument.Version ?? doc.Version + 1;

        _logger.LogDebug(
            "Document changed: {Uri} v{Version}",
            uri, doc.Version);

        // Trigger debounced validation
        _ = ValidateDocumentAsync(uri, cancellationToken);

        return Unit.Task;
    }

    public override Task<Unit> Handle(
        DidCloseTextDocumentParams request,
        CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToString();

        _documents.TryRemove(uri, out _);
        _parser.InvalidateCache(uri);

        _logger.LogInformation("Document closed: {Uri}", uri);

        return Unit.Task;
    }

    public override Task<Unit> Handle(
        DidSaveTextDocumentParams request,
        CancellationToken cancellationToken)
    {
        // Nothing special to do on save
        return Unit.Task;
    }

    private async Task ValidateDocumentAsync(
        string uri,
        CancellationToken cancellationToken)
    {
        // Cancel previous validation
        if (_validationTokens.TryRemove(uri, out var oldCts))
        {
            await oldCts.CancelAsync();
            oldCts.Dispose();
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _validationTokens[uri] = cts;

        try
        {
            // Debounce - wait for user to stop typing
            await Task.Delay(250, cts.Token);

            if (!_documents.TryGetValue(uri, out var doc))
            {
                return;
            }

            _logger.LogDebug("Validating {Uri} v{Version}", uri, doc.Version);

            // Get diagnostics (uses cache!)
            var diagnostics = await _parser.GetDiagnosticsAsync(
                uri,
                doc.Text,
                doc.Version,
                cts.Token);

            // Publish to client
            _languageServer.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
            {
                Uri = DocumentUri.From(uri),
                Version = doc.Version,
                Diagnostics = new Container<Diagnostic>(diagnostics)
            });

            _logger.LogInformation(
                "Published {Count} diagnostics for {Uri}",
                diagnostics.Count, uri);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Validation cancelled for {Uri}", uri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed for {Uri}", uri);
        }
        finally
        {
            _validationTokens.TryRemove(uri, out _);
            cts.Dispose();
        }
    }

    private static string ApplyChange(string text, TextDocumentContentChangeEvent change)
    {
        if (change.Range == null)
        {
            return change.Text;
        }

        // Convert to position indices
        var lines = text.Split('\n');
        var startLine = change.Range.Start.Line;
        var startChar = change.Range.Start.Character;
        var endLine = change.Range.End.Line;
        var endChar = change.Range.End.Character;

        // Calculate start position
        int startPos = lines.Take(startLine).Sum(l => l.Length + 1) + startChar;

        // Calculate end position
        int endPos = lines.Take(endLine).Sum(l => l.Length + 1) + endChar;

        // Apply change
        return text[..startPos] + change.Text + text[endPos..];
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new TextDocumentSyncRegistrationOptions
        {
            // DocumentSelector, Change, and Save options will be configured
            // when full LSP server is set up
        };
    }
}
