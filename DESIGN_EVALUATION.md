# Scriban Language Server - Design Evaluation Report

**Date:** 2025-11-11
**Evaluator:** Design Review
**Documents Reviewed:**
- High level specs.md
- Backend/Backend details.md
- Frontend/Frontend details.md

---

## Executive Summary

**Overall Rating: 7.5/10**

The Scriban Language Server design demonstrates **excellent architectural thinking** with clear separation of concerns, comprehensive documentation, and proper use of industry standards (LSP). However, the design requires **significant hardening** for production readiness, particularly in error handling, performance optimization, and security considerations.

### Key Strengths
- ✅ Clear client-server separation ("dumb terminal" vs "brain")
- ✅ Metadata-driven architecture (ApiSpec.json)
- ✅ Comprehensive documentation quality
- ✅ Proper LSP integration
- ✅ Well-defined test strategy

### Critical Gaps
- ❌ Missing production-readiness features (timeouts, rate limiting, monitoring)
- ❌ Performance optimizations not implemented (caching, debouncing)
- ❌ Incomplete error handling and recovery mechanisms
- ❌ Security hardening insufficient
- ❌ Edge cases and failure modes under-specified

---

## Critical Issues (P0 - Fix Immediately)

### Issue #1: Race Condition in Diagnostics Publishing

**Severity:** HIGH
**Location:** Backend details.md, Specification 11
**Impact:** Stale diagnostics shown to users, wasted CPU cycles

#### Problem
```csharp
public override async Task<Unit> Handle(DidChangeTextDocumentParams request, ...)
{
    var allDiagnostics = _parserService.GetDiagnostics(document);
    _languageServerFacade.TextDocument.PublishDiagnostics(...);
    return Unit.Value;
}
```

**Issues:**
1. No debouncing - runs on every keystroke
2. No cancellation - earlier validations complete after later ones
3. Concurrent validations for same document waste resources
4. User types fast → sees flickering/incorrect diagnostics

#### Solution
```csharp
public class CustomTextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource>
        _validationTokens = new();
    private readonly ILogger<CustomTextDocumentSyncHandler> _logger;

    public override async Task<Unit> Handle(
        DidChangeTextDocumentParams request,
        CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri.ToString();

        // Cancel previous validation for this document
        if (_validationTokens.TryRemove(uri, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        // Create new cancellation token
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _validationTokens[uri] = cts;

        try
        {
            // Debounce - wait 250ms for user to stop typing
            await Task.Delay(250, cts.Token);

            var document = _documentStore.GetDocument(request.TextDocument.Uri);
            if (document == null) return Unit.Value;

            // Run validation with cancellation support
            var diagnostics = await _parserService.GetDiagnosticsAsync(
                document, cts.Token);

            // Only publish if not cancelled
            if (!cts.Token.IsCancellationRequested)
            {
                _languageServerFacade.TextDocument.PublishDiagnostics(
                    new PublishDiagnosticsParams
                    {
                        Uri = document.Uri,
                        Diagnostics = new Container<Diagnostic>(diagnostics)
                    }
                );
            }
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

        return Unit.Value;
    }
}
```

**Estimated Effort:** 4 hours
**Testing Required:** Unit tests for concurrent validation, debounce timing tests

---

### Issue #2: No AST Caching - Performance Bottleneck

**Severity:** HIGH
**Location:** Backend details.md, Specification 7, lines 97-100
**Impact:** 3-5x slower response times, unnecessary CPU usage

#### Problem
```csharp
public ParameterContext GetParameterContext(TextDocument document, Position position)
{
    var ast = this.Parse(document.GetText()); // ← EXPENSIVE, runs every time
    // ...
}
```

**Scenario:**
1. User types `copy_file(` → CheckTrigger → **full parse**
2. User presses Ctrl+Space → textDocument/completion → **full parse again** (same document!)
3. User right-clicks → textDocument/codeAction → **full parse again**

Result: **Same document parsed 3+ times within 500ms**

#### Solution: Implement Document-Version-Based Caching
```csharp
public class ScribanParserService
{
    private readonly ConcurrentDictionary<string, CachedAst> _astCache = new();
    private readonly ILogger<ScribanParserService> _logger;
    private Timer _cleanupTimer;

    private class CachedAst
    {
        public int Version { get; set; }
        public ScriptPage Ast { get; set; }
        public List<Diagnostic> SyntaxErrors { get; set; }
        public DateTime LastAccess { get; set; }
        public long ParseTimeMs { get; set; }
    }

    public ScribanParserService(ApiSpecService apiSpec, ILogger<ScribanParserService> logger)
    {
        _apiSpecService = apiSpec;
        _logger = logger;

        // Cleanup stale entries every 5 minutes
        _cleanupTimer = new Timer(
            _ => EvictStaleEntries(),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
    }

    public ParameterContext GetParameterContext(TextDocument document, Position position)
    {
        var uri = document.Uri.ToString();
        var version = document.Version;

        // Try to get from cache
        ScriptPage ast;
        if (_astCache.TryGetValue(uri, out var cached) && cached.Version == version)
        {
            cached.LastAccess = DateTime.UtcNow;
            ast = cached.Ast;
            _logger.LogDebug("AST cache hit for {Uri} v{Version}", uri, version);
        }
        else
        {
            // Cache miss - parse and store
            var sw = Stopwatch.StartNew();
            var template = Template.Parse(document.GetText());
            sw.Stop();

            ast = template.Page;
            var syntaxErrors = template.Messages
                .Select(m => ConvertToLspDiagnostic(m))
                .ToList();

            _astCache[uri] = new CachedAst
            {
                Version = version,
                Ast = ast,
                SyntaxErrors = syntaxErrors,
                LastAccess = DateTime.UtcNow,
                ParseTimeMs = sw.ElapsedMilliseconds
            };

            _logger.LogInformation(
                "Parsed {Uri} v{Version} in {Ms}ms (size: {Size} chars)",
                uri, version, sw.ElapsedMilliseconds, document.GetText().Length);
        }

        // ... rest of method using cached AST
    }

    public async Task<List<Diagnostic>> GetDiagnosticsAsync(
        TextDocument document,
        CancellationToken cancellationToken)
    {
        var uri = document.Uri.ToString();
        var version = document.Version;

        // Check cache for syntax errors too
        if (_astCache.TryGetValue(uri, out var cached) && cached.Version == version)
        {
            cached.LastAccess = DateTime.UtcNow;

            // We have syntax errors cached
            var allDiagnostics = new List<Diagnostic>(cached.SyntaxErrors);

            // Only run semantic analysis if syntax is valid
            if (!allDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                var semanticErrors = await Task.Run(() =>
                    GetSemanticErrors(cached.Ast, cancellationToken),
                    cancellationToken);
                allDiagnostics.AddRange(semanticErrors);
            }

            return allDiagnostics;
        }

        // Not cached - parse (will be cached by Parse method)
        return await Task.Run(() =>
        {
            var template = Template.Parse(document.GetText());
            var diagnostics = new List<Diagnostic>();

            // Add syntax errors
            foreach (var msg in template.Messages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                diagnostics.Add(ConvertToLspDiagnostic(msg));
            }

            // Add semantic errors if no syntax errors
            if (!diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                var semanticErrors = GetSemanticErrors(template.Page, cancellationToken);
                diagnostics.AddRange(semanticErrors);
            }

            return diagnostics;
        }, cancellationToken);
    }

    private void EvictStaleEntries()
    {
        var threshold = DateTime.UtcNow.AddMinutes(-10);
        var evicted = 0;

        foreach (var key in _astCache.Keys.ToArray())
        {
            if (_astCache.TryGetValue(key, out var entry) &&
                entry.LastAccess < threshold)
            {
                if (_astCache.TryRemove(key, out _))
                {
                    evicted++;
                }
            }
        }

        if (evicted > 0)
        {
            _logger.LogInformation("Evicted {Count} stale AST cache entries", evicted);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}
```

**Expected Performance Improvement:**
- First parse: ~50ms (baseline)
- Cached reads: ~0.1ms (500x faster)
- Overall user experience: 3-5x faster completions/hovers

**Estimated Effort:** 6 hours
**Testing Required:**
- Cache hit/miss metrics
- Concurrent access tests
- Memory leak tests (cache growth)
- Performance benchmarks

---

### Issue #3: Missing Document URI Validation

**Severity:** HIGH
**Location:** Backend details.md, Specification 9, lines 356-359
**Impact:** Security risk, undefined behavior with multiple documents

#### Problem
```csharp
// From spec-9, CheckTrigger method
// Note: This requires a custom way to map SignalR ConnectionId to document URI
// Or, simpler: the client includes the document URI in the TriggerContext
TextDocument document = _textDocumentStore.GetDocument(context.Uri);
if (document == null) return;
```

**Issues:**
1. No validation that the connection owns this document
2. Client can send arbitrary URIs
3. Multi-document scenarios undefined
4. Race condition: document closed on server but client still references it

#### Solution: Add Document Session Management
```csharp
// New service: DocumentSessionService.cs
public interface IDocumentSessionService
{
    void RegisterDocument(string connectionId, string documentUri);
    void UnregisterDocument(string connectionId, string documentUri);
    bool ValidateAccess(string connectionId, string documentUri);
    IEnumerable<string> GetDocumentsForConnection(string connectionId);
    void CleanupConnection(string connectionId);
}

public class DocumentSessionService : IDocumentSessionService
{
    private readonly ConcurrentDictionary<string, HashSet<string>>
        _connectionToDocuments = new();
    private readonly ConcurrentDictionary<string, string>
        _documentToConnection = new();
    private readonly ILogger<DocumentSessionService> _logger;

    public DocumentSessionService(ILogger<DocumentSessionService> logger)
    {
        _logger = logger;
    }

    public void RegisterDocument(string connectionId, string documentUri)
    {
        _connectionToDocuments.AddOrUpdate(
            connectionId,
            _ => new HashSet<string> { documentUri },
            (_, docs) => { docs.Add(documentUri); return docs; }
        );

        _documentToConnection[documentUri] = connectionId;

        _logger.LogInformation(
            "Document {Uri} registered to connection {ConnectionId}",
            documentUri, connectionId);
    }

    public void UnregisterDocument(string connectionId, string documentUri)
    {
        if (_connectionToDocuments.TryGetValue(connectionId, out var docs))
        {
            docs.Remove(documentUri);
        }

        _documentToConnection.TryRemove(documentUri, out _);

        _logger.LogInformation(
            "Document {Uri} unregistered from connection {ConnectionId}",
            documentUri, connectionId);
    }

    public bool ValidateAccess(string connectionId, string documentUri)
    {
        // Check if this connection owns this document
        if (_documentToConnection.TryGetValue(documentUri, out var ownerConnectionId))
        {
            return ownerConnectionId == connectionId;
        }

        _logger.LogWarning(
            "Access denied: Connection {ConnectionId} attempted to access {Uri}",
            connectionId, documentUri);
        return false;
    }

    public IEnumerable<string> GetDocumentsForConnection(string connectionId)
    {
        if (_connectionToDocuments.TryGetValue(connectionId, out var docs))
        {
            return docs.ToList();
        }
        return Enumerable.Empty<string>();
    }

    public void CleanupConnection(string connectionId)
    {
        if (_connectionToDocuments.TryRemove(connectionId, out var docs))
        {
            foreach (var doc in docs)
            {
                _documentToConnection.TryRemove(doc, out _);
            }

            _logger.LogInformation(
                "Cleaned up {Count} documents for connection {ConnectionId}",
                docs.Count, connectionId);
        }
    }
}

// Update ScribanHub.cs
public class ScribanHub : Hub<IScribanClient>
{
    private readonly IDocumentSessionService _sessionService;
    private readonly LspBridge _lspBridge;
    private readonly ILogger<ScribanHub> _logger;

    public ScribanHub(
        IDocumentSessionService sessionService,
        LspBridge lspBridge,
        ILogger<ScribanHub> logger)
    {
        _sessionService = sessionService;
        _lspBridge = lspBridge;
        _logger = logger;
    }

    public async Task CheckTrigger(TriggerContext context)
    {
        // Validate access
        if (!_sessionService.ValidateAccess(Context.ConnectionId, context.Uri))
        {
            _logger.LogWarning(
                "CheckTrigger rejected: Connection {ConnectionId} doesn't own {Uri}",
                Context.ConnectionId, context.Uri);
            throw new UnauthorizedAccessException("Access denied to document");
        }

        // Proceed with logic...
        var document = _textDocumentStore.GetDocument(context.Uri);
        if (document == null)
        {
            _logger.LogWarning("Document not found: {Uri}", context.Uri);
            return;
        }

        // ... rest of CheckTrigger logic
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        _logger.LogInformation(
            "Client disconnected: {ConnectionId}, reason: {Reason}",
            Context.ConnectionId, exception?.Message ?? "normal");

        // Cleanup all documents for this connection
        _sessionService.CleanupConnection(Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }
}

// Update TextDocumentSyncHandler to register documents
public class CustomTextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly IDocumentSessionService _sessionService;

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, ...)
    {
        // Get connection ID from context (need to pass this through)
        var connectionId = GetConnectionIdFromContext(); // Implementation needed
        var uri = request.TextDocument.Uri.ToString();

        _sessionService.RegisterDocument(connectionId, uri);

        // ... rest of logic
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, ...)
    {
        var connectionId = GetConnectionIdFromContext();
        var uri = request.TextDocument.Uri.ToString();

        _sessionService.UnregisterDocument(connectionId, uri);

        // ... rest of logic
    }
}

// Register in Program.cs
builder.Services.AddSingleton<IDocumentSessionService, DocumentSessionService>();
```

**Estimated Effort:** 8 hours
**Testing Required:**
- Multi-document scenarios
- Connection cleanup tests
- Unauthorized access attempts
- Concurrent document operations

---

## High Priority Issues (P1 - Fix Before Production)

### Issue #4: Memory Leak in SignalRMessageAdapter

**Severity:** MEDIUM
**Location:** Frontend details.md, lines 107-116
**Impact:** Browser memory grows unbounded with reconnections

#### Problem
```typescript
this.hubConnection.on('ReceiveMessage', (message: any) => {
    // Handler registered but never removed
});
```

SignalR's `on()` method registers event handlers that persist until explicitly removed. If the adapter is disposed and recreated (e.g., during reconnection), old handlers accumulate.

#### Solution
```typescript
export class SignalRMessageAdapter implements MessageConnection {
    private receiveMessageHandler: ((message: any) => void) | null = null;
    private closeHandler: ((error?: Error) => void) | null = null;

    public async listen(): Promise<void> {
        if (this.hubConnection.state === HubConnectionState.Disconnected) {
            try {
                await this.hubConnection.start();
            } catch (e) {
                console.error('SignalR connection failed to start:', e);
                this._onError.fire([e as Error, undefined, 0]);
                this._onClose.fire();
                return;
            }
        }

        // Create named handlers for proper cleanup
        this.receiveMessageHandler = (message: any) => {
            if (this.isDisposed) return;
            this.handleMessage(message);
        };

        this.closeHandler = (error?: Error) => {
            if (this.isDisposed) return;
            this._onError.fire([error as Error, undefined, 0]);
            this._onClose.fire();
        };

        // Register handlers
        this.hubConnection.on('ReceiveMessage', this.receiveMessageHandler);
        this.hubConnection.onclose(this.closeHandler);
    }

    public dispose(): void {
        if (this.isDisposed) return;
        this.isDisposed = true;

        // Remove event handlers BEFORE stopping connection
        if (this.receiveMessageHandler) {
            this.hubConnection.off('ReceiveMessage', this.receiveMessageHandler);
            this.receiveMessageHandler = null;
        }

        if (this.closeHandler) {
            this.hubConnection.onclose(null); // Clear close handler
            this.closeHandler = null;
        }

        // Reject all pending requests
        for (const [id, handlers] of this.pendingRequests.entries()) {
            handlers.reject(new Error('Connection disposed'));
        }
        this.pendingRequests.clear();

        // Dispose emitters
        this._onNotification.dispose();
        this._onRequest.dispose();
        this._onError.dispose();
        this._onClose.dispose();

        // Stop connection
        this.hubConnection.stop();
    }
}
```

**Estimated Effort:** 2 hours
**Testing Required:**
- Multiple dispose/reconnect cycles
- Memory profiling in browser dev tools
- Long-running session tests

---

### Issue #5: Missing Reconnection State Sync

**Severity:** MEDIUM
**Location:** Frontend details.md, line 410
**Impact:** LSP features break silently after reconnection

#### Problem
```typescript
hubConnection.current = new HubConnectionBuilder()
    .withUrl(hubUrl)
    .withAutomaticReconnect() // ← Reconnects but doesn't resync state
    .build();
```

After reconnection:
- Server has lost document state (in-memory store is empty)
- Client doesn't re-send `textDocument/didOpen`
- Completions, hovers, diagnostics all fail
- User sees no error, just broken features

#### Solution
```typescript
export function useScribanEditor({
    editorRef,
    languageId,
    hubUrl,
}: UseScribanEditorProps): ScribanEditorApi {

    const [connectionState, setConnectionState] = useState<'connected' | 'reconnecting' | 'disconnected'>('disconnected');

    useEffect(() => {
        // ... existing setup code ...

        // Create SignalR Connection
        hubConnection.current = new HubConnectionBuilder()
            .withUrl(hubUrl)
            .withAutomaticReconnect({
                nextRetryDelayInMilliseconds: (retryContext) => {
                    // Exponential backoff: 0s, 2s, 10s, 30s
                    if (retryContext.previousRetryCount === 0) return 0;
                    if (retryContext.previousRetryCount === 1) return 2000;
                    if (retryContext.previousRetryCount === 2) return 10000;
                    return 30000;
                }
            })
            .configureLogging(LogLevel.Information)
            .build();

        // Handle reconnecting state
        hubConnection.current.onreconnecting((error) => {
            console.warn('SignalR reconnecting...', error);
            setConnectionState('reconnecting');

            // Optionally show UI notification
            // showNotification('Connection lost, reconnecting...');
        });

        // Handle successful reconnection
        hubConnection.current.onreconnected(async (connectionId) => {
            console.info('SignalR reconnected:', connectionId);
            setConnectionState('connected');

            try {
                // CRITICAL: Resync document state with server
                await resyncDocumentState();

                // showNotification('Connection restored', 'success');
            } catch (error) {
                console.error('Failed to resync after reconnection:', error);
                // showNotification('Connection restored but state sync failed', 'warning');
            }
        });

        // Handle disconnection
        hubConnection.current.onclose((error) => {
            console.error('SignalR closed:', error);
            setConnectionState('disconnected');

            // showNotification('Connection lost', 'error');
        });

        // ... rest of setup

    }, [editorRef, languageId, hubUrl]);

    /**
     * Resynchronizes document state with server after reconnection
     */
    const resyncDocumentState = useCallback(async () => {
        if (!editor.current || !languageClient.current) {
            return;
        }

        const model = editor.current.getModel();
        if (!model) {
            return;
        }

        console.info('Resyncing document state...');

        try {
            // 1. Re-send textDocument/didOpen to restore document on server
            await languageClient.current.sendNotification('textDocument/didOpen', {
                textDocument: {
                    uri: model.uri.toString(),
                    languageId: languageId,
                    version: model.getVersionId(),
                    text: model.getValue()
                }
            });

            // 2. Request fresh diagnostics
            // The server will automatically send them after processing didOpen

            // 3. Clear any stale picker state
            setPickerState(DEFAULT_PICKER_STATE);

            console.info('Document state resynced successfully');
        } catch (error) {
            console.error('Document resync failed:', error);
            throw error;
        }
    }, [languageId]);

    // Show connection state in UI (optional)
    useEffect(() => {
        if (connectionState === 'reconnecting') {
            // Show loading overlay or notification
        }
    }, [connectionState]);

    // ... rest of hook
}
```

**Additional: Add Server-Side Reconnection Detection**
```csharp
// In ScribanHub.cs
public override async Task OnConnectedAsync()
{
    var httpContext = Context.GetHttpContext();
    var isReconnection = httpContext?.Request.Query.ContainsKey("reconnect") ?? false;

    _logger.LogInformation(
        "Client connected: {ConnectionId} (reconnection: {IsReconnection})",
        Context.ConnectionId, isReconnection);

    if (isReconnection)
    {
        // Clear any stale state for this connection
        _sessionService.CleanupConnection(Context.ConnectionId);
    }

    await base.OnConnectedAsync();
}
```

**Estimated Effort:** 4 hours
**Testing Required:**
- Simulate network disconnection during editing
- Verify diagnostics work after reconnect
- Test multiple reconnection cycles
- Verify no duplicate documents registered

---

### Issue #6: No ApiSpec.json Validation

**Severity:** MEDIUM
**Location:** Backend details.md, Specification 2, lines 274-278
**Impact:** Runtime crashes from malformed metadata

#### Problem
ApiSpec.json is loaded and parsed without validation:
- Malformed JSON crashes server on startup
- Missing required fields cause `NullReferenceException` during runtime
- Invalid `picker` values (typos like "file-pikcer") fail silently
- No schema enforcement

#### Solution
```csharp
// ApiSpec/ApiSpecModels.cs - Add data annotations
public class ApiSpec
{
    [Required]
    public List<GlobalEntry> Globals { get; set; } = new();
}

public class GlobalEntry
{
    [Required, MinLength(1)]
    public string Name { get; set; }

    [Required, RegularExpression("^(object|function)$")]
    public string Type { get; set; }

    [Required]
    public string Hover { get; set; }

    public List<FunctionEntry> Members { get; set; } // For type="object"

    public List<ParameterEntry> Parameters { get; set; } // For type="function"
}

public class ParameterEntry
{
    [Required, MinLength(1)]
    public string Name { get; set; }

    [Required, RegularExpression("^(path|constant|string|number|boolean|any)$")]
    public string Type { get; set; }

    [Required, RegularExpression("^(file-picker|enum-list|none)$")]
    public string Picker { get; set; }

    public List<string> Options { get; set; } // Required if Picker == "enum-list"

    public List<string> Macros { get; set; } // Optional
}

// ApiSpec/ApiSpecValidator.cs
public class ApiSpecValidator
{
    public static ValidationResult Validate(ApiSpec spec)
    {
        var errors = new List<string>();

        // 1. Check for duplicate global names
        var duplicateGlobals = spec.Globals
            .GroupBy(g => g.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateGlobals.Any())
        {
            errors.Add($"Duplicate global names: {string.Join(", ", duplicateGlobals)}");
        }

        // 2. Validate each global entry
        foreach (var global in spec.Globals)
        {
            ValidateGlobalEntry(global, errors);
        }

        // 3. Check for reserved names
        var reservedNames = new[] { "for", "if", "end", "else", "while" };
        var conflicts = spec.Globals
            .Where(g => reservedNames.Contains(g.Name))
            .Select(g => g.Name)
            .ToList();

        if (conflicts.Any())
        {
            errors.Add($"Reserved names used: {string.Join(", ", conflicts)}");
        }

        return new ValidationResult
        {
            IsValid = !errors.Any(),
            Errors = errors
        };
    }

    private static void ValidateGlobalEntry(GlobalEntry entry, List<string> errors)
    {
        var context = $"Global '{entry.Name}'";

        // Type-specific validation
        if (entry.Type == "object")
        {
            if (entry.Members == null || !entry.Members.Any())
            {
                errors.Add($"{context}: Objects must have at least one member");
            }
            else
            {
                // Check for duplicate member names
                var duplicates = entry.Members
                    .GroupBy(m => m.Name)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key);

                if (duplicates.Any())
                {
                    errors.Add($"{context}: Duplicate member names: {string.Join(", ", duplicates)}");
                }

                // Validate each member
                foreach (var member in entry.Members)
                {
                    ValidateFunctionEntry(member, $"{context}.{member.Name}", errors);
                }
            }
        }
        else if (entry.Type == "function")
        {
            ValidateFunctionEntry(entry, context, errors);
        }
    }

    private static void ValidateFunctionEntry(dynamic entry, string context, List<string> errors)
    {
        if (entry.Parameters == null)
        {
            errors.Add($"{context}: Parameters array is required (use empty array if no parameters)");
            return;
        }

        // Validate each parameter
        for (int i = 0; i < entry.Parameters.Count; i++)
        {
            var param = entry.Parameters[i];
            ValidateParameter(param, $"{context}.param[{i}]({param.Name})", errors);
        }
    }

    private static void ValidateParameter(ParameterEntry param, string context, List<string> errors)
    {
        // Enum-list must have options
        if (param.Picker == "enum-list")
        {
            if (param.Options == null || !param.Options.Any())
            {
                errors.Add($"{context}: Picker 'enum-list' requires 'options' array");
            }

            if (param.Type != "constant")
            {
                errors.Add($"{context}: Picker 'enum-list' should have type 'constant' (found '{param.Type}')");
            }
        }

        // File-picker should be path type
        if (param.Picker == "file-picker" && param.Type != "path")
        {
            errors.Add($"{context}: Picker 'file-picker' should have type 'path' (found '{param.Type}')");
        }

        // Macros only valid for strings
        if (param.Macros != null && param.Macros.Any() && param.Type != "string")
        {
            errors.Add($"{context}: Macros are only valid for type 'string' (found '{param.Type}')");
        }

        // Options should only exist for enum-list
        if (param.Options != null && param.Options.Any() && param.Picker != "enum-list")
        {
            errors.Add($"{context}: Options should only be specified for picker 'enum-list' (found '{param.Picker}')");
        }
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}

// ApiSpec/ApiSpecService.cs - Updated
public class ApiSpecService
{
    private readonly ILogger<ApiSpecService> _logger;
    private readonly ApiSpec _spec;

    public ApiSpecService(IConfiguration configuration, ILogger<ApiSpecService> logger)
    {
        _logger = logger;

        var apiSpecPath = configuration["ApiSpec:Path"] ?? "ApiSpec.json";

        // 1. Check file exists
        if (!File.Exists(apiSpecPath))
        {
            throw new FileNotFoundException(
                $"ApiSpec.json not found at path: {apiSpecPath}. " +
                $"Please ensure the file exists and the path is correct.");
        }

        try
        {
            // 2. Read and deserialize
            var json = File.ReadAllText(apiSpecPath);
            _spec = JsonConvert.DeserializeObject<ApiSpec>(json);

            if (_spec == null)
            {
                throw new InvalidOperationException("Failed to deserialize ApiSpec.json");
            }

            // 3. Run validation
            var validationResult = ApiSpecValidator.Validate(_spec);

            if (!validationResult.IsValid)
            {
                var errorMessage = "ApiSpec.json validation failed:\n" +
                    string.Join("\n", validationResult.Errors.Select(e => $"  - {e}"));

                _logger.LogError(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            // 4. Log successful load
            _logger.LogInformation(
                "ApiSpec loaded successfully: {GlobalCount} globals, {FunctionCount} functions",
                _spec.Globals.Count,
                _spec.Globals.Count(g => g.Type == "function"));
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse ApiSpec.json");
            throw new InvalidOperationException(
                "ApiSpec.json contains invalid JSON. Please check the file format.", ex);
        }
    }

    // ... rest of service methods
}
```

**Add JSON Schema File**
Create `ApiSpec/apispec-schema.json`:
```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "title": "Scriban API Specification",
  "type": "object",
  "required": ["globals"],
  "properties": {
    "globals": {
      "type": "array",
      "items": {
        "$ref": "#/definitions/GlobalEntry"
      }
    }
  },
  "definitions": {
    "GlobalEntry": {
      "type": "object",
      "required": ["name", "type", "hover"],
      "properties": {
        "name": { "type": "string", "minLength": 1 },
        "type": { "enum": ["object", "function"] },
        "hover": { "type": "string" },
        "members": {
          "type": "array",
          "items": { "$ref": "#/definitions/FunctionEntry" }
        },
        "parameters": {
          "type": "array",
          "items": { "$ref": "#/definitions/ParameterEntry" }
        }
      }
    },
    "ParameterEntry": {
      "type": "object",
      "required": ["name", "type", "picker"],
      "properties": {
        "name": { "type": "string", "minLength": 1 },
        "type": { "enum": ["path", "constant", "string", "number", "boolean", "any"] },
        "picker": { "enum": ["file-picker", "enum-list", "none"] },
        "options": {
          "type": "array",
          "items": { "type": "string" }
        },
        "macros": {
          "type": "array",
          "items": { "type": "string" }
        }
      }
    }
  }
}
```

**Unit Tests**
```csharp
[Fact]
public void ApiSpecValidator_DuplicateGlobals_ReturnsError()
{
    var spec = new ApiSpec
    {
        Globals = new List<GlobalEntry>
        {
            new GlobalEntry { Name = "duplicate", Type = "function", Hover = "test" },
            new GlobalEntry { Name = "duplicate", Type = "object", Hover = "test" }
        }
    };

    var result = ApiSpecValidator.Validate(spec);

    Assert.False(result.IsValid);
    Assert.Contains("Duplicate global names: duplicate", result.Errors);
}

[Fact]
public void ApiSpecValidator_EnumListWithoutOptions_ReturnsError()
{
    var spec = new ApiSpec
    {
        Globals = new List<GlobalEntry>
        {
            new GlobalEntry
            {
                Name = "test_func",
                Type = "function",
                Hover = "test",
                Parameters = new List<ParameterEntry>
                {
                    new ParameterEntry
                    {
                        Name = "mode",
                        Type = "constant",
                        Picker = "enum-list"
                        // Missing Options!
                    }
                }
            }
        }
    };

    var result = ApiSpecValidator.Validate(spec);

    Assert.False(result.IsValid);
    Assert.Contains("enum-list' requires 'options' array", result.Errors[0]);
}
```

**Estimated Effort:** 6 hours
**Testing Required:**
- Valid ApiSpec loads successfully
- Invalid ApiSpec throws with clear error
- All validation rules tested
- Integration test with full app startup

---

## Medium Priority Issues (P2 - Before v1.0)

### Issue #7: Dual Protocol Complexity

**Severity:** MEDIUM
**Location:** High level specs.md, Section 2.4
**Impact:** Race conditions, developer confusion, debugging difficulty

#### Problem
The system runs two protocols in parallel:
1. **Standard LSP** (for completion, hover, diagnostics)
2. **Custom SignalR RPC** (for CheckTrigger, picker data)

**Conflicts:**
- User presses Ctrl+Space → sends BOTH `textDocument/completion` AND `CheckTrigger`
- Server must coordinate responses
- No clear ordering guarantee
- Debugging is complex (which protocol handled what?)

#### Current Behavior (from spec-12)
```csharp
// CompletionHandler case for file-picker
if (paramSpec.Picker == "file-picker")
{
    // Do NOTHING - let CheckTrigger handle this
    return new CompletionList();
}
```

This is fragile and implicit.

#### Recommendation: Choose One Approach

**Option A: LSP-Only (Preferred)**
```typescript
// Extend LSP with custom methods instead of parallel SignalR
languageClient.sendNotification('scriban/checkTrigger', {
    event: 'char',
    char: '(',
    position: lspPosition,
    uri: model.uri.toString()
});

// Server sends back custom notification
languageClient.onNotification('scriban/openPicker', (data) => {
    openPicker(data);
});
```

**Benefits:**
- Single protocol reduces complexity
- Standard LSP tooling works
- Easier debugging
- Better ordering guarantees

**Option B: Clear Protocol Boundaries**
```
LSP ONLY handles:
- textDocument/* (completion, hover, diagnostics, codeAction)
- workspace/* (applyEdit, executeCommand)

SignalR RPC ONLY handles:
- Picker data fetching (GetPathSuggestions)
- Real-time notifications (future: file watchers)

NEVER send both for same trigger
```

**Estimated Effort:** 16 hours (significant refactor)
**Recommendation:** Plan for v2.0, document workarounds for v1.0

---

### Issue #8: No Timeouts on Server Operations

**Severity:** MEDIUM
**Location:** Backend details.md, Specification 2
**Impact:** Hung requests, resource exhaustion

#### Problem
No timeouts specified for:
- File system operations (`GetPathSuggestions`)
- Parsing large documents
- Long-running validations
- SignalR method calls

**Scenario:**
```
User opens file picker → GetPathSuggestions called
→ Reads network drive → Drive offline → Hangs 60+ seconds
→ SignalR connection times out → User confused
```

#### Solution: Add Timeouts Everywhere

**1. Add Timeout Middleware**
```csharp
// Middleware/TimeoutMiddleware.cs
public class TimeoutMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TimeoutMiddleware> _logger;

    public TimeoutMiddleware(RequestDelegate next, ILogger<TimeoutMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(30)); // Global timeout

        try
        {
            context.RequestAborted = CancellationTokenSource
                .CreateLinkedTokenSource(context.RequestAborted, cts.Token)
                .Token;

            await _next(context);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Request timed out: {Path}",
                context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
            await context.Response.WriteAsync("Request timeout");
        }
    }
}
```

**2. Add Operation-Specific Timeouts**
```csharp
// FileSystemService.cs
public class FileSystemService
{
    private readonly ILogger<FileSystemService> _logger;
    private readonly SemaphoreSlim _throttle = new SemaphoreSlim(5);

    public async Task<List<string>> GetPathSuggestionsAsync(
        ParameterSpec spec,
        CancellationToken cancellationToken = default)
    {
        await _throttle.WaitAsync(cancellationToken);

        try
        {
            // Create timeout of 5 seconds
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            return await Task.Run(() =>
            {
                var results = new List<string>();
                var basePath = spec.BasePath ?? Directory.GetCurrentDirectory();

                // Guard against infinite loops
                int itemCount = 0;
                const int maxItems = 10000;

                try
                {
                    foreach (var path in Directory.EnumerateFileSystemEntries(
                        basePath,
                        "*",
                        SearchOption.TopDirectoryOnly))
                    {
                        // Check cancellation frequently
                        cts.Token.ThrowIfCancellationRequested();

                        if (++itemCount > maxItems)
                        {
                            _logger.LogWarning(
                                "Path suggestions exceeded max items ({Max})",
                                maxItems);
                            break;
                        }

                        results.Add(path);
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning(ex, "Access denied to path: {Path}", basePath);
                    // Return partial results rather than failing
                }
                catch (DirectoryNotFoundException ex)
                {
                    _logger.LogWarning(ex, "Directory not found: {Path}", basePath);
                    // Return empty results
                }

                return results;
            }, cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("GetPathSuggestions timed out for spec: {Spec}", spec);
            throw new TimeoutException("File system operation timed out");
        }
        finally
        {
            _throttle.Release();
        }
    }
}

// ScribanParserService.cs
public async Task<ScriptPage> ParseAsync(
    string code,
    CancellationToken cancellationToken = default)
{
    // Add parsing timeout (adjust based on document size)
    var timeout = TimeSpan.FromMilliseconds(Math.Min(5000, code.Length / 10 + 500));
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    cts.CancelAfter(timeout);

    return await Task.Run(() =>
    {
        try
        {
            var template = Template.Parse(code);
            cts.Token.ThrowIfCancellationRequested();
            return template.Page;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Parse timed out for document of size {Size} bytes",
                code.Length);
            throw new TimeoutException("Parsing timed out");
        }
    }, cts.Token);
}
```

**3. Add Timeout Configuration**
```json
// appsettings.json
{
  "Timeouts": {
    "GlobalRequestTimeoutSeconds": 30,
    "ParsingTimeoutSeconds": 10,
    "FileSystemTimeoutSeconds": 5,
    "ValidationTimeoutSeconds": 5
  },
  "Limits": {
    "MaxDocumentSizeBytes": 1048576,
    "MaxFileListSize": 10000,
    "MaxConcurrentFileOperations": 5
  }
}
```

**4. Add Timeout Attribute for SignalR**
```csharp
// Attributes/TimeoutAttribute.cs
[AttributeUsage(AttributeTargets.Method)]
public class TimeoutAttribute : Attribute
{
    public int Milliseconds { get; }

    public TimeoutAttribute(int milliseconds)
    {
        Milliseconds = milliseconds;
    }
}

// ScribanHub.cs
public class ScribanHub : Hub<IScribanClient>
{
    [Timeout(5000)] // 5 second timeout
    public async Task CheckTrigger(TriggerContext context)
    {
        // Implementation with timeout enforcement
    }

    [Timeout(10000)] // 10 second timeout for file operations
    public async Task<List<string>> GetPathSuggestions(
        string functionName,
        int parameterIndex)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await _fileSystemService.GetPathSuggestionsAsync(spec, cts.Token);
    }
}
```

**Estimated Effort:** 8 hours
**Testing Required:**
- Slow file system simulation
- Large document parsing
- Concurrent operation limits
- Timeout error handling

---

### Issue #9: Picker Positioning Fragility

**Severity:** LOW
**Location:** Frontend details.md, lines 515-523
**Impact:** Poor UX with scrolling, zoom, transforms

#### Problem
Manual position calculation breaks in many scenarios:
```typescript
const editorRect = editorDom.getBoundingClientRect();
return {
    x: editorRect.left + coords.left,
    y: editorRect.top + coords.top + coords.height,
};
```

**Breaks with:**
- Page scroll
- CSS transforms on parent
- Iframe embedding
- Browser zoom
- Editor inside scrollable container

#### Solution: Use Monaco Content Widgets

```typescript
// components/MonacoPickerWidget.ts
export class MonacoPickerWidget implements monaco.editor.IContentWidget {
    private domNode: HTMLElement;
    private position: monaco.IPosition | null = null;

    constructor(
        private readonly editor: monaco.editor.IStandaloneCodeEditor,
        private readonly onSelect: (value: string) => void,
        private readonly onCancel: () => void
    ) {
        this.domNode = document.createElement('div');
        this.domNode.className = 'scriban-picker-widget';
        this.domNode.style.zIndex = '1000';
    }

    getId(): string {
        return 'scriban.picker.widget';
    }

    getDomNode(): HTMLElement {
        return this.domNode;
    }

    getPosition(): monaco.editor.IContentWidgetPosition | null {
        if (!this.position) {
            return null;
        }

        return {
            position: this.position,
            preference: [
                monaco.editor.ContentWidgetPositionPreference.BELOW,
                monaco.editor.ContentWidgetPositionPreference.ABOVE
            ]
        };
    }

    show(position: monaco.IPosition, component: React.ReactElement): void {
        this.position = position;

        // Render React component into widget DOM
        const root = createRoot(this.domNode);
        root.render(
            <PickerWrapper onSelect={this.onSelect} onCancel={this.onCancel}>
                {component}
            </PickerWrapper>
        );

        // Add widget to editor - Monaco handles positioning!
        this.editor.addContentWidget(this);

        // Add keyboard handler to close on Escape
        this.editor.focus();
    }

    hide(): void {
        this.editor.removeContentWidget(this);
        this.position = null;
    }

    dispose(): void {
        this.hide();
    }
}

// Update useScribanEditor
export function useScribanEditor(props: UseScribanEditorProps) {
    const pickerWidget = useRef<MonacoPickerWidget | null>(null);

    const openPicker = useCallback((
        functionName: string,
        parameterIndex: number,
        currentValue: string | null = null
    ) => {
        if (!editor.current) return;

        // Create widget if needed
        if (!pickerWidget.current) {
            pickerWidget.current = new MonacoPickerWidget(
                editor.current,
                handlePickerSelect,
                handlePickerCancel
            );
        }

        const position = editor.current.getPosition();
        if (!position) return;

        // Show picker at current position
        const component = (
            <FilePicker
                functionName={functionName}
                parameterIndex={parameterIndex}
                currentValue={currentValue}
            />
        );

        pickerWidget.current.show(position, component);
    }, [handlePickerSelect, handlePickerCancel]);

    const handlePickerCancel = useCallback(() => {
        pickerWidget.current?.hide();
    }, []);

    // Cleanup on unmount
    useEffect(() => {
        return () => {
            pickerWidget.current?.dispose();
        };
    }, []);
}
```

**Benefits:**
- Monaco handles all positioning logic
- Works with scroll, zoom, transforms
- Consistent with editor UI
- Auto-repositions on editor layout changes

**Estimated Effort:** 6 hours
**Testing Required:**
- Test in iframe
- Test with CSS transforms
- Test with page scroll
- Test with browser zoom

---

## Security Issues

### Issue #10: CORS Configuration Hardcoded

**Severity:** MEDIUM
**Location:** Backend details.md, Specification 13, lines 938-948

#### Problem
```csharp
policy.WithOrigins("http://localhost:3000") // ← Will deploy to production!
      .AllowAnyHeader()  // ← Too permissive
      .AllowAnyMethod(); // ← Too permissive
```

#### Solution
See full solution in consolidated section below.

---

### Issue #11: No Input Validation

**Severity:** MEDIUM
**Impact:** Potential DoS, injection attacks

#### Problem
SignalR methods accept user input without validation:
- No URI validation in `CheckTrigger`
- No parameter sanitization
- No document size limits
- No rate limiting

#### Solution: Add Comprehensive Validation

```csharp
// Validation/InputValidator.cs
public static class InputValidator
{
    private const int MaxDocumentUriLength = 2048;
    private const int MaxLineLength = 10000;
    private const int MaxDocumentSize = 1024 * 1024; // 1MB

    public static void ValidateDocumentUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("Document URI cannot be empty");
        }

        if (uri.Length > MaxDocumentUriLength)
        {
            throw new ArgumentException(
                $"Document URI too long (max {MaxDocumentUriLength} chars)");
        }

        // Must be a valid URI
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
        {
            throw new ArgumentException("Invalid document URI format");
        }

        // Only allow specific schemes
        var allowedSchemes = new[] { "file", "untitled", "inmemory" };
        if (!allowedSchemes.Contains(parsedUri.Scheme.ToLowerInvariant()))
        {
            throw new ArgumentException(
                $"URI scheme '{parsedUri.Scheme}' not allowed");
        }
    }

    public static void ValidatePosition(Position position)
    {
        if (position == null)
        {
            throw new ArgumentNullException(nameof(position));
        }

        if (position.Line < 0 || position.Line > 100000)
        {
            throw new ArgumentException($"Invalid line number: {position.Line}");
        }

        if (position.Character < 0 || position.Character > MaxLineLength)
        {
            throw new ArgumentException(
                $"Invalid character position: {position.Character}");
        }
    }

    public static void ValidateDocumentSize(string content)
    {
        if (content.Length > MaxDocumentSize)
        {
            throw new ArgumentException(
                $"Document too large (max {MaxDocumentSize} bytes)");
        }
    }

    public static string SanitizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        // Remove dangerous path traversal patterns
        path = path.Replace("..", "").Replace("~", "");

        // Normalize path separators
        path = path.Replace('\\', '/');

        return path;
    }
}

// Update ScribanHub.cs
public class ScribanHub : Hub<IScribanClient>
{
    private static readonly SemaphoreSlim _rateLimiter = new SemaphoreSlim(10);

    public async Task CheckTrigger(TriggerContext context)
    {
        // Rate limiting
        if (!await _rateLimiter.WaitAsync(0))
        {
            _logger.LogWarning(
                "Rate limit exceeded for connection {ConnectionId}",
                Context.ConnectionId);
            throw new InvalidOperationException("Too many requests");
        }

        try
        {
            // Input validation
            InputValidator.ValidateDocumentUri(context.Uri);
            InputValidator.ValidatePosition(context.Position);

            if (context.Line?.Length > 10000)
            {
                throw new ArgumentException("Line too long");
            }

            // Validate access
            if (!_sessionService.ValidateAccess(Context.ConnectionId, context.Uri))
            {
                throw new UnauthorizedAccessException("Access denied");
            }

            // ... rest of logic
        }
        finally
        {
            _rateLimiter.Release();
        }
    }
}
```

**Add Rate Limiting Per Connection**
```csharp
// Services/RateLimitService.cs
public class RateLimitService
{
    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new();

    private class TokenBucket
    {
        private int _tokens;
        private DateTime _lastRefill;
        private readonly int _maxTokens;
        private readonly TimeSpan _refillInterval;

        public TokenBucket(int maxTokens, TimeSpan refillInterval)
        {
            _maxTokens = maxTokens;
            _tokens = maxTokens;
            _refillInterval = refillInterval;
            _lastRefill = DateTime.UtcNow;
        }

        public bool TryConsume()
        {
            lock (this)
            {
                Refill();

                if (_tokens > 0)
                {
                    _tokens--;
                    return true;
                }

                return false;
            }
        }

        private void Refill()
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastRefill;

            if (elapsed >= _refillInterval)
            {
                _tokens = _maxTokens;
                _lastRefill = now;
            }
        }
    }

    public bool TryAcquire(string connectionId)
    {
        var bucket = _buckets.GetOrAdd(
            connectionId,
            _ => new TokenBucket(maxTokens: 10, refillInterval: TimeSpan.FromSeconds(1)));

        return bucket.TryConsume();
    }

    public void RemoveConnection(string connectionId)
    {
        _buckets.TryRemove(connectionId, out _);
    }
}

// Use in Hub
public async Task CheckTrigger(TriggerContext context)
{
    if (!_rateLimitService.TryAcquire(Context.ConnectionId))
    {
        throw new InvalidOperationException(
            "Rate limit exceeded: max 10 requests per second");
    }

    // ... rest of logic
}
```

**Estimated Effort:** 10 hours
**Testing Required:**
- Fuzzing tests with malformed input
- Rate limiting tests
- Path traversal attempts
- Large input tests

---

## Testing Strategy Issues

### Issue #12: Missing Critical Test Scenarios

**Severity:** MEDIUM
**Location:** High level specs.md, Test Categories section

#### Missing Test Categories

**1. Chaos Engineering Tests**
```csharp
[Fact]
public async Task ServerDisconnectsDuringCheckTrigger_ShouldHandleGracefully()
{
    // Simulate server crash during CheckTrigger
    var hub = CreateHub();
    var task = hub.CheckTrigger(validContext);

    // Kill server mid-request
    await server.StopAsync();

    // Should throw clear exception, not hang
    await Assert.ThrowsAsync<HubException>(() => task);
}

[Fact]
public async Task NetworkLatency500ms_ShouldStillComplete()
{
    // Add 500ms delay to all network calls
    using var latencySimulator = new NetworkLatencySimulator(500);

    var response = await client.SendCompletion(...);

    Assert.NotNull(response);
    Assert.True(response.LatencyMs < 1000); // Should still be under 1s
}
```

**2. Load Tests**
```csharp
[Fact]
public async Task _100ConcurrentUsers_ShouldHandleLoad()
{
    var clients = Enumerable.Range(0, 100)
        .Select(_ => CreateClient())
        .ToList();

    var tasks = clients.Select(async client =>
    {
        await client.OpenDocument(...);
        await client.TypeText("os.");
        var completion = await client.WaitForCompletion();
        Assert.NotEmpty(completion.Items);
    });

    await Task.WhenAll(tasks);

    // Check server didn't crash
    Assert.True(server.IsHealthy);
}
```

**3. Malicious Input Tests**
```csharp
[Theory]
[InlineData("{{ for i in (1..1000000) }} x {{ end }}")]  // Huge loop
[InlineData("{{ func(func(func(func(func(func(func(func(func(...)))))))))) }}")]  // Deep nesting
[InlineData(/* 10MB string */)]  // Huge document
public async Task MaliciousInput_ShouldNotCrashServer(string code)
{
    var result = await parserService.Parse(code);

    // Should handle gracefully with error, not crash
    Assert.NotNull(result);
}
```

**Estimated Effort:** 16 hours
**Priority:** Before v1.0 release

---

## Architecture Recommendations

### Recommendation #1: Add Request/Response Middleware Layer

**Current:**
```
[Client] ↔ [Hub] ↔ [Handler]
```

**Proposed:**
```
[Client] ↔ [Hub] ↔ [Middleware] ↔ [Handler]
                       ↓
                 - Logging
                 - Rate Limiting
                 - Timeout
                 - Validation
                 - Metrics
```

**Implementation:**
```csharp
// Middleware/IHubMiddleware.cs
public interface IHubMiddleware
{
    Task InvokeAsync(HubInvocationContext context, Func<Task> next);
}

// Middleware/LoggingMiddleware.cs
public class LoggingMiddleware : IHubMiddleware
{
    private readonly ILogger _logger;

    public async Task InvokeAsync(HubInvocationContext context, Func<Task> next)
    {
        var sw = Stopwatch.StartNew();

        _logger.LogInformation(
            "Hub method invoked: {Method} by {ConnectionId}",
            context.HubMethodName,
            context.Context.ConnectionId);

        try
        {
            await next();

            _logger.LogInformation(
                "Hub method completed: {Method} in {Ms}ms",
                context.HubMethodName,
                sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Hub method failed: {Method} after {Ms}ms",
                context.HubMethodName,
                sw.ElapsedMilliseconds);
            throw;
        }
    }
}

// Register in Program.cs
builder.Services.AddSignalR(options =>
{
    options.AddFilter<LoggingMiddleware>();
    options.AddFilter<RateLimitMiddleware>();
    options.AddFilter<ValidationMiddleware>();
});
```

---

### Recommendation #2: Add Health Checks

```csharp
// Health/ApiSpecHealthCheck.cs
public class ApiSpecHealthCheck : IHealthCheck
{
    private readonly ApiSpecService _apiSpec;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var globals = _apiSpec.GetGlobals();

            if (globals == null || !globals.Any())
            {
                return HealthCheckResult.Unhealthy("No globals loaded");
            }

            return HealthCheckResult.Healthy(
                $"{globals.Count()} globals loaded");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("ApiSpec error", ex);
        }
    }
}

// Register in Program.cs
builder.Services.AddHealthChecks()
    .AddCheck<ApiSpecHealthCheck>("apispec")
    .AddCheck("signalr", () =>
    {
        // Check if SignalR hub is responsive
        return HealthCheckResult.Healthy();
    })
    .AddCheck("cache", () =>
    {
        var cacheSize = _parserService.GetCacheSize();
        return HealthCheckResult.Healthy($"Cache: {cacheSize} entries");
    });

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
```

---

### Recommendation #3: Add Telemetry

```csharp
// Services/MetricsService.cs
public interface IMetricsService
{
    void RecordRequest(string method, long durationMs, bool success);
    void RecordCacheHit(string operation);
    void RecordCacheMiss(string operation);
    void RecordError(string category);
    IDisposable StartTimer(string operation);
}

public class MetricsService : IMetricsService
{
    private readonly ILogger<MetricsService> _logger;

    // Use System.Diagnostics.Metrics (new in .NET 6+)
    private readonly Meter _meter;
    private readonly Counter<long> _requestCounter;
    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;

    public MetricsService(ILogger<MetricsService> logger)
    {
        _logger = logger;
        _meter = new Meter("ScribanLanguageServer");

        _requestCounter = _meter.CreateCounter<long>(
            "requests_total",
            description: "Total number of requests");

        _requestDuration = _meter.CreateHistogram<double>(
            "request_duration_ms",
            description: "Request duration in milliseconds");

        _cacheHits = _meter.CreateCounter<long>("cache_hits_total");
        _cacheMisses = _meter.CreateCounter<long>("cache_misses_total");
    }

    public void RecordRequest(string method, long durationMs, bool success)
    {
        _requestCounter.Add(1, new KeyValuePair<string, object>("method", method));
        _requestDuration.Record(durationMs, new KeyValuePair<string, object>("method", method));

        _logger.LogInformation(
            "Request: {Method} completed in {Ms}ms (success: {Success})",
            method, durationMs, success);
    }

    public IDisposable StartTimer(string operation)
    {
        return new TimerScope(operation, this);
    }

    private class TimerScope : IDisposable
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly string _operation;
        private readonly MetricsService _metrics;

        public TimerScope(string operation, MetricsService metrics)
        {
            _operation = operation;
            _metrics = metrics;
        }

        public void Dispose()
        {
            _sw.Stop();
            _metrics.RecordRequest(_operation, _sw.ElapsedMilliseconds, true);
        }
    }
}

// Use in handlers
public class CompletionHandler
{
    private readonly IMetricsService _metrics;

    public override async Task<CompletionList> Handle(...)
    {
        using var timer = _metrics.StartTimer("completion");

        try
        {
            var result = await GetCompletions();
            return result;
        }
        catch (Exception ex)
        {
            _metrics.RecordError("completion");
            throw;
        }
    }
}
```

---

## Performance Optimization Summary

| Optimization | Impact | Effort | Priority |
|--------------|--------|--------|----------|
| AST Caching | 3-5x faster | Medium | P0 |
| Debounced validation | 50% less CPU | Low | P0 |
| Concurrent request limiting | Prevent DoS | Low | P1 |
| File system timeouts | Prevent hangs | Low | P1 |
| Request batching | 20% faster | Medium | P2 |
| Incremental parsing | 2x faster | High | P3 |

---

## Security Hardening Checklist

### Must-Have for Production

- [ ] Input validation on all endpoints
- [ ] Rate limiting (10 req/sec per connection)
- [ ] Timeout on all operations (5-30s)
- [ ] Maximum document size (1MB)
- [ ] Maximum file list size (10K items)
- [ ] Path traversal prevention
- [ ] CORS properly configured from appsettings
- [ ] ApiSpec.json validation on startup
- [ ] Error messages don't leak system paths
- [ ] Connection cleanup on disconnect

### Nice-to-Have

- [ ] Authentication/authorization hooks
- [ ] Request signing
- [ ] Content Security Policy headers
- [ ] API versioning
- [ ] Audit logging

---

## Implementation Roadmap

### Phase 1: Critical Fixes (Week 1-2)
**Goal:** Make system stable and performant

- [ ] Issue #1: Race condition in diagnostics
- [ ] Issue #2: AST caching
- [ ] Issue #3: Document URI validation
- [ ] Issue #4: Memory leak in adapter
- [ ] Basic error handling
- [ ] Basic logging

**Deliverable:** System works reliably for single user

---

### Phase 2: Production Readiness (Week 3-4)
**Goal:** Make system production-ready

- [ ] Issue #5: Reconnection logic
- [ ] Issue #6: ApiSpec validation
- [ ] Issue #8: Timeouts everywhere
- [ ] Issue #11: Input validation
- [ ] Health checks
- [ ] Basic metrics

**Deliverable:** System can run in production

---

### Phase 3: Scale & Polish (Week 5-6)
**Goal:** Handle load and edge cases

- [ ] Issue #12: Extended test coverage
- [ ] Load testing (100 users)
- [ ] Chaos testing
- [ ] Performance tuning
- [ ] Documentation updates
- [ ] Deployment guide

**Deliverable:** System handles production load

---

### Phase 4: Enhancement (Week 7+)
**Goal:** Improve UX and add features

- [ ] Issue #7: Protocol simplification
- [ ] Issue #9: Better picker positioning
- [ ] Advanced telemetry
- [ ] User analytics
- [ ] Performance dashboards

**Deliverable:** Polished v1.0 release

---

## Quick Wins (Do These First)

### 1. Add Startup Validation (30 min)
```csharp
if (!File.Exists("ApiSpec.json"))
    throw new FileNotFoundException("ApiSpec.json required");
```

### 2. Add Basic Logging (1 hour)
```csharp
_logger.LogDebug("CheckTrigger: {Context}", context);
```

### 3. Add Global Exception Handler (1 hour)
```csharp
app.UseExceptionHandler("/error");
```

### 4. Add Timeout Middleware (2 hours)
```csharp
app.UseTimeout(TimeSpan.FromSeconds(30));
```

### 5. Add Memory Cache (30 min)
```csharp
builder.Services.AddMemoryCache();
```

### Total Quick Wins: ~5 hours for major stability improvements

---

## Metrics to Track

### Performance Metrics
- **Request Latency** (p50, p95, p99)
  - Completion: < 100ms (p95)
  - Hover: < 50ms (p95)
  - Diagnostics: < 300ms (p95)
  - Picker open: < 200ms (p95)

- **Cache Hit Rate**
  - Target: > 80% for AST cache

- **Parse Time vs Document Size**
  - Target: < 50ms for 10KB document

### Reliability Metrics
- **Error Rate**
  - Target: < 1% of all requests

- **Connection Success Rate**
  - Target: > 99% successful connections

- **Reconnection Success Rate**
  - Target: > 95% successful reconnections

### Resource Metrics
- **Memory per Document**
  - Target: < 5MB per document

- **CPU Usage**
  - Target: < 50% under normal load

- **Concurrent Connections**
  - Target: Support 1000 simultaneous users

---

## Configuration Best Practices

### appsettings.json Structure
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "ScribanLanguageServer": "Debug"
    }
  },

  "ApiSpec": {
    "Path": "ApiSpec.json",
    "ValidateOnStartup": true,
    "ReloadOnChange": false
  },

  "Cors": {
    "AllowedOrigins": ["http://localhost:3000"],
    "AllowedHeaders": ["Content-Type", "Authorization"],
    "AllowedMethods": ["GET", "POST"]
  },

  "Timeouts": {
    "GlobalRequestSeconds": 30,
    "ParsingSeconds": 10,
    "FileSystemSeconds": 5,
    "ValidationSeconds": 5
  },

  "Limits": {
    "MaxDocumentSizeBytes": 1048576,
    "MaxFileListSize": 10000,
    "MaxConcurrentFileOperations": 5,
    "MaxConnectionsPerUser": 3
  },

  "Cache": {
    "AstCacheMaxEntries": 1000,
    "AstCacheExpirationMinutes": 10,
    "EnableCaching": true
  },

  "RateLimiting": {
    "RequestsPerSecond": 10,
    "BurstSize": 20
  }
}
```

---

## Conclusion

The Scriban Language Server design is **fundamentally sound** with excellent architecture and documentation. However, it requires significant **production hardening** before deployment.

### Critical Path to Production

1. **Week 1-2:** Fix P0 issues (race conditions, caching, validation)
2. **Week 3-4:** Add production requirements (timeouts, monitoring, security)
3. **Week 5-6:** Test and tune (load testing, chaos engineering)
4. **Week 7+:** Polish and enhance

### Investment Required
- **Development:** ~120 hours (3 weeks for 1 developer)
- **Testing:** ~40 hours
- **Documentation:** ~20 hours
- **Total:** ~180 hours (~1 month)

### Expected Outcome
A robust, production-ready language server that provides an excellent developer experience while maintaining stability, security, and performance under load.

---

**Next Steps:**
1. Review and prioritize issues
2. Create detailed implementation tickets
3. Set up CI/CD pipeline with tests
4. Begin Phase 1 implementation

---

*End of Design Evaluation Report*
