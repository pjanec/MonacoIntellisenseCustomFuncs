import {
  useState,
  useEffect,
  useRef,
  useCallback,
} from 'react';
import * as monaco from 'monaco-editor';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
} from '@microsoft/signalr';
import { createMessageConnection, MessageConnection } from 'vscode-jsonrpc';
import {
  SignalRMessageReader,
  SignalRMessageWriter,
} from './SignalRMessageAdapter';
import { SCRIBAN_LANGUAGE_ID } from './scribanLanguage';

/**
 * # Scriban Language Server: Specification 15
 * ## Detailed Design: The `useScribanEditor` Hook
 *
 * ## 1. Overview
 *
 * This hook is the "brain" of the client-side application.
 *
 * It is responsible for:
 * 1. Initializing and managing the lifecycle of the Monaco editor, SignalR connection, and Language Client.
 * 2. Managing the state of the custom picker UI (`pickerState`).
 * 3. Listening to Monaco editor events (key presses, content changes) and forwarding them to the server.
 * 4. Listening to server-sent commands (both custom SignalR and standard LSP) and reacting to them.
 */

// --- 1. State & Types ---

/**
 * The internal state for the custom picker UI.
 */
export interface PickerState {
  isVisible: boolean;
  pickerType: 'file-picker' | 'enum-list' | string | null;
  functionName: string | null;
  parameterIndex: number | null;
  currentValue: string | null;
  position: { x: number; y: number } | null; // Screen coordinates
  options?: string[]; // For enum-list picker
  // Store editor state at picker open time for reliable replacement
  editorPosition?: monaco.Position;
  lineContent?: string;
}

const DEFAULT_PICKER_STATE: PickerState = {
  isVisible: false,
  pickerType: null,
  functionName: null,
  parameterIndex: null,
  currentValue: null,
  position: null,
};

/**
 * Props for the hook.
 * @param editorRef A React ref to the `div` element where Monaco should be mounted.
 * @param languageId The language ID (e.g., "scriban").
 * @param hubUrl The URL for the SignalR Hub (e.g., "/scribanhub").
 */
export interface UseScribanEditorProps {
  editorRef: React.RefObject<HTMLDivElement | null>;
  languageId?: string;
  hubUrl: string;
}

/**
 * What the hook returns to the consuming component (e.g., App.tsx).
 */
export interface ScribanEditorApi {
  editor: monaco.editor.IStandaloneCodeEditor | null;
  hubConnection: HubConnection | null;
  pickerState: PickerState;
  handlePickerSelect: (value: string) => void;
  handlePickerCancel: () => void;
  isConnected: boolean;
}

// --- 2. The Hook Implementation ---

export function useScribanEditor({
  editorRef,
  languageId = SCRIBAN_LANGUAGE_ID,
  hubUrl,
}: UseScribanEditorProps): ScribanEditorApi {
  // --- Refs for long-lived objects ---
  const editorInstance = useRef<monaco.editor.IStandaloneCodeEditor | null>(null);
  const hubConnection = useRef<HubConnection | null>(null);
  const messageConnection = useRef<MessageConnection | null>(null);
  const disposables = useRef<monaco.IDisposable[]>([]);
  const isPickerOpenRef = useRef<boolean>(false);
  const openPickerRef = useRef<Function | null>(null);
  // Store editor state when picker opens for reliable replacement
  const pickerEditorStateRef = useRef<{ position: monaco.Position; lineContent: string } | null>(null);

  // --- State ---
  const [pickerState, setPickerState] =
    useState<PickerState>(DEFAULT_PICKER_STATE);
  const [isConnected, setIsConnected] = useState(false);

  // --- Core Initialization (useEffect) ---
  useEffect(() => {
    if (!editorRef.current) {
      return; // Mount point not ready
    }

    // 1. Create Editor Instance
    editorInstance.current = monaco.editor.create(editorRef.current, {
      model: monaco.editor.createModel(
        '# Type `os.` or `copy_file(` to start\nx = 5\nfor item in items\n  item\nend',
        languageId,
      ),
      language: languageId,
      automaticLayout: true,
      theme: 'vs-dark',
      fontSize: 14,
      minimap: { enabled: false },
      lineNumbers: 'on',
      scrollBeyondLastLine: false,
      wordWrap: 'on',
    });

    // 2. Create SignalR Connection
    hubConnection.current = new HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .build();

    // 3. Create MessageReader and MessageWriter
    const reader = new SignalRMessageReader(hubConnection.current);
    const writer = new SignalRMessageWriter(hubConnection.current);

    // 4. Create the message connection
    const connection = createMessageConnection(reader, writer);
    messageConnection.current = connection;

    // 5. Listen for the connection lifecycle
    connection.listen();

    // Track connection state
    hubConnection.current.onclose(() => {
      setIsConnected(false);
    });

    hubConnection.current.onreconnecting(() => {
      setIsConnected(false);
    });

    hubConnection.current.onreconnected(() => {
      setIsConnected(true);
    });

    // 6. Register SignalR "OpenPicker" handler BEFORE starting connection
    hubConnection.current.on('OpenPicker', (data: {
      functionName: string;
      parameterIndex: number;
      currentValue?: string;
      pickerType?: string;
      options?: string[];
    }) => {
      console.log('OpenPicker received:', data);
      // Use ref to call the latest version of openPicker
      if (openPickerRef.current) {
        openPickerRef.current(
          data.functionName,
          data.parameterIndex,
          data.currentValue,
          data.pickerType,
          data.options,
        );
      }
    });

    // 7. Start SignalR connection and register notification handlers
    void hubConnection.current.start().then(() => {
      setIsConnected(true);

      // 6a. Standard LSP "executeCommand" (for right-click menu)
      connection.onNotification(
        'workspace/executeCommand',
        (params: any) => {
          if (params.command === 'scriban.openPicker') {
            const [functionName, parameterIndex] = params.arguments as [
              string,
              number,
            ];
            openPicker(functionName, parameterIndex);
          }
          if (params.command === 'scriban.insertMacro') {
            const [text] = params.arguments as [string];
            insertTextAtCursor(text);
          }
        },
      );

      // Send LSP initialize request
      const model = editorInstance.current?.getModel();
      if (model) {
        connection.sendRequest('initialize', {
          processId: null,
          clientInfo: {
            name: 'Scriban Web Client',
            version: '1.0.0',
          },
          rootUri: null,
          capabilities: {
            textDocument: {
              completion: {
                completionItem: {
                  snippetSupport: true,
                },
              },
              hover: { contentFormat: ['markdown', 'plaintext'] },
            },
          },
        }).then(() => {
          // Send initialized notification
          connection.sendNotification('initialized', {});

          // Register completion provider
          const completionProvider = monaco.languages.registerCompletionItemProvider(languageId, {
            triggerCharacters: ['.'],  // Only '.' - '(' and ',' are handled by CheckTrigger
            provideCompletionItems: async (model, position) => {
              try {
                // Don't show completions if picker is open
                if (isPickerOpenRef.current) {
                  return { suggestions: [] };
                }

                const completionRequest = {
                  jsonrpc: '2.0',
                  id: Math.floor(Math.random() * 1000000),
                  method: 'textDocument/completion',
                  params: {
                    textDocument: { uri: model.uri.toString() },
                    position: {
                      line: position.lineNumber - 1,
                      character: position.column - 1
                    }
                  }
                };

                // Send completion request
                const response = await connection.sendRequest('textDocument/completion', completionRequest.params);

                // Convert LSP completion items to Monaco completion items
                if (response && Array.isArray(response.items)) {
                  return {
                    suggestions: response.items.map((item: any) => ({
                      label: item.label,
                      kind: item.kind || monaco.languages.CompletionItemKind.Text,
                      documentation: item.documentation,
                      insertText: item.insertText || item.label,
                      range: undefined
                    }))
                  };
                }

                return { suggestions: [] };
              } catch (error) {
                console.error('Completion error:', error);
                return { suggestions: [] };
              }
            }
          });

          disposables.current.push(completionProvider);

          // Register hover provider
          const hoverProvider = monaco.languages.registerHoverProvider(languageId, {
            provideHover: async (model, position) => {
              try {
                // Get the word at the cursor position
                const wordInfo = model.getWordAtPosition(position);
                if (!wordInfo) {
                  return null;
                }

                const word = wordInfo.word;

                // Send hover request with the word
                const hoverRequest = {
                  jsonrpc: '2.0',
                  id: Math.floor(Math.random() * 1000000),
                  method: 'textDocument/hover',
                  params: {
                    textDocument: { uri: model.uri.toString() },
                    position: {
                      line: position.lineNumber - 1,
                      character: position.column - 1
                    },
                    // Non-standard extension: include the word for easier lookup
                    word: word
                  }
                };

                // Send hover request
                const response = await connection.sendRequest('textDocument/hover', hoverRequest.params);

                if (response && response.contents) {
                  return {
                    contents: Array.isArray(response.contents)
                      ? response.contents
                      : [{ value: response.contents.value || response.contents }]
                  };
                }

                return null;
              } catch (error) {
                console.error('Hover error:', error);
                return null;
              }
            }
          });

          disposables.current.push(hoverProvider);
        });
      }
    });

    // 7. Register Client-to-Server Event Listeners
    // 7a. Listen for `(`, `,`
    disposables.current.push(
      editorInstance.current.onDidChangeModelContent(e => {
        // Simple check for single-char trigger
        if (e.changes.length === 1) {
          const change = e.changes[0];
          const text = change.text;
          console.log('Content changed:', text, 'rangeLength:', change.rangeLength);

          // Check if text contains '(' or ',' (Monaco might insert '()' together)
          if (change.rangeLength === 0) {
            if (text.includes('(')) {
              console.log('Trigger detected, calling sendCheckTrigger for: (');
              sendCheckTrigger('char', '(');
            } else if (text.includes(',')) {
              console.log('Trigger detected, calling sendCheckTrigger for: ,');
              sendCheckTrigger('char', ',');
            }
          }
        }
      }),
    );

    // 7b. Listen for `Ctrl+Space`
    disposables.current.push(
      editorInstance.current.onKeyDown(e => {
        if (e.ctrlKey && e.keyCode === monaco.KeyCode.Space) {
          // We don't preventDefault here. We let both our custom
          // trigger AND the standard LSP completion request go to the server.
          // The server will correctly handle this (as per spec-12).
          sendCheckTrigger('hotkey');
        }
      }),
    );

    // 8. Cleanup
    return () => {
      messageConnection.current?.dispose();
      hubConnection.current?.stop();
      editorInstance.current?.dispose();
      disposables.current.forEach(d => d.dispose());
    };
  }, [editorRef, languageId, hubUrl]); // Run once on mount

  // --- Helper Functions (Internal) ---

  /**
   * Calculates the screen position of the current cursor.
   */
  const getScreenCoordinates = useCallback(
    (pos?: monaco.Position) => {
      if (!editorInstance.current) return { x: 0, y: 0 };
      const cursorPosition = pos || editorInstance.current.getPosition();
      if (!cursorPosition) return { x: 0, y: 0 };

      const coords = editorInstance.current.getScrolledVisiblePosition(cursorPosition);
      const editorDom = editorInstance.current.getDomNode();
      if (!coords || !editorDom) return { x: 0, y: 0 };

      const editorRect = editorDom.getBoundingClientRect();
      return {
        x: editorRect.left + coords.left,
        y: editorRect.top + coords.top + coords.height, // Position *below* the line
      };
    },
    [],
  );

  /**
   * The main function to open the picker.
   */
  const openPicker = useCallback(
    (
      functionName: string,
      parameterIndex: number,
      currentValue: string | null = null,
      pickerType: string | null = null,
      options?: string[],
    ) => {
      // Close Monaco's completion widget if it's open
      if (editorInstance.current) {
        editorInstance.current.trigger('picker', 'hideSuggestWidget', undefined);
      }

      // Mark picker as open
      isPickerOpenRef.current = true;

      // Capture current editor state for reliable replacement later
      const model = editorInstance.current?.getModel();
      const position = editorInstance.current?.getPosition();
      const lineContent = position && model ? model.getLineContent(position.lineNumber) : undefined;

      // Store in ref for immediate access (no async state update delay)
      if (position && lineContent) {
        pickerEditorStateRef.current = { position, lineContent };
      }

      const pos = getScreenCoordinates();
      setPickerState({
        isVisible: true,
        pickerType: pickerType || 'file-picker',
        functionName,
        parameterIndex,
        currentValue,
        position: pos,
        options,
        editorPosition: position || undefined,
        lineContent: lineContent || undefined,
      });
    },
    [getScreenCoordinates],
  );

  // Store openPicker in ref so SignalR handler can always call the latest version
  openPickerRef.current = openPicker;

  /**
   * Inserts text at the current cursor.
   */
  const insertTextAtCursor = useCallback((text: string) => {
    if (!editorInstance.current) return;
    const position = editorInstance.current.getPosition();
    if (!position) return;

    editorInstance.current.executeEdits('scriban-macro', [
      {
        range: new monaco.Range(
          position.lineNumber,
          position.column,
          position.lineNumber,
          position.column,
        ),
        text: text,
      },
    ]);
  }, []);

  /**
   * Sends the `CheckTrigger` message to the server.
   */
  const sendCheckTrigger = useCallback(
    (event: 'char' | 'hotkey', char: string | null = null) => {
      console.log('sendCheckTrigger called with event:', event, 'char:', char);
      console.log('hubConnection.current:', hubConnection.current);
      console.log('hubConnection state:', hubConnection.current?.state);
      console.log('editorInstance.current:', editorInstance.current);

      if (
        !hubConnection.current ||
        hubConnection.current.state !== HubConnectionState.Connected ||
        !editorInstance.current
      ) {
        console.log('sendCheckTrigger: early return due to connection/editor check');
        return;
      }

      const model = editorInstance.current.getModel();
      const position = editorInstance.current.getPosition();
      if (!model || !position) {
        console.log('sendCheckTrigger: early return due to model/position check');
        return;
      }

      const context = {
        documentUri: model.uri.toString(),
        code: model.getValue(), // Full document text
        position: {
          line: position.lineNumber - 1, // LSP is 0-indexed
          character: position.column - 1, // LSP is 0-indexed
        },
        currentLine: model.getLineContent(position.lineNumber),
        triggerCharacter: char,
        event: event
      };

      console.log('sendCheckTrigger: invoking CheckTrigger with context:', context);
      hubConnection.current.invoke('CheckTrigger', context);
    },
    [],
  );

  // --- Callbacks for the UI Components ---

  const handlePickerSelect = useCallback((value: string) => {
    if (!editorInstance.current || !pickerState.functionName) return;

    const model = editorInstance.current.getModel();
    if (!model) return;

    // Use stored editor state from ref (captured when picker opened)
    // This prevents race conditions - we use the exact state from when picker opened
    const editorState = pickerEditorStateRef.current;
    if (!editorState) {
      console.warn('No stored editor state for picker selection');
      return;
    }

    const position = editorState.position;
    const lineContent = editorState.lineContent;

    const currentChar = lineContent[position.column - 1];

    let replaceRange: monaco.Range;

    // Try to find if we're inside a string literal (quoted)
    let startCol = position.column;
    let endCol = position.column;

    // Find the start of the current token (go backwards to find delimiter)
    // Note: lineContent is 0-based, but Monaco columns are 1-based
    for (let i = position.column - 1; i >= 0; i--) {
      const ch = lineContent[i];
      if (ch === ',' || ch === '(' || ch === ' ') {
        // i is JavaScript index (0-based), Monaco column is i+1
        // We want the column AFTER the delimiter, so i+2
        startCol = i + 2;
        break;
      }
      if (i === 0) {
        startCol = 1;
      }
    }

    // Find the end of the current token (go forwards to find delimiter)
    for (let i = position.column - 1; i < lineContent.length; i++) {
      const ch = lineContent[i];
      if (ch === ',' || ch === ')' || ch === ' ') {
        // We want the column OF the delimiter (not after), so i+1
        endCol = i + 1;
        break;
      }
      if (i === lineContent.length - 1) {
        endCol = lineContent.length + 1;
      }
    }

    // Skip leading/trailing whitespace and quotes
    while (startCol <= lineContent.length && (lineContent[startCol - 1] === ' ' || lineContent[startCol - 1] === '"' || lineContent[startCol - 1] === "'")) {
      startCol++;
    }
    while (endCol > 1 && (lineContent[endCol - 2] === ' ' || lineContent[endCol - 2] === '"' || lineContent[endCol - 2] === "'")) {
      endCol--;
    }

    // If we found a non-empty range, replace it; otherwise insert
    if (startCol < endCol && startCol < position.column) {
      replaceRange = new monaco.Range(
        position.lineNumber,
        startCol,
        position.lineNumber,
        endCol
      );
    } else {
      // Just insert at current position
      replaceRange = new monaco.Range(
        position.lineNumber,
        position.column,
        position.lineNumber,
        position.column
      );
    }

    const edit = {
      range: replaceRange,
      text: value,
    };

    editorInstance.current.executeEdits('picker-select', [edit]);

    // Move cursor to after the inserted text
    const newPosition = new monaco.Position(
      position.lineNumber,
      replaceRange.startColumn + value.length
    );
    editorInstance.current.setPosition(newPosition);

    // Focus the editor
    editorInstance.current.focus();

    // Mark picker as closed
    isPickerOpenRef.current = false;
    pickerEditorStateRef.current = null; // Clear stored state
    setPickerState(DEFAULT_PICKER_STATE);
  }, [pickerState]);

  const handlePickerCancel = useCallback(() => {
    // Focus the editor
    if (editorInstance.current) {
      editorInstance.current.focus();
    }

    // Mark picker as closed
    isPickerOpenRef.current = false;
    pickerEditorStateRef.current = null; // Clear stored state
    setPickerState(DEFAULT_PICKER_STATE);
  }, []);

  // --- 3. Return the Public API ---

  return {
    editor: editorInstance.current,
    hubConnection: hubConnection.current,
    pickerState,
    handlePickerSelect,
    handlePickerCancel,
    isConnected,
  };
}
