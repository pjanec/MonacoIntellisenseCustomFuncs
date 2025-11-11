import * as monaco from 'monaco-editor';
import { findParameterAtPosition as findParam } from '../../utils/findParameterAtPosition';
import { TIMING } from '../../constants/timing';
import { delayedClickProcessing } from '../../utils/timingUtils';

export interface ParameterClickInfo {
  range: monaco.Range;
  functionName: string;
  parameterIndex: number;
  clickPosition?: monaco.Position;
}

interface ClickHandlerOptions {
  onParameterClick: (info: ParameterClickInfo) => void;
}

/**
 * Sets up click handler for parameter detection in Monaco editor.
 * When user clicks on a path parameter, triggers the parameter picker.
 */
export function setupMonacoClickHandler(
  editor: monaco.editor.IStandaloneCodeEditor,
  options: ClickHandlerOptions
): monaco.IDisposable {

  return editor.onMouseDown((e) => {
    // Check if clicking on content text or content view zone
    if (
      (e.target.type === monaco.editor.MouseTargetType.CONTENT_TEXT ||
       e.target.type === monaco.editor.MouseTargetType.CONTENT_VIEW_ZONE ||
       e.target.type === monaco.editor.MouseTargetType.CONTENT_EMPTY) &&
      e.target.position
    ) {
      const position = e.target.position;
      // Use a delay to ensure the click is processed and cursor is set
      delayedClickProcessing(() => {
        const model = editor.getModel();
        if (!model) return;

        // Use the clicked position to find parameter
        const paramInfo = findParam(model, position);
        if (paramInfo) {
          // Pass the original click position so we can restore it later
          options.onParameterClick({
            ...paramInfo,
            clickPosition: position
          });
        }
      });
    }
  });
}
