import { Diagnostic } from '../types';

export class ScribanParser {
  static validate(script: string): Diagnostic[] {
    const diagnostics: Diagnostic[] = [];
    const lines = script.split('\n');
    const validFunctions = ['copy', 'move', 'delete', 'read', 'write', 'for', 'if', 'end', 'while', 'break', 'continue', 'ret', 'func', 'import', 'include', 'with', 'tablerow', 'raw', 'wrap', 'case', 'when', 'default'];

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      const lineNum = i + 1;

      const markerMatch = line.match(/__PARAM__MARKER__/);
      if (markerMatch) {
        diagnostics.push({
          startLine: lineNum,
          startCol: markerMatch.index! + 1,
          endLine: lineNum,
          endCol: markerMatch.index! + markerMatch[0].length + 1,
          message: 'Parameter marker detected - please select a value',
          severity: 'warning'
        });
      }

      const functionMatch = line.match(/([a-zA-Z_]\w*)\s*\(/);
      if (functionMatch) {
        const funcName = functionMatch[1];
        if (!validFunctions.includes(funcName) && !funcName.startsWith('__')) {
          diagnostics.push({
            startLine: lineNum,
            startCol: functionMatch.index! + 1,
            endLine: lineNum,
            endCol: functionMatch.index! + funcName.length + 1,
            message: `Unknown function "${funcName}"`,
            severity: 'warning'
          });
        }
      }

      const unclosedString = line.match(/["'][^"']*$/);
      if (unclosedString) {
        diagnostics.push({
          startLine: lineNum,
          startCol: unclosedString.index! + 1,
          endLine: lineNum,
          endCol: line.length + 1,
          message: 'Unclosed string literal',
          severity: 'error'
        });
      }

      const unclosedComment = line.match(/\/\*[^*]*$/);
      if (unclosedComment) {
        diagnostics.push({
          startLine: lineNum,
          startCol: unclosedComment.index! + 1,
          endLine: lineNum,
          endCol: line.length + 1,
          message: 'Unclosed block comment',
          severity: 'warning'
        });
      }
    }

    return diagnostics;
  }
}
