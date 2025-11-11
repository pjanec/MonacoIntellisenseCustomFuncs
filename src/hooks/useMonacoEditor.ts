import { useEffect, useRef } from 'react';
import * as monaco from 'monaco-editor';
import { MARKER_TOKEN } from '../config';
import { Diagnostic } from '../types';
import { FunctionCallParser } from '../services/functionCallParser';
import { RangeUtils } from '../services/rangeUtils';
import { TIMING } from '../constants/timing';
import { MarkerDetectionService, MarkerInfo } from '../services/markerDetectionService';
import { AutoInsertionService } from '../services/autoInsertionService';
import { useMonacoLanguageSetup } from './monaco/useMonacoLanguageSetup';
import { setupMonacoClickHandler, ParameterClickInfo } from './monaco/setupMonacoClickHandler';
import { DebugLogger } from '../utils/debugLogger';
import { CtrlSpaceHandlerService } from '../services/ctrlSpaceHandlerService';
import { DelayedCallback, delayedMarkerDetection, delayedCallback, cancelDelayedCallback } from '../utils/timingUtils';

interface UseMonacoEditorOptions {
  onMarkerDetected: (info: MarkerInfo) => void;
  onParameterClick: (info: ParameterClickInfo) => void;
  onValidation: (script: string) => Promise<void>;
  onManualTyping?: () => void;
}

export function useMonacoEditor(
  containerRef: React.RefObject<HTMLDivElement>,
  options: UseMonacoEditorOptions
) {
  const editorRef = useRef<monaco.editor.IStandaloneCodeEditor | null>(null);
  const markerDetectionRef = useRef<boolean>(false);
  const previousContentLengthRef = useRef<number>(0);
  const controlsRef = useRef<{
    replaceParameter: (functionName: string, parameterIndex: number, text: string, nearPosition?: monaco.Position) => void;
    setDiagnostics: (diagnostics: Diagnostic[]) => void;
    getMarkerPosition: (range: monaco.Range) => { x: number; y: number } | null;
    getModel: () => monaco.editor.ITextModel | null;
    getEditor: () => monaco.editor.IStandaloneCodeEditor | null;
    deselectAndPositionCursor: (range: monaco.Range) => void;
  } | null>(null);

  // Set up Scriban language support (tokenizer, completion, signature help, hover)
  useMonacoLanguageSetup();

  useEffect(() => {
    if (!containerRef.current) return;

    const editor = monaco.editor.create(containerRef.current, {
      value: `copy("src/index.js")\nmove("static/logo.png", "assets/logo.png")\ndelete("temp.txt")\n\nfor item in collection\n  item\nend`,
      language: 'scriban',
      theme: 'vs-light',
      automaticLayout: true,
      minimap: { enabled: false },
      scrollBeyondLastLine: false,
      fontSize: 14,
      lineNumbers: 'on',
      roundedSelection: false,
      cursorStyle: 'line',
      wordWrap: 'on'
    });

    editorRef.current = editor;

    let skipNextMarkerDetection = false;
    let autoInsertTimer: DelayedCallback | null = null;
    
    // Initialize previous content length
    const model = editor.getModel();
    if (model) {
      previousContentLengthRef.current = model.getValue().length;
    }
    
    const disposable = editor.onDidChangeModelContent(() => {
      const model = editor.getModel();
      if (!model) return;

      DebugLogger.contentChange('Model content changed');

      // Skip marker detection if we're in the middle of sequential parameter picking
      if (skipNextMarkerDetection) {
        DebugLogger.contentChange('Skipping marker detection (skipNextMarkerDetection flag set)');
        skipNextMarkerDetection = false;
        const currentContent = model.getValue();
        previousContentLengthRef.current = currentContent.length;
        debouncedValidation(currentContent);
        return;
      }

      const currentContent = model.getValue();
      const currentContentLength = currentContent.length;
      const previousContentLength = previousContentLengthRef.current;
      
      // Only trigger auto-insertion and marker detection when content is added (typing), not removed (deletion)
      const contentWasAdded = currentContentLength > previousContentLength;
      const contentWasReplaced = currentContentLength === previousContentLength && previousContentLength > 0;
      
      DebugLogger.contentChange('Content length:', { previousContentLength, currentContentLength, contentWasAdded, contentWasReplaced });
      
      // Check if markers exist in the document (for completion insertion detection)
      const hasMarkersInDocument = currentContent.includes(MARKER_TOKEN);
      DebugLogger.contentChange('Has markers in document:', hasMarkersInDocument);
      
      // Update previous content length
      previousContentLengthRef.current = currentContentLength;
      
      // If content was removed, only run validation
      if (!contentWasAdded && !contentWasReplaced) {
        DebugLogger.contentChange('Content removed, only running validation');
        debouncedValidation(currentContent);
        return;
      }

      const position = editor.getPosition();
      if (!position) {
        DebugLogger.contentChange('No position available');
        debouncedValidation(currentContent);
        return;
      }

      // Auto-detect function calls: when user types "functionName("
      const lineText = model.getLineContent(position.lineNumber);
      const cursorOffset = position.column - 1;

      DebugLogger.contentChange('Position:', { line: position.lineNumber, column: position.column, cursorOffset });
      DebugLogger.contentChange('Line text:', lineText);

      // Check if we should trigger auto-insertion after '('
      if (AutoInsertionService.shouldTriggerOpenParenInsertion(lineText, cursorOffset, contentWasAdded, contentWasReplaced)) {
        DebugLogger.contentChange('Detected open paren, setting up auto-insert timer');
        // Clear any existing timer
        if (autoInsertTimer) {
          clearTimeout(autoInsertTimer);
        }

        // Use service to handle the insertion
        autoInsertTimer = AutoInsertionService.insertMarkersAfterOpenParen(
          editor,
          model,
          options.onMarkerDetected,
          markerDetectionRef,
          (value: boolean) => { skipNextMarkerDetection = value; },
          debouncedValidation
        );
      }
      
      // Auto-detect comma: when user types comma after a parameter
      const charBeforeCursor = cursorOffset > 0 ? lineText[cursorOffset - 1] : null;

      // Check if we should trigger auto-insertion after ','
      const { shouldInsert, commaPos } = AutoInsertionService.shouldTriggerCommaInsertion(
        lineText,
        cursorOffset,
        contentWasAdded,
        contentWasReplaced
      );

      if (shouldInsert) {
        const inserted = AutoInsertionService.insertMarkerAfterComma(
          model,
          position,
          commaPos,
          charBeforeCursor,
          options.onMarkerDetected,
          markerDetectionRef,
          (value: boolean) => { skipNextMarkerDetection = value; },
          debouncedValidation
        );

        if (inserted) {
          return;
        }
      }

      // Close picker if user is typing manually (not from marker insertion)
      // Only close if we didn't just insert markers programmatically
      if (contentWasAdded && !skipNextMarkerDetection && options.onManualTyping) {
        // Check if this is manual typing (not auto-insertion of markers on '(' or ',')
        // lineText and cursorOffset are already declared above
        const isAutoInsertion = (cursorOffset > 0 && lineText[cursorOffset - 1] === '(') ||
                                (cursorOffset > 0 && lineText[cursorOffset - 1] === ',');
        DebugLogger.contentChange('Manual typing check:', { isAutoInsertion, willClosePicker: !isAutoInsertion });
        if (!isAutoInsertion) {
          options.onManualTyping();
        }
      }

      // Check for markers in the document when content was added but not from '(' or ',' triggers
      // This handles completion item insertion which inserts markers directly
      const openParenTrigger = AutoInsertionService.shouldTriggerOpenParenInsertion(lineText, cursorOffset, contentWasAdded, contentWasReplaced);
      const commaTrigger = contentWasAdded && cursorOffset > 0 && lineText[cursorOffset - 1] === ',';
      if (contentWasAdded && hasMarkersInDocument && !openParenTrigger && !commaTrigger) {
        DebugLogger.contentChange('Markers detected in document (possibly from completion), checking for marker detection');
        // Small delay to let Monaco finish processing the completion insertion
        delayedMarkerDetection(() => {
          MarkerDetectionService.detectAndOpenPicker(
            model,
            options.onMarkerDetected,
            markerDetectionRef
          );
        });
      }

      debouncedValidation(currentContent);
    });

    let validationTimer: DelayedCallback | null = null;
    const debouncedValidation = (text: string) => {
      cancelDelayedCallback(validationTimer);
      validationTimer = delayedCallback(() => {
        options.onValidation(text);
      }, TIMING.VALIDATION_DEBOUNCE);
    };

    options.onValidation(editor.getValue());

    const replaceParameter = (functionName: string, parameterIndex: number, text: string, nearPosition?: monaco.Position) => {
      const model = editor.getModel();
      if (!model) {
        console.error('[REPLACE_PARAMETER] No model available');
        return;
      }

      DebugLogger.replaceParameter('Replacing parameter:', { functionName, parameterIndex, text, nearPosition });

      // Find the function call using fresh document state
      const parsed = FunctionCallParser.findFunctionCallByName(model, functionName, nearPosition);
      if (!parsed) {
        console.error(`[REPLACE_PARAMETER] Function "${functionName}" not found in document`);
        return;
      }

      // Validate parameter index
      if (parameterIndex < 0 || parameterIndex >= parsed.parameters.length) {
        console.error(`[REPLACE_PARAMETER] Invalid parameter index ${parameterIndex} for function ${functionName}`);
        return;
      }

      // Get the parameter and calculate fresh range
      const param = parsed.parameters[parameterIndex];
      const actualRange = RangeUtils.getReplacementRange(param);

      DebugLogger.replaceParameter('Replacing range:', actualRange, 'with text:', text);

      // Set flag to skip marker detection on next content change
      skipNextMarkerDetection = true;

      // Calculate cursor position after insertion (after the inserted text)
      const endLine = actualRange.endLineNumber;
      const endColumn = actualRange.startColumn + text.length;
      const cursorPosition = new monaco.Position(endLine, endColumn);

      // Perform the replacement
      model.pushEditOperations(
        [],
        [{
          range: actualRange,
          text
        }],
        () => null
      );

      // Position cursor after the inserted text
      delayedCallback(() => {
        editor.setPosition(cursorPosition);
        editor.setSelection(new monaco.Selection(
          cursorPosition.lineNumber,
          cursorPosition.column,
          cursorPosition.lineNumber,
          cursorPosition.column
        ));
        editor.focus();
      }, 0);
    };

    const setDiagnostics = (diagnostics: Diagnostic[]) => {
      const model = editor.getModel();
      if (!model) return;

      const markers = diagnostics.map(d => ({
        startLineNumber: d.startLine,
        startColumn: d.startCol,
        endLineNumber: d.endLine,
        endColumn: d.endCol,
        message: d.message,
        severity: d.severity === 'error'
          ? monaco.MarkerSeverity.Error
          : monaco.MarkerSeverity.Warning
      }));

      monaco.editor.setModelMarkers(model, 'scriban', markers);
    };

    const getMarkerPosition = (range: monaco.Range): { x: number; y: number } | null => {
      const position = range.getStartPosition();
      const coords = editor.getScrolledVisiblePosition(position);
      if (!coords) return null;

      const editorDom = editor.getDomNode();
      if (!editorDom) return null;
      
      const editorRect = editorDom.getBoundingClientRect();

      return {
        x: editorRect.left + coords.left,
        y: editorRect.top + coords.top + coords.height
      };
    };

    // Set up click handler for parameter detection
    const clickDisposable = setupMonacoClickHandler(editor, {
      onParameterClick: options.onParameterClick
    });

    // Intercept Ctrl+Space before Monaco's default handler
    // Use onKeyDown to catch the event early, but we need to handle it properly
    const keyDownDisposable = editor.onKeyDown((e) => {
      // Close picker when Enter is pressed in the editor
      if (e.keyCode === monaco.KeyCode.Enter) {
        // Call onManualTyping to close the picker
        if (options.onManualTyping) {
          options.onManualTyping();
        }
        // Don't prevent default - let Enter go through to create new line
        return;
      }
      
      // Check for Ctrl+Space (or Cmd+Space on Mac)
      // Monaco's KeyCode.Space is 32
      const isSpace = e.keyCode === monaco.KeyCode.Space || e.keyCode === 32;
      const isCtrlOrCmd = e.ctrlKey || e.metaKey;
      
      if (isCtrlOrCmd && isSpace) {
        const model = editor.getModel();
        const position = editor.getPosition();
        if (!model || !position) return;

        // Use the centralized Ctrl+Space handler
        const result = CtrlSpaceHandlerService.handleCtrlSpace(model, position);

        if (result.action === 'open-picker') {
          // Stop event propagation to prevent default completion
          e.stopPropagation();
          // Open picker for this parameter
          const pickerPosition = getMarkerPosition(result.range!);
          if (pickerPosition) {
            options.onParameterClick({
              range: result.range!,
              functionName: result.functionName!,
              parameterIndex: result.parameterIndex!
            });
          }
          return;
        }

        if (result.action === 'insert-marker') {
          // Stop event propagation to prevent default completion
          e.stopPropagation();
          // Insert marker for the next path parameter
          skipNextMarkerDetection = true;
          model.pushEditOperations(
            [],
            [{
              range: result.range!,
              text: result.insertText!
            }],
            () => null
          );

          // Trigger marker detection
          delayedMarkerDetection(() => {
            MarkerDetectionService.detectAndOpenPicker(
              model,
              options.onMarkerDetected,
              markerDetectionRef
            );
          });
          return;
        }
      }
    });

    const deselectAndPositionCursor = (range: monaco.Range) => {
      // Position cursor at the end of the range content
      // The range is the content between quotes, so we position at the end of the content
      const position = new monaco.Position(range.startLineNumber, range.endColumn);
      editor.setPosition(position);
      // Clear selection by setting selection to a single position (no selection)
      editor.setSelection(new monaco.Selection(
        position.lineNumber,
        position.column,
        position.lineNumber,
        position.column
      ));
      editor.focus();
    };

        controlsRef.current = {
          replaceParameter,
          setDiagnostics,
          getMarkerPosition,
          getModel: () => editor.getModel(),
          getEditor: () => editor,
          deselectAndPositionCursor
        };

        // Add Ctrl+Space command to open picker when cursor is in a parameter
        // Use a context key to ensure our command runs before default completion
        const handleCtrlSpace = () => {
          const model = editor.getModel();
          const position = editor.getPosition();
          if (!model || !position) return false; // Return false to allow default

          // Use the centralized Ctrl+Space handler
          const result = CtrlSpaceHandlerService.handleCtrlSpace(model, position);

          if (result.action === 'open-picker') {
            // Open picker for this parameter
            const pickerPosition = getMarkerPosition(result.range!);
            if (pickerPosition) {
              options.onParameterClick({
                range: result.range!,
                functionName: result.functionName!,
                parameterIndex: result.parameterIndex!
              });
              return true; // Handled
            }
            return false;
          }

          if (result.action === 'insert-marker') {
            // Insert marker for the next path parameter
            skipNextMarkerDetection = true;
            model.pushEditOperations(
              [],
              [{
                range: result.range!,
                text: result.insertText!
              }],
              () => null
            );

            // Trigger marker detection
            delayedMarkerDetection(() => {
              MarkerDetectionService.detectAndOpenPicker(
                model,
                options.onMarkerDetected,
                markerDetectionRef
              );
            });
            return true; // Handled
          }

          return false; // Not handled, allow default
        };

        // Register command - Monaco will execute commands in order, so we need to prevent default
        // by handling it ourselves and not calling the default trigger
        editor.addCommand(
          monaco.KeyMod.CtrlCmd | monaco.KeyCode.Space,
          () => {
            if (!handleCtrlSpace()) {
              // If we didn't handle it, trigger default completion
              editor.trigger('keyboard', 'editor.action.triggerSuggest', {});
            }
          }
        );

      return () => {
        disposable.dispose();
        clickDisposable.dispose();
        keyDownDisposable.dispose();
        editor.dispose();
        if (validationTimer) clearTimeout(validationTimer);
        if (autoInsertTimer) clearTimeout(autoInsertTimer);
        controlsRef.current = null;
      };
  }, []);

  return {
    editor: editorRef.current,
    replaceParameter: (functionName: string, parameterIndex: number, text: string, nearPosition?: monaco.Position) =>
      controlsRef.current?.replaceParameter(functionName, parameterIndex, text, nearPosition),
    setDiagnostics: (diagnostics: Diagnostic[]) => controlsRef.current?.setDiagnostics(diagnostics),
    getMarkerPosition: (range: monaco.Range) => controlsRef.current?.getMarkerPosition(range) || null,
    getModel: () => editorRef.current?.getModel() || null,
    getEditor: () => editorRef.current,
    deselectAndPositionCursor: (range: monaco.Range) => controlsRef.current?.deselectAndPositionCursor(range)
  };
}

