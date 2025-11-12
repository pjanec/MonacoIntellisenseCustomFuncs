import { useRef, useEffect } from 'react';
import { useScribanEditor } from './useScribanEditor';
import { FilePicker } from './components/FilePicker';
import { EnumPicker } from './components/EnumPicker';
import { registerScribanLanguage, SCRIBAN_LANGUAGE_ID } from './scribanLanguage';
import './App.css';

function App() {
  const editorRef = useRef<HTMLDivElement>(null);

  // Register Scriban language once on mount
  useEffect(() => {
    registerScribanLanguage();
  }, []);

  // Initialize the Scriban editor with SignalR connection
  const {
    hubConnection,
    pickerState,
    handlePickerSelect,
    handlePickerCancel,
    isConnected,
  } = useScribanEditor({
    editorRef,
    languageId: SCRIBAN_LANGUAGE_ID,
    hubUrl: 'http://localhost:5232/scribanhub', // Backend server URL
  });

  return (
    <div className="app">
      <header className="app-header">
        <h1>Scriban Language Server - Interactive Editor</h1>
        <div className="connection-status">
          <span className={`status-indicator ${isConnected ? 'connected' : 'disconnected'}`}></span>
          <span>{isConnected ? 'Connected' : 'Disconnected'}</span>
        </div>
      </header>

      <main className="app-main">
        <div className="editor-container" ref={editorRef}></div>

        {/* Render the appropriate picker based on state */}
        {pickerState.isVisible && pickerState.pickerType === 'file-picker' && (
          <FilePicker
            hubConnection={hubConnection}
            functionName={pickerState.functionName || ''}
            parameterIndex={pickerState.parameterIndex || 0}
            currentValue={pickerState.currentValue}
            position={pickerState.position || { x: 0, y: 0 }}
            onSelect={handlePickerSelect}
            onCancel={handlePickerCancel}
          />
        )}

        {pickerState.isVisible && pickerState.pickerType === 'enum-list' && (
          <EnumPicker
            options={pickerState.options || []}
            functionName={pickerState.functionName || ''}
            parameterIndex={pickerState.parameterIndex || 0}
            currentValue={pickerState.currentValue}
            position={pickerState.position || { x: 0, y: 0 }}
            onSelect={handlePickerSelect}
            onCancel={handlePickerCancel}
          />
        )}
      </main>

      <footer className="app-footer">
        <div className="footer-info">
          <span>
            💡 Try typing <code>copy_file(</code> or <code>os.</code> to trigger IntelliSense
          </span>
          <span className="separator">•</span>
          <span>
            Press <kbd>Ctrl+Space</kbd> for completions
          </span>
          <span className="separator">•</span>
          <span>
            Backend: {isConnected ? '✅ Running' : '❌ Not connected'}
          </span>
        </div>
      </footer>
    </div>
  );
}

export default App;
