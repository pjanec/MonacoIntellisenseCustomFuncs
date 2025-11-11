# **Scriban Language Server: Specification 7 \- Detailed Design: The ScribanParserService**

## **1\. Overview**

This document details the internal design of the ScribanParserService. This service is the "brain" of the backend. It is responsible for bridging the raw text from the Scriban nuget's parser with the rich semantic metadata from our ApiSpecService.

Its core responsibility is to answer the question: **"What is the user's cursor currently looking at, and what does our API spec say about it?"**

## **2\. Core Responsibilities**

* **Parsing:** Convert raw document text into a Scriban Abstract Syntax Tree (AST) using the Scriban.Template.Parse method.  
* **AST Traversal:** Provide utilities to find specific nodes within the AST based on a cursor position (e.g., find the ScriptFunctionCall or ScriptMemberExpression at line 5, char 10).  
* **Semantic Analysis:** Compare the nodes in the AST against the ApiSpecService to find semantic errors (unknown functions, incorrect argument counts, invalid enum values).  
* **Contextualization:** Provide high-level "context" objects (e.g., ParameterContext) that combine AST information (what was typed) with API Spec information (what was *expected*).

## **3\. Dependencies (Injected)**

This service will be a singleton and will depend on:

* **ApiSpecService:** (Singleton) Used to fetch the API specification metadata.  
* **TextDocumentStore:** (Singleton, from OmniSharp) Used to retrieve the current text document.

## **4\. Implementation Strategy: AST Traversal & Mapping**

The core challenge is mapping a cursor position to a specific parameter. The Scriban nuget's parser generates a detailed AST, but it only knows about *syntax*, not our *semantics*.

Our strategy will be to:

1. Parse the full document into an AST (ScriptPage).  
2. Implement a custom Scriban ScriptVisitor class (e.g., NodeFinderVisitor) that can traverse the AST.  
3. This visitor's goal is to find the **smallest syntax node** that completely contains the user's cursor position (Position).  
4. Once we have this node (e.g., a ScriptFunctionCall), we can analyze its properties (.Arguments, .Target, etc.) to determine the function name and parameter index.  
5. We then cross-reference this information with the ApiSpecService to build our rich ParameterContext object.

## **5\. Key Internal Data Structures**

This service will define several internal C\# models to pass information between handlers.

/// \<summary\>  
/// The primary data object returned by the service.  
/// It tells a handler everything it needs to know about the cursor's context.  
/// \</summary\>  
public class ParameterContext  
{  
    /// \<summary\>The name of the function being called (e.g., "copy\_file").\</summary\>  
    public string FunctionName { get; set; }

    /// \<summary\>The 0-based index of the parameter the cursor is at.\</summary\>  
    public int ParameterIndex { get; set; }

    /// \<summary\>The full FunctionSpec from ApiSpec.json for this function.\</summary\>  
    public FunctionSpec FunctionSpec { get; set; }

    /// \<summary\>The full ParameterSpec from ApiSpec.json for this \*specific\* parameter.\</summary\>  
    public ParameterSpec ParameterSpec { get; set; }

    /// \<summary\>The Scriban AST node for the function call itself.\</summary\>  
    public ScriptFunctionCall FunctionCallNode { get; set; }

    /// \<summary\>The current string value of the parameter, if one exists (e.g., "src/").\</summary\>  
    public string CurrentValue { get; set; }  
}

## **6\. Key Method Designs (Pseudo-code)**

### **6.1. GetNodeAtPosition(ScriptNode rootNode, Position position)**

This is the core traversal helper.

// Internal C\# pseudo-code  
private ScriptNode GetNodeAtPosition(ScriptNode rootNode, Position position)  
{  
    // 1\. Create a custom NodeFinderVisitor : ScriptVisitor  
    var visitor \= new NodeFinderVisitor(position);

    // 2\. The visitor will override Visit(ScriptNode node)  
    // In the override:  
    //   \- Check if node.Span contains the position.  
    //   \- If YES:  
    //     \- Set this node as the 'CurrentBestMatch'.  
    //     \- Call base.Visit(node) to traverse \*deeper\*.  
    //   \- If NO:  
    //     \- Do nothing; this branch is not relevant.

    // 3\. Start the traversal  
    visitor.Visit(rootNode);

    // 4\. Return the "deepest" (smallest) node that contained the position  
    return visitor.CurrentBestMatch;  
}

### **6.2. GetParameterContext(TextDocument document, Position position)**

This is the primary public method used by most handlers.

// Public C\# pseudo-code  
public ParameterContext GetParameterContext(TextDocument document, Position position)  
{  
    // 1\. Get the AST  
    var ast \= this.Parse(document.GetText()); // Parse is a helper that wraps Template.Parse  
    if (ast \== null) return null;

    // 2\. Find the node at the cursor  
    var node \= this.GetNodeAtPosition(ast, position);  
    if (node \== null) return null;

    // 3\. Find the parent function call  
    //    Traverse \*up\* the tree from 'node' until we find a 'ScriptFunctionCall'  
    ScriptFunctionCall functionCallNode \= node.FindParent\<ScriptFunctionCall\>();  
    if (functionCallNode \== null) return null;

    // 4\. Resolve the function name  
    //    'functionCallNode.Target' can be a 'ScriptVariable' (e.g., "copy\_file")  
    //    or a 'ScriptMemberExpression' (e.g., "os.execute")  
    string functionName \= this.ResolveFunctionName(functionCallNode.Target);  
    if (functionName \== null) return null;

    // 5\. Get the API Spec  
    var functionSpec \= \_apiSpecService.GetFunction(functionName);  
    if (functionSpec \== null) return null; // Unknown function

    // 6\. Determine the parameter index  
    //    This is the most complex logic. We find which argument  
    //    the original 'node' (or its parent) belongs to.  
    int parameterIndex \= this.ResolveParameterIndex(functionCallNode, node);  
    if (parameterIndex \< 0 || parameterIndex \>= functionSpec.Parameters.Count)  
    {  
        return null; // Cursor is on a param that doesn't exist  
    }

    // 7\. Get the parameter spec  
    var parameterSpec \= functionSpec.Parameters\[parameterIndex\];

    // 8\. Get the current value  
    var currentValue \= this.ExtractStringValue(functionCallNode.Arguments\[parameterIndex\]);

    // 9\. Return the complete context  
    return new ParameterContext  
    {  
        FunctionName \= functionName,  
        ParameterIndex \= parameterIndex,  
        FunctionSpec \= functionSpec,  
        ParameterSpec \= parameterSpec,  
        FunctionCallNode \= functionCallNode,  
        CurrentValue \= currentValue  
    };  
}

### **6.3. GetSemanticErrors(TextDocument document)**

This method drives the diagnostic squiggly lines.

// Public C\# pseudo-code  
public List\<Diagnostic\> GetSemanticErrors(TextDocument document)  
{  
    var diagnostics \= new List\<Diagnostic\>();  
    var ast \= this.Parse(document.GetText());  
    if (ast \== null) return diagnostics; // Syntax errors are handled by the parser itself

    // 1\. Create a custom ValidationVisitor : ScriptVisitor  
    var visitor \= new ValidationVisitor(\_apiSpecService, diagnostics);

    // 2\. The visitor will override Visit(ScriptFunctionCall call)  
    // In this Visit method:  
    //   \- Resolve the function name (e.g., "copy\_file" or "os.execute")  
    //   \- functionSpec \= \_apiSpecService.GetFunction(functionName)  
    //   \- if (functionSpec \== null):  
    //     \- diagnostics.Add(new Diagnostic(call.Span, "Unknown function " \+ functionName))  
    //     \- return  
    //  
    //   \- if (call.Arguments.Count \!= functionSpec.Parameters.Count):  
    //     \- diagnostics.Add(new Diagnostic(call.Span, "Wrong number of arguments..."))  
    //  
    //   \- for (int i \= 0; i \< call.Arguments.Count; i++):  
    //     \- paramSpec \= functionSpec.Parameters\[i\]  
    //     \- if (paramSpec.Picker \== "enum-list"):  
    //       \- value \= this.ExtractStringValue(call.Arguments\[i\])  
    //       \- if (\!paramSpec.Options.Contains(value)):  
    //         \- diagnostics.Add(new Diagnostic(call.Arguments\[i\].Span, "Invalid value..."))

    // 3\. Start the traversal  
    visitor.Visit(ast);

    return diagnostics;  
}

# **Scriban Language Server: Specification 8 \- Detailed Design: Standard LSP Handler Workflow**

## **1\. Overview**

This document provides a detailed, step-by-step trace of a standard LSP request. This demonstrates how the backend's "Internal APIs" (ScribanParserService, ApiSpecService) are orchestrated by an LSP handler to produce a user-facing result.

We will use the **textDocument/codeAction** (right-click) request as our primary example. This workflow is ideal as it showcases the system's ability to provide context-aware responses based on both the API specification and the cursor's precise location.

## **2\. Actors (Involved Backend Components)**

* **ScribanHub (SignalR):** The raw transport entrypoint.  
* **LspBridge (Service):** The "pipe" that connects SignalR to the LSP framework.  
* **ILanguageServer (OmniSharp):** The LSP host that routes incoming messages.  
* **TextDocumentStore (OmniSharp):** The in-memory cache holding the current state of the document.  
* **CodeActionHandler (Our Handler):** The C\# class that contains the business logic for this specific request.  
* **ScribanParserService (Our Service):** The "brain" that analyzes the code.  
* **ApiSpecService (Our Service):** The "database" that holds the API metadata.

## **3\. Trigger**

The user right-clicks inside the Monaco editor.

## **4\. Step-by-Step Workflow**

This trace follows a request from the client, through the server, and back to the client.

### **Part 1: Client \-\> Server (The Request)**

1. **\[Client\] User Action:** The user right-clicks at Line 10, Char 15\.  
2. **\[Client\] monaco-languageclient:** Intercepts the click and creates a textDocument/codeAction request. The payload includes the document URI and the cursor's Range.  
3. **\[Client\] SignalRMessageAdapter:** The send(message) method is called by the language client.  
4. **\[Client\] SignalR:** The adapter calls hubConnection.invoke("SendMessage", lspMessage).  
5. **\[Network\]**: The SignalR message travels to the ASP.NET Core server.  
6. **\[Server\] ScribanHub:** The SendMessage(JToken message) method on the Hub is invoked.  
7. **\[Server\] LspBridge:** The Hub, which has the LspBridge injected, calls \_lspBridge.SendToLanguageServer(message).  
8. **\[Server\] ILanguageServer:** The bridge's method pushes the JToken into the \_lspServer.Input stream.  
9. **\[Server\] OmniSharp Host:** The LSP framework host reads from the stream, parses the JSON-RPC message, identifies it as textDocument/codeAction, and routes it to the *single* registered handler for that message: our CodeActionHandler.

### **Part 2: Server-Side Logic (The "Brain" at Work)**

This section details the execution of the CodeActionHandler.Handle(...) method.

1. **\[Server\] CodeActionHandler:** The Handle(CodeActionParams request, ...) method is invoked by the OmniSharp host.  
2. **\[Server\] TextDocumentStore:** The handler retrieves the current document state:

// 1\. Get the document from the in-memory store  
TextDocument document \= \_textDocumentStore.GetDocument(request.TextDocument.Uri);  
if (document \== null) return Task.FromResult(new CommandOrCodeActionContainer());

3.   
4. **\[Server\] ScribanParserService:** The handler delegates all complex analysis to the parser service:

// 2\. Ask the parser for the semantic context at the cursor  
ParameterContext context \= \_parserService.GetParameterContext(document, request.Range.Start);  
if (context \== null) return Task.FromResult(new CommandOrCodeActionContainer());

5.   
6. *(For this call, the ScribanParserService performs the detailed steps outlined in spec-7-parser-service.md: it parses the AST, finds the node at the position, resolves the function name, and cross-references it with the ApiSpecService to build the ParameterContext.)*  
7. **\[Server\] CodeActionHandler:** The handler now has a rich context object (e.g., { FunctionName: "copy\_file", ParameterIndex: 0, ParameterSpec: { ... } }). It makes decisions based on the API specification.  
8. **\[Server\] CodeActionHandler (Decision Logic):**

var actions \= new List\<CommandOrCodeAction\>();

// Scenario 1: Is this a "file-picker" parameter?  
if (context.ParameterSpec.Picker \== "file-picker")  
{  
    var command \= new Command  
    {  
        Title \= "Select Path...", // The text in the right-click menu  
        Name \= "scriban.openPicker", // The command ID the client listens for  
        Arguments \= new JArray(context.FunctionName, context.ParameterIndex)  
    };  
    actions.Add(new CodeAction { Title \= "Select Path...", Command \= command });  
}

// Scenario 2: Does this string parameter support "macros"?  
if (context.ParameterSpec.Macros \!= null)  
{  
    foreach (var macroKey in context.ParameterSpec.Macros)  
    {  
        // (e.g., macroKey \= "TIMESTAMP")  
        // We look up the macro's insertion text from a helper/config service  
        string macroText \= \_macroService.GetMacroText(macroKey); // "e.g., {{ date.now }}"

        var command \= new Command  
        {  
            Title \= $"Insert Macro: {macroKey}",  
            Name \= "scriban.insertMacro", // The command ID  
            Arguments \= new JArray(macroText)  
        };  
        actions.Add(new CodeAction { Title \= $"Insert Macro: {macroKey}", Command \= command });  
    }  
}

// 6\. Return all found actions  
return Task.FromResult(new CommandOrCodeActionContainer(actions));

9. 

### **Part 3: Server \-\> Client (The Response)**

1. **\[Server\] OmniSharp Host:** The CommandOrCodeActionContainer (containing our list of CodeActions) is serialized by the framework into a JSON-RPC response JToken.  
2. **\[Server\] LspBridge:** The \_lspServer.Output.Subscribe(...) callback (which we set up during initialization) fires with the JToken response.  
3. **\[Server\] ScribanHub:** The LspBridge calls the injected IHubContext: \_hubContext.Clients.All.ReceiveMessage(responseToken).  
4. **\[Network\]**: The SignalR server sends the ReceiveMessage payload to the connected client.  
5. **\[Client\] SignalR:** The hubConnection.on("ReceiveMessage", ...) listener in the SignalRMessageAdapter fires.  
6. **\[Client\] SignalRMessageAdapter:** It passes the raw message to the monaco-languageclient's internal callback.  
7. **\[Client\] monaco-languageclient:** The language client parses the JSON-RPC response and identifies it as the answer to the textDocument/codeAction request.  
8. **\[Client\] Monaco Editor:** The client library passes the list of actions to Monaco, which renders them in the right-click context menu. The user now sees:  
   * Select Path...  
   * Insert Macro: TIMESTAMP  
   * Insert Macro: USER\_ID

# **Scriban Language Server: Specification 9 \- Detailed Design: Custom UI Trigger Workflow**

## **1\. Overview**

This document details the workflow for our most unique feature: the **custom UI trigger**. This is the non-LSP mechanism that allows the server to command the client to open a custom React picker.

This flow is initiated by the client, but the *decision* to act is made entirely by the server. It is designed to handle cases that fall outside the standard LSP, such as opening a file picker instead of a simple completion list.

We will trace the "happy path" of a user typing copy\_file( to automatically trigger the file picker.

## **2\. Actors**

* **useScribanEditor Hook (Client):** Listens for user key presses.  
* **HubConnection (Client):** The SignalR connection.  
* **ScribanHub (Server):** The SignalR Hub, which has a custom CheckTrigger method.  
* **ScribanParserService (Server Service):** The "brain" that analyzes the code context.  
* **ApiSpecService (Server Service):** The "database" that holds the API metadata.  
* **PickerRouter (Client Component):** The React component that listens for state changes and renders the correct picker.

## **3\. Trigger**

The user types a trigger character ((, ,) or presses the Ctrl+Space hotkey.

## **4\. Step-by-Step Workflow: Auto-Open on (**

### **Part 1: Client \-\> Server (The "Trigger")**

1. **\[Client\] User Action:** The user types copy\_file(.  
2. **\[Client\] useScribanEditor Hook:** The editor.onDidChangeModelContent listener fires.  
3. **\[Client\] useScribanEditor Hook:** The listener's logic detects that the change was a single ( character.  
4. **\[Client\] useScribanEditor Hook:** The hook gathers the necessary context:

// Inside the onDidChangeModelContent listener  
const model \= editor.getModel();  
const position \= editor.getPosition();  
const context \= {  
  event: "char",  
  char: "(",  
  line: model.getLineContent(position.lineNumber), // "copy\_file("  
  position: lspPosition // The cursor position  
};

5.   
6. **\[Client\] HubConnection:** The hook invokes the custom SignalR method:

hubConnection.invoke("CheckTrigger", context);

7. 

### **Part 2: Server-Side Logic (The "Decision")**

This section details the execution of the ScribanHub.CheckTrigger(...) method.

1. **\[Server\] ScribanHub:** The CheckTrigger(TriggerContext context) method is invoked.  
2. **\[Server\] ScribanHub:** The Hub immediately delegates the complex work to the ScribanParserService. It first gets the in-memory document:

// Note: This requires a custom way to map SignalR ConnectionId to document URI  
// Or, simpler: the client includes the document URI in the TriggerContext  
TextDocument document \= \_textDocumentStore.GetDocument(context.Uri);  
if (document \== null) return;

3.   
4. **\[Server\] ScribanParserService:** The Hub asks the parser for the semantic context at the trigger position.

// GetParameterContext is smart enough to handle a position  
// just after an opening parenthesis.  
ParameterContext paramContext \= \_parserService.GetParameterContext(document, context.Position);  
if (paramContext \== null) return; // Not in a function call

5.   
6. **\[Server\] ScribanHub (Decision Logic):** The Hub now has the rich ParameterContext. It inspects the API specification to decide what to do.

// paramContext.ParameterSpec.Picker comes from ApiSpec.json  
string pickerType \= paramContext.ParameterSpec.Picker;

switch (pickerType)  
{  
    case "file-picker":  
        // 1\. This is a custom UI trigger. Command the client.  
        var data \= new OpenPickerData  
        {  
            PickerType \= "file-picker",  
            FunctionName \= paramContext.FunctionName,  
            ParameterIndex \= paramContext.ParameterIndex,  
            CurrentValue \= paramContext.CurrentValue  
        };

        // 2\. Send the command \*back\* to the calling client  
        await Clients.Caller.SendAsync("OpenPicker", data);  
        break;

    case "enum-list":  
        // 2\. This is an enum. Do \*nothing\*.  
        // Why? Because the client \*also\* sent a standard  
        // textDocument/completion request. Our CompletionHandler  
        // will see this and return the enum list.  
        break;

    case "none":  
    default:  
        // 3\. This is a plain string/number. Do \*nothing\*.  
        break;  
}

7. 

### **Part 3: Server \-\> Client (The "Command")**

1. **\[Network\]**: The SignalR server sends the OpenPicker message to the specific client.  
2. **\[Client\] useScribanEditor Hook:** The hook's hubConnection.on("OpenPicker", ...) listener fires.  
3. **\[Client\] useScribanEditor Hook:** The listener function executes:

// Inside the hubConnection.on("OpenPicker", ...) listener

// 1\. Get the current cursor screen position.  
// This MUST be done on the client.  
const position \= editor.getPosition();   
const coords \= getScreenCoordinates(position); // Our helper function

// 2\. Update the React state  
setPickerState({  
  isVisible: true,  
  pickerType: data.pickerType,  
  functionName: data.functionName,  
  parameterIndex: data.parameterIndex,  
  currentValue: data.currentValue,  
  position: coords // The screen coordinates for the UI  
});

4.   
5. **\[Client\] React:** React re-renders.  
6. **\[Client\] \<PickerRouter\> Component:** The router component sees pickerState.isVisible is true.  
7. **\[Client\] \<PickerRouter\> Component:** It inspects pickerState.pickerType and renders the correct component:

return \<FilePicker  
         functionName={pickerState.functionName}  
         parameterIndex={pickerState.parameterIndex}  
         currentValue={pickerState.currentValue}  
         onSelect={handlePickerSelect}  
         onCancel={handlePickerCancel}  
       /\>;

8.   
9. **\[Client\] \<FilePicker\> Component:** The file picker component mounts. Its internal useEffect hook now fires, calling hubConnection.invoke("GetPathSuggestions", ...) to fetch its own list of files.

This workflow cleanly separates the *trigger detection* (client) from the *decision logic* (server) and the *data fetching* (picker component).

# **Scriban Language Server: Specification 10 \- Detailed Design: The LspBridge Service**

## **1\. Overview**

This document details the internal design of the LspBridge service. This singleton service is a small but critical piece of plumbing. It solves the core problem of our architecture: connecting the **SignalR** transport layer (which speaks invoke/on) to the **OmniSharp** LSP framework (which speaks Input/Output streams).

It acts as a simple, bi-directional, in-memory "pipe."

## **2\. Core Responsibilities**

1. **Forward Client-to-Server:** It must expose a public method that the ScribanHub can call to push raw JToken messages (received from the client) into the ILanguageServer's Input stream.  
2. **Forward Server-to-Client:** It must subscribe to the ILanguageServer's Output stream and, for every JToken message the server produces, broadcast it to all connected SignalR clients.

## **3\. Dependencies (Injected)**

The LspBridge will be registered as a **Singleton** in Program.cs.

// Constructor for LspBridge  
public class LspBridge  
{  
    private readonly IHubContext\<ScribanHub, IScribanClient\> \_hubContext;

    public LspBridge(IHubContext\<ScribanHub, IScribanClient\> hubContext)  
    {  
        \_hubContext \= hubContext;  
    }

    // ... methods ...  
}

* **IHubContext\<ScribanHub, IScribanClient\>:** The ASP.NET Core service for accessing a Hub from *outside* the Hub itself. This is what allows the bridge to send messages to clients.

## **4\. Initialization**

The bridge *cannot* be fully functional on its own. It needs to be explicitly handed the ILanguageServer instance *after* the application has been built.

This initialization will happen in Program.cs.

**File: Program.cs**

// ... after builder.Build() ...  
var app \= builder.Build();

// 1\. Get the two services from the container  
var lspServer \= app.Services.GetRequiredService\<ILanguageServer\>();  
var lspBridge \= app.Services.GetRequiredService\<LspBridge\>();

// 2\. Give the LSP Server instance to the bridge and start the pipe  
lspBridge.Initialize(lspServer);

// 3\. Start the app  
app.Run();

## **5\. Key Method Designs (Pseudo-code)**

### **5.1. Initialize(ILanguageServer lspServer)**

This method is called once at startup (as shown above) to "activate" the bridge.

// Internal C\# pseudo-code for LspBridge.cs

private ILanguageServer \_lspServer;  
private CancellationTokenSource \_cancellationSource \= new CancellationTokenSource();

public void Initialize(ILanguageServer lspServer)  
{  
    \_lspServer \= lspServer;

    // 1\. Subscribe to the LSP Server's output stream.  
    //    This is an IObservable\<JToken\>. We subscribe a delegate to its OnNext event.  
    \_lspServer.Output.Subscribe(  
          
        // 2\. This delegate is our "Server-to-Client" forwarder.  
        (JToken responseMessage) \=\>  
        {  
            // 3\. Use the HubContext to broadcast this message to \*all\* clients.  
            //    The client adapter's "ReceiveMessage" listener will pick this up.  
            \_hubContext.Clients.All.ReceiveMessage(responseMessage);  
        },  
        \_cancellationSource.Token  
    );  
}

### **5.2. SendToLanguageServer(JToken message)**

This method is the "Client-to-Server" forwarder. It is the *only* other public method on the LspBridge.

// Public C\# pseudo-code for LspBridge.cs

public Task SendToLanguageServer(JToken message)  
{  
    if (\_lspServer \== null)  
    {  
        // This should not happen if Initialize() was called, but as a safeguard:  
        return Task.CompletedTask;  
    }

    // 1\. Get the LSP Server's Input stream.  
    // 2\. Asynchronously send the JToken into the stream.  
    // 3\. The OmniSharp host is listening to this stream and will  
    //    pick up the message for routing to the correct handler.  
    return \_lspServer.Input.Send(message);  
}

### **5.3. Hub Integration**

The ScribanHub itself becomes extremely simple, acting only as the public-facing entrypoint for these bridge methods.

**File: ScribanHub.cs**

// C\# pseudo-code for the Hub

public class ScribanHub : Hub\<IScribanClient\>  
{  
    private readonly LspBridge \_lspBridge;

    // The bridge is injected as a singleton  
    public ScribanHub(LspBridge lspBridge)  
    {  
        \_lspBridge \= lspBridge;  
    }

    // This method handles ALL standard LSP messages from the client  
    public Task SendMessage(JToken message)  
    {  
        // Immediately pass the message to the bridge  
        return \_lspBridge.SendToLanguageServer(message);  
    }

    // \--- All other custom methods (CheckTrigger, GetPathSuggestions) \---  
    // are defined here as separate, non-LSP endpoints.  
      
    public async Task CheckTrigger(TriggerContext context)  
    {  
        // ... logic from spec-9 ...  
    }  
}

# **Scriban Language Server: Specification 11 \- Detailed Design: Validation & Diagnostics Workflow**

## **1\. Overview**

This document details the server's **proactive validation** workflow. This is the "push" mechanism responsible for sending all syntax and semantic errors (squiggliest) to the client.

Unlike request/response workflows (like codeAction or hover), diagnostics are not requested by the client. The server *initiates* this push whenever the document changes, ensuring the user has immediate, live feedback on their code's validity.

## **2\. Actors**

* **TextDocumentSyncHandler (OmniSharp):** The handler that receives textDocument/didChange notifications.  
* **TextDocumentStore (OmniSharp):** The in-memory cache of the document's current state.  
* **ILanguageServerFacade (OmniSharp):** The service used to *send* notifications to the client.  
* **ScribanParserService (Our Service):** The service that performs the actual parsing and validation.  
* **ApiSpecService (Our Service):** The service providing the API metadata for semantic validation.

## **3\. Trigger**

The textDocument/didChange notification is sent from the client. This happens after a short, configurable debounce period following the user's last keystroke.

## **4\. Step-by-Step Workflow**

### **Part 1: The didChange Notification**

1. **\[Client\] User Action:** The user types a character (e.g., mistyping copy\_file as copy\_fille).  
2. **\[Client\] monaco-languageclient:** Detects the text change. After a short debounce (e.g., 250ms), it sends a textDocument/didChange notification to the server. This message contains *only the change* (e.g., "inserted 'e' at L5:C10").  
3. **\[Server\] LSP "Plumbing":** The message is routed through the ScribanHub \-\> LspBridge \-\> ILanguageServer input stream.  
4. **\[Server\] OmniSharp Host:** The host routes the didChange notification to the registered TextDocumentSyncHandler.  
5. **\[Server\] TextDocumentStore:** The TextDocumentSyncHandler's primary job is to apply this change to the in-memory TextDocument in the TextDocumentStore. **This happens automatically.**  
6. **\[Server\] TextDocumentSyncHandler:** After the document is updated, the handler's Handle(DidChangeTextDocumentParams request, ...) method continues. This is our hook to trigger validation.

### **Part 2: Server-Side Logic (The Validation)**

This section details the custom logic inside our TextDocumentSyncHandler.

// C\# pseudo-code for our TextDocumentSyncHandler.cs

// We inject the services we need  
public class CustomTextDocumentSyncHandler : TextDocumentSyncHandlerBase  
{  
    private readonly ILanguageServerFacade \_languageServerFacade;  
    private readonly TextDocumentStore \_documentStore;  
    private readonly ScribanParserService \_parserService;

    public CustomTextDocumentSyncHandler(  
        ILanguageServerFacade languageServerFacade,  
        TextDocumentStore documentStore,  
        ScribanParserService parserService)  
    {  
        \_languageServerFacade \= languageServerFacade;  
        \_documentStore \= documentStore;  
        \_parserService \= parserService;  
    }

    // This method is called by OmniSharp after a document changes  
    public override async Task\<Unit\> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)  
    {  
        // 1\. The document is already updated in the store by the time  
        //    this code runs. We just need to fetch it.  
        var document \= \_documentStore.GetDocument(request.TextDocument.Uri);  
        if (document \== null) return Unit.Value;

        // 2\. Delegate all validation logic to the ScribanParserService.  
        //    This one call gets \*all\* errors.  
        List\<Diagnostic\> allDiagnostics \= \_parserService.GetDiagnostics(document);

        // 3\. Publish the diagnostics to the client.  
        //    This is the "push" part.  
        \_languageServerFacade.TextDocument.PublishDiagnostics(  
            new PublishDiagnosticsParams  
            {  
                Uri \= document.Uri,  
                Diagnostics \= new Container\<Diagnostic\>(allDiagnostics)  
            }  
        );

        return Unit.Value;  
    }  
      
    // ... other required overrides ...  
}

### **Part 3: ScribanParserService.GetDiagnostics (Internal)**

This is the internal implementation of the "brain" performing the validation.

// C\# pseudo-code inside ScribanParserService.cs

public List\<Diagnostic\> GetDiagnostics(TextDocument document)  
{  
    var allDiagnostics \= new List\<Diagnostic\>();  
    var code \= document.GetText();

    // \=== 1\. Syntactic Validation \===  
    // We parse the template using the Scriban nuget.  
    // The Scriban parser will return its \*own\* list of syntax errors.  
    var template \= Template.Parse(code);

    if (template.HasErrors)  
    {  
        foreach (var error in template.Messages)  
        {  
            // Convert Scriban's LogMessage to an LSP Diagnostic  
            allDiagnostics.Add(ConvertToLspDiagnostic(error));  
        }  
    }  
      
    // If there are fundamental syntax errors, don't bother with  
    // semantic checks, as the AST will be unreliable.  
    if (allDiagnostics.Any(d \=\> d.Severity \== DiagnosticSeverity.Error))  
    {  
        return allDiagnostics;  
    }

    // \=== 2\. Semantic Validation \===  
    // The AST is valid, so now we check it against our API spec.  
    // We re-use the "ValidationVisitor" from spec-7, which  
    // traverses the AST (template.Page).  
      
    var semanticDiagnostics \= new List\<Diagnostic\>();  
    var visitor \= new ValidationVisitor(\_apiSpecService, semanticDiagnostics);  
      
    // This visitor finds:  
    // \- Unknown functions (e.g., "copy\_fille")  
    // \- Wrong argument counts (e.g., copy\_file("one"))  
    // \- Invalid enum values (e.g., set\_mode("INVALID"))  
    visitor.Visit(template.Page);

    // 3\. Combine both lists  
    allDiagnostics.AddRange(semanticDiagnostics);

    return allDiagnostics;  
}

### **Part 4: Client-Side Result**

1. **\[Client\] SignalRMessageAdapter:** Receives the textDocument/publishDiagnostics notification from the server.  
2. **\[Client\] monaco-languageclient:** Receives the notification from the adapter.  
3. **\[Client\] Monaco Editor:** The language client calls Monaco's native monaco.editor.setModelMarkers API.  
4. **\[User\]** The user sees red squiggly lines for all syntax and semantic errors, and the "Problems" panel is populated.

This "push" workflow ensures the user *always* has immediate feedback on the validity of their code without ever having to ask for it.

# **Scriban Language Server: Specification 12 \- Detailed Design: The CompletionHandler**

## **1\. Overview**

This document details the internal logic of the CompletionHandler. This handler is responsible for responding to textDocument/completion requests, which are triggered by the client in two ways:

1. **Automatic:** The user types a "trigger character" (which we will define as .).  
2. **Manual:** The user presses Ctrl+Space.

The primary challenge for this handler is to **intelligently decide what to do** based on the cursor's context, as defined in our ApiSpec.json. It must correctly differentiate between "member completion", "enum completion", and "custom picker" scenarios.

## **2\. Actors**

* **CompletionHandler (Our Handler):** The OmniSharp handler for textDocument/completion.  
* **ScribanParserService (Our Service):** The "brain" that analyzes the code context.  
* **ApiSpecService (Our Service):** The "database" that holds the API metadata.

## **3\. Handler Registration**

In Program.cs, we will register the handler and explicitly define . as a trigger character.

// C\# pseudo-code for Program.cs  
builder.Services.AddLanguageServer(options \=\>  
{  
    options  
        .WithHandler\<CompletionHandler\>()  
        // ... other handlers  
});

// C\# pseudo-code for CompletionHandler.cs  
protected override CompletionRegistrationOptions CreateRegistrationOptions(  
    CompletionCapability capability, ClientCapabilities clientCapabilities)  
{  
    return new CompletionRegistrationOptions  
    {  
        DocumentSelector \= DocumentSelector.ForLanguage("scriban"),  
        // Define "." as the character that auto-triggers this handler  
        TriggerCharacters \= new Container\<string\>("."),  
        ResolveProvider \= false // We will provide full info up-front  
    };  
}

## **4\. Step-by-Step Workflow: Handle(CompletionParams request, ...)**

This is the central logic inside the CompletionHandler.

1. **\[Server\] CompletionHandler:** The Handle method is invoked. It first gets the document and the trigger context.

// C\# pseudo-code for CompletionHandler.Handle  
var document \= \_documentStore.GetDocument(request.TextDocument.Uri);  
if (document \== null) return new CompletionList();

var position \= request.Position;  
var triggerChar \= request.Context?.TriggerCharacter;

2.   
3. **\[Server\] ScribanParserService:** The handler asks the parser for the semantic context at the cursor.

// Ask the parser what's happening at the cursor  
var context \= \_parserService.GetContextForCompletion(document, position);

4.   
5. **\[Server\] CompletionHandler (Decision Logic):** The handler now executes a switch statement based on the context it received.

### **Workflow Case 1: Member Completion (e.g., os.)**

* **Trigger:** triggerChar was ..  
* **Parser Result:** context is { Type: "MemberAccess", ObjectName: "os" }.

// C\# pseudo-code for CompletionHandler.Handle

// (context.Type \== "MemberAccess")  
// 1\. Get the object spec  
var objectSpec \= \_apiSpecService.GetObject("os");  
if (objectSpec \== null || objectSpec.Members \== null)  
{  
    return new CompletionList(); // No members found  
}

// 2\. Build CompletionItems from the spec  
var completionItems \= new List\<CompletionItem\>();  
foreach (var member in objectSpec.Members)  
{  
    completionItems.Add(new CompletionItem  
    {  
        Label \= member.Name,  
        Kind \= CompletionItemKind.Function, // Or Property, etc.  
        Documentation \= new MarkupContent(MarkupKind.Markdown, member.Hover)  
    });  
}

// 3\. Return the list  
return new CompletionList(completionItems);

* **User Experience:** The user types os. and instantly sees a dropdown list with execute.

### **Workflow Case 2: "Enum" Completion (e.g., set\_mode(|))**

* **Trigger:** User pressed Ctrl+Space (so triggerChar is null).  
* **Parser Result:** context is { Type: "Parameter", ParameterSpec: { Picker: "enum-list", Options: \[...\] } }.

// C\# pseudo-code for CompletionHandler.Handle

// (context.Type \== "Parameter")  
var paramSpec \= context.ParameterSpec;

if (paramSpec.Picker \== "enum-list")  
{  
    // 1\. Get the options from the spec  
    var completionItems \= new List\<CompletionItem\>();  
    foreach (var option in paramSpec.Options)  
    {  
        completionItems.Add(new CompletionItem  
        {  
            Label \= option,  
            Kind \= CompletionItemKind.EnumMember // Or Constant  
        });  
    }  
    // 2\. Return the list  
    return new CompletionList(completionItems);  
}

* **User Experience:** The user types set\_mode( (which *does not* auto-trigger the picker), presses Ctrl+Space, and sees a standard suggestion list with MODE\_FAST, MODE\_SLOW, etc.

### **Workflow Case 3: "File Picker" (e.g., copy\_file(|))**

* **Trigger:** User pressed Ctrl+Space.  
* **Parser Result:** context is { Type: "Parameter", ParameterSpec: { Picker: "file-picker" } }.

// C\# pseudo-code for CompletionHandler.Handle

// (context.Type \== "Parameter")  
var paramSpec \= context.ParameterSpec;

if (paramSpec.Picker \== "file-picker")  
{  
    // 1\. THIS IS THE KEY: We do \*nothing\*.  
    // We return an empty list because we want the \*other\*  
    // custom "CheckTrigger" workflow (from spec-9) to handle this.  
    // The client's Ctrl+Space handler will see no completion  
    // items and will proceed to call \`hubConnection.invoke("CheckTrigger")\`.  
    return new CompletionList();  
}

* **User Experience:** The user types copy\_file( and presses Ctrl+Space. The CompletionHandler returns nothing. The client's useScribanEditor hook, seeing no native completion, proceeds with its CheckTrigger call, which causes the server to send the OpenPicker command. The custom file picker opens, as designed.

### **Workflow Case 4: Default / No Context**

* **Trigger:** User presses Ctrl+Space in whitespace or on a variable.  
* **Parser Result:** context is null.

// C\# pseudo-code for CompletionHandler.Handle

if (context \== null)  
{  
    // 1\. We are not in a special context.  
    //    Return the list of all global functions and objects.  
    var allGlobals \= \_apiSpecService.GetGlobals(); // "os", "copy\_file", "set\_mode", ...  
    var completionItems \= allGlobals.Select(g \=\> new CompletionItem  
    {  
        Label \= g.Name,  
        Kind \= g.Type \== "object" ? CompletionItemKind.Module : CompletionItemKind.Function  
    }).ToList();

    return new CompletionList(completionItems);  
}

* **User Experience:** The user is on a blank line, presses Ctrl+Space, and sees a list of all available root-level functions and objects (os, copy\_file, set\_mode, etc.).

# **Scriban Language Server: Specification 13 \- Detailed Design: Startup & DI Configuration**

## **1\. Overview**

This document provides a concrete implementation plan for the server's Program.cs file. This file is the "glue" that holds the entire backend together. It is responsible for:

1. Registering all custom services with the correct lifetimes.  
2. Configuring and launching the OmniSharp.Extensions.LanguageServer host.  
3. Registering all custom LSP handlers with the OmniSharp host.  
4. Configuring and launching the ASP.NET Core web application and the SignalR Hub.  
5. Connecting the LspBridge to the ILanguageServer to complete the communication pipe.

## **2\. Service Lifetime Philosophy**

* **Singleton:** Services that hold state, are expensive to create, or need to be shared across all handlers and connections. This is the default for our core services.  
  * ApiSpecService: Holds the loaded ApiSpec.json metadata.  
  * ScribanParserService: Is thread-safe and holds no state; can be a singleton for performance.  
  * FileSystemService: Manages file system access.  
  * LspBridge: Must be a singleton to bridge all connections to the single LSP server.  
  * TextDocumentStore: (From OmniSharp) This is registered as a singleton by the framework and holds the state of all open documents.  
* **Scoped/Transient:** Handlers are lightweight and should be created on-demand for each request. OmniSharp manages this for us.  
  * CodeActionHandler, CompletionHandler, HoverHandler, CustomTextDocumentSyncHandler.

## **3\. Program.cs Detailed Implementation Plan**

The following is a complete, commented, implementation-ready pseudo-code for Program.cs.

// \--- Namespaces \---  
using Microsoft.AspNetCore.Builder;  
using Microsoft.Extensions.DependencyInjection;  
using Microsoft.Extensions.Hosting;  
using OmniSharp.Extensions.LanguageServer.Protocol.Server;  
using OmniSharp.Extensions.LanguageServer.Server;  
using ScribanLanguageServer.Handlers; // Our custom handlers  
using ScribanLanguageServer.Services; // Our custom services

// \--- 1\. Create the Web Application Builder \---  
var builder \= WebApplication.CreateBuilder(args);

// \--- 2\. Register Standard ASP.NET & SignalR Services \---  
builder.Services.AddSignalR();  
builder.Services.AddCors(options \=\>  
{  
    options.AddDefaultPolicy(policy \=\>  
    {  
        // TODO: This should be configured from appsettings.json  
        policy.WithOrigins("http://localhost:3000") // Client's URL  
              .AllowAnyHeader()  
              .AllowAnyMethod()  
              .AllowCredentials();  
    });  
});

// \--- 3\. Register Our Custom Singleton Services \---  
// These services are available to the \*entire\* application, including the Hub.  
builder.Services.AddSingleton\<LspBridge\>();  
builder.Services.AddSingleton\<ApiSpecService\>(); // Loads ApiSpec.json  
builder.Services.AddSingleton\<ScribanParserService\>();  
builder.Services.AddSingleton\<FileSystemService\>();  
// Note: The LspBridge \*cannot\* be given the ILanguageServer here,  
// as the server hasn't been built yet.

// \--- 4\. Configure and Register the Language Server \---  
builder.Services.AddLanguageServer(options \=\>  
{  
    options  
        // 4a. Set Input/Output to null.  
        // This is CRITICAL. It tells OmniSharp not to use stdin/stdout.  
        // Our LspBridge will provide the streams manually.  
        .WithInput(null)  
        .WithOutput(null)

        // 4b. Register all our LSP handlers.  
        // OmniSharp will automatically find these and register them  
        // with the correct interfaces (e.g., ICodeActionHandler).  
        .WithHandler\<CustomTextDocumentSyncHandler\>() // Handles didOpen, didChange  
        .WithHandler\<HoverHandler\>()  
        .WithHandler\<CompletionHandler\>()  
        .WithHandler\<CodeActionHandler\>()  
        // .WithHandler\<ExecuteCommandHandler\>() // If we implement it  
        // .WithHandler\<DocumentFormattingHandler\>() // If we implement it

        // 4c. Register services \*within\* the LSP container.  
        // This allows the handlers above to inject them.  
        .WithServices(lspServices \=\>  
        {  
            // We re-register our singletons here so the LSP-scoped  
            // handlers can access the \*same instances\* as the Hub.  
            lspServices.AddSingleton(provider \=\>  
                provider.GetRequiredService\<ApiSpecService\>());

            lspServices.AddSingleton(provider \=\>  
                provider.GetRequiredService\<ScribanParserService\>());  
              
            // Note: We do NOT register the LspBridge here, as no  
            // handler should ever need to talk to it.  
        });  
});

// \--- 5\. Build the Application \---  
var app \= builder.Build();

// \--- 6\. Configure the HTTP Pipeline \---  
if (app.Environment.IsDevelopment())  
{  
    // ... any dev-specific middleware  
}  
app.UseRouting();  
app.UseCors();  
app.MapHub\<ScribanHub\>("/scribanhub");

// \--- 7\. STARTUP ORCHESTRATION \---  
// This is the final, critical step to connect the bridge.  
// We must do this \*after\* app.Build() but \*before\* app.Run().

// 7a. Get the fully constructed ILanguageServer  
var lspServer \= app.Services.GetRequiredService\<ILanguageServer\>();

// 7b. Get our singleton LspBridge  
var lspBridge \= app.Services.GetRequiredService\<LspBridge\>();

// 7c. Pass the server instance to the bridge.  
// This triggers the bridge's Initialize() method, which subscribes  
// to the server's Output stream. The pipe is now complete.  
lspBridge.Initialize(lspServer);

// 7d. Manually start the LSP Server's "Initialize" task in the background.  
// This is not strictly required by OmniSharp, but is good practice  
// to ensure it's ready before clients connect.  
var lspTask \= lspServer.Initialize(CancellationToken.None);

// \--- 8\. Run the Application \---  
// This starts the Kestrel web server and begins listening for requests.  
app.Run();

