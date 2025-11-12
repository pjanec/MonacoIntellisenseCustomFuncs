using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Scriban.Syntax;
using ScribanLanguageServer.Core.ApiSpec;
using ScribanLanguageServer.Core.Services;

namespace ScribanLanguageServer.Server.Handlers;

/// <summary>
/// Handles LSP hover requests to show documentation for functions
/// </summary>
public class HoverHandler : HoverHandlerBase
{
    private readonly IApiSpecService _apiSpec;
    private readonly IScribanParserService _parser;
    private readonly ILogger<HoverHandler> _logger;

    public HoverHandler(
        IApiSpecService apiSpec,
        IScribanParserService parser,
        ILogger<HoverHandler> logger)
    {
        _apiSpec = apiSpec ?? throw new ArgumentNullException(nameof(apiSpec));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override async Task<Hover?> Handle(
        HoverParams request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug(
                "Hover requested at {Uri}:{Line}:{Char}",
                request.TextDocument.Uri,
                request.Position.Line,
                request.Position.Character);

            // Note: In full implementation, we would:
            // 1. Get document text from document store
            // 2. Parse to get AST
            // 3. Find node at position
            // 4. Look up documentation in API spec
            // 5. Return hover info
            //
            // For now, return null as document storage will be implemented
            // in integration phase when full LSP server is running
            return await Task.FromResult<Hover?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hover failed for {Uri}", request.TextDocument.Uri);
            return null;
        }
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new HoverRegistrationOptions
        {
            // DocumentSelector will be configured when full LSP server is set up
        };
    }
}
