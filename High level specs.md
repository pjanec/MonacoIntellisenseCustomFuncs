# **Scriban Language Server: Specification 0 \- System Overview**

## **1\. Purpose**

This document provides a high-level overview of the **Scriban Language Server** system. The primary goal of this system is to provide a rich, comfortable, and guided IDE-like experience for developers writing custom Scriban scripts.

Currently, editing these scripts is a manual, error-prone process that relies heavily on user memory for function names, parameter order, and valid constant values. This system is designed to solve that problem by providing real-time validation, intelligent code completion, and context-aware UI assistance.

The core objective is to reduce cognitive load, minimize syntax and semantic errors, and significantly improve developer velocity and confidence.

## **2\. System Architecture at a Glance**

The system is a classic **client-server** architecture, designed to separate presentation logic (the editor) from language logic (the parser).

\<-\> \[SignalR Transport\] \<-\> \[Backend Server (LSP)\]\]

### **2.1. The Web Client (Frontend)**

The client is a web application that acts as a **"dumb terminal."** It has no knowledge of the Scriban language syntax.

* **Key Technologies:** React, Monaco Editor, SignalR Client, monaco-languageclient.  
* **Core Responsibilities:**  
  * Render the Monaco editor component.  
  * Establish a persistent SignalR connection to the backend.  
  * Report all user interactions (text changes, cursor movements, key presses) to the server.  
  * Listen for commands from the server (e.g., "show squiggly line," "show hover info," "open this specific picker").  
  * Render custom React-based UI components (like a file picker) on command from the server.

### **2.2. The Language Server (Backend)**

The backend is an ASP.NET Core application that acts as the **"smart brain."** It contains 100% of the language logic.

* **Key Technologies:** ASP.NET Core, SignalR, OmniSharp.Extensions.LanguageServer (LSP Framework), Scriban (Nuget).  
* **Core Responsibilities:**  
  * Host the Language Server and the SignalR Hub.  
  * Maintain the complete and correct state of all documents being edited.  
  * Parse and analyze the script on every change.  
  * Respond to standard LSP requests (e.g., hover, completion).  
  * Proactively send validation errors (diagnostics) to the client.  
  * Listen for custom client triggers (e.g., CheckTrigger) and send back custom commands (e.g., OpenPicker).  
  * Serve data for custom UI components (e.g., file lists).

### **2.3. The Metadata-Driven Core**

A foundational principle of this architecture is that the server is **metadata-driven**. The server's logic is not hardcoded. Instead, it loads a formal **ApiSpec.json** file on startup.

This specification file defines the entire "shape" of the available script API, including:

* Global objects (e.g., os) and their members (e.g., execute).  
* Global functions (e.g., copy\_file).  
* Parameter details for every function, including names and types.  
* The specific **picker UI** to use for each parameter (e.g., "file-picker" or "enum-list").  
* The list of valid options for "enum" parameters.  
* The list of valid "macros" for in-string insertion.

This approach makes the system highly extensible and maintainable. Adding a new function with a new custom picker only requires updating the ApiSpec.json, not recompiling the server's core logic.

### **2.4. The Hybrid Communication Protocol**

Communication runs over a single SignalR connection, but it consists of two distinct protocols working in parallel:

1. **Standard LSP Protocol:** Handled automatically by monaco-languageclient and OmniSharp.Extensions.LanguageServer. This covers standard features like hovers, diagnostics, and code actions.  
2. **Custom Event/Command Protocol:** A small set of custom messages we define to handle our unique, non-standard UI.  
   * **Client-to-Server:** e.g., CheckTrigger (when ( is typed).  
   * **Server-to-Client:** e.g., OpenPicker (commands the client to show a specific React component).

## **3\. Key Features (User-Facing)**

This architecture will enable the following key features:

* **Live Validation:** Immediate red squiggly lines for both syntax errors (e.g., missing end) and semantic errors (e.g., using an unknown function or an invalid enum value).  
* **Guided Completion:**  
  * **Member Completion:** os. will automatically pop up a list of members (e.g., execute).  
  * **Enum Completion:** set\_mode( \+ Ctrl+Space will show a list of valid constants.  
* **Rich Contextual Info:** Hovering over any function or object will display detailed help text and code examples loaded directly from the API spec.  
* **Dynamic, Multi-Picker UI:** The editor will automatically (or via Ctrl+Space / Right-Click) open the *correct* UI for the current parameter—a rich file picker for paths, a simple list for enums.  
* **Context-Aware Actions:** The right-click menu will show relevant "Insert Macro" options only when the user is inside a string that supports them.

## **4\. Document Structure**

This overview (Section 0\) is the first of several documents. The subsequent sections will provide a detailed breakdown of each component and its responsibilities.

* **Section 1:** User Stories & Experience Goals  
* **Section 2:** Backend (LSP Server) Detailed Architecture  
* **Section 3:** Web Client (Monaco) Detailed Architecture  
* **Section 4:** Communication Protocol Specification

# **Scriban Language Server: Specification 1 \- User Stories & Experience Goals**

## **1\. Introduction**

This document details the user-centric requirements for the Scriban language editor. These stories define the "what" and "why" of the system's features, focusing on the desired end-user experience. The goal is to build an editor that is intuitive, guided, and significantly reduces the potential for user error.

## **1.1. Core Editing & Feedback**

These stories cover the passive, real-time feedback users receive as they type.

### **1.1.1. Live Syntactic Validation**

* **Story:** As a script developer, I want to see a **red squiggly line** under any code that violates the core Scriban syntax, so that I can fix typos and structural errors immediately.  
* **User Experience (UX):** A developer is typing a for loop. They type for item in collection but forget to type end at the end of the script. As soon as they pause typing, a red line should appear under the for token, and a corresponding error should appear in the "Problems" panel indicating that an end statement is missing.  
* **Acceptance Criteria (AC):**  
  * **AC 1:** Validation must run passively in the background on every text change (after a short debounce).  
  * **AC 2:** Errors from the Scriban nuget parser (e.g., Unexpected token ')', Missing 'end' statement) must be mapped to Monaco diagnostics (squiggly lines).  
  * **AC 3:** Errors must appear in the editor without the user saving the file.  
  * **AC 4:** Clearing the text that caused the error (e.g., adding the end statement) must immediately (after debounce) clear the squiggly line.

### **1.1.2. Live Semantic Validation**

* **Story:** As a script developer, I want to see a **warning squiggly line** if I use a function or object that isn't defined in the API specification, so that I can correct misspellings or discover the correct API.  
* **User Experience (UX):** A developer tries to call os.execute, but mistypes it as os.execut. The system should immediately place a warning or error line under execut, with a hover message like "Function 'execut' is not a valid member of 'os'."  
* **Acceptance Criteria (AC):**  
  * **AC 1:** The server must use the ApiSpec.json to check all function calls.  
  * **AC 2:** A call to an unknown global function (e.g., copy\_fille(...)) must be marked as an error/warning.  
  * **AC 3:** A call to an unknown member function (e.g., os.exicute(...)) must be marked as an error/warning.  
  * **AC 4:** Passing an invalid "enum" constant (e.g., set\_mode("RETRY\_FAST") when only RETRY\_LINEAR is allowed) must be marked as an error/warning.  
  * **AC 5:** Calling a function with the wrong number of arguments (e.g., copy\_file("src.txt") when two are required) must be marked as an error/warning.

### **1.1.3. Rich Hover Information**

* **Story:** As a script developer, I want to hover my mouse over any function name (like os.execute) or object (os) and see a rich tooltip, so that I can instantly understand what it does and how to use it without leaving the editor.  
* **User Experience (UX):** A developer can't remember the parameter order for copy\_file. They hover their mouse over the copy\_file text. A VS Code-style hover box appears, formatted in Markdown, showing the function's description, its full signature (copy\_file(source, destination)), and a simple code example (copy\_file("src/a.txt", "dest/")).  
* **Acceptance Criteria (AC):**  
  * **AC 1:** Hovering over a function name defined in the spec must trigger a textDocument/hover response.  
  * **AC 2:** The hover content must be formatted as MarkupContent (Markdown).  
  * **AC 3:** The content must be sourced from the hover property in the ApiSpec.json and should clearly display (if available) the description, signature, and a code example.  
  * **AC 4:** Hovering over an object (like os) should also show its description from the spec.

## **1.2. Guided Completion & Input**

These stories cover the active, triggered assistance that guides users as they write code.

### **1.2.1. Object Member Completion**

* **Story:** As a script developer, when I type an object name (e.g., os) followed by a dot (.), I want a suggestion list to pop up *automatically*, so that I can see all available member functions.  
* **User Experience (UX):** The developer types os.. Instantly, a completion list appears showing execute with a "function" icon and its description.  
* **Acceptance Criteria (AC):**  
  * **AC 1:** The . character must be a completionTriggerCharacter.  
  * **AC 2:** When a trigger is detected, the server must parse the object os, look it up in the ApiSpec.json, and find its members array.  
  * **AC 3:** The server must return a CompletionList containing all valid members (e.g., execute).  
  * **AC 4:** Selecting an item from the list should correctly insert the text.

### **1.2.2. "Enum" Parameter Completion**

* **Story:** As a script developer, when my cursor is at a parameter that expects a set of constants (e.g., set\_mode(|)), I want to press Ctrl+Space to see a simple list of valid options, so I don't have to guess or memorize them.  
* **User Experience (UX):** The developer types set\_mode( and presses Ctrl+Space. A standard Monaco suggestion list appears with the options RETRY\_NONE, RETRY\_LINEAR, and RETRY\_EXPONENTIAL. The user selects RETRY\_LINEAR and the text is inserted.  
* **Acceptance Criteria (AC):**  
  * **AC 1:** When the client sends a CheckTrigger (or textDocument/completion) request from within a function parameter, the server must check its spec.  
  * **AC 2:** If the spec defines the parameter's picker as "enum-list", the server should handle the request by returning a CompletionList.  
  * **AC 3:** The CompletionList must contain the options array from the ApiSpec.json for that parameter.

### **1.2.3. Automatic File Picker (on ()**

* **Story:** As a script developer, when I type a function name and its opening parenthesis (e.g., copy\_file(), I want the custom file picker to open **automatically**, so that I am immediately guided to provide a valid path.  
* **User Experience (UX):** The developer types copy\_file(. A half-second later, the custom \<FilePicker\> React component appears, positioned near their cursor, already showing the file system list.  
* **Acceptance Criteria (AC):**  
  * **AC 1:** The ( character must be a trigger for the client to send a CheckTrigger message.  
  * **AC 2:** The server must parse the context. If it determines the cursor is at parameter 0 of copy\_file, and the spec defines that parameter's picker as "file-picker", it must respond.  
  * **AC 3:** The server's response must be the custom OpenPicker command, specifying pickerType: "file-picker".  
  * **AC 4:** The client must receive this command and render the \<FilePicker\> component.

### **1.2.4. Manual File Picker (Keyboard & Mouse)**

* **Story:** As a script developer, if I'm editing an *existing* path or at an *empty* path parameter, I want to use Ctrl+Space OR my right-click menu to manually open the file picker, so I can easily select or change the value.  
* **User Experience (UX):**  
  * **Keyboard:** The developer is at copy\_file(|, "dest/") and presses Ctrl+Space. The \<FilePicker\> opens.  
  * **Mouse:** The developer is at copy\_file("src/old.txt"|, "dest/"), right-clicks on "src/old.txt", and sees a "Select Path..." option in the context menu. Clicking it opens the \<FilePicker\>.  
* **Acceptance Criteria (AC):**  
  * **AC 1 (Keyboard):** A Ctrl+Space keypress must send a CheckTrigger message to the server. The server must parse the context, see it's a "file-picker" parameter, and send the OpenPicker command.  
  * **AC 2 (Mouse):** A right-click must trigger a textDocument/codeAction request.  
  * **AC 3 (Mouse):** The server must parse the context. If it's a "file-picker" parameter, it must return a CodeAction with the title "Select Path..." and the command scriban.openPicker.  
  * **AC 4 (Client):** The client must have a command handler for scriban.openPicker that opens the \<FilePicker\> component.

### **1.2.5. In-String Macro Insertion**

* **Story:** As a script developer, when I'm editing a string for a parameter that supports macros (like a log message), I want to right-click and see a context-aware list of available macros, so I can insert them easily without remembering their {{...}} syntax.  
* **User Experience (UX):** The developer is typing log\_message("Error: |"). They right-click inside the string. The context menu shows "Insert Macro" submenu, which contains "Insert Timestamp" and "Insert User ID". Clicking "Insert Timestamp" inserts {{ date.now }} at their cursor.  
* **Acceptance Criteria (AC):**  
  * **AC 1:** A right-click inside a string must trigger a textDocument/codeAction request.  
  * **AC 2:** The server must parse the context and identify the function and parameter (e.g., log\_message, param 0).  
  * **AC 3:** The server must look up the macros array for that parameter in the ApiSpec.json.  
  * **AC 4:** The server must return a CodeAction for each macro, containing a title (e.g., "Insert Timestamp") and a workspace/executeCommand command.  
  * **AC 5:** The command's arguments must be the literal text to insert (e.g., {{ date.now }}).  
  * **AC 6:** The client's command handler will execute the text insertion.

# **Scriban Language Server: Specification 2 \- Backend (LSP Server) Architecture**

## **1\. Core Responsibilities**

The Backend Server is the **authoritative "brain"** of the entire system. It acts as a dedicated ASP.NET Core application responsible for all language-specific logic, validation, and data retrieval.

Its primary responsibilities are:

1. **Be the Single Source of Truth:** It alone understands the Scriban language and the custom API. The client is considered a "dumb terminal."  
2. **Be Metadata-Driven:** All language features (completions, hovers, validation) must be driven by a formal, external API specification (ApiSpec.json), not by hardcoded logic.  
3. **Maintain State:** It must maintain an in-memory, real-time representation of all text documents being edited by users.  
4. **Provide Hybrid Communication:** It must service both standard LSP requests (via monaco-languageclient) and custom, non-LSP data requests (for custom UI) over a single SignalR transport.

## **2\. Core Components & Stack**

The backend is a single ASP.NET Core application with the following key technologies:

* **Host:** ASP.NET Core (latest)  
* **Transport:** ASP.NET Core SignalR  
* **LSP Framework:** OmniSharp.Extensions.LanguageServer  
  * This framework provides the host (ILanguageServer), document management (TextDocumentStore), and message routing for all standard LSP protocol messages.  
* **Language Parser:** Scriban (NuGet)  
  * Used for its high-performance parser to turn raw text into a Concrete Syntax Tree (CST) for syntactic validation and analysis.

## **3\. The Metadata Engine (The "Brain")**

The server's logic is entirely dependent on an external metadata file, ApiSpec.json. This ensures the system is extensible and maintainable.

### **3.1. ApiSpec.json Specification**

This JSON file defines the entire API surface available to the script.

* **File Schema (High-Level):**

{  
  "globals": \[  
    {  
      "name": "os",  
      "type": "object",  
      "hover": "Provides access to operating system functions.",  
      "members": \[ /\* ... member function definitions ... \*/ \]  
    },  
    {  
      "name": "copy\_file",  
      "type": "function",  
      "hover": "Copies a file.\\n\\n\*\*Example:\*\*\\n\`copy\_file(\\"a.txt\\", \\"b.txt\\")\`",  
      "parameters": \[  
        { "name": "source", "type": "path", "picker": "file-picker" },  
        { "name": "destination", "type": "path", "picker": "file-picker" }  
      \]  
    },  
    {  
      "name": "set\_mode",  
      "type": "function",  
      "hover": "Sets the operational mode.",  
      "parameters": \[  
        {  
          "name": "mode",  
          "type": "constant",  
          "picker": "enum-list",  
          "options": \["MODE\_FAST", "MODE\_SLOW", "MODE\_SAFE"\]  
        }  
      \]  
    },  
    {  
      "name": "log\_message",  
      "type": "function",  
      "hover": "Writes a message to the system log.",  
      "parameters": \[  
        {  
          "name": "message",  
          "type": "string",  
          "picker": "none",  
          "macros": \["TIMESTAMP", "USER\_ID"\]  
        }  
      \]  
    }  
  \]  
}

*   
* **picker Property:** This is the key that drives the UI.  
  * "file-picker": Tells the server to command the client to open the custom React \<FilePicker\>.  
  * "enum-list": Tells the server to provide standard CompletionItems for a native Monaco list.  
  * "none": Indicates this parameter requires no special UI.  
* **macros Property:** A list of valid macro keys for a given string parameter, used by the CodeActionHandler.

### **3.2. ApiSpecService**

* A C\# **Singleton** service registered in Program.cs.  
* It loads and parses ApiSpec.json on application startup.  
* It exposes helper methods (e.g., TryGetFunction(string name), GetParameterSpec(string functionName, int paramIndex)) for all other services and handlers to consume.  
* It acts as the in-memory, read-only database for all API metadata.

## **4\. Internal Services (The "Internal APIs")**

These are the core C\# services that perform the work. They are registered as singletons and injected into the LSP handlers.

### **4.1. ScribanParserService**

* **Responsibility:** Performs all text parsing and semantic analysis.  
* **Dependencies:** ApiSpecService.  
* **Key Methods:**  
  * Parse(TextDocument document): Takes a document from the TextDocumentStore, parses it with the Scriban nuget, and returns a Scriban.Parsing.ScriptNode.  
  * GetSemanticErrors(ScriptNode ast): Analyzes the AST and compares it against the ApiSpecService to find semantic errors (unknown functions, wrong arg counts, invalid enum values). Returns a list of Diagnostic objects.  
  * GetNodeAtPosition(ScriptNode ast, Position position): Finds the specific syntax node (e.g., FunctionCall, StringLiteral) at the user's cursor.  
  * GetParameterContext(ScriptNode ast, Position position): A high-level helper that, given a cursor position, returns a rich object: { FunctionName: "copy\_file", ParameterIndex: 0, ParameterSpec: { ... }, IsOnPathParameter: true }. This is the most-used method by handlers.

### **4.2. FileSystemService**

* **Responsibility:** Provides file and folder lists for the custom picker.  
* **Implementation:** A simple service that can (for example) read a directory structure from disk or a configured source.  
* **Key Methods:**  
  * GetPathSuggestions(ParameterSpec spec): Returns a list of file/folder strings based on the parameter's requirements (e.g., "files only").

## **5\. Server-Side Handlers (The "Entrypoints")**

These are the classes that directly respond to client requests. They are orchestrated by OmniSharp.Extensions.LanguageServer and are generally lightweight, delegating the hard work to the internal services.

### **5.1. LSP Handlers (Standard Protocol)**

* **TextDocumentSyncHandler:**  
  * **Handles:** textDocument/didOpen, textDocument/didChange.  
  * **Action:** Receives text changes from the client. OmniSharp automatically uses this to update its internal TextDocumentStore.  
  * **Triggers:** This handler (on didChange) will immediately call the ValidationService to kick off a new validation pass.  
* **DiagnosticsHandler (Custom, but uses LSP):**  
  * **Handles:** Runs after didChange.  
  * **Action:** Gets the latest AST from ScribanParserService, gets syntax errors from the Scriban parser, and gets semantic errors from the ScribanParserService.GetSemanticErrors().  
  * **Result:** Pushes a textDocument/publishDiagnostics message to the client, which appears as squiggly lines.  
* **CompletionHandler:**  
  * **Handles:** textDocument/completion (when . is typed or Ctrl+Space is pressed).  
  * **Action:**  
    1. Calls ScribanParserService.GetNodeAtPosition().  
    2. **If os.:** Gets os from ApiSpecService and returns its members.  
    3. **If in set\_mode(|):** Gets context from ScribanParserService, sees picker: "enum-list", and returns the options from the spec as CompletionItems.  
    4. **If in copy\_file(|):** Gets context, sees picker: "file-picker". **Does nothing** (returns null), because the custom CheckTrigger flow will handle this.  
* **HoverHandler:**  
  * **Handles:** textDocument/hover.  
  * **Action:** Calls ScribanParserService.GetNodeAtPosition(), finds the corresponding function/object in the ApiSpecService, and returns its hover string as MarkupContent.  
* **CodeActionHandler:**  
  * **Handles:** textDocument/codeAction (when right-click menu is opened).  
  * **Action:** Calls ScribanParserService.GetParameterContext().  
    1. **If on a "file-picker" param:** Returns a CodeAction with the title "Select Path..." and the command scriban.openPicker.  
    2. **If on a string param with "macros":** Returns a CodeAction for each macro (e.g., "Insert Timestamp") with the command scriban.insertMacro.  
* **ExecuteCommandHandler:**  
  * **Handles:** workspace/executeCommand (when a user clicks a CodeAction).  
  * **Action:**  
    1. **If scriban.openPicker:** (This is a fallback/alternative flow) It can send the custom OpenPicker command to the client.  
    2. **If scriban.insertMacro:** It returns a WorkspaceEdit to the client, which tells Monaco to insert the macro text (e.g., {{ date.now }}).

### **5.2. SignalR Hub (ScribanHub)**

* **Responsibility:** Handles all non-LSP communication.  
* **Key Methods (Client-Callable):**  
  * **LspBridge:** SendMessage(JToken message): This is the "pipe" for all standard LSP messages.  
  * **CheckTrigger(context):**  
    * Called by the client on (, ,, or Ctrl+Space.  
    * Delegates to ScribanParserService.GetParameterContext().  
    * If the context has a picker: "file-picker", it calls the client-side OpenPicker method.  
  * **GetPathSuggestions(functionName, parameterIndex):**  
    * Called by the client's \<FilePicker\> component.  
    * Delegates to the FileSystemService to get the list of strings.  
* **Key Methods (Server-Callable):**  
  * **ReceiveMessage(JToken message):** The "pipe" for all standard LSP messages going *out* to the client.  
  * **OpenPicker(data):**  
    * Called by the CheckTrigger handler.  
    * Payload: { pickerType: "file-picker", functionName: "copy\_file", ... }.  
    * Commands the client to open its custom React UI.

# **Scriban Language Server: Specification 3 \- Web Client (Monaco) Architecture**

## **1\. Core Responsibilities**

The Web Client is the **"dumb terminal"** in the client-server architecture. Its primary responsibility is **presentation**, not logic. It must be completely "Scriban-ignorant" and must not contain any parsers, validation rules, or semantic knowledge of the language.

Its responsibilities are:

1. **Render the Editor:** Provide a responsive Monaco Editor instance.  
2. **Establish & Maintain Connection:** Use SignalR to establish a persistent, real-time connection to the backend.  
3. **Forward User Input:** Report all significant user interactions (text changes, key presses, clicks) to the server.  
4. **Handle Standard LSP:** Use monaco-languageclient to automatically enable standard IDE features (hovers, diagnostics, etc.) powered by the server.  
5. **Obey Server Commands:** Listen for custom commands from the server (e.g., OpenPicker) and render the appropriate custom React UI in response.

## **2\. Core Components & Stack**

The client is a standard React application (e.g., via Vite or Create React App).

* **UI Framework:** React 18+  
* **Editor:** monaco-editor  
* **Transport:** @microsoft/signalr  
* **LSP Client:** monaco-languageclient  
* **State Management:** React Hooks, Context (for providing the HubConnection to components).

## **3\. Initialization & Connection Flow**

The root of the application (e.g., App.tsx) is responsible for orchestrating the entire startup sequence.

1. **Establish SignalR Connection:** On application load, a HubConnection to the server's /scribanhub endpoint is created and started. This connection instance is then provided to all child components via a React Context (HubConnectionContext).  
2. **Instantiate SignalR Adapter:** A custom SignalR-LSP-Adapter class is instantiated, receiving the HubConnection as a constructor argument.  
3. **Launch Language Client:** The monaco-languageclient is initialized. Instead of a typical WebSocket or Worker, it is configured to use the SignalR-LSP-Adapter as its message transport.  
4. **Start Services:** The language client is started. It automatically handles the LSP initialization handshake (initialize) with the server.  
5. **Register Command Handlers:** The client registers handlers for all custom commands that can be triggered by the server (e.g., scriban.openPicker, scriban.insertMacro).

## **4\. The SignalR-LSP-Adapter (The "Pipe")**

This is a small but critical TypeScript class that bridges the monaco-languageclient (which expects a MessageConnection) with the SignalR (which has invoke and on methods).

* **File:** src/services/SignalRMessageAdapter.ts  
* **Purpose:** To make the SignalR connection "look like" a standard LSP message stream.

// Pseudo-code for the adapter  
import { HubConnection } from "@microsoft/signalr";  
import { MessageConnection, /\*...LSP types...\*/ } from "monaco-languageclient";

export class SignalRMessageAdapter implements MessageConnection {  
  private hubConnection: HubConnection;  
  private messageCallback: (message: any) \=\> void;

  constructor(hubConnection: HubConnection) {  
    this.hubConnection \= hubConnection;  
      
    // 1\. Listen for ALL LSP messages from the server  
    this.hubConnection.on("ReceiveMessage", (message: any) \=\> {  
      if (this.messageCallback) {  
        this.messageCallback(message);  
      }  
    });  
  }

  // Called by monaco-languageclient to register its main listener  
  listen(callback: (message: any) \=\> void) {  
    this.messageCallback \= callback;  
  }

  // Called by monaco-languageclient to send any message to the server  
  send(message: any) {  
    // 2\. Forward the LSP message to the hub's generic "pipe"  
    this.hubConnection.invoke("SendMessage", message);  
  }

  // ... other required methods (dispose, onNotification, etc.)  
}

## **5\. The Main Editor Hook (useScribanEditor)**

This is the "brain" of the client-side. It is a React hook that encapsulates all editor-related logic, state, and event listeners.

* **File:** src/hooks/useScribanEditor.ts  
* **State:**  
  * pickerState: An object that holds the state for the custom UI.  
    * isVisible: boolean  
    * pickerType: "file-picker" | "other-picker" | null  
    * functionName: string  
    * parameterIndex: number  
    * currentValue: string | null  
    * position: { x: number, y: number } (Screen coordinates)  
* **Responsibilities & Listeners:**  
  * **Listen for monaco-languageclient commands:**  
    * languageClient.onNotification("workspace/executeCommand", ...):  
      * Handles commands from the server's CodeAction (right-click menu).  
      * if (command \=== "scriban.openPicker"): Gets the arguments (functionName, parameterIndex), calculates the current cursor's screen position, and updates pickerState to open the picker.  
      * if (command \=== "scriban.insertMacro"): Gets the text to insert from the arguments and executes a Monaco InsertText edit.  
  * **Listen for custom server-to-client commands:**  
    * hubConnection.on("OpenPicker", (data) \=\> ...):  
      * This is the listener for the *automatic* trigger.  
      * It receives the payload from the server (e.g., { pickerType: "file-picker", ... }).  
      * It calculates the current cursor's screen position and updates pickerState to open the picker.  
  * **Listen for user input (client-to-server triggers):**  
    * editor.onDidChangeModelContent(...):  
      * Detects if the change was a single ( or , character.  
      * If so, it calls hubConnection.invoke("CheckTrigger", { event: 'char', char: '(', ...context }).  
    * editor.onKeyDown(...):  
      * Detects Ctrl+Space.  
      * Prevents the default Monaco suggestion widget *if* it's going to send a custom trigger.  
      * It calls hubConnection.invoke("CheckTrigger", { event: 'hotkey', ...context }).  
      * **Note:** The client *always* sends CheckTrigger. It is the **server's** responsibility to decide what to do. If the server does nothing (e.g., for an "enum" parameter), the client's monaco-languageclient will *also* have sent a standard textDocument/completion request, which the server *will* answer. This parallel flow correctly handles both custom pickers and standard enum lists.

## **6\. Custom UI Components (The "View")**

These are the React components that render the custom user interfaces commanded by the server.

### **6.1. \<PickerRouter\>**

* **Responsibility:** A conditional renderer. It is the single entry point for all custom picker UI.  
* **Logic:**  
  * It is rendered by App.tsx and receives the pickerState from the useScribanEditor hook.  
  * if (\!pickerState.isVisible) return null;  
  * It uses a switch (pickerState.pickerType) to determine which specific picker to render.

### **6.2. \<FilePicker\>**

* **Responsibility:** Renders the UI for selecting files and folders.  
* **Props:** It receives functionName, parameterIndex, and currentValue from the pickerState.  
* **Data Fetching:**  
  * It is a **data-independent** component. It does not receive the file list as a prop.  
  * On mount, it uses a useEffect hook to call the server:

const \[items, setItems\] \= useState(\[\]);  
const hubConnection \= useContext(HubConnectionContext);

useEffect(() \=\> {  
  hubConnection.invoke("GetPathSuggestions", functionName, parameterIndex)  
    .then(setItems);  
}, \[hubConnection, functionName, parameterIndex\]);

*   
  * **UI:** It renders a filterable list based on the items state. When an item is selected, it calls the onSelect prop (which is passed down from the hook) to insert the text.

# **Scriban Language Server: Specification 4 \- Communication Protocol**

This document defines the complete API contract between the Web Client (Monaco) and the Backend (LSP Server). It is a hybrid protocol, using SignalR as a transport for two types of payloads:

1. **Standard LSP:** Standard Language Server Protocol messages, piped by monaco-languageclient.  
2. **Custom Messages:** A small set of custom RPC calls to handle custom UI triggers and data fetching.

## **1\. Transport Layer**

* **Technology:** ASP.NET Core SignalR  
* **Endpoint:** /scribanhub  
* **Connection:** A single, persistent bi-directional connection is established by the client on startup.

## **2\. Standard LSP Messages (Piped)**

These messages are created and consumed by monaco-languageclient on the client and OmniSharp.Extensions.LanguageServer on the server. They are transported over the SignalR "pipe" methods.

* **Pipe Methods:**  
  * **Client-to-Server:** SendMessage(JToken message)  
  * **Server-to-Client:** ReceiveMessage(JToken message)

### **2.1. Client-to-Server (Requests)**

* **initialize**  
  * **Trigger:** languageClient.start()  
  * **Purpose:** Standard LSP handshake.  
* **textDocument/didOpen**  
  * **Trigger:** Monaco editor is loaded with initial text.  
  * **Purpose:** Sends the full document text to the server to create it in the TextDocumentStore.  
* **textDocument/didChange**  
  * **Trigger:** Any user key press, deletion, or paste.  
  * **Purpose:** Sends an incremental text change (e.g., "inserted 'a' at L5:C10") to keep the server's document in sync.  
* **textDocument/hover**  
  * **Trigger:** User hovers the mouse over a word.  
  * **Purpose:** Asks the server for hover information.  
* **textDocument/completion**  
  * **Trigger:** User types a trigger character (like .) or presses Ctrl+Space.  
  * **Purpose:** Asks the server for a list of standard completion items. (Used for os. and "enum" parameters).  
* **textDocument/codeAction**  
  * **Trigger:** User right-clicks in the editor.  
  * **Purpose:** Asks the server for a list of available context-menu actions.  
* **workspace/executeCommand**  
  * **Trigger:** User clicks a CodeAction from the context menu.  
  * **Purpose:** Tells the server to execute the command associated with that action.  
* **textDocument/formatting**  
  * **Trigger:** User presses Shift+Alt+F (or runs format command).  
  * **Purpose:** Asks the server to return a formatted version of the script.

### **2.2. Server-to-Client (Notifications & Responses)**

* **textDocument/publishDiagnostics**  
  * **Trigger:** Server finishes validation after a didChange.  
  * **Purpose:** Sends a list of all errors and warnings. The client automatically renders these as squiggly lines.  
* **Response (Hover)**  
  * **Payload:** Hover { Contents: MarkupContent } (where Contents is Markdown from the ApiSpec.json).  
* **Response (Completion)**  
  * **Payload:** CompletionList { Items: \[CompletionItem, ...\] } (e.g., for os.execute or MODE\_FAST).  
* **Response (CodeAction)**  
  * **Payload:** \[CodeAction, ...\]  
  * **Example Action:** { Title: "Select Path...", Kind: "refactor", Command: { Name: "scriban.openPicker", ... } }  
* **Response (ExecuteCommand)**  
  * **Payload:** WorkspaceEdit  
  * **Purpose:** Used for "Insert Macro" to send the text to be inserted back to the client.

## **3\. Custom SignalR Messages (The "Side-Channel")**

These are custom methods defined on the SignalR Hub, existing *alongside* the LSP pipe. They are used to control the custom React UI.

### **3.1. Client-to-Server (Custom RPC)**

* **CheckTrigger(context)**  
  * **Trigger:** Client's useScribanEditor hook detects (, ,, or Ctrl+Space.  
  * **Payload (context):**

{  
  "event": "char" | "hotkey",  
  "char": "(", // (or "," or null)  
  "line": "copy\_file(", // The full line text  
  "position": { "line": 0, "character": 10 } // LSP position  
}

*   
  * **Purpose:** Asks the server, "The user just did something. Based on this context, should I open a custom picker?"  
* **GetPathSuggestions(functionName, parameterIndex)**  
  * **Trigger:** The client's \<FilePicker\> component mounts.  
  * **Payload:**

{ "functionName": "copy\_file", "parameterIndex": 0 }

*   
  * **Purpose:** Asks the server for the list of strings to show in the file picker.  
  * **Returns:** Promise\<string\[\]\> (e.g., \["src/", "src/index.js", "README.md"\])  
* **GetEnumOptions(functionName, parameterIndex)**  
  * **Trigger:** The client's \<EnumPicker\> component mounts (if you choose to implement it this way).  
  * **Payload:**

{ "functionName": "set\_mode", "parameterIndex": 0 }

*   
  * **Purpose:** Asks the server for the list of valid enum options.  
  * **Returns:** Promise\<string\[\]\> (e.g., \["MODE\_FAST", "MODE\_SLOW"\])  
  * **Note:** This is an *alternative* to using the standard textDocument/completion for enums.

### **3.2. Server-to-Client (Custom Commands)**

* **OpenPicker(data)**  
  * **Trigger:** Server's CheckTrigger handler determines a custom UI should be shown.  
  * **Payload (data):**

{  
  "pickerType": "file-picker", // From ApiSpec.json  
  "functionName": "copy\_file",  
  "parameterIndex": 0,  
  "currentValue": null // or "src/a.txt" if editing  
}

*   
  * **Purpose:** Commands the client, "Open the \<FilePicker\> component *now* for this specific parameter."

# Test Categories

All the code must be written in a testable way.

Here are the test categories I suggest. We can think of them as concentric circles, from the fast, simple "Core" tests to the slower, complex "Integration" tests. Your test pyramid should be heavily weighted toward the first two categories.

### **Category 1: Backend Core Logic (C\# Unit Tests)**

* **Goal:** To prove the "brain" of the server works in complete isolation.  
* **What to Test:** The ScribanParserService, ApiSpecService, and FileSystemService.  
* **Test Method:**  
  * **ScribanParserService:** Instantiate the service. Pass it raw code strings. Assert that GetParameterContext returns the correct function name, parameter index, and spec details for a given cursor position. This is your most important set of tests.  
  * **ApiSpecService:** Instantiate the service. Load a mock ApiSpec.json string. Assert that GetFunction("os.execute") returns the correct data.  
* **Speed:** Blazing fast. No network, no LSP host, no async.

### **Category 2: Backend Handler Logic (C\# Unit Tests)**

* **Goal:** To prove the internal API of each LSP handler works. This is what you mentioned as a high priority.  
* **What to Test:** CodeActionHandler, CompletionHandler, HoverHandler, and the custom CheckTrigger logic in your ScribanHub.  
* **Test Method:**  
  * Instantiate the handler class (e.g., new CodeActionHandler(...)).  
  * **Mock all dependencies** using a library like Moq. Mock the IScribanParserService, IApiSpecService, and TextDocumentStore.  
  * Set up your mocks: parserMock.Setup(...).Returns(myTestParameter).  
  * Call the Handle(...) method directly with a manually crafted CodeActionParams object.  
  * Assert that the returned POCO (the CodeAction object) contains the correct command and title.  
* **Speed:** Very fast. No network, no LSP host. Tests your internal API contract.

### **Category 3: Client Component Logic (TS/React Unit Tests)**

* **Goal:** To prove the internal API of your React components.  
* **What to Test:** Your UI components like \<FilePicker\>, \<EnumPicker\>, and \<PickerRouter\>.  
* **Test Method:**  
  * Use React Testing Library and vitest/Jest.  
  * Render the component in isolation (e.g., render(\<FilePicker ... /\>)).  
  * Pass it **static mock data** (e.g., items={\["file1.txt", "file2.txt"\]}) and mock functions (e.g., onSelect={mockOnSelect}).  
  * Simulate user events (fireEvent.click, fireEvent.change).  
  * Assert that the component filters correctly and that mockOnSelect was called with the right value.  
* **Speed:** Very fast. No browser, no Monaco, no network.

### **Category 4: Client State Logic (TS/React Hook Tests)**

* **Goal:** To prove the "brain" of the client works. This is the client-side equivalent of Category 2\.  
* **What to Test:** The main useScribanEditor hook that manages the pickerState.  
* **Test Method:**  
  * Use renderHook from React Testing Library.  
  * **Mock the HubConnection**. Provide a mock implementation that you can control.  
  * Simulate server-sent messages (e.g., mockHub.simulateServerMessage("OpenPicker", ...)).  
  * Assert that the hook's returned pickerState updates correctly.  
  * Simulate hook function calls (e.g., act(() \=\> result.current.handleTrigger(...))).  
  * Assert that the mockHub.invoke was called with the correct CheckTrigger payload.  
* **Speed:** Very fast. No browser, no Monaco.

---

### **Category 5: Server Integration Tests (LSP)**

* **Goal:** To prove that the OmniSharp LSP host is correctly wired to your handlers.  
* **What to Test:** The full flow from a *real LSP message* to a *real LSP response*.  
* **Test Method:**  
  * Use OmniSharp.Extensions.LanguageServer.Testing.  
  * Create an in-memory LanguageServerTestHost and load your real handlers (CodeActionHandler, etc.) and services (ScribanParserService).  
  * Simulate a client connecting and opening a document (testServer.OpenDocument(...)).  
  * Send a real request (testServer.Client.RequestCodeAction(...)).  
  * Assert the JSON-RPC response is correct.  
* **Speed:** Medium. Slower than unit tests, but no real network.

### **Category 6: Full End-to-End (E2E) Tests**

* **Goal:** To prove the *entire system* works, from the user's keystroke to the UI response.  
* **What to Test:** A few critical "happy path" user stories.  
  * User types copy( \-\> CheckTrigger is sent \-\> OpenPicker is received \-\> \<FilePicker\> appears.  
  * User right-clicks \-\> codeAction is sent \-\> CodeAction response is received \-\> Menu item appears.  
* **Test Method:**  
  * **Client:** Use React Testing Library to render the *full* \<App\>, including the real monaco-languageclient and a **mocked HubConnection**. This is the test you labeled "mocking the LSP server side."  
  * **Server:** Use TestServer (WebAppFactory) to launch your *full* ASP.NET app in memory. Connect a *real SignalR client* to it. Send a CheckTrigger message and assert the OpenPicker message is received.  
* **Speed:** Slow. Use these sparingly for your most critical workflows.

This layered approach gives you the high-speed, high-confidence "internal API" tests you want (Categories 1-4) while still verifying the integration points (Categories 5-6).

# **Scriban Language Server: Specification 5 \- Test Plan**

## **1\. Guiding Principles**

This test plan is designed to ensure the Scriban Language Server is robust, reliable, and maintainable. Our strategy is based on the "Test Pyramid," prioritizing fast, isolated, "internal API" tests over slow, brittle, end-to-end tests.

The primary goal is to **test logic, not plumbing.** We will heavily mock the network (SignalR) and host (LSP, Monaco) layers to test the core business logic of our services and handlers in isolation.

## **2\. Test Categories**

### **Category 1: Backend Core Logic (C\# Unit Tests)**

* **Objective:** To prove that the core "brain" of the server—the ScribanParserService and ApiSpecService—works correctly in complete isolation.  
* **Services Under Test:** ScribanParserService, ApiSpecService, FileSystemService.  
* **Technology:** xUnit (or NUnit).  
* **Methodology:**  
  * Instantiate the service class (e.g., new ScribanParserService(...)).  
  * If testing ScribanParserService, pass it a mock IApiSpecService that returns predefined spec objects.  
  * Pass raw string data (code snippets) to its public methods.  
  * Assert the POCO (Plain Old C\# Object) response.  
* **Example Test Scenarios:**  
  * **ApiSpecService:**  
    * GetFunction("copy\_file") returns the correct FunctionSpec object.  
    * GetFunction("os.execute") returns the correct MemberFunctionSpec object.  
    * GetParameterSpec("log\_message", 0\) returns the correct ParameterSpec with its macros list.  
  * **ScribanParserService:**  
    * GetParameterContext("copy\_file(|)", ...) correctly identifies (function: "copy\_file", index: 0\).  
    * GetParameterContext("copy\_file(\\"src/\\", |)", ...) correctly identifies (function: "copy\_file", index: 1\).  
    * GetParameterContext("os.execute(|)", ...) correctly identifies (function: "os.execute", index: 0\).  
    * GetSemanticErrors("copy\_fille()") returns a diagnostic for an unknown function.  
    * GetSemanticErrors("set\_mode('INVALID')") returns a diagnostic for an invalid enum value.  
    * GetSemanticErrors("copy\_file('one')") returns a diagnostic for the wrong number of arguments.

### **Category 2: Backend Handler Logic (C\# Unit Tests)**

* **Objective:** To prove the internal API of each LSP/SignalR handler. This verifies that our entrypoints correctly consume core logic and produce the right commands, *without* needing a real server.  
* **Handlers Under Test:** CodeActionHandler, CompletionHandler, HoverHandler, ScribanHub (custom methods).  
* **Technology:** xUnit/NUnit \+ Moq.  
* **Methodology:**  
  * Instantiate the *handler* class (e.g., new CodeActionHandler(...)).  
  * **Mock all external dependencies** (e.g., Mock\<IScribanParserService\>, Mock\<TextDocumentStore\>, Mock\<IHubCallerClients\>).  
  * Set up the mocks to return specific data (e.g., parserMock.Setup(...).Returns(myTestParameterContext)).  
  * Call the Handle(...) or custom Hub method (e.g., CheckTrigger(...)) directly.  
  * Assert the returned POCO response (CodeAction) or verify a mock method was called (clientsMock.Verify(c \=\> c.SendAsync("OpenPicker", ...))).  
* **Example Test Scenarios:**  
  * **CodeActionHandler.Handle(...):**  
    * Given a context from the parser for a "file-picker" param, assert the handler returns a CodeAction with the scriban.openPicker command.  
    * Given a context for a "macro" param, assert it returns multiple CodeActions ("Insert Timestamp", etc.) with the scriban.insertMacro command.  
    * Given a context for a non-path, non-macro param, assert it returns an empty list.  
  * **CompletionHandler.Handle(...):**  
    * Given a context for os., assert the handler returns CompletionItems for execute.  
    * Given a context for an "enum-list" param, assert it returns CompletionItems for MODE\_FAST, MODE\_SLOW.  
  * **ScribanHub.CheckTrigger(...):**  
    * Given a CheckTrigger payload for copy\_file( (a "file-picker"), verify Clients.Caller.SendAsync("OpenPicker", ...) is called with the correct pickerType.  
    * Given a CheckTrigger payload for set\_mode( (an "enum-list"), verify that **no client method** is called (as this is handled by textDocument/completion).

### **Category 3: Client Component Logic (React Unit Tests)**

* **Objective:** To prove that our custom React UI components render, filter, and respond to user input correctly in isolation.  
* **Components Under Test:** \<FilePicker\>, \<EnumPicker\>, \<PickerRouter\>.  
* **Technology:** React Testing Library \+ vitest/Jest.  
* **Methodology:**  
  * Render the component in isolation (render(\<FilePicker ... /\>)).  
  * Pass static data via props (e.g., items={\["file1.txt", "file2.txt"\]}).  
  * Pass mock functions via props (e.g., onSelect={mockOnSelect}).  
  * Simulate user events (fireEvent.click(getByText("file1.txt"))).  
  * Assert the component's output (e.g., expect(mockOnSelect).toHaveBeenCalledWith('"file1.txt"')).  
* **Example Test Scenarios:**  
  * \<PickerRouter\> renders \<FilePicker\> when pickerState.pickerType is "file-picker".  
  * \<FilePicker\> correctly filters a list of 100 items to 5 when the user types "src/" in its search box.  
  * \<FilePicker\> calls the onSelect prop with the correct string when an item is clicked.

### **Category 4: Client State Logic (React Hook Tests)**

* **Objective:** To prove the "brain" of the client (the useScribanEditor hook) correctly manages its state machine.  
* **Hook Under Test:** useScribanEditor.  
* **Technology:** React Testing Library (renderHook) \+ vitest/Jest.  
* **Methodology:**  
  * Create a **mock HubConnection** class.  
  * Render the hook (renderHook(() \=\> useScribanEditor(mockHub))).  
  * Simulate server-sent messages (act(() \=\> mockHub.simulateMessage("OpenPicker", ...))).  
  * Assert the hook's returned state (result.current.pickerState.isVisible).  
  * Simulate hook function calls (act(() \=\> result.current.handleTrigger(...))).  
  * Assert that the mock hub's methods were called (expect(mockHub.invoke).toHaveBeenCalledWith("CheckTrigger", ...)).  
* **Example Test Scenarios:**  
  * On initial render, pickerState.isVisible is false.  
  * When the server sends OpenPicker, the pickerState becomes visible and contains the correct functionName and parameterIndex.  
  * When the handleTrigger function (simulating a ( keypress) is called, the hook correctly calls mockHub.invoke("CheckTrigger", ...).  
  * When a workspace/executeCommand notification for scriban.openPicker arrives, the pickerState is correctly updated.

### **Category 5: Server Integration Tests (LSP Host)**

* **Objective:** To prove that the OmniSharp LSP host is correctly wired to our real handlers and services.  
* **Technology:** OmniSharp.Extensions.LanguageServer.Testing.  
* **Methodology:**  
  1. Create an in-memory LanguageServerTestHost.  
  2. Load *real* handlers (CodeActionHandler) and *real* services (ScribanParserService, ApiSpecService).  
  3. Simulate a client connecting and opening a document (testServer.OpenDocument(...)).  
  4. Send a *real* LSP request (testServer.Client.RequestCodeAction(...)).  
  5. Assert the *real* JSON-RPC response is correct.  
* **Priority:** **Medium.** These are valuable sanity checks but are slower than Category 2 tests. A few tests per handler are sufficient.

### **Category 6: End-to-End (E2E) Tests**

* **Objective:** To prove the *entire system* works from the user's keystroke to the UI response.  
* **Technology:** Playwright or Cypress (Client) \+ WebAppFactory (Server).  
* **Methodology:**  
  1. Launch the full backend server in-memory using WebAppFactory.  
  2. Launch a real browser (headless) using Playwright.  
  3. Simulate real user actions (e.g., page.press("Control+Space")).  
  4. Assert that the *real DOM* updates (e.g., expect(page.locator(".file-picker")).toBeVisible()).  
* **Priority:** **Low.** We will write very few (2-3) of these tests. They are brittle and slow, and their scenarios should be 99% covered by the faster tests. They only serve as a final "golden path" verification.

# **Scriban Language Server: Specification 6 \- API Specification (ApiSpec.json) Schema**

## **1\. Overview**

This document defines the formal schema for the ApiSpec.json file. This file is the **single source of truth** for the backend server. It provides all the metadata required to drive validation, completion, hover info, and the custom picker UI.

The server's ApiSpecService will parse this file on startup and provide its contents to all other services and handlers. The entire system is metadata-driven; if a function is not in this file, it does not exist.

## **2\. Root Object**

The root of the ApiSpec.json file is an object containing a single key, globals.

| Key | Type | Description |
| :---- | :---- | :---- |
| globals | array | An array of Entry objects. Each object represents a global variable, object, or function available in the script's root scope. |

## **3\. The Entry Object Schema**

Each item in the globals array is an Entry object. This object has two primary variants, determined by the type property: object or function.

### **3.1. Common Entry Properties**

All entries share these base properties:

| Key | Type | Required | Description |
| :---- | :---- | :---- | :---- |
| name | string | Yes | The name of the variable as used in the script (e.g., os, copy\_file). |
| type | string | Yes | The type of the entry. Must be either "object" or "function". |
| hover | string | Yes | The Markdown-formatted string to be shown in a hover tooltip. Should include a description and a code example. |

### **3.2. type: "object" Entry**

This variant represents a global object that has its own members (e.g., os).

| Key | Type | Required | Description |
| :---- | :---- | :---- | :---- |
| name | string | Yes | The name of the object (e.g., os). |
| type | string | Yes | Must be "object". |
| hover | string | Yes | Hover text for the object itself. |
| members | array | Yes | An array of Function Entry objects, representing the members of this object (e.g., os.execute). |

### **3.3. type: "function" Entry**

This variant represents a global function (e.g., copy\_file) or a member function (if nested inside an object's members array).

| Key | Type | Required | Description |
| :---- | :---- | :---- | :---- |
| name | string | Yes | The name of the function (e.g., copy\_file, execute). |
| type | string | Yes | Must be "function". |
| hover | string | Yes | Hover text for the function, including examples. |
| parameters | array | Yes | An array of Parameter Entry objects. An empty \[\] is required for functions with no parameters. |

## **4\. The Parameter Entry Schema**

This is the most critical schema, as it defines the behavior of each parameter.

| Key | Type | Required | Description |
| :---- | :---- | :---- | :---- |
| name | string | Yes | The name of the parameter (e.g., source, mode). Used for signature help. |
| type | string | Yes | The semantic *type* of the parameter, used for validation. Valid values: "path", "constant", "string", "number", "boolean", "any". |
| picker | string | Yes | **The UI Driver.** This key tells the server how to handle completion and triggers. |
| options | array | No | An array of strings. **Required if picker is "enum-list"**. Defines the valid constant values. |
| macros | array | No | An array of strings. **Optional, only for type: "string"**. Defines the valid macro keys for the right-click menu. |

### **4.1. The picker Property (UI Driver)**

This property is the primary mechanism for controlling the user experience.

| picker Value | type (Typical) | Server Behavior (Trigger: (, ,, Ctrl+Space) | Server Behavior (Completion) |
| :---- | :---- | :---- | :---- |
| **"file-picker"** | "path" | **Sends OpenPicker command** to client. | Returns null. The custom UI is responsible for completion. |
| **"enum-list"** | "constant" | **Does nothing.** | Responds to textDocument/completion with CompletionItems built from the options array. |
| **"none"** | "string", "number", etc. | **Does nothing.** | Returns null. No special completion is offered. |

## **5\. Full ApiSpec.json Example**

This example demonstrates all the schemas defined above.

{  
  "globals": \[  
    {  
      "name": "os",  
      "type": "object",  
      "hover": "Provides access to operating system functions.",  
      "members": \[  
        {  
          "name": "execute",  
          "type": "function",  
          "hover": "Executes a shell command.\\n\\n\*\*Example:\*\*\\n\`os.execute(\\"echo 'hello'\\")\`",  
          "parameters": \[  
            {  
              "name": "command",  
              "type": "string",  
              "picker": "none"  
            }  
          \]  
        }  
      \]  
    },  
    {  
      "name": "copy\_file",  
      "type": "function",  
      "hover": "Copies a file from a source to a destination.\\n\\n\*\*Example:\*\*\\n\`copy\_file(\\"src/a.txt\\", \\"dest/\\")\`",  
      "parameters": \[  
        {  
          "name": "source",  
          "type": "path",  
          "picker": "file-picker"  
        },  
        {  
          "name": "destination",  
          "type": "path",  
          "picker": "file-picker"  
        }  
      \]  
    },  
    {  
      "name": "set\_mode",  
      "type": "function",  
      "hover": "Sets the operational mode for the current context.",  
      "parameters": \[  
        {  
          "name": "mode",  
          "type": "constant",  
          "picker": "enum-list",  
          "options": \[  
            "MODE\_FAST",  
            "MODE\_SLOW",  
            "MODE\_SAFE"  
          \]  
        }  
      \]  
    },  
    {  
      "name": "log\_message",  
      "type": "function",  
      "hover": "Writes a formatted message to the system log.",  
      "parameters": \[  
        {  
          "name": "message",  
          "type": "string",  
          "picker": "none",  
          "macros": \[  
            "TIMESTAMP",  
            "USER\_ID",  
            "SESSION\_ID"  
          \]  
        },  
        {  
          "name": "level",  
          "type": "constant",  
          "picker": "enum-list",  
          "options": \["INFO", "WARN", "ERROR"\]  
        }  
      \]  
    }  
  \]  
}

