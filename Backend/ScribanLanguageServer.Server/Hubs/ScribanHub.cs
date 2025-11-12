using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using ScribanLanguageServer.Core.ApiSpec;
using ScribanLanguageServer.Core.Services;
using ScribanLanguageServer.Core.Validation;

namespace ScribanLanguageServer.Server.Hubs;

/// <summary>
/// SignalR hub for custom Scriban language server protocol
/// Handles trigger-based picker opening and path suggestions
/// </summary>
public class ScribanHub : Hub<IScribanClient>
{
    private readonly IApiSpecService _apiSpec;
    private readonly IScribanParserService _parser;
    private readonly IFileSystemService _fileSystem;
    private readonly IDocumentSessionService _sessionService;
    private readonly IRateLimitService _rateLimit;
    private readonly ILogger<ScribanHub> _logger;

    public ScribanHub(
        IApiSpecService apiSpec,
        IScribanParserService parser,
        IFileSystemService fileSystem,
        IDocumentSessionService sessionService,
        IRateLimitService rateLimit,
        ILogger<ScribanHub> logger)
    {
        _apiSpec = apiSpec ?? throw new ArgumentNullException(nameof(apiSpec));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _rateLimit = rateLimit ?? throw new ArgumentNullException(nameof(rateLimit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Called when a client connects
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a client disconnects
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);

        // Clean up all documents owned by this connection
        _sessionService.CleanupConnection(Context.ConnectionId);

        // Clean up rate limit bucket
        _rateLimit.RemoveConnection(Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Registers a document as being opened by this connection
    /// </summary>
    public Task RegisterDocument(string documentUri)
    {
        try
        {
            _sessionService.RegisterDocument(Context.ConnectionId, documentUri);
            _logger.LogDebug("Document registered: {Uri} by {ConnectionId}", documentUri, Context.ConnectionId);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering document: {Uri}", documentUri);
            throw new HubException($"Failed to register document: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if a trigger character should open a picker
    /// If yes, sends OpenPicker to the client
    /// </summary>
    public async Task CheckTrigger(TriggerContext context)
    {
        try
        {
            // Rate limiting
            if (!_rateLimit.TryAcquire(Context.ConnectionId))
            {
                throw new HubException("Rate limit exceeded: maximum 10 requests per second");
            }

            // Input validation
            try
            {
                InputValidator.ValidateDocumentUri(context.DocumentUri);
                InputValidator.ValidatePosition(context.Position.Line, context.Position.Character);

                if (!string.IsNullOrEmpty(context.CurrentLine) && context.CurrentLine.Length > 10000)
                {
                    throw new ArgumentException("Line too long");
                }
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid input in CheckTrigger");
                throw new HubException($"Invalid input: {ex.Message}");
            }

            _logger.LogDebug(
                "CheckTrigger: {Uri} at {Line}:{Char}",
                context.DocumentUri,
                context.Position.Line,
                context.Position.Character);

            // Validate document access
            if (!_sessionService.ValidateAccess(Context.ConnectionId, context.DocumentUri))
            {
                _logger.LogWarning(
                    "Unauthorized access attempt: {ConnectionId} -> {Uri}",
                    Context.ConnectionId,
                    context.DocumentUri);
                throw new HubException("Access denied to document");
            }

            // Get parameter context
            var paramContext = await GetParameterContextAsync(context);

            if (!paramContext.IsValid || paramContext.ParameterSpec == null)
            {
                // Not in a parameter context or couldn't determine parameter
                _logger.LogDebug("No valid parameter context found");
                return;
            }

            _logger.LogDebug("Found parameter context: {Function}.param[{Index}]",
                paramContext.FunctionName, paramContext.ParameterIndex);

            // Check if this parameter has a picker
            if (paramContext.ParameterSpec.Picker == "file-picker")
            {
                // Send OpenPicker to client
                await Clients.Caller.OpenPicker(new OpenPickerData
                {
                    PickerType = "file-picker",
                    FunctionName = paramContext.FunctionName,
                    ParameterIndex = paramContext.ParameterIndex,
                    CurrentValue = paramContext.CurrentValue,
                    BasePath = Environment.CurrentDirectory
                });

                _logger.LogInformation(
                    "Opened file-picker for {Function}.param[{Index}]",
                    paramContext.FunctionName,
                    paramContext.ParameterIndex);
            }
            else if (paramContext.ParameterSpec.Picker == "enum-list")
            {
                // Send OpenPicker with enum options
                await Clients.Caller.OpenPicker(new OpenPickerData
                {
                    PickerType = "enum-list",
                    FunctionName = paramContext.FunctionName,
                    ParameterIndex = paramContext.ParameterIndex,
                    CurrentValue = paramContext.CurrentValue,
                    Options = paramContext.ParameterSpec.Options
                });

                _logger.LogInformation(
                    "Opened enum-list for {Function}.param[{Index}]",
                    paramContext.FunctionName,
                    paramContext.ParameterIndex);
            }
            // If picker is "none", do nothing
        }
        catch (HubException)
        {
            throw; // Re-throw HubExceptions as-is
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in CheckTrigger");
            throw new HubException($"Error checking trigger: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets file/folder suggestions for a file-picker parameter
    /// </summary>
    public async Task<List<string>> GetPathSuggestions(
        string functionName,
        int parameterIndex,
        string? currentValue)
    {
        try
        {
            // Rate limiting
            if (!_rateLimit.TryAcquire(Context.ConnectionId))
            {
                throw new HubException("Rate limit exceeded: maximum 10 requests per second");
            }

            // Input validation
            try
            {
                InputValidator.ValidateFunctionName(functionName);
                InputValidator.ValidateParameterIndex(parameterIndex);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid input in GetPathSuggestions");
                throw new HubException($"Invalid input: {ex.Message}");
            }

            _logger.LogDebug(
                "GetPathSuggestions: {Function}.param[{Index}], current: {Value}",
                functionName,
                parameterIndex,
                currentValue);

            // Validate function and parameter
            var global = _apiSpec.GetGlobal(functionName);
            if (global == null || global.Parameters == null || parameterIndex >= global.Parameters.Count)
            {
                _logger.LogWarning("Invalid function or parameter index: {Function}.param[{Index}]", functionName, parameterIndex);
                return new List<string>();
            }

            var param = global.Parameters[parameterIndex];
            if (param.Picker != "file-picker")
            {
                _logger.LogWarning("Parameter is not a file-picker: {Function}.param[{Index}]", functionName, parameterIndex);
                return new List<string>();
            }

            // Determine base path from current value or use current directory
            string basePath = Environment.CurrentDirectory;
            if (!string.IsNullOrWhiteSpace(currentValue))
            {
                try
                {
                    // Extract directory from current value
                    var dir = Path.GetDirectoryName(currentValue);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        basePath = dir;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not extract directory from current value: {Value}", currentValue);
                }
            }

            // Get path suggestions with timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var suggestions = await _fileSystem.GetPathSuggestionsAsync(basePath, "*", cts.Token);

            _logger.LogInformation("Returned {Count} path suggestions for {Function}.param[{Index}]",
                suggestions.Count, functionName, parameterIndex);

            return suggestions;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("GetPathSuggestions timed out for {Function}.param[{Index}]", functionName, parameterIndex);
            throw new HubException("Operation timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetPathSuggestions");
            throw new HubException($"Error getting path suggestions: {ex.Message}");
        }
    }

    /// <summary>
    /// Analyzes the code at the given position to determine parameter context
    /// </summary>
    private async Task<ParameterContext> GetParameterContextAsync(TriggerContext context)
    {
        try
        {
            // Parse the code to get AST (not currently used for parameter detection)
            var ast = await _parser.ParseAsync(context.Code, CancellationToken.None);

            // For now, use simple regex-based detection on current line
            var currentLine = context.CurrentLine ?? string.Empty;
            _logger.LogDebug("GetParameterContext: line='{Line}', position={Position}", currentLine, context.Position.Character);

            // Simple regex to detect function calls
            var match = System.Text.RegularExpressions.Regex.Match(
                currentLine,
                @"(\w+)\s*\("
            );

            if (!match.Success)
            {
                _logger.LogDebug("No function call pattern matched");
                return new ParameterContext { IsValid = false };
            }

            var functionName = match.Groups[1].Value;
            _logger.LogDebug("Found function: {Function}", functionName);

            var global = _apiSpec.GetGlobal(functionName);
            if (global == null)
            {
                _logger.LogDebug("Function {Function} not found in API spec", functionName);
                return new ParameterContext { IsValid = false };
            }

            if (global.Parameters == null || !global.Parameters.Any())
            {
                _logger.LogDebug("Function {Function} has no parameters", functionName);
                return new ParameterContext { IsValid = false };
            }

            // Calculate parameter index by counting commas from opening paren to cursor position
            // Extract the substring from after '(' to the cursor position
            var openParenIndex = currentLine.IndexOf('(', match.Index);
            if (openParenIndex == -1)
            {
                _logger.LogDebug("Could not find opening paren");
                return new ParameterContext { IsValid = false };
            }

            // Get the text from after '(' up to cursor position (character position in line)
            int startPos = openParenIndex + 1;
            int endPos = context.Position.Character;
            int length = Math.Max(0, Math.Min(endPos - startPos, currentLine.Length - startPos));

            _logger.LogDebug("Extracting substring: start={Start}, end={End}, length={Length}, lineLength={LineLength}",
                startPos, endPos, length, currentLine.Length);

            var textAfterParen = currentLine.Substring(startPos, length);
            _logger.LogDebug("Text after paren: '{Text}'", textAfterParen);

            // Count commas to determine parameter index
            // Simple approach: count commas not inside quotes
            int paramIndex = 0;
            bool inString = false;
            char stringChar = '\0';

            foreach (char c in textAfterParen)
            {
                if ((c == '"' || c == '\'') && !inString)
                {
                    inString = true;
                    stringChar = c;
                }
                else if (c == stringChar && inString)
                {
                    inString = false;
                    stringChar = '\0';
                }
                else if (c == ',' && !inString)
                {
                    paramIndex++;
                }
            }

            _logger.LogDebug("Detected parameter index {Index} for {Function} (commas counted: {CommaCount})",
                paramIndex, functionName, paramIndex);

            // Validate parameter index is within bounds
            if (paramIndex >= global.Parameters.Count)
            {
                _logger.LogDebug("Parameter index {Index} out of bounds for {Function} (max: {Max})",
                    paramIndex, functionName, global.Parameters.Count - 1);
                return new ParameterContext { IsValid = false };
            }

            var paramSpec = global.Parameters[paramIndex];
            _logger.LogDebug("Parameter spec: name={Name}, type={Type}, picker={Picker}",
                paramSpec.Name, paramSpec.Type, paramSpec.Picker);

            // Extract current parameter value (between the comma/paren and next comma/paren)
            string? currentValue = null;
            try
            {
                // Find the range of the current parameter value
                int valueStart = openParenIndex + 1;

                // Skip past previous parameters (count commas)
                int commasSeen = 0;
                for (int i = openParenIndex + 1; i < context.Position.Character && i < currentLine.Length; i++)
                {
                    if (currentLine[i] == ',')
                    {
                        commasSeen++;
                        if (commasSeen == paramIndex)
                        {
                            valueStart = i + 1;
                            break;
                        }
                    }
                }

                // Find the end of the current parameter value (next comma or closing paren)
                int valueEnd = currentLine.Length;
                for (int i = valueStart; i < currentLine.Length; i++)
                {
                    if (currentLine[i] == ',' || currentLine[i] == ')')
                    {
                        valueEnd = i;
                        break;
                    }
                }

                // Extract and trim the value
                if (valueStart < valueEnd && valueStart < currentLine.Length)
                {
                    currentValue = currentLine.Substring(valueStart, valueEnd - valueStart).Trim();
                    // Remove quotes if present
                    if (currentValue.Length >= 2 &&
                        ((currentValue[0] == '"' && currentValue[^1] == '"') ||
                         (currentValue[0] == '\'' && currentValue[^1] == '\'')))
                    {
                        currentValue = currentValue.Substring(1, currentValue.Length - 2);
                    }

                    // If empty after trimming, set to null
                    if (string.IsNullOrWhiteSpace(currentValue))
                    {
                        currentValue = null;
                    }

                    _logger.LogDebug("Extracted current value: '{Value}'", currentValue ?? "(null)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not extract current parameter value");
            }

            return new ParameterContext
            {
                FunctionName = functionName,
                ParameterIndex = paramIndex,
                ParameterSpec = paramSpec,
                IsValid = true,
                CurrentValue = currentValue
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting parameter context");
            return new ParameterContext { IsValid = false };
        }
    }

    /// <summary>
    /// Handles LSP JSON-RPC messages from the client
    /// </summary>
    public async Task SendMessage(object message)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(message);
            _logger.LogDebug("Received LSP message from {ConnectionId}: {Message}",
                Context.ConnectionId, json);

            // Parse the JSON-RPC message
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Check if this is a request (has 'method' and 'id' fields)
            if (root.TryGetProperty("method", out var methodElement) &&
                root.TryGetProperty("id", out var idElement))
            {
                var method = methodElement.GetString();

                if (method == "initialize")
                {
                    // Send initialize response
                    var response = new
                    {
                        jsonrpc = "2.0",
                        id = idElement.GetInt32(),
                        result = new
                        {
                            capabilities = new
                            {
                                textDocumentSync = new
                                {
                                    openClose = true,
                                    change = 1 // Full sync
                                },
                                completionProvider = new
                                {
                                    triggerCharacters = new[] { ".", "(", "," },
                                    resolveProvider = false
                                },
                                hoverProvider = true,
                                signatureHelpProvider = new
                                {
                                    triggerCharacters = new[] { "(", "," }
                                }
                            },
                            serverInfo = new
                            {
                                name = "Scriban Language Server",
                                version = "1.0.0"
                            }
                        }
                    };

                    await Clients.Caller.ReceiveMessage(response);
                    _logger.LogInformation("Sent initialize response to {ConnectionId}", Context.ConnectionId);
                    return;
                }

                if (method == "textDocument/completion")
                {
                    // Extract position from params
                    if (root.TryGetProperty("params", out var paramsElement) &&
                        paramsElement.TryGetProperty("position", out var positionElement))
                    {
                        var line = positionElement.GetProperty("line").GetInt32();
                        var character = positionElement.GetProperty("character").GetInt32();

                        // Get text document URI
                        var uri = paramsElement.GetProperty("textDocument").GetProperty("uri").GetString();

                        _logger.LogDebug("Completion request at {Line}:{Character} in {Uri}",
                            line, character, uri);

                        var items = new List<object>();

                        // Get globals from API spec if loaded
                        if (_apiSpec.CurrentSpec != null)
                        {
                            foreach (var global in _apiSpec.CurrentSpec.Globals)
                            {
                                if (global.Type == "function")
                                {
                                    items.Add(new
                                    {
                                        label = global.Name,
                                        kind = 3, // Function
                                        documentation = global.Hover,
                                        insertText = global.Name,
                                        detail = "function"
                                    });
                                }
                                else if (global.Type == "object")
                                {
                                    items.Add(new
                                    {
                                        label = global.Name,
                                        kind = 9, // Module/Object
                                        documentation = global.Hover,
                                        insertText = global.Name,
                                        detail = "object"
                                    });
                                }
                            }
                        }
                        else
                        {
                            // Fallback completions if API spec not loaded
                            items.Add(new { label = "os", kind = 9, documentation = "Operating system functions", insertText = "os", detail = "object" });
                            items.Add(new { label = "string", kind = 9, documentation = "String manipulation functions", insertText = "string", detail = "object" });
                            items.Add(new { label = "array", kind = 9, documentation = "Array manipulation functions", insertText = "array", detail = "object" });
                        }

                        var completionResponse = new
                        {
                            jsonrpc = "2.0",
                            id = idElement.GetInt32(),
                            result = new
                            {
                                items = items.ToArray()
                            }
                        };

                        await Clients.Caller.ReceiveMessage(completionResponse);
                        _logger.LogInformation("Sent completion response with {Count} items to {ConnectionId}",
                            items.Count, Context.ConnectionId);
                        return;
                    }
                }

                if (method == "textDocument/hover")
                {
                    // Extract position from params
                    if (root.TryGetProperty("params", out var paramsElement) &&
                        paramsElement.TryGetProperty("position", out var positionElement))
                    {
                        var line = positionElement.GetProperty("line").GetInt32();
                        var character = positionElement.GetProperty("character").GetInt32();

                        // Get text document URI
                        var uri = paramsElement.GetProperty("textDocument").GetProperty("uri").GetString();

                        // Get the word (non-standard extension from frontend)
                        string? word = null;
                        if (paramsElement.TryGetProperty("word", out var wordElement))
                        {
                            word = wordElement.GetString();
                        }

                        _logger.LogDebug("Hover request at {Line}:{Character} in {Uri} for word '{Word}'",
                            line, character, uri, word);

                        string? hoverContent = null;

                        // Check if API spec is loaded and we have a word
                        if (_apiSpec.CurrentSpec != null && !string.IsNullOrEmpty(word))
                        {
                            // Look up the word in globals
                            var global = _apiSpec.CurrentSpec.Globals.FirstOrDefault(g =>
                                g.Name.Equals(word, StringComparison.OrdinalIgnoreCase));

                            if (global != null)
                            {
                                // Build hover content with type and description
                                hoverContent = $"**{global.Name}** ({global.Type})\n\n{global.Hover}";

                                // If it's an object with members, list them
                                if (global.Type == "object" && global.Members != null && global.Members.Any())
                                {
                                    hoverContent += "\n\n**Members:**\n";
                                    foreach (var member in global.Members.Take(5))
                                    {
                                        hoverContent += $"- `{member.Name}`: {member.Hover}\n";
                                    }
                                    if (global.Members.Count > 5)
                                    {
                                        hoverContent += $"- ... and {global.Members.Count - 5} more\n";
                                    }
                                }

                                // If it's a function with parameters, list them
                                if (global.Type == "function" && global.Parameters != null && global.Parameters.Any())
                                {
                                    hoverContent += "\n\n**Parameters:**\n";
                                    foreach (var param in global.Parameters)
                                    {
                                        hoverContent += $"- `{param.Name}` ({param.Type})\n";
                                    }
                                }

                                _logger.LogInformation("Returning hover for '{Word}': {Type}", word, global.Type);
                            }
                        }

                        var hoverResponse = new
                        {
                            jsonrpc = "2.0",
                            id = idElement.GetInt32(),
                            result = hoverContent != null ? new
                            {
                                contents = new
                                {
                                    kind = "markdown",
                                    value = hoverContent
                                }
                            } : null
                        };

                        await Clients.Caller.ReceiveMessage(hoverResponse);
                        _logger.LogDebug("Sent hover response to {ConnectionId}", Context.ConnectionId);
                        return;
                    }
                }

                // Handle other methods
                _logger.LogWarning("Unhandled LSP method: {Method}", method);
            }

            // For notifications (no 'id') or unhandled requests, just acknowledge
            _logger.LogDebug("Processed message from {ConnectionId}", Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing LSP message");
            throw new HubException($"Failed to process message: {ex.Message}");
        }
    }
}
