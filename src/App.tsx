import { useRef, useState, useCallback } from 'react';
import * as monaco from 'monaco-editor';
import { useMonacoEditor } from './hooks/useMonacoEditor';
import { ParameterPicker } from './components/ParameterPicker';
import { validateScriban } from './services/mockBackend';
import { CUSTOM_FUNCTIONS } from './config';
import { Diagnostic } from './types';
import { FunctionCallParser } from './services/functionCallParser';
import { ParameterValueExtractor } from './services/parameterValueExtractor';
import { DebugLogger } from './utils/debugLogger';
import { PickerState, DEFAULT_PICKER_STATE, closePickerState, createPickerState } from './utils/pickerStateUtils';
import { delayedSequentialPicker } from './utils/timingUtils';
import './App.css';

function App() {
  const editorContainerRef = useRef<HTMLDivElement>(null);
  const editorControlsRef = useRef<{
    replaceParameter: (functionName: string, parameterIndex: number, text: string, nearPosition?: monaco.Position) => void;
    setDiagnostics: (diagnostics: Diagnostic[]) => void;
    getMarkerPosition: (range: monaco.Range) => { x: number; y: number } | null;
    editor: monaco.editor.IStandaloneCodeEditor | null;
    getModel: () => monaco.editor.ITextModel | null;
    getEditor: () => monaco.editor.IStandaloneCodeEditor | null;
    deselectAndPositionCursor: (range: monaco.Range) => void;
  } | null>(null);

    const [pickerState, setPickerState] = useState<PickerState>(DEFAULT_PICKER_STATE);

  const handleMarkerDetected = useCallback((info: { range: monaco.Range; functionName: string; parameterIndex: number }) => {
    DebugLogger.handleMarkerDetected('Called with info:', info);
    if (editorControlsRef.current) {
      const position = editorControlsRef.current.getMarkerPosition(info.range);
      DebugLogger.handleMarkerDetected('Marker position:', position);
      if (position) {
        DebugLogger.handleMarkerDetected('Setting picker state to visible');
        setPickerState(createPickerState({
          position,
          range: info.range,
          parameterIndex: info.parameterIndex,
          functionName: info.functionName,
          autoFocus: true, // Auto-focus when opened from marker detection
          currentValue: null, // Markers have no current value
          cursorPosition: null // No cursor position to restore for markers
        }));
      } else {
        DebugLogger.handleMarkerDetected('No position available, not opening picker');
      }
    } else {
      DebugLogger.handleMarkerDetected('editorControlsRef.current is null');
    }
  }, []);

  const handleValidation = useCallback(async (script: string) => {
    const diagnostics = await validateScriban(script);
    if (editorControlsRef.current) {
      editorControlsRef.current.setDiagnostics(diagnostics);
    }
  }, []);

  const handleParameterClick = useCallback((info: { range: monaco.Range; functionName: string; parameterIndex: number; clickPosition?: monaco.Position }) => {
    if (editorControlsRef.current) {
      const position = editorControlsRef.current.getMarkerPosition(info.range);
      if (position) {
        // Get current parameter value
        const model = editorControlsRef.current.getModel();
        const currentValue = model 
          ? ParameterValueExtractor.getParameterValue(model, info.range)
          : null;
        
        // Use the original click position if available, otherwise get current cursor position
        // The click position is the position before Monaco processes the click (which might select the word)
        const cursorPosition = info.clickPosition || (editorControlsRef.current.getEditor()?.getPosition() || null);

        setPickerState(createPickerState({
          position,
          range: info.range,
          parameterIndex: info.parameterIndex,
          functionName: info.functionName,
          autoFocus: false, // Don't auto-focus when opened from click (user might want to keep typing)
          currentValue,
          cursorPosition
        }));
      }
    }
  }, []);

  const handleManualTyping = useCallback(() => {
    // Close picker when user types manually
    if (pickerState.visible) {
      setPickerState(closePickerState());
    }
  }, [pickerState.visible]);

  const editorControls = useMonacoEditor(editorContainerRef, {
    onMarkerDetected: handleMarkerDetected,
    onParameterClick: handleParameterClick,
    onValidation: handleValidation,
    onManualTyping: handleManualTyping
  });

  // Update ref immediately
  if (editorControls) {
    editorControlsRef.current = editorControls;
  }

  const handleParameterSelect = (value: string) => {
    if (editorControlsRef.current) {
      const { functionName, parameterIndex, range: currentRange } = pickerState;

      // Close current picker first
      setPickerState(closePickerState());

      // Use replaceParameter with fresh range calculation
      // Pass the range's start position as nearPosition hint for finding the correct function call
      const nearPosition = currentRange?.getStartPosition();
      editorControlsRef.current.replaceParameter(functionName, parameterIndex, value, nearPosition);
      
      // Wait for the replacement to complete and then check for next marker
      delayedSequentialPicker(() => {
        if (!editorControlsRef.current) return;

        const model = editorControlsRef.current.getModel();
        if (!model) return;

        // Find next marker using parser
        const nextMarkerRange = FunctionCallParser.findMarkerRange(model);
        if (nextMarkerRange) {
          // Use parser to get parameter info for the marker
          const paramInfo = FunctionCallParser.getParameterIndexAtPosition(model, nextMarkerRange.getStartPosition());
          if (paramInfo) {
            const param = paramInfo.functionCall.parameters[paramInfo.parameterIndex];
            if (param.isMarker && param.type === 'path') {
              // Get the range including quotes for the marker
              // For markers, param.range already includes quotes (startColumn is at opening quote, endColumn is after closing quote)
              // So we can use it directly
              const markerRange = param.range;

              const position = editorControlsRef.current.getMarkerPosition(markerRange);
              if (position) {
                setPickerState(createPickerState({
                  position,
                  range: markerRange,
                  parameterIndex: paramInfo.parameterIndex,
                  functionName: paramInfo.functionCall.functionName,
                  autoFocus: true, // Auto-focus for sequential picking
                  currentValue: null, // Markers have no current value
                  cursorPosition: null // No cursor position to restore for markers
                }));
                return;
              }
            }
          }
        }
      });
    } else {
      setPickerState(closePickerState());
    }
  };

  const handleParameterCancel = () => {
    // Restore cursor position if it was saved (picker opened via click)
    if (pickerState.cursorPosition && editorControlsRef.current) {
      const editor = editorControlsRef.current.getEditor();
      if (editor) {
        editor.setPosition(pickerState.cursorPosition);
        editor.setSelection(new monaco.Selection(
          pickerState.cursorPosition.lineNumber,
          pickerState.cursorPosition.column,
          pickerState.cursorPosition.lineNumber,
          pickerState.cursorPosition.column
        ));
        editor.focus();
      }
    } else if (pickerState.range && editorControlsRef.current) {
      // Fallback: deselect text and position cursor at end of range (for markers)
      editorControlsRef.current.deselectAndPositionCursor(pickerState.range);
    }
    // Close picker
    setPickerState(closePickerState());
  };

  return (
    <div className="app">
      <header className="app-header">
        <h1>Scriban IntelliSense Demo</h1>
        <p>Monaco Editor with custom IntelliSense, syntax checking, and parameter pickers</p>
      </header>
      
      <div className="app-content">
        <div className="editor-container">
          <div className="editor-header">
            <h2>Scriban Script Editor</h2>
            <div className="editor-info">
              <span>💡 Try typing: copy, move, delete, read, write</span>
            </div>
          </div>
          <div ref={editorContainerRef} className="editor" />
        </div>

        <div className="sidebar">
          <div className="sidebar-section">
            <h3>Available Functions</h3>
            <ul className="function-list">
              <li>
                <code>copy(source, dest)</code>
                <p>Copy files or directories. Source supports globbing, dest folders must end with /</p>
              </li>
              <li>
                <code>move(source, dest)</code>
                <p>Move files or directories. Source supports globbing, dest folders must end with /</p>
              </li>
              <li>
                <code>delete(path)</code>
                <p>Delete files or directories</p>
              </li>
              <li>
                <code>read(path)</code>
                <p>Read file contents</p>
              </li>
              <li>
                <code>write(path, content)</code>
                <p>Write content to a file</p>
              </li>
            </ul>
          </div>

          <div className="sidebar-section">
            <h3>Features</h3>
            <ul className="feature-list">
              <li>✅ Syntax highlighting</li>
              <li>✅ Auto-completion</li>
              <li>✅ Parameter pickers</li>
              <li>✅ File system navigation</li>
              <li>✅ Globbing support</li>
              <li>✅ Syntax validation</li>
              <li>✅ Signature help</li>
              <li>✅ Hover documentation</li>
            </ul>
          </div>

          <div className="sidebar-section">
            <h3>Instructions</h3>
            <ol className="instructions-list">
              <li>Type a function name (e.g., <code>copy</code>)</li>
              <li>Press <kbd>Ctrl+Space</kbd> or wait for suggestions</li>
              <li>Select a function to insert it</li>
              <li>A parameter picker will appear automatically</li>
              <li>Browse files, enable globbing, and select</li>
            </ol>
          </div>
        </div>
      </div>

      {pickerState.visible && (() => {
        const func = CUSTOM_FUNCTIONS.find(f => f.name === pickerState.functionName);
        const parameter = func?.parameters[pickerState.parameterIndex];
        if (!parameter || parameter.type !== 'path') {
          return null;
        }
        
        // Use metadata to determine if this is a source parameter
        const isSource = parameter.isSource ?? false;
        
        return (
          <ParameterPicker
            position={pickerState.position}
            parameter={parameter}
            parameterIndex={pickerState.parameterIndex}
            isSource={isSource}
            currentValue={pickerState.currentValue}
            autoFocus={pickerState.autoFocus}
            onSelect={handleParameterSelect}
            onCancel={handleParameterCancel}
          />
        );
      })()}
    </div>
  );
}

export default App;

