Here is the next detailed design document for the frontend, `spec-14`.

This is a highly technical and critical piece of "plumbing." It provides the complete, reusable TypeScript class required to bridge the `monaco-languageclient` library with the `@microsoft/signalr` transport.

import {  
  HubConnection,  
  HubConnectionState,  
} from '@microsoft/signalr';  
import {  
  MessageConnection,  
  NotificationMessage,  
  RequestMessage,  
  ResponseMessage,  
  ErrorResponseMessage,  
  Message,  
} from 'monaco-languageclient/lib/common/connection';  
import {  
  Emitter,  
  Event,  
  Disposable,  
} from 'vscode-jsonrpc';  
import {  
  CancellationToken,  
  CancellationTokenSource,  
} from 'vscode-jsonrpc/lib/common/cancellation';

/\*\*  
 \* \# Scriban Language Server: Specification 14  
 \* \#\# Detailed Design: The \`SignalRMessageAdapter\`  
 \*  
 \* \#\# 1\. Overview  
 \*  
 \* This document provides the detailed design and implementation for the \`SignalRMessageAdapter\`.  
 \* This is a critical piece of frontend "plumbing" that solves the core transport problem.  
 \*  
 \* The \`monaco-languageclient\` library is designed to speak to a \`MessageConnection\`.  
 \* The \`@microsoft/signalr\` library provides a \`HubConnection\`.  
 \*  
 \* This class implements the \`MessageConnection\` interface and "wraps" a \`HubConnection\`  
 \* instance, making it compatible with the \`monaco-languageclient\`.  
 \*  
 \* \#\# 2\. Core Responsibilities  
 \*  
 \* 1\.  \*\*Implement \`MessageConnection\`:\*\* Fulfill the API contract required by \`monaco-languageclient\`.  
 \* 2\.  \*\*Forward Client-to-Server:\*\* Correctly format LSP requests and notifications as JSON-RPC messages and send them to the Hub's \`SendMessage\` method.  
 \* 3\.  \*\*Handle Server-to-Client:\*\* Listen for the Hub's \`ReceiveMessage\` event, parse the JSON-RPC message, and route it to the correct \`monaco-languageclient\` listener (e.g., as a request, notification, or response).  
 \* 4\.  \*\*Manage Request/Response Lifecycle:\*\* Track pending requests with unique IDs to correctly resolve or reject Promises when responses arrive from the server.  
 \*/

// We use a simple counter for request IDs  
let messageId \= 0;

/\*\*  
 \* The adapter class that implements the \`MessageConnection\` interface.  
 \*/  
export class SignalRMessageAdapter implements MessageConnection {  
  private readonly hubConnection: HubConnection;  
  private isDisposed \= false;

  // Emitters for a valid MessageConnection implementation  
  private readonly \_onNotification \= new Emitter\<NotificationMessage\>();  
  private readonly \_onRequest \= new Emitter\<RequestMessage\>();  
  private readonly \_onError \= new Emitter\<\[Error, Message, number\]\>();  
  private readonly \_onClose \= new Emitter\<void\>();

  /\*\*  
   \* A Map to store pending request handlers.  
   \* key \= request ID  
   \* value \= { resolve, reject }  
   \*/  
  private readonly pendingRequests \= new Map\<  
    string | number,  
    {  
      resolve: (result: any) \=\> void;  
      reject: (error: any) \=\> void;  
    }  
  \>();

  /\*\*  
   \* Creates the adapter.  
   \* @param hubConnection The SignalR connection, which is assumed to be  
   \* configured but \*not yet started\*.  
   \*/  
  constructor(hubConnection: HubConnection) {  
    this.hubConnection \= hubConnection;  
  }

  /\*\*  
   \* This is the "start" method for the connection.  
   \* It is called by \`monaco-languageclient\` when it's ready to connect.  
   \*/  
  public async listen(): Promise\<void\> {  
    if (this.hubConnection.state \=== HubConnectionState.Disconnected) {  
      try {  
        // 1\. Start the SignalR connection  
        await this.hubConnection.start();  
      } catch (e) {  
        console.error('SignalR connection failed to start:', e);  
        this.\_onError.fire(\[e as Error, undefined, 0\]);  
        this.\_onClose.fire();  
        return;  
      }  
    }

    // 2\. Register the main "ReceiveMessage" handler.  
    // This is the single entry point for \*all\* LSP messages from the server.  
    this.hubConnection.on(  
      'ReceiveMessage',  
      (message: any) \=\> {  
        if (this.isDisposed) {  
          return;  
        }  
        // 3\. Dispatch the incoming message  
        this.handleMessage(message);  
      },  
    );

    // 4\. Handle connection close events  
    this.hubConnection.onclose(e \=\> {  
      if (this.isDisposed) {  
        return;  
      }  
      this.\_onError.fire(\[e as Error, undefined, 0\]);  
      this.\_onClose.fire();  
    });  
  }

  /\*\*  
   \* Handles an incoming message from the server.  
   \* Parses the JSON-RPC message and routes it to the correct emitter.  
   \*/  
  private handleMessage(message: any): void {  
    if (\!message) {  
      return;  
    }

    if (this.isNotification(message)) {  
      // It's a Notification (e.g., publishDiagnostics)  
      this.\_onNotification.fire(message);  
    } else if (this.isResponse(message)) {  
      // It's a Response to a request we sent  
      const pending \= this.pendingRequests.get(message.id);  
      if (pending) {  
        this.pendingRequests.delete(message.id);  
        if (this.isErrorResponse(message)) {  
          pending.reject(message.error);  
        } else {  
          pending.resolve(message.result);  
        }  
      }  
    } else if (this.isRequest(message)) {  
      // It's a Request from the server (e.g., workspace/applyEdit)  
      this.\_onRequest.fire(message);  
    }  
  }

  /\*\*  
   \* Sends a Request to the server.  
   \* This is called by \`monaco-languageclient\` for things like \`textDocument/hover\`.  
   \*/  
  public sendRequest\<R\>(  
    method: string,  
    params?: any,  
    token?: CancellationToken,  
  ): Promise\<R\> {  
    return new Promise((resolve, reject) \=\> {  
      if (this.isDisposed) {  
        return reject(new Error('Connection is disposed'));  
      }

      const id \= messageId++;  
      const message: RequestMessage \= {  
        jsonrpc: '2.0',  
        id: id,  
        method: method,  
        params: params,  
      };

      // Store the promise handlers to be resolved in \`handleMessage\`  
      this.pendingRequests.set(id, { resolve, reject });

      // Use a CancellationTokenSource if one is provided  
      let cancellationSource: CancellationTokenSource | undefined;  
      if (token) {  
        cancellationSource \= new CancellationTokenSource();  
        token.onCancellationRequested(() \=\> {  
          cancellationSource.cancel();  
          // Send the $/cancelRequest notification to the server  
          this.sendNotification('$/cancelRequest', { id: id });  
          reject(new Error(\`Request ${id} (method ${method}) cancelled\`));  
        });  
      }

      // Send the request over the wire  
      this.hubConnection  
        .invoke('SendMessage', message)  
        .catch(e \=\> {  
          console.error('SignalR invoke failed:', e);  
          this.pendingRequests.delete(id);  
          reject(e);  
        });

      if (cancellationSource) {  
        cancellationSource.token.onCancellationRequested(() \=\> {  
          this.pendingRequests.delete(id);  
        });  
      }  
    });  
  }

  /\*\*  
   \* Sends a Notification to the server.  
   \* This is called by \`monaco-languageclient\` for things like \`textDocument/didChange\`.  
   \*/  
  public sendNotification(  
    method: string,  
    params?: any,  
  ): void {  
    if (this.isDisposed) {  
      return;  
    }

    const message: NotificationMessage \= {  
      jsonrpc: '2.0',  
      method: method,  
      params: params,  
    };

    this.hubConnection  
      .invoke('SendMessage', message)  
      .catch(e \=\> {  
        console.error('SignalR notification invoke failed:', e);  
      });  
  }

  // \--- Type guards for message parsing \---

  private isNotification(msg: any): msg is NotificationMessage {  
    return msg && msg.method && msg.id \=== undefined;  
  }  
  private isRequest(msg: any): msg is RequestMessage {  
    return msg && msg.method && msg.id \!== undefined;  
  }  
  private isResponse(msg: any): msg is ResponseMessage {  
    return msg && msg.id \!== undefined && (msg.result \!== undefined || msg.error \!== undefined);  
  }  
  private isErrorResponse(msg: any): msg is ErrorResponseMessage {  
    return msg && msg.id \!== undefined && msg.error \!== undefined;  
  }

  // \--- Required Emitter properties \---

  public get onNotification(): Event\<NotificationMessage\> {  
    return this.\_onNotification.event;  
  }  
  public get onRequest(): Event\<RequestMessage\> {  
    return this.\_onRequest.event;  
  }  
  public get onError(): Event\<\[Error, Message, number\]\> {  
    return this.\_onError.event;  
  }  
  public get onClose(): Event\<void\> {  
    return this.\_onClose.event;  
  }

  /\*\*  
   \* Disposes of the connection, stopping the SignalR hub  
   \* and cleaning up all emitters.  
   \*/  
  public dispose(): void {  
    if (this.isDisposed) {  
      return;  
    }  
    this.isDisposed \= true;

    // Reject all pending requests  
    for (const \[id, handlers\] of this.pendingRequests.entries()) {  
      handlers.reject(new Error('Connection disposed'));  
    }  
    this.pendingRequests.clear();

    // Dispose all emitters  
    this.\_onNotification.dispose();  
    this.\_onRequest.dispose();  
    this.\_onError.dispose();  
    this.\_onClose.dispose();

    // Stop the SignalR connection  
    this.hubConnection.stop();  
  }  
}

import {  
  useState,  
  useEffect,  
  useRef,  
  useCallback,  
} from 'react';  
import \* as monaco from 'monaco-editor';  
import {  
  HubConnection,  
  HubConnectionBuilder,  
} from '@microsoft/signalr';  
import {  
  MonacoLanguageClient,  
  CloseAction,  
  ErrorAction,  
} from 'monaco-languageclient';  
import { SignalRMessageAdapter } from './SignalRMessageAdapter'; // From spec-14

/\*\*  
 \* \# Scriban Language Server: Specification 15  
 \* \#\# Detailed Design: The \`useScribanEditor\` Hook  
 \*  
 \* \#\# 1\. Overview  
 \*  
 \* This document provides the detailed design and implementation for the  
 \* \`useScribanEditor\` React hook. This hook is the "brain" of the  
 \* client-side application.  
 \*  
 \* It is responsible for:  
 \* 1\.  Initializing and managing the lifecycle of the Monaco editor, SignalR connection, and Language Client.  
 \* 2\.  Managing the state of the custom picker UI (\`pickerState\`).  
 \* 3\.  Listening to Monaco editor events (key presses, content changes) and forwarding them to the server.  
 \* 4\.  Listening to server-sent commands (both custom SignalR and standard LSP) and reacting to them.  
 \*/

// \--- 1\. State & Types \---

/\*\*  
 \* The internal state for the custom picker UI.  
 \*/  
export interface PickerState {  
  isVisible: boolean;  
  pickerType: 'file-picker' | 'enum-list' | string | null;  
  functionName: string | null;  
  parameterIndex: number | null;  
  currentValue: string | null;  
  position: { x: number; y: number } | null; // Screen coordinates  
}

const DEFAULT\_PICKER\_STATE: PickerState \= {  
  isVisible: false,  
  pickerType: null,  
  functionName: null,  
  parameterIndex: null,  
  currentValue: null,  
  position: null,  
};

/\*\*  
 \* Props for the hook.  
 \* @param editorRef A React ref to the \`div\` element where Monaco should be mounted.  
 \* @param languageId The language ID (e.g., "scriban").  
 \* @param hubUrl The URL for the SignalR Hub (e.g., "/scribanhub").  
 \*/  
export interface UseScribanEditorProps {  
  editorRef: React.RefObject\<HTMLDivElement\>;  
  languageId: string;  
  hubUrl: string;  
}

/\*\*  
 \* What the hook returns to the consuming component (e.g., App.tsx).  
 \*/  
export interface ScribanEditorApi {  
  editor: monaco.editor.IStandaloneCodeEditor | null;  
  pickerState: PickerState;  
  handlePickerSelect: (value: string) \=\> void;  
  handlePickerCancel: () \=\> void;  
}

// \--- 2\. The Hook Implementation \---

export function useScribanEditor({  
  editorRef,  
  languageId,  
  hubUrl,  
}: UseScribanEditorProps): ScribanEditorApi {  
  // \--- Refs for long-lived objects \---  
  const editor \= useRef\<monaco.editor.IStandaloneCodeEditor | null\>(null);  
  const hubConnection \= useRef\<HubConnection | null\>(null);  
  const languageClient \= useRef\<MonacoLanguageClient | null\>(null);  
  const disposables \= useRef\<monaco.IDisposable\[\]\>(\[\]);

  // \--- State \---  
  const \[pickerState, setPickerState\] \=  
    useState\<PickerState\>(DEFAULT\_PICKER\_STATE);

  // \--- Core Initialization (useEffect) \---  
  useEffect(() \=\> {  
    if (\!editorRef.current) {  
      return; // Mount point not ready  
    }

    // 1\. Create Editor Instance  
    editor.current \= monaco.editor.create(editorRef.current, {  
      model: monaco.editor.createModel(  
        '\# Type \`os.\` or \`copy\_file(\` to start',  
        languageId,  
      ),  
      language: languageId,  
      automaticLayout: true,  
      // ... other monaco options  
    });

    // 2\. Create SignalR Connection  
    hubConnection.current \= new HubConnectionBuilder()  
      .withUrl(hubUrl)  
      .withAutomaticReconnect()  
      .build();

    // 3\. Create the Adapter (from spec-14)  
    const adapter \= new SignalRMessageAdapter(hubConnection.current);

    // 4\. Create the Language Client  
    languageClient.current \= new MonacoLanguageClient({  
      name: 'Scriban Language Client',  
      clientOptions: {  
        documentSelector: \[languageId\],  
        errorHandler: {  
          error: () \=\> ({ action: ErrorAction.Continue }),  
          closed: () \=\> ({ action: CloseAction.DoNotRestart }),  
        },  
      },  
      connectionProvider: {  
        get: () \=\> Promise.resolve(adapter),  
      },  
    });

    // 5\. Start the Language Client  
    const lc \= languageClient.current;  
    lc.start();

    // 6\. Register Server-to-Client Command Handlers  
    // 6a. Standard LSP "executeCommand" (for right-click menu)  
    lc.onReady().then(() \=\> {  
      lc.onNotification(  
        'workspace/executeCommand',  
        params \=\> {  
          if (params.command \=== 'scriban.openPicker') {  
            const \[functionName, parameterIndex\] \= params.arguments as \[  
              string,  
              number,  
            \];  
            openPicker(functionName, parameterIndex);  
          }  
          if (params.command \=== 'scriban.insertMacro') {  
            const \[text\] \= params.arguments as \[string\];  
            insertTextAtCursor(text);  
          }  
        },  
      );  
    });

    // 6b. Custom SignalR "OpenPicker" (for auto-triggers)  
    hubConnection.current.on('OpenPicker', data \=\> {  
      openPicker(  
        data.functionName,  
        data.parameterIndex,  
        data.currentValue,  
      );  
    });

    // 7\. Register Client-to-Server Event Listeners  
    // 7a. Listen for \`(\`, \`,\`  
    disposables.current.push(  
      editor.current.onDidChangeModelContent(e \=\> {  
        // Simple check for single-char trigger  
        if (e.changes.length \=== 1\) {  
          const change \= e.changes\[0\];  
          const text \= change.text;  
          if (  
            (text \=== '(' || text \=== ',') &&  
            change.rangeLength \=== 0  
          ) {  
            sendCheckTrigger('char', text);  
          }  
        }  
      }),  
    );

    // 7b. Listen for \`Ctrl+Space\`  
    disposables.current.push(  
      editor.current.onKeyDown(e \=\> {  
        if (e.ctrlKey && e.keyCode \=== monaco.KeyCode.Space) {  
          // We don't preventDefault here. We let both our custom  
          // trigger AND the standard LSP completion request go to the server.  
          // The server will correctly handle this (as per spec-12).  
          sendCheckTrigger('hotkey');  
        }  
      }),  
    );

    // 8\. Cleanup  
    return () \=\> {  
      lc.stop();  
      hubConnection.current?.stop();  
      editor.current?.dispose();  
      disposables.current.forEach(d \=\> d.dispose());  
    };  
  }, \[editorRef, languageId, hubUrl\]); // Run once on mount

  // \--- Helper Functions (Internal) \---

  /\*\*  
   \* Calculates the screen position of the current cursor.  
   \*/  
  const getScreenCoordinates \= useCallback(  
    (pos?: monaco.Position) \=\> {  
      if (\!editor.current) return { x: 0, y: 0 };  
      const cursorPosition \= pos || editor.current.getPosition();  
      if (\!cursorPosition) return { x: 0, y: 0 };

      const coords \= editor.current.getScrolledVisiblePosition(cursorPosition);  
      const editorDom \= editor.current.getDomNode();  
      if (\!coords || \!editorDom) return { x: 0, y: 0 };

      const editorRect \= editorDom.getBoundingClientRect();  
      return {  
        x: editorRect.left \+ coords.left,  
        y: editorRect.top \+ coords.top \+ coords.height, // Position \*below\* the line  
      };  
    },  
    \[\],  
  );

  /\*\*  
   \* The main function to open the picker.  
   \*/  
  const openPicker \= useCallback(  
    (  
      functionName: string,  
      parameterIndex: number,  
      currentValue: string | null \= null,  
    ) \=\> {  
      const pos \= getScreenCoordinates();  
      setPickerState({  
        isVisible: true,  
        pickerType: 'file-picker', // This would be dynamic based on server data  
        functionName,  
        parameterIndex,  
        currentValue,  
        position: pos,  
      });  
    },  
    \[getScreenCoordinates\],  
  );

  /\*\*  
   \* Inserts text at the current cursor.  
   \*/  
  const insertTextAtCursor \= useCallback((text: string) \=\> {  
    if (\!editor.current) return;  
    const position \= editor.current.getPosition();  
    if (\!position) return;

    editor.current.executeEdits('scriban-macro', \[  
      {  
        range: new monaco.Range(  
          position.lineNumber,  
          position.column,  
          position.lineNumber,  
          position.column,  
        ),  
        text: text,  
      },  
    \]);  
  }, \[\]);

  /\*\*  
   \* Sends the \`CheckTrigger\` message to the server.  
   \*/  
  const sendCheckTrigger \= useCallback(  
    (event: 'char' | 'hotkey', char: string | null \= null) \=\> {  
      if (  
        \!hubConnection.current ||  
        hubConnection.current.state \!== HubConnectionState.Connected ||  
        \!editor.current  
      ) {  
        return;  
      }

      const model \= editor.current.getModel();  
      const position \= editor.current.getPosition();  
      if (\!model || \!position) return;

      const context \= {  
        event: event,  
        char: char,  
        line: model.getLineContent(position.lineNumber),  
        position: {  
          line: position.lineNumber \- 1, // LSP is 0-indexed  
          character: position.column \- 1, // LSP is 0-indexed  
        },  
        uri: model.uri.toString(),  
      };

      hubConnection.current.invoke('CheckTrigger', context);  
    },  
    \[\],  
  );

  // \--- Callbacks for the UI Components \---

  const handlePickerSelect \= useCallback((value: string) \=\> {  
    if (\!editor.current || \!pickerState.functionName) return;

    // This is a simplified replacement.  
    // A more robust version would ask the server for the \*exact\* range  
    // of the parameter to replace.  
    const position \= editor.current.getPosition();  
    if (\!position) return;

    // For now, just insert the text.  
    editor.current.executeEdits('picker-select', \[  
      {  
        range: new monaco.Range(  
          position.lineNumber,  
          position.column,  
          position.lineNumber,  
          position.column,  
        ),  
        text: value,  
      },  
    \]);

    setPickerState(DEFAULT\_PICKER\_STATE);  
  }, \[pickerState\]);

  const handlePickerCancel \= useCallback(() \=\> {  
    setPickerState(DEFAULT\_PICKER\_STATE);  
  }, \[\]);

  // \--- 3\. Return the Public API \---

  return {  
    editor: editor.current,  
    pickerState,  
    handlePickerSelect,  
    handlePickerCancel,  
  };  
}  
