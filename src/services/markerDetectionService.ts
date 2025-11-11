import * as monaco from 'monaco-editor';
import { FunctionCallParser } from './functionCallParser';
import { RangeUtils } from './rangeUtils';
import { TIMING } from '../constants/timing';
import { DebugLogger } from '../utils/debugLogger';
import { delayedPickerOpen } from '../utils/timingUtils';

export interface MarkerInfo {
  range: monaco.Range;
  functionName: string;
  parameterIndex: number;
}

export class MarkerDetectionService {
  /**
   * Detects a marker in the model and triggers the onMarkerDetected callback.
   * Uses a ref to prevent duplicate detections.
   */
  static detectAndOpenPicker(
    model: monaco.editor.ITextModel,
    onMarkerDetected: (info: MarkerInfo) => void,
    detectionInProgressRef: React.MutableRefObject<boolean>
  ): void {
    DebugLogger.markerDetection('Looking for marker range');
    const markerRange = FunctionCallParser.findMarkerRange(model);
    DebugLogger.markerDetection('Marker range found:', markerRange);
    DebugLogger.markerDetection('Detection in progress:', detectionInProgressRef.current);

    if (!markerRange || detectionInProgressRef.current) {
      DebugLogger.markerDetection('Marker detection skipped:', {
        markerRange: !!markerRange,
        detectionInProgress: detectionInProgressRef.current
      });
      return;
    }

    detectionInProgressRef.current = true;

    delayedPickerOpen(() => {
      const markerInfo = this.getMarkerInfo(model, markerRange);
      DebugLogger.markerDetection('Marker info:', markerInfo);

      if (markerInfo) {
        DebugLogger.markerDetection('Calling onMarkerDetected');
        onMarkerDetected(markerInfo);
      } else {
        DebugLogger.markerDetection('No marker info, not calling onMarkerDetected');
      }

      detectionInProgressRef.current = false;
    });
  }

  /**
   * Get marker info from a marker range.
   * Returns null if the range doesn't contain a valid marker.
   */
  static getMarkerInfo(
    model: monaco.editor.ITextModel,
    range: monaco.Range
  ): MarkerInfo | null {
    // Use FunctionCallParser to get parameter info at marker position
    // Try parsing at a position inside the marker token (after the opening quote)
    // This ensures the parser correctly identifies the parameter
    let position = range.getStartPosition();

    // If the range starts at a quote, move inside the marker token
    const lineText = model.getLineContent(position.lineNumber);
    if (lineText[position.column - 1] === '"' && range.endColumn > range.startColumn + 1) {
      // Position is at opening quote, move inside the marker token
      position = new monaco.Position(position.lineNumber, position.column + 1);
    }

    DebugLogger.markerDetection('Getting marker info at position:', position, 'range:', range);
    const paramInfo = FunctionCallParser.getParameterIndexAtPosition(model, position);
    DebugLogger.markerDetection('paramInfo:', paramInfo);

    if (!paramInfo) {
      DebugLogger.markerDetection('No paramInfo found, trying to parse function call directly');

      // Fallback: search backwards from marker to find the function call
      // Look for the opening parenthesis before the marker
      const markerLine = range.startLineNumber;
      const markerCol = range.startColumn;
      const lineText = model.getLineContent(markerLine);

      // Search backwards from marker to find opening parenthesis
      let parenPos = -1;
      for (let i = markerCol - 2; i >= 0; i--) {
        if (lineText[i] === '(') {
          parenPos = i;
          break;
        }
      }

      if (parenPos >= 0) {
        // Parse at the opening parenthesis position
        const parsePos = new monaco.Position(markerLine, parenPos + 1);
        DebugLogger.markerDetection('Parsing at opening paren position:', parsePos);
        const parsed = FunctionCallParser.parseAtPosition(model, parsePos);

        if (parsed) {
          DebugLogger.markerDetection('Parsed function call:', parsed);

          for (let i = 0; i < parsed.parameters.length; i++) {
            const param = parsed.parameters[i];
            DebugLogger.markerDetection('Checking parameter', i, {
              ...param,
              isMarker: param.isMarker,
              isQuoted: param.isQuoted
            });

            if (param.isMarker) {
              // Check if the marker range overlaps with the parameter range
              // For quoted parameters, expand the range to include quotes
              const paramRange = param.isQuoted
                ? RangeUtils.expandRangeForPositionCheck(param.range)
                : param.range;

              const markerStart = range.getStartPosition();
              const markerEnd = range.getEndPosition();
              const paramStart = paramRange.getStartPosition();
              const paramEnd = paramRange.getEndPosition();

              // Check if ranges overlap: marker starts before param ends AND marker ends after param starts
              const overlaps = (markerStart.lineNumber < paramEnd.lineNumber ||
                               (markerStart.lineNumber === paramEnd.lineNumber && markerStart.column <= paramEnd.column)) &&
                              (markerEnd.lineNumber > paramStart.lineNumber ||
                               (markerEnd.lineNumber === paramStart.lineNumber && markerEnd.column >= paramStart.column));

              DebugLogger.markerDetection('Range overlap check:', {
                paramRange,
                markerRange: range,
                overlaps
              });

              if (overlaps) {
                DebugLogger.markerDetection('Found marker parameter via fallback');
                return {
                  range: range,
                  functionName: parsed.functionName,
                  parameterIndex: i
                };
              }
            }
          }
        }
      }

      DebugLogger.markerDetection('No paramInfo found after fallback');
      return null;
    }

    // Verify this is actually a marker
    const param = paramInfo.functionCall.parameters[paramInfo.parameterIndex];
    DebugLogger.markerDetection('Parameter:', param);

    if (!param.isMarker) {
      DebugLogger.markerDetection('Parameter is not a marker');
      return null;
    }

    // Use the range from findMarkerRange (which is accurate) instead of param.range
    // The range passed in should be the correct range from findMarkerRange
    DebugLogger.markerDetection('Returning marker info');
    return {
      range: range, // Use the range from findMarkerRange which is accurate
      functionName: paramInfo.functionCall.functionName,
      parameterIndex: paramInfo.parameterIndex
    };
  }
}
