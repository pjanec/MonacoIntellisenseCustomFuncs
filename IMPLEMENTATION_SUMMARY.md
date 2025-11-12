# Implementation Plan - Summary & Remaining Stages

**Complete Implementation Roadmap**
**Companion to:** IMPLEMENTATION_PLAN.md and IMPLEMENTATION_PLAN_PART2.md

---

## Quick Reference: All Stages

| Stage | Component | Focus | Duration | Tests | Can Start After |
|-------|-----------|-------|----------|-------|-----------------|
| **B1** | Backend | Foundation & Validation | 1 week | 12 | - |
| **B2** | Backend | Core Services | 2 weeks | 35 | B1 |
| **B3** | Backend | LSP Handlers | 2 weeks | 28 | B2 |
| **B4** | Backend | SignalR & Integration | 1.5 weeks | 15 | B3 |
| **F1** | Frontend | Foundation & Mocks | 1 week | 10 | - |
| **F2** | Frontend | Editor Setup | 1.5 weeks | 12 | F1 |
| **F3** | Frontend | LSP Client | 2 weeks | 18 | F2 |
| **F4** | Frontend | Custom UI | 1.5 weeks | 15 | F3 |
| **I1** | Integration | Basic Flow | 1 week | 8 | B4, F4 |
| **B5** | Backend | Advanced Features | 2 weeks | 20 | I1 |
| **F5** | Frontend | Advanced Features | 2 weeks | 18 | I1 |
| **I2** | Integration | Full Integration | 1 week | 12 | B5, F5 |
| **P1** | Polish | Performance & Hardening | 2 weeks | 25 | I2 |

**Total:** ~14 weeks with parallel development

---

## Stage B4: SignalR & Communication Layer

**Duration:** 1.5 weeks
**Dependencies:** B3 complete

### Objectives
1. Implement SignalR hub with custom methods
2. Implement LspBridge service
3. Wire up LSP server with SignalR transport
4. Test with mock SignalR clients

### Key Deliverables

**Files to Create:**
- `Server/Hubs/ScribanHub.cs` - Main SignalR hub
- `Server/Hubs/IScribanClient.cs` - Client interface
- `Server/Services/LspBridge.cs` - LSP<->SignalR bridge
- `Server/Validation/InputValidator.cs` - Input validation
- `Server/Program.cs` - Application startup

**Critical Features:**
1. **Input Validation on All Hub Methods**
   ```csharp
   public async Task CheckTrigger(TriggerContext context)
   {
       // Validate URI
       InputValidator.ValidateDocumentUri(context.Uri);
       InputValidator.ValidatePosition(context.Position);

       // Validate access
       if (!_sessionService.ValidateAccess(Context.ConnectionId, context.Uri))
           throw new UnauthorizedAccessException();

       // Process request...
   }
   ```

2. **Rate Limiting Per Connection**
   ```csharp
   private readonly ConcurrentDictionary<string, TokenBucket> _rateLimiters = new();

   public async Task CheckTrigger(TriggerContext context)
   {
       if (!TryAcquireToken(Context.ConnectionId))
           throw new InvalidOperationException("Rate limit exceeded");
       // ...
   }
   ```

3. **LspBridge Implementation**
   ```csharp
   public class LspBridge
   {
       public void Initialize(ILanguageServer lspServer)
       {
           _lspServer = lspServer;

           // Forward server output to all clients
           _lspServer.Output.Subscribe(message =>
           {
               _hubContext.Clients.All.ReceiveMessage(message);
           });
       }

       public Task SendToLanguageServer(JToken message)
       {
           return _lspServer.Input.Send(message);
       }
   }
   ```

### Tests (15 total)

**Unit Tests:**
- Hub method input validation (5 tests)
- Rate limiting behavior (3 tests)
- LspBridge message forwarding (4 tests)

**Integration Tests:**
- Mock SignalR client connects and sends messages (3 tests)

### Success Criteria
- ✅ Hub accepts connections from test clients
- ✅ CheckTrigger validates input and access
- ✅ GetPathSuggestions returns mocked file lists
- ✅ LspBridge forwards messages bidirectionally
- ✅ Rate limiting prevents abuse
- ✅ All tests pass: `dotnet test --filter "Stage=B4"`

---

## Stage F1: Frontend Foundation & Mocks

**Duration:** 1 week
**Dependencies:** None (can run parallel with B1-B3)

### Objectives
1. Set up React + Vite project
2. Create mock services for all backend interactions
3. Set up testing infrastructure
4. Create basic project structure

### Key Deliverables

**Project Setup:**
```bash
npm create vite@latest scriban-client -- --template react-ts
cd scriban-client
npm install @microsoft/signalr
npm install monaco-editor monaco-languageclient
npm install vscode-languageserver-protocol
npm install -D vitest @testing-library/react @testing-library/jest-dom
npm install -D @testing-library/user-event msw
```

**Directory Structure:**
```
Frontend/
├── src/
│   ├── services/
│   │   ├── SignalRAdapter.ts
│   │   └── __mocks__/
│   │       ├── MockSignalRConnection.ts
│   │       └── MockLspServer.ts
│   ├── hooks/
│   │   └── useScribanEditor.ts
│   ├── components/
│   │   ├── Editor/
│   │   └── Pickers/
│   ├── mocks/
│   │   ├── handlers.ts           # MSW request handlers
│   │   └── server.ts              # MSW server setup
│   └── __tests__/
│       └── setup.ts
├── vitest.config.ts
└── package.json
```

**Mock SignalR Connection:**
```typescript
export class MockSignalRConnection {
    private handlers = new Map<string, Function>();
    private isConnected = false;

    async start(): Promise<void> {
        this.isConnected = true;
    }

    async stop(): Promise<void> {
        this.isConnected = false;
    }

    on(methodName: string, handler: Function): void {
        this.handlers.set(methodName, handler);
    }

    off(methodName: string): void {
        this.handlers.delete(methodName);
    }

    async invoke<T>(methodName: string, ...args: any[]): Promise<T> {
        if (!this.isConnected) {
            throw new Error('Not connected');
        }

        // Return canned responses based on method
        switch (methodName) {
            case 'CheckTrigger':
                return undefined as T;
            case 'GetPathSuggestions':
                return ['file1.txt', 'file2.txt', 'folder/'] as any;
            default:
                return undefined as T;
        }
    }

    // Simulate server-sent messages
    simulateServerMessage(methodName: string, data: any): void {
        const handler = this.handlers.get(methodName);
        if (handler) {
            handler(data);
        }
    }
}
```

### Tests (10 total)
- Mock connection lifecycle (3 tests)
- Mock server message simulation (4 tests)
- Mock data fetching (3 tests)

### Success Criteria
- ✅ Project builds successfully
- ✅ Vitest runs and discovers tests
- ✅ Mock services provide predictable responses
- ✅ Can simulate full request/response cycle without backend
- ✅ All tests pass: `npm test`

---

## Stage F2: Frontend Editor Setup

**Duration:** 1.5 weeks
**Dependencies:** F1 complete

### Objectives
1. Integrate Monaco Editor
2. Set up basic editor configuration
3. Create editor wrapper component
4. Test editor renders and accepts input

### Key Deliverables

**Editor Component:**
```typescript
// src/components/Editor/ScribanEditor.tsx
export function ScribanEditor() {
    const editorRef = useRef<HTMLDivElement>(null);
    const [editor, setEditor] = useState<monaco.editor.IStandaloneCodeEditor | null>(null);

    useEffect(() => {
        if (!editorRef.current) return;

        const instance = monaco.editor.create(editorRef.current, {
            value: '{{ # Start typing... }}',
            language: 'scriban',
            theme: 'vs-dark',
            automaticLayout: true,
            minimap: { enabled: false }
        });

        setEditor(instance);

        return () => instance.dispose();
    }, []);

    return <div ref={editorRef} style={{ height: '100vh', width: '100%' }} />;
}
```

**Custom Language Registration:**
```typescript
// src/services/scribanLanguage.ts
export function registerScribanLanguage() {
    monaco.languages.register({ id: 'scriban' });

    monaco.languages.setMonarchTokensProvider('scriban', {
        tokenizer: {
            root: [
                [/{{/, 'delimiter.curly'],
                [/}}/, 'delimiter.curly'],
                [/[a-zA-Z_]\w*/, 'identifier'],
                // Add more tokenization rules...
            ]
        }
    });
}
```

### Tests (12 total)
- Editor renders without errors (2 tests)
- Editor accepts text input (3 tests)
- Editor configuration applied (3 tests)
- Language registration works (2 tests)
- Editor disposal cleans up (2 tests)

### Success Criteria
- ✅ Editor renders in browser
- ✅ Can type and see syntax highlighting
- ✅ Editor responds to keyboard input
- ✅ Component tests pass
- ✅ No memory leaks on mount/unmount

---

## Stage F3: Frontend LSP Client

**Duration:** 2 weeks
**Dependencies:** F2 complete

### Objectives
1. Implement SignalRMessageAdapter
2. Set up monaco-languageclient
3. Connect adapter to mock SignalR
4. Test LSP message flow

### Key Deliverables

**SignalRMessageAdapter (from spec):**
```typescript
export class SignalRMessageAdapter implements MessageConnection {
    private pendingRequests = new Map<number, {
        resolve: (result: any) => void;
        reject: (error: any) => void;
    }>();

    private messageId = 0;

    constructor(private hubConnection: HubConnection) {
        this.hubConnection.on('ReceiveMessage', (message) => {
            this.handleMessage(message);
        });
    }

    public sendRequest<R>(method: string, params?: any): Promise<R> {
        return new Promise((resolve, reject) => {
            const id = this.messageId++;
            const message = {
                jsonrpc: '2.0',
                id,
                method,
                params
            };

            this.pendingRequests.set(id, { resolve, reject });
            this.hubConnection.invoke('SendMessage', message)
                .catch(reject);
        });
    }

    private handleMessage(message: any): void {
        if (message.id !== undefined && message.result !== undefined) {
            // Response
            const pending = this.pendingRequests.get(message.id);
            if (pending) {
                pending.resolve(message.result);
                this.pendingRequests.delete(message.id);
            }
        }
    }

    // ... other MessageConnection methods
}
```

**Language Client Setup:**
```typescript
export function createLanguageClient(
    hubConnection: HubConnection
): MonacoLanguageClient {
    const adapter = new SignalRMessageAdapter(hubConnection);

    return new MonacoLanguageClient({
        name: 'Scriban Language Client',
        clientOptions: {
            documentSelector: [{ language: 'scriban' }],
            errorHandler: {
                error: () => ({ action: ErrorAction.Continue }),
                closed: () => ({ action: CloseAction.DoNotRestart })
            }
        },
        connectionProvider: {
            get: () => Promise.resolve(adapter)
        }
    });
}
```

### Tests (18 total)
- Adapter message handling (8 tests)
- Request/response lifecycle (5 tests)
- Error handling (3 tests)
- Memory cleanup (2 tests)

### Success Criteria
- ✅ Adapter forwards messages correctly
- ✅ Pending requests resolved when responses arrive
- ✅ Can send multiple requests concurrently
- ✅ Proper cleanup on dispose
- ✅ Works with mock hub connection
- ✅ All tests pass

---

## Stage F4: Frontend Custom UI

**Duration:** 1.5 weeks
**Dependencies:** F3 complete

### Objectives
1. Create FilePicker component
2. Create PickerRouter
3. Implement useScribanEditor hook
4. Test components in isolation

### Key Components

**FilePicker Component:**
```typescript
interface FilePickerProps {
    onSelect: (path: string) => void;
    onCancel: () => void;
    currentValue?: string;
}

export function FilePicker({ onSelect, onCancel, currentValue }: FilePickerProps) {
    const [files, setFiles] = useState<string[]>([]);
    const [filter, setFilter] = useState('');
    const hubConnection = useContext(HubConnectionContext);

    useEffect(() => {
        hubConnection?.invoke<string[]>('GetPathSuggestions', 'copy_file', 0)
            .then(setFiles)
            .catch(console.error);
    }, [hubConnection]);

    const filteredFiles = files.filter(f =>
        f.toLowerCase().includes(filter.toLowerCase())
    );

    return (
        <div className="file-picker">
            <input
                type="text"
                placeholder="Filter..."
                value={filter}
                onChange={e => setFilter(e.target.value)}
            />
            <ul>
                {filteredFiles.map(file => (
                    <li key={file} onClick={() => onSelect(file)}>
                        {file}
                    </li>
                ))}
            </ul>
            <button onClick={onCancel}>Cancel</button>
        </div>
    );
}
```

**useScribanEditor Hook:**
```typescript
export function useScribanEditor() {
    const [pickerState, setPickerState] = useState<PickerState>({
        isVisible: false,
        pickerType: null,
        // ...
    });

    const openPicker = useCallback((functionName: string, paramIndex: number) => {
        setPickerState({
            isVisible: true,
            pickerType: 'file-picker',
            functionName,
            parameterIndex: paramIndex,
            // ...
        });
    }, []);

    // Listen for server commands
    useEffect(() => {
        hubConnection?.on('OpenPicker', (data) => {
            openPicker(data.functionName, data.parameterIndex);
        });

        return () => {
            hubConnection?.off('OpenPicker');
        };
    }, [hubConnection, openPicker]);

    return { pickerState, openPicker, /* ... */ };
}
```

### Tests (15 total)
- FilePicker renders and filters (4 tests)
- FilePicker selection works (3 tests)
- PickerRouter shows correct component (2 tests)
- useScribanEditor hook state management (4 tests)
- Server command handling (2 tests)

### Success Criteria
- ✅ Picker displays file list from mock
- ✅ Filtering works correctly
- ✅ Selection calls onSelect callback
- ✅ Hook responds to server commands
- ✅ All component tests pass

---

## INTEGRATION CHECKPOINT #1 (Stage I1)

**Duration:** 1 week
**Dependencies:** B4 AND F4 both complete
**This is the first time backend and frontend run together!**

### Objectives
1. Start real backend server
2. Connect real frontend to real backend
3. Run smoke tests for basic flow
4. Fix integration issues

### Integration Test Scenarios

**Test 1: Connection Establishment**
```typescript
test('Frontend connects to backend successfully', async () => {
    // Start backend server
    const backend = await startBackendServer();

    // Start frontend with real SignalR
    const { editor } = renderApp({ serverUrl: backend.url });

    // Wait for connection
    await waitFor(() => expect(backend.isConnected).toBe(true));

    // Cleanup
    await backend.stop();
});
```

**Test 2: Basic Hover Flow**
```typescript
test('Hover shows function documentation', async () => {
    const { editor, user } = await setupIntegration();

    // Type function name
    await user.type(editor, 'copy_file');

    // Hover over it
    await user.hover(screen.getByText('copy_file'));

    // Should see hover info
    await waitFor(() => {
        expect(screen.getByText(/Copies a file/)).toBeInTheDocument();
    });
});
```

**Test 3: Diagnostics Flow**
```typescript
test('Invalid code shows diagnostics', async () => {
    const { editor, user } = await setupIntegration();

    // Type invalid code
    await user.type(editor, '{{ unknown_function() }}');

    // Wait for diagnostics
    await waitFor(() => {
        const diagnostics = editor.getModel()?.getAllDecorations();
        expect(diagnostics).toHaveLength(1);
        expect(diagnostics[0].options.className).toContain('error');
    });
});
```

**Test 4: Custom Picker Flow**
```typescript
test('Typing ( opens file picker', async () => {
    const { editor, user } = await setupIntegration();

    // Type function with file picker parameter
    await user.type(editor, 'copy_file(');

    // Should open picker
    await waitFor(() => {
        expect(screen.getByRole('dialog')).toBeInTheDocument();
        expect(screen.getByText('file1.txt')).toBeInTheDocument();
    });

    // Select file
    await user.click(screen.getByText('file1.txt'));

    // Should insert into editor
    expect(editor.getValue()).toContain('copy_file("file1.txt"');
});
```

### Success Criteria
- ✅ 8/8 integration tests pass
- ✅ Connection established within 2 seconds
- ✅ Hover responds within 100ms
- ✅ Diagnostics appear within 500ms
- ✅ Custom picker opens within 200ms
- ✅ No console errors during tests
- ✅ Memory stable over 1000 operations

### Known Integration Issues to Watch For

1. **CORS Configuration**
   - Symptom: "CORS policy blocked"
   - Fix: Verify appsettings.json CORS matches frontend URL

2. **SignalR Negotiation Failure**
   - Symptom: "Failed to complete negotiation"
   - Fix: Ensure WebSockets enabled, check firewall

3. **LSP Initialization Timeout**
   - Symptom: No diagnostics/hover working
   - Fix: Check server logs, verify LSP server started

4. **Message Deserialization Errors**
   - Symptom: "Cannot deserialize JToken"
   - Fix: Verify message formats match exactly

### Integration Checkpoint Deliverables

After I1 completes, you should have:
- ✅ Working end-to-end system with core features
- ✅ Documented integration test suite
- ✅ Performance baseline metrics
- ✅ List of remaining features to implement

---

## Stages B5 & F5: Advanced Features

**Duration:** 2 weeks each (parallel)
**Dependencies:** I1 complete

### Backend B5 Additions
- Code actions (right-click menu)
- Macro insertion
- Advanced completion (context-aware)
- Performance optimizations
- **Tests:** 20 additional tests

### Frontend F5 Additions
- Error boundaries
- Reconnection handling with state sync
- Monaco content widgets for picker positioning
- Loading states and error messages
- **Tests:** 18 additional tests

---

## INTEGRATION CHECKPOINT #2 (Stage I2)

**Duration:** 1 week
**Dependencies:** B5 AND F5 complete

### Full Integration Testing

All features working together:
- ✅ Hover, completion, diagnostics
- ✅ File picker, enum picker
- ✅ Right-click actions
- ✅ Macro insertion
- ✅ Reconnection with state preservation
- ✅ Error handling throughout

### Success Criteria
- ✅ 12/12 advanced integration tests pass
- ✅ All user stories from spec satisfied
- ✅ Performance targets met:
  - Hover: < 100ms (p95)
  - Completion: < 150ms (p95)
  - Diagnostics: < 500ms (p95)
- ✅ Zero critical bugs
- ✅ Memory stable over extended use

---

## Stage P1: Polish & Production Readiness

**Duration:** 2 weeks
**Dependencies:** I2 complete

### Focus Areas

**Performance Optimization:**
- Profile and optimize hot paths
- Tune cache sizes
- Optimize bundle size
- Add performance monitoring

**Hardening:**
- Add rate limiting to all endpoints
- Implement request timeouts
- Add comprehensive error handling
- Security audit

**Testing:**
- Load testing (100 concurrent users)
- Chaos testing (random disconnections)
- Browser compatibility testing
- Accessibility audit

**Documentation:**
- API documentation
- Deployment guide
- User guide
- Troubleshooting guide

### Final Acceptance Criteria

**Automated Tests:**
- ✅ Backend: 160 tests, 90%+ coverage
- ✅ Frontend: 108 tests, 85%+ coverage
- ✅ Integration: 20 tests, all critical paths
- ✅ E2E: 10 tests, main user flows

**Performance:**
- ✅ Hover: p95 < 100ms ✓
- ✅ Completion: p95 < 150ms ✓
- ✅ Diagnostics: p95 < 500ms ✓
- ✅ Picker open: p95 < 200ms ✓
- ✅ Memory: < 5MB per document ✓
- ✅ CPU: < 50% under load ✓

**Quality:**
- ✅ Zero critical bugs
- ✅ < 5 known minor bugs
- ✅ All security checklist items complete
- ✅ Documentation complete

---

## Complete Timeline Visualization

```
Week 1      2       3       4       5       6       7       8       9      10      11      12      13      14
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Backend:
[B1]   [====B2====]  [====B3====]  [B4==]                    [====B5====]        [==P1==]
                                           \                 /            \      /
                                            [====I1====]                   [I2==]
                                           /                 \            /      \
Frontend:
[F1]   [==F2==]      [====F3====]    [F4==]                [====F5====]        [==P1==]
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Key Milestones:
• Week 1:  Backend & Frontend foundations ready
• Week 4:  Core services complete (both sides)
• Week 7:  First integration! Basic features working
• Week 10: All features implemented
• Week 12: Full integration complete
• Week 14: Production ready
```

---

## Risk Management

### High-Risk Areas

**Risk 1: Integration Complexity**
- **Probability:** High
- **Impact:** High
- **Mitigation:**
  - Extensive mocking allows independent development
  - Two integration checkpoints catch issues early
  - Detailed integration test scenarios

**Risk 2: Performance Issues**
- **Probability:** Medium
- **Impact:** High
- **Mitigation:**
  - Performance optimization built in from start (caching, debouncing)
  - Performance tests at each stage
  - Profiling tools ready

**Risk 3: SignalR/LSP Protocol Issues**
- **Probability:** Medium
- **Impact:** High
- **Mitigation:**
  - Comprehensive mocking of both sides
  - Message format tests
  - Protocol documentation

**Risk 4: Scriban Parser Limitations**
- **Probability:** Low
- **Impact:** Medium
- **Mitigation:**
  - Test with diverse Scriban code
  - Graceful degradation for unsupported features
  - Clear error messages

---

## Testing Dashboard

Create this script to run all tests and show progress:

**`run-all-tests.sh`:**
```bash
#!/bin/bash

echo "================================"
echo "SCRIBAN LANGUAGE SERVER - TEST SUITE"
echo "================================"
echo ""

# Backend tests
echo "🔧 Backend Tests"
echo "----------------"
cd Backend
dotnet test --logger "console;verbosity=minimal" --collect:"XPlat Code Coverage"
BACKEND_EXIT=$?

echo ""
echo "Coverage Report:"
dotnet reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"../coverage-backend" -reporttypes:TextSummary
cat ../coverage-backend/Summary.txt

echo ""

# Frontend tests
echo "⚛️  Frontend Tests"
echo "----------------"
cd ../Frontend
npm test -- --reporter=verbose --coverage
FRONTEND_EXIT=$?

echo ""
echo "Coverage Report:"
cat coverage/coverage-summary.txt

echo ""

# Integration tests (if I1 or I2 complete)
if [ -d "../Integration" ]; then
    echo "🔗 Integration Tests"
    echo "-------------------"
    cd ../Integration
    npm test -- --reporter=verbose
    INTEGRATION_EXIT=$?
fi

# Summary
echo ""
echo "================================"
echo "SUMMARY"
echo "================================"
[ $BACKEND_EXIT -eq 0 ] && echo "✅ Backend: PASS" || echo "❌ Backend: FAIL"
[ $FRONTEND_EXIT -eq 0 ] && echo "✅ Frontend: PASS" || echo "❌ Frontend: FAIL"
[ -z "$INTEGRATION_EXIT" ] || ([ $INTEGRATION_EXIT -eq 0 ] && echo "✅ Integration: PASS" || echo "❌ Integration: FAIL")

exit $(( $BACKEND_EXIT + $FRONTEND_EXIT + ${INTEGRATION_EXIT:-0} ))
```

---

## Success Indicators at Each Stage

| Stage | Key Indicator | How to Verify |
|-------|---------------|---------------|
| **B1** | ApiSpec loads and validates | `dotnet test --filter B1` all pass |
| **B2** | Cache hit rate > 50% | Check test output for cache stats |
| **B3** | Handlers work with mocks | `dotnet test --filter B3` all pass |
| **B4** | Hub accepts connections | Integration test connects successfully |
| **F1** | Mocks return data | `npm test` all pass |
| **F2** | Editor renders | Open browser, see Monaco |
| **F3** | LSP messages flow | Check browser console for LSP logs |
| **F4** | Picker opens on click | Click test button, see picker |
| **I1** | Basic flow works end-to-end | All 8 integration tests pass |
| **B5/F5** | Advanced features work | Specific feature tests pass |
| **I2** | All features integrated | All 12 integration tests pass |
| **P1** | Production metrics met | Load test results show targets met |

---

## Quick Start Commands

**Set up workspace:**
```bash
# Clone and setup
git clone <repo>
cd scriban-language-server

# Backend
cd Backend
dotnet restore
dotnet build

# Frontend
cd ../Frontend
npm install
npm run build

# Verify
./run-all-tests.sh
```

**Daily development:**
```bash
# Backend (watch mode)
cd Backend
dotnet watch test --filter "Stage=B2"

# Frontend (watch mode)
cd Frontend
npm run test:watch

# Integration (after I1)
cd Integration
npm run test:watch
```

**Before committing:**
```bash
# Run full test suite
./run-all-tests.sh

# Run linters
cd Backend && dotnet format
cd Frontend && npm run lint

# Check coverage
./generate-coverage-report.sh
```

---

## Final Notes

### This Plan Is Designed To

1. **Minimize Risk:** Independent development with mocks means issues are caught early
2. **Maximize Confidence:** Every stage has clear, measurable success criteria
3. **Enable Parallel Work:** Backend and frontend teams can work independently
4. **Provide Visibility:** Run tests at any time to see exact progress
5. **Ensure Quality:** >85% test coverage, clear performance targets

### When You're Ready to Start

1. **Review all three documents:**
   - `IMPLEMENTATION_PLAN.md` - Detailed stages B1-B2
   - `IMPLEMENTATION_PLAN_PART2.md` - Detailed stages B2-B4
   - `IMPLEMENTATION_SUMMARY.md` - Overview and remaining stages

2. **Set up tracking:** Create issues/tickets for each stage

3. **Begin with B1 and F1 simultaneously**

4. **Run `./run-all-tests.sh` daily** to track progress

5. **Reach out when you hit Integration Checkpoint #1** - that's the critical milestone!

---

**Good luck! You have a complete, executable plan. Every test can be written and run. Every file can be created exactly as specified. The architecture improvements are baked in. You're ready to build!** 🚀
