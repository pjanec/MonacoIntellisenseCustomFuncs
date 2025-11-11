import * as monaco from 'monaco-editor';
import { FunctionCallParser } from '../services/functionCallParser';
import { RangeUtils } from '../services/rangeUtils';

export interface ParameterPositionInfo {
  range: monaco.Range;
  functionName: string;
  parameterIndex: number;
}

/**
 * Find if a given position in the editor is within a path parameter.
 * Returns parameter info if found, null otherwise.
 */
export function findParameterAtPosition(
  model: monaco.editor.ITextModel,
  position: monaco.Position
): ParameterPositionInfo | null {
  // Use FunctionCallParser to get parameter info at position
  const paramInfo = FunctionCallParser.getParameterIndexAtPosition(model, position);
  if (!paramInfo) return null;

  // Only return if it's a path parameter
  const param = paramInfo.functionCall.parameters[paramInfo.parameterIndex];
  if (param.type !== 'path') return null;

  // Get the replacement range (includes quotes if present)
  const range = RangeUtils.getReplacementRange(param);

  return {
    range,
    functionName: paramInfo.functionCall.functionName,
    parameterIndex: paramInfo.parameterIndex
  };
}
