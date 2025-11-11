import * as monaco from 'monaco-editor';
import { CUSTOM_FUNCTIONS, MARKER_TOKEN } from '../config';
import { CustomFunction } from '../types';
import { RangeUtils } from './rangeUtils';

export interface ParsedParameter {
  index: number;           // Index in function definition (0-based)
  type: 'path' | 'string' | 'number' | 'boolean';
  name: string;
  range: monaco.Range;     // Range of the parameter value (or marker), excluding quotes for content
  value: string | null;    // Current value (without quotes), or null if marker/empty
  isMarker: boolean;       // True if contains MARKER_TOKEN
  isQuoted: boolean;       // True if the parameter is a quoted string
}

export interface ParsedFunctionCall {
  functionName: string;
  functionDef: CustomFunction;
  parameters: ParsedParameter[];
  openParenRange: monaco.Range;
  closeParenRange: monaco.Range | null;
  fullRange: monaco.Range; // Range of entire function call including function name
}

export class FunctionCallParser {
  /**
   * Parse a function call at a given position in the editor.
   * Returns null if position is not within a function call.
   */
  static parseAtPosition(
    model: monaco.editor.ITextModel,
    position: monaco.Position
  ): ParsedFunctionCall | null {
    const lineNumber = position.lineNumber;
    const lineText = model.getLineContent(lineNumber);
    const column = position.column - 1; // Convert to 0-based

    // Find the function call containing this position
    // Look backwards from position to find opening parenthesis
    let openParenCol = -1;
    let parenDepth = 0;
    let funcStartCol = -1;
    let funcName = '';

    // First, find the opening parenthesis
    for (let i = column; i >= 0; i--) {
      if (lineText[i] === ')') {
        parenDepth++;
      } else if (lineText[i] === '(') {
        if (parenDepth === 0) {
          openParenCol = i;
          // Look backwards to find function name
          let j = i - 1;
          while (j >= 0 && /\s/.test(lineText[j])) j--;
          while (j >= 0 && /[a-zA-Z_0-9]/.test(lineText[j])) j--;
          j++;
          funcStartCol = j;
          funcName = lineText.substring(j, i).trim();
          break;
        } else {
          parenDepth--;
        }
      }
    }

    if (openParenCol === -1 || !funcName) {
      return null;
    }

    // Find function definition
    const functionDef = CUSTOM_FUNCTIONS.find(f => f.name === funcName);
    if (!functionDef) {
      return null;
    }

    // Find closing parenthesis
    let closeParenCol = -1;
    parenDepth = 0;
    for (let i = openParenCol + 1; i < lineText.length; i++) {
      if (lineText[i] === '(') {
        parenDepth++;
      } else if (lineText[i] === ')') {
        if (parenDepth === 0) {
          closeParenCol = i;
          break;
        } else {
          parenDepth--;
        }
      }
    }

    // Extract parameters text
    const paramsText = lineText.substring(openParenCol + 1, closeParenCol !== -1 ? closeParenCol : lineText.length);
    
    // Parse parameters
    const parameters = this.parseParameters(
      model,
      lineNumber,
      openParenCol + 2, // Monaco column after opening paren (1-based)
      paramsText,
      functionDef
    );

    return {
      functionName: funcName,
      functionDef,
      parameters,
      openParenRange: new monaco.Range(lineNumber, openParenCol + 1, lineNumber, openParenCol + 2),
      closeParenRange: closeParenCol !== -1 
        ? new monaco.Range(lineNumber, closeParenCol + 1, lineNumber, closeParenCol + 2)
        : null,
      fullRange: new monaco.Range(
        lineNumber,
        funcStartCol + 1,
        lineNumber,
        closeParenCol !== -1 ? closeParenCol + 2 : lineText.length + 1
      )
    };
  }

  /**
   * Parse parameters from a parameter string.
   */
  private static parseParameters(
    _model: monaco.editor.ITextModel,
    lineNumber: number,
    startColumn: number, // Column where parameters start (after opening paren)
    paramsText: string,
    functionDef: CustomFunction
  ): ParsedParameter[] {
    const parameters: ParsedParameter[] = [];
    
    if (!paramsText.trim()) {
      // No parameters
      return functionDef.parameters.map((param, index) => ({
        index,
        type: param.type,
        name: param.name,
        range: new monaco.Range(lineNumber, startColumn, lineNumber, startColumn),
        value: null,
        isMarker: false,
        isQuoted: false
      }));
    }

    // Parse parameters by splitting on commas, respecting quoted strings
    const paramParts: Array<{ text: string; startOffset: number; endOffset: number; originalText: string }> = [];
    let currentPart = '';
    let currentStart = 0;
    let inQuotes = false;
    let quoteChar = '';
    let quoteStartOffset: number | null = null; // Track the offset of the opening quote
    
    for (let i = 0; i < paramsText.length; i++) {
      const char = paramsText[i];
      const prevChar = i > 0 ? paramsText[i - 1] : '';
      
      if ((char === '"' || char === "'") && prevChar !== '\\') {
        if (!inQuotes) {
          inQuotes = true;
          quoteChar = char;
          quoteStartOffset = i; // Remember the position of the opening quote
          if (currentPart.trim() === '') {
            currentStart = i;
          }
        } else if (char === quoteChar) {
          inQuotes = false;
          // Don't reset quoteStartOffset here - we need it when pushing the parameter
        }
        currentPart += char;
      } else if (char === ',' && !inQuotes) {
        if (currentPart.trim() || paramParts.length < functionDef.parameters.length) {
          // For quoted parameters, use the quote start offset if available
          const startOffset = quoteStartOffset !== null ? quoteStartOffset : currentStart;
          paramParts.push({
            text: currentPart.trim(),
            startOffset: startOffset,
            endOffset: i,
            originalText: currentPart // Keep original for accurate length calculation
          });
        }
        currentPart = '';
        currentStart = i + 1;
        quoteStartOffset = null;
      } else {
        if (currentPart.trim() === '' && !inQuotes && !/\s/.test(char)) {
          currentStart = i;
        }
        currentPart += char;
      }
    }
    
    // Add last parameter
    if (currentPart.trim() || paramParts.length < functionDef.parameters.length) {
      // For quoted parameters, use the quote start offset if available
      const startOffset = quoteStartOffset !== null ? quoteStartOffset : currentStart;
      paramParts.push({
        text: currentPart.trim(),
        startOffset: startOffset,
        endOffset: paramsText.length,
        originalText: currentPart // Keep original for accurate length calculation
      });
    }

    // Map parsed parts to function parameters
    for (let i = 0; i < functionDef.parameters.length; i++) {
      const paramDef = functionDef.parameters[i];
      const part = paramParts[i];
      
      if (!part) {
        // Parameter not present
        const prevEnd = i > 0 && parameters[i - 1]
          ? parameters[i - 1].range.endColumn
          : startColumn;
        parameters.push({
          index: i,
          type: paramDef.type,
          name: paramDef.name,
          range: new monaco.Range(lineNumber, prevEnd, lineNumber, prevEnd),
          value: null,
          isMarker: false,
          isQuoted: false
        });
        continue;
      }

      const partText = part.text;
      const isQuoted = (partText.startsWith('"') && partText.endsWith('"')) ||
                       (partText.startsWith("'") && partText.endsWith("'"));
      const isMarker = partText.includes(MARKER_TOKEN);
      
      // Extract value (remove quotes if present)
      let value: string | null = null;
      if (isMarker) {
        value = null;
      } else if (isQuoted) {
        value = partText.substring(1, partText.length - 1);
      } else {
        value = partText || null;
      }

      // Calculate range
      // For quoted strings, range should be content between quotes (for editing)
      // For markers, range should be the full quoted string (for replacement)
      let rangeStart = startColumn + part.startOffset;
      let rangeEnd: number;
      
      if (isQuoted && !isMarker) {
        // Content between quotes (for existing values)
        // Use originalText to get the actual text including quotes, but trim to remove leading/trailing whitespace
        const originalText = (part as any).originalText || partText;
        const trimmedOriginal = originalText.trim();
        // rangeStart: after opening quote (first char of content)
        // rangeEnd: after closing quote (Monaco ranges are exclusive at end)
        // Both are calculated from the same base: startColumn + part.startOffset (opening quote position)
        const quoteStartPos = startColumn + part.startOffset; // Position of opening quote
        rangeStart = quoteStartPos + 1; // After opening quote (first char of content)
        // Use trimmed length to avoid issues with leading/trailing whitespace
        rangeEnd = quoteStartPos + trimmedOriginal.length; // After closing quote (trimmedOriginal includes both quotes)
      } else if (isMarker && isQuoted) {
        // For markers, range should include quotes for replacement
        // partText includes quotes, so rangeEnd should be after the closing quote
        rangeEnd = startColumn + part.startOffset + partText.length;
      } else {
        // Unquoted parameter
        // part.endOffset is the position of the comma or end of params
        rangeEnd = startColumn + part.endOffset;
      }

      parameters.push({
        index: i,
        type: paramDef.type,
        name: paramDef.name,
        range: new monaco.Range(lineNumber, rangeStart, lineNumber, rangeEnd),
        value,
        isMarker,
        isQuoted
      });
    }

    return parameters;
  }

  /**
   * Get which parameter (by index) is at the given position.
   */
  static getParameterIndexAtPosition(
    model: monaco.editor.ITextModel,
    position: monaco.Position
  ): { functionCall: ParsedFunctionCall; parameterIndex: number } | null {
    const parsed = this.parseAtPosition(model, position);
    if (!parsed) return null;

    // Find which parameter contains this position
    for (const param of parsed.parameters) {
      // For quoted parameters, check if position is within the quoted string (including quotes)
      // For unquoted, check if within the range
      if (param.isQuoted) {
        // Expand range to include quotes for comparison
        const quotedRange = RangeUtils.expandRangeForPositionCheck(param.range);
        if (quotedRange.containsPosition(position)) {
          return { functionCall: parsed, parameterIndex: param.index };
        }
      } else {
        // For unquoted parameters, check if position is within range
        // Also check if position is at the start/end (inclusive)
        if (param.range.containsPosition(position) || 
            (position.lineNumber === param.range.startLineNumber && 
             position.column >= param.range.startColumn && 
             position.column <= param.range.endColumn)) {
          return { functionCall: parsed, parameterIndex: param.index };
        }
      }
    }

    return null;
  }

  /**
   * Get the next path parameter that needs a value (has marker, is empty, or doesn't exist yet).
   */
  static getNextPathParameter(
    parsed: ParsedFunctionCall,
    model: monaco.editor.ITextModel
  ): { parameterIndex: number; range: monaco.Range } | null {
    // Find the first path parameter that needs a value
    for (let i = 0; i < parsed.functionDef.parameters.length; i++) {
      const paramDef = parsed.functionDef.parameters[i];
      if (paramDef.type === 'path') {
        const param = parsed.parameters[i];
        
        // If parameter doesn't exist, is a marker, or is empty, it needs a value
        if (!param || param.isMarker || param.value === null || param.value === '') {
          // Determine the range where the marker should be inserted
          let range: monaco.Range;
          
          if (param && param.isMarker && param.isQuoted) {
            // For markers, param.range already includes quotes (startColumn is at opening quote, endColumn is after closing quote)
            // So we can use it directly without expansion
            range = param.range;
          } else if (param) {
            // Existing parameter (empty) - use its range
            range = param.range;
          } else {
            // Parameter doesn't exist yet - calculate position after previous parameter or opening paren
            const prevParam = i > 0 ? parsed.parameters[i - 1] : null;
            let startCol: number;
            
            if (prevParam) {
              // After previous parameter - need to account for comma and space
              // Get the actual line text to find where the comma is
              const lineNumber = parsed.openParenRange.startLineNumber;
              const lineText = model.getLineContent(lineNumber);
              
              // Find the end of the previous parameter (including quotes)
              // For quoted params, endColumn is already after closing quote
              let prevEndCol = prevParam.range.endColumn;
              
              // Look for comma after the previous parameter
              // Start from prevEndCol (which is 1-based Monaco column, so convert to 0-based index)
              let calculatedStartCol = prevEndCol; // Start from prevEndCol (1-based)
              
              // Search for comma starting from the position after the previous parameter
              // lineText is 0-based, prevEndCol is 1-based, so lineText[prevEndCol - 1] is the char at prevEndCol
              for (let col = prevEndCol; col <= lineText.length; col++) {
                // col is 1-based Monaco column, convert to 0-based index: col - 1
                const charIndex = col - 1;
                if (charIndex >= 0 && charIndex < lineText.length && lineText[charIndex] === ',') {
                  // Found comma at col (1-based), next parameter starts at col + 1
                  calculatedStartCol = col + 1;
                  // Skip any whitespace after comma
                  while (calculatedStartCol <= lineText.length) {
                    const wsIndex = calculatedStartCol - 1;
                    if (wsIndex >= 0 && wsIndex < lineText.length && /\s/.test(lineText[wsIndex])) {
                      calculatedStartCol++;
                    } else {
                      break;
                    }
                  }
                  break;
                }
              }
              
              startCol = calculatedStartCol;
            } else {
              // First parameter - after opening paren
              startCol = parsed.openParenRange.endColumn;
            }
            
            range = new monaco.Range(
              parsed.openParenRange.startLineNumber,
              startCol,
              parsed.openParenRange.startLineNumber,
              startCol
            );
          }
          
          return { parameterIndex: i, range };
        }
      }
    }
    return null;
  }

  /**
   * Find marker range in the model.
   */
  static findMarkerRange(
    model: monaco.editor.ITextModel
  ): monaco.Range | null {
    const escapedMarker = MARKER_TOKEN.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const regexPattern = `"${escapedMarker}"`;
    const matches = model.findMatches(regexPattern, true, true, false, null, false);
    if (!matches || matches.length === 0) return null;
    
    const range = matches[0].range;
    const textAtRange = model.getValueInRange(range);
    const expectedText = `"${MARKER_TOKEN}"`;
    
    if (textAtRange === expectedText) {
      return range;
    }
    
    return null;
  }

  /**
   * Find a function call by name in the current document.
   * If multiple calls exist, returns the most recent one (closest to current cursor).
   *
   * @param model - The text model to search in
   * @param functionName - The name of the function to find
   * @param nearPosition - Optional position to find the closest match
   * @returns ParsedFunctionCall or null if not found
   */
  static findFunctionCallByName(
    model: monaco.editor.ITextModel,
    functionName: string,
    nearPosition?: monaco.Position
  ): ParsedFunctionCall | null {
    const lineCount = model.getLineCount();
    let closestMatch: { parsed: ParsedFunctionCall; distance: number } | null = null;

    // Search through all lines
    for (let line = 1; line <= lineCount; line++) {
      const lineText = model.getLineContent(line);
      const funcNamePattern = new RegExp(`\\b${functionName}\\s*\\(`, 'g');
      let match: RegExpExecArray | null;

      while ((match = funcNamePattern.exec(lineText)) !== null) {
        // Position right after the opening paren (inside the function call)
        // match[0] is the full match like "copy(" - we want position after the "("
        // match.index is 0-based, Monaco columns are 1-based, so +1 for conversion and +match[0].length for position
        const position = new monaco.Position(line, match.index + match[0].length + 1);
        const parsed = this.parseAtPosition(model, position);

        if (parsed && parsed.functionName === functionName) {
          // Calculate distance from nearPosition if provided
          if (nearPosition) {
            const distance = Math.abs(line - nearPosition.lineNumber);
            if (!closestMatch || distance < closestMatch.distance) {
              closestMatch = { parsed, distance };
            }
          } else {
            // Return first match if no position specified
            return parsed;
          }
        }
      }
    }

    return closestMatch?.parsed || null;
  }
}

