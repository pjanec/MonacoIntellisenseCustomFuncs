import * as monaco from 'monaco-editor';
import { FunctionCallParser } from './functionCallParser';

/**
 * Utility for extracting parameter values from the editor.
 * Centralizes the logic for getting current parameter values.
 */
export class ParameterValueExtractor {
  /**
   * Get the current value of a parameter at the given range.
   * 
   * @param model - The Monaco editor model
   * @param range - The range of the parameter (including quotes if present)
   * @returns The parameter value (without quotes) or null if not found
   */
  static getParameterValue(
    model: monaco.editor.ITextModel,
    range: monaco.Range
  ): string | null {
    const paramInfo = FunctionCallParser.getParameterIndexAtPosition(
      model,
      range.getStartPosition()
    );
    
    if (!paramInfo) {
      return null;
    }
    
    const param = paramInfo.functionCall.parameters[paramInfo.parameterIndex];
    return param.value;
  }

  /**
   * Get parameter information at a given position.
   * 
   * @param model - The Monaco editor model
   * @param position - The position in the editor
   * @returns Parameter info or null if not at a parameter
   */
  static getParameterInfo(
    model: monaco.editor.ITextModel,
    position: monaco.Position
  ) {
    return FunctionCallParser.getParameterIndexAtPosition(model, position);
  }
}

