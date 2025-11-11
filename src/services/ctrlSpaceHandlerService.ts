import * as monaco from 'monaco-editor';
import { FunctionCallParser } from './functionCallParser';
import { findParameterAtPosition } from '../utils/findParameterAtPosition';
import { MARKER_TOKEN } from '../config';

/**
 * Result of handling Ctrl+Space
 */
export interface CtrlSpaceHandlerResult {
  /** Type of action to perform */
  action: 'open-picker' | 'insert-marker' | 'none';
  /** Range for the parameter (if action is 'open-picker' or 'insert-marker') */
  range?: monaco.Range;
  /** Function name (if action is 'open-picker' or 'insert-marker') */
  functionName?: string;
  /** Parameter index (if action is 'open-picker' or 'insert-marker') */
  parameterIndex?: number;
  /** Text to insert (if action is 'insert-marker') */
  insertText?: string;
}

/**
 * Service to handle Ctrl+Space key press in the editor
 * Centralizes logic for checking if we should open a picker for an existing parameter
 * or insert a marker for the next path parameter
 */
export class CtrlSpaceHandlerService {
  /**
   * Handles Ctrl+Space key press at the given position
   * Returns information about what action to take
   */
  static handleCtrlSpace(
    model: monaco.editor.ITextModel,
    position: monaco.Position
  ): CtrlSpaceHandlerResult {
    // Check if cursor is in a path parameter
    const paramInfo = findParameterAtPosition(model, position);
    if (paramInfo) {
      // Open picker for this parameter
      return {
        action: 'open-picker',
        range: paramInfo.range,
        functionName: paramInfo.functionName,
        parameterIndex: paramInfo.parameterIndex,
      };
    }

    // If not in an existing parameter, check if we're in a function call
    const parsed = FunctionCallParser.parseAtPosition(model, position);
    if (parsed) {
      // Get the next path parameter that needs a value
      const nextPathParam = FunctionCallParser.getNextPathParameter(parsed, model);
      if (nextPathParam) {
        // Insert marker for the next path parameter
        return {
          action: 'insert-marker',
          range: nextPathParam.range,
          functionName: parsed.functionName,
          parameterIndex: nextPathParam.parameterIndex,
          insertText: `"${MARKER_TOKEN}"`,
        };
      }
    }

    // Not handled - allow default behavior
    return {
      action: 'none',
    };
  }
}
