import * as monaco from 'monaco-editor';
import { ParsedParameter } from './functionCallParser';

/**
 * Utility functions for working with Monaco Editor ranges.
 * Centralizes range expansion logic to ensure consistency.
 */
export class RangeUtils {
  /**
   * Expand a parameter range to include surrounding quotes.
   * For quoted parameters, the parser returns a range for content between quotes.
   * This expands it to include the quotes themselves for replacement operations.
   * 
   * @param param - The parsed parameter with range information
   * @returns Range including quotes, or original range if not quoted
   */
  static expandToIncludeQuotes(param: ParsedParameter): monaco.Range {
    if (param.isQuoted && !param.isMarker) {
      // For quoted non-marker parameters:
      // - param.range.startColumn is after opening quote (first char of content)
      // - param.range.endColumn is after closing quote (Monaco ranges are exclusive at end)
      // To include quotes: go back 1 for opening quote, endColumn is already correct
      return new monaco.Range(
        param.range.startLineNumber,
        param.range.startColumn - 1, // Opening quote
        param.range.endLineNumber,
        param.range.endColumn        // Already after closing quote
      );
    }
    
    // For markers, the range already includes quotes
    // For unquoted parameters, return as-is
    return param.range;
  }

  /**
   * Get the range for replacing a parameter value.
   * This is the range that should be replaced when inserting a new value.
   * 
   * @param param - The parsed parameter
   * @returns Range suitable for replacement (includes quotes if present)
   */
  static getReplacementRange(param: ParsedParameter): monaco.Range {
    // Markers already have the correct range (including quotes)
    if (param.isMarker) {
      return param.range;
    }
    
    // For existing quoted parameters, expand to include quotes
    return this.expandToIncludeQuotes(param);
  }

  /**
   * Expand a range to include quotes for position checking.
   * Used when checking if a position is within a quoted parameter.
   * 
   * @param range - The parameter range (content between quotes)
   * @returns Range including quotes
   */
  static expandRangeForPositionCheck(range: monaco.Range): monaco.Range {
    return new monaco.Range(
      range.startLineNumber,
      range.startColumn - 1, // Include opening quote
      range.endLineNumber,
      range.endColumn        // Already after closing quote
    );
  }
}

