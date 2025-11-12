using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ScribanLanguageServer.Core.ApiSpec;
using ScribanLanguageServer.Core.Services;

namespace ScribanLanguageServer.Server.Handlers;

/// <summary>
/// Handles LSP completion requests to provide autocomplete suggestions
/// </summary>
public class CompletionHandler : CompletionHandlerBase
{
    private readonly IApiSpecService _apiSpec;
    private readonly IScribanParserService _parser;
    private readonly IFileSystemService _fileSystem;
    private readonly ILogger<CompletionHandler> _logger;

    public CompletionHandler(
        IApiSpecService apiSpec,
        IScribanParserService parser,
        IFileSystemService fileSystem,
        ILogger<CompletionHandler> logger)
    {
        _apiSpec = apiSpec ?? throw new ArgumentNullException(nameof(apiSpec));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override async Task<CompletionList> Handle(
        CompletionParams request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug(
                "Completion requested at {Uri}:{Line}:{Char}",
                request.TextDocument.Uri,
                request.Position.Line,
                request.Position.Character);

            // Note: In full implementation, we would:
            // 1. Get document text from document store
            // 2. Parse to get AST
            // 3. Determine completion context (function name, parameter, etc.)
            // 4. Look up available completions in API spec
            // 5. For file picker parameters, use IFileSystemService
            // 6. Return completion items
            //
            // For now, return empty list as document storage will be implemented
            // in integration phase when full LSP server is running
            return await Task.FromResult(new CompletionList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Completion failed for {Uri}", request.TextDocument.Uri);
            return new CompletionList();
        }
    }

    public override Task<CompletionItem> Handle(
        CompletionItem request,
        CancellationToken cancellationToken)
    {
        // Completion item resolve - used to provide additional details
        // when a completion item is selected
        // For now, just return the item as-is
        return Task.FromResult(request);
    }

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new CompletionRegistrationOptions
        {
            // DocumentSelector and trigger characters will be configured
            // when full LSP server is set up
            TriggerCharacters = new[] { ".", "(", "\"", "/" },
            ResolveProvider = false
        };
    }
}
