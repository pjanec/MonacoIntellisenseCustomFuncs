using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using Scriban.Syntax;
using ScribanLanguageServer.Core.ApiSpec;
using ScribanLanguageServer.Core.Services;

namespace ScribanLanguageServer.Server.Handlers;

/// <summary>
/// Base class for all LSP handlers with common functionality
/// </summary>
public abstract class HandlerBase
{
    protected readonly IApiSpecService ApiSpecService;
    protected readonly IScribanParserService ParserService;
    protected readonly ILogger Logger;

    protected HandlerBase(
        IApiSpecService apiSpecService,
        IScribanParserService parserService,
        ILogger logger)
    {
        ApiSpecService = apiSpecService ?? throw new ArgumentNullException(nameof(apiSpecService));
        ParserService = parserService ?? throw new ArgumentNullException(nameof(parserService));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Converts string URI to DocumentUri
    /// </summary>
    protected static DocumentUri GetDocumentUri(string uri)
    {
        return DocumentUri.From(uri);
    }

    /// <summary>
    /// Gets AST from cached parser
    /// </summary>
    protected Task<ScriptPage?> GetAstAsync(
        string documentUri,
        string code,
        CancellationToken cancellationToken)
    {
        return ParserService.ParseAsync(code, cancellationToken);
    }
}
