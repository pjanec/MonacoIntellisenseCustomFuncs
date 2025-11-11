import * as monaco from 'monaco-editor';
import { MARKER_TOKEN } from '../config';
import { TIMING } from '../constants/timing';
import { FunctionCallParser } from './functionCallParser';
import { MarkerDetectionService, MarkerInfo } from './markerDetectionService';
import { DebugLogger } from '../utils/debugLogger';
import { DelayedCallback, debouncedAutoInsert, delayedMarkerDetection } from '../utils/timingUtils';

/**
 * Service for automatically inserting parameter markers when user types '(' or ','.
 *
 * Handles:
 * - Detection of '(' character to trigger marker insertion for all parameters
 * - Detection of ',' character to trigger marker insertion for next parameter
 * - Debouncing to avoid rapid-fire insertions
 * - Position validation before insertion
 */
export class AutoInsertionService {
  /**
   * Check if the last character typed was '(' and conditions are met for auto-insertion.
   */
  static shouldTriggerOpenParenInsertion(
    lineText: string,
    cursorOffset: number,
    contentWasAdded: boolean,
    contentWasReplaced: boolean
  ): boolean {
    const lastCharIsOpenParen = (contentWasAdded || contentWasReplaced) &&
                                 cursorOffset > 0 &&
                                 lineText[cursorOffset - 1] === '(' &&
                                 lineText.trim().endsWith('(');

    DebugLogger.autoInsert('Last char is open paren:', lastCharIsOpenParen, {
      contentWasAdded,
      contentWasReplaced,
      cursorOffset,
      lineText
    });

    return lastCharIsOpenParen;
  }

  /**
   * Insert parameter markers after '(' character.
   * Returns a cleanup function to clear the timer if needed.
   */
  static insertMarkersAfterOpenParen(
    editor: monaco.editor.IStandaloneCodeEditor,
    model: monaco.editor.ITextModel,
    onMarkerDetected: (info: MarkerInfo) => void,
    markerDetectionRef: React.MutableRefObject<boolean>,
    setSkipNextMarkerDetection: (value: boolean) => void,
    debouncedValidation: (content: string) => void
  ): DelayedCallback {
    DebugLogger.autoInsert('Setting up auto-insert timer');

    return debouncedAutoInsert(() => {
      DebugLogger.autoInsert('Timer fired, checking if still at open paren');
      const currentPosition = editor.getPosition();
      if (!currentPosition) {
        DebugLogger.autoInsert('No position available');
        return;
      }

      const currentLineText = model.getLineContent(currentPosition.lineNumber);
      const currentCursorOffset = currentPosition.column - 1;

      DebugLogger.autoInsert('Current position:', {
        line: currentPosition.lineNumber,
        column: currentPosition.column,
        cursorOffset: currentCursorOffset
      });
      DebugLogger.autoInsert('Current line text:', currentLineText);

      // Re-check if we're still at a '(' position
      const stillAtOpenParen = currentCursorOffset > 0 && currentLineText[currentCursorOffset - 1] === '(';
      DebugLogger.autoInsert('Still at open paren:', stillAtOpenParen);

      if (!stillAtOpenParen) {
        DebugLogger.autoInsert('No longer at open paren position');
        return;
      }

      // Use parser to check if this is a function call
      const parsed = FunctionCallParser.parseAtPosition(model, currentPosition);
      DebugLogger.autoInsert('Parsed function call:', parsed ? {
        functionName: parsed.functionName,
        hasPathParams: parsed.functionDef.parameters.some(p => p.type === 'path')
      } : null);

      if (!parsed || !parsed.functionDef.parameters.some(p => p.type === 'path')) {
        DebugLogger.autoInsert('No parsed function or no path parameters');
        return;
      }

      // Check if markers already exist in this function call
      const hasMarkers = parsed.parameters.some(p => p.isMarker);
      DebugLogger.autoInsert('Has markers in function call:', hasMarkers);

      // Check if there's already content after the opening paren
      const openParenPos = currentCursorOffset - 1; // Position of the '('
      const textAfterParen = currentLineText.substring(openParenPos + 1).trim();
      const hasContentAfterParen = textAfterParen.length > 0 && !textAfterParen.startsWith(')');

      DebugLogger.autoInsert('Text after paren:', textAfterParen, 'hasContentAfterParen:', hasContentAfterParen);

      // Only insert markers if no markers exist and no content after paren
      if (hasMarkers || hasContentAfterParen) {
        DebugLogger.autoInsert('Conditions not met, not inserting markers');
        return;
      }

      DebugLogger.autoInsert('Conditions met, inserting markers');

      // Build marker text for all parameters
      const insertText = this.buildMarkerTextForAllParameters(parsed.functionDef.parameters);

      // Insert at cursor position
      const insertRange = new monaco.Range(
        currentPosition.lineNumber,
        currentPosition.column,
        currentPosition.lineNumber,
        currentPosition.column
      );

      setSkipNextMarkerDetection(true);
      DebugLogger.autoInsert('Inserting text:', insertText, 'at range:', insertRange);

      model.pushEditOperations(
        [],
        [{
          range: insertRange,
          text: insertText
        }],
        () => null
      );

      // Move cursor back before the closing paren
      delayedMarkerDetection(() => {
        const newPosition = new monaco.Position(
          currentPosition.lineNumber,
          currentPosition.column + insertText.length - 1
        );
        editor.setPosition(newPosition);
        DebugLogger.autoInsert('Moved cursor to:', newPosition);

        // Trigger marker detection
        delayedMarkerDetection(() => {
          MarkerDetectionService.detectAndOpenPicker(
            model,
            onMarkerDetected,
            markerDetectionRef
          );
        });
      });

      debouncedValidation(model.getValue());
    }, TIMING.AUTO_INSERT_DEBOUNCE);
  }

  /**
   * Build marker text for all parameters of a function.
   */
  private static buildMarkerTextForAllParameters(parameters: Array<{ type: string }>): string {
    const pathParams = parameters.filter(p => p.type === 'path');
    const nonPathParams = parameters.filter(p => p.type !== 'path');

    const parts: string[] = [];
    pathParams.forEach(() => {
      parts.push(`"${MARKER_TOKEN}"`);
    });
    nonPathParams.forEach(() => {
      parts.push(MARKER_TOKEN);
    });

    return parts.join(', ') + ')';
  }

  /**
   * Check if the last character typed was ',' and conditions are met for auto-insertion.
   */
  static shouldTriggerCommaInsertion(
    lineText: string,
    cursorOffset: number,
    contentWasAdded: boolean,
    contentWasReplaced: boolean
  ): { shouldInsert: boolean; commaPos: number } {
    const charBeforeCursor = cursorOffset > 0 ? lineText[cursorOffset - 1] : null;

    // Check if there's a comma before the cursor
    let hasCommaBeforeCursor = false;
    let commaPos = cursorOffset - 1;

    if (cursorOffset > 0) {
      // Check if char before cursor is comma
      if (charBeforeCursor === ',') {
        hasCommaBeforeCursor = true;
        commaPos = cursorOffset - 1;
      } else if (charBeforeCursor === ')' && cursorOffset > 1) {
        // Cursor is at closing paren, check if there's a comma before it
        if (lineText[cursorOffset - 2] === ',') {
          hasCommaBeforeCursor = true;
          commaPos = cursorOffset - 2;
        }
      }
    }

    const lastCharIsComma = (contentWasAdded || contentWasReplaced) && hasCommaBeforeCursor;
    DebugLogger.comma('Last char is comma:', lastCharIsComma, {
      contentWasAdded,
      contentWasReplaced,
      cursorOffset,
      lineText,
      charBeforeCursor,
      hasCommaBeforeCursor
    });

    return { shouldInsert: lastCharIsComma, commaPos };
  }

  /**
   * Insert marker after ',' character for the next path parameter.
   */
  static insertMarkerAfterComma(
    model: monaco.editor.ITextModel,
    position: monaco.Position,
    commaPos: number,
    charBeforeCursor: string | null,
    onMarkerDetected: (info: MarkerInfo) => void,
    markerDetectionRef: React.MutableRefObject<boolean>,
    setSkipNextMarkerDetection: (value: boolean) => void,
    debouncedValidation: (content: string) => void
  ): boolean {
    // Determine parse position based on where the comma is
    let parsePosition = position;
    if (charBeforeCursor === ')') {
      // Cursor is at closing paren, comma is right before it
      parsePosition = new monaco.Position(position.lineNumber, commaPos + 1); // Position at comma (1-based)
    } else if (charBeforeCursor === ',') {
      // Cursor is right after comma, parse at the comma position itself
      parsePosition = new monaco.Position(position.lineNumber, commaPos + 1); // Position at comma (1-based)
    }

    DebugLogger.comma('Parsing at position:', parsePosition);
    const parsed = FunctionCallParser.parseAtPosition(model, parsePosition);
    DebugLogger.comma('Parsed result:', parsed ? {
      functionName: parsed.functionName,
      parameters: parsed.parameters.length
    } : null);

    if (!parsed) {
      return false;
    }

    // Get the next path parameter that needs a value
    const nextPathParam = FunctionCallParser.getNextPathParameter(parsed, model);
    DebugLogger.comma('Next path parameter:', nextPathParam);

    if (!nextPathParam) {
      return false;
    }

    // Check if rest of line after the comma is empty/whitespace/closing paren
    const currentLineText = model.getLineContent(position.lineNumber);
    const textAfterComma = currentLineText.substring(commaPos + 1).trim();
    const textAfterCommas = textAfterComma.replace(/^,+\s*/, '').trim();

    // Check if there are already markers in the text after comma
    const hasMarkersAfterComma = textAfterCommas.includes(MARKER_TOKEN);
    const isAfterCommaEmpty = !hasMarkersAfterComma &&
                              (textAfterCommas === '' ||
                               textAfterCommas === ')' ||
                               (textAfterCommas.startsWith(')') && !textAfterCommas.includes(MARKER_TOKEN)));

    DebugLogger.comma('Checking after comma:', {
      commaPos,
      textAfterComma,
      textAfterCommas,
      hasMarkersAfterComma,
      isAfterCommaEmpty
    });

    if (!isAfterCommaEmpty) {
      return false;
    }

    DebugLogger.comma('Inserting marker at column', nextPathParam.range.startColumn,
                'for parameter index', nextPathParam.parameterIndex);
    DebugLogger.comma('Line text before insertion:', currentLineText);
    DebugLogger.comma('Cursor position:', position);

    // Calculate insert position: after the comma, skipping whitespace
    let insertCol = commaPos + 2; // After comma (1-based, so +2 for comma + 1)

    // Skip any whitespace after the comma
    while (insertCol <= currentLineText.length && /\s/.test(currentLineText[insertCol - 1])) {
      insertCol++;
    }

    const insertRange = new monaco.Range(
      position.lineNumber,
      insertCol,
      position.lineNumber,
      insertCol
    );

    DebugLogger.comma('Adjusted insert range to after comma:', insertRange,
                'commaPos (0-based):', commaPos,
                'comma column (1-based):', commaPos + 1,
                'insertCol:', insertCol);

    const insertText = `"${MARKER_TOKEN}"`; // No leading space - range already accounts for it

    DebugLogger.comma('Insert text:', insertText);
    DebugLogger.comma('Final insert range:', insertRange);

    setSkipNextMarkerDetection(true);
    model.pushEditOperations(
      [],
      [{
        range: insertRange,
        text: insertText
      }],
      () => null
    );

    DebugLogger.comma('Line text after insertion:', model.getLineContent(position.lineNumber));

    // Trigger marker detection
    delayedMarkerDetection(() => {
      MarkerDetectionService.detectAndOpenPicker(
        model,
        onMarkerDetected,
        markerDetectionRef
      );
    });

    debouncedValidation(model.getValue());
    return true;
  }
}
