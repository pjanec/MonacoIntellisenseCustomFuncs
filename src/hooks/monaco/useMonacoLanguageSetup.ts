import { useEffect } from 'react';
import * as monaco from 'monaco-editor';
import { CUSTOM_FUNCTIONS, MARKER_TOKEN } from '../../config';

/**
 * Hook to set up Scriban language support in Monaco Editor.
 * Registers language, tokenizer, and IntelliSense providers.
 * Only runs once on mount.
 */
export function useMonacoLanguageSetup() {
  useEffect(() => {
    // Register the Scriban language
    monaco.languages.register({ id: 'scriban' });

    // Set up syntax highlighting tokenizer
    monaco.languages.setMonarchTokensProvider('scriban', {
      tokenizer: {
        root: [
          [/[a-zA-Z_]\w*/, {
            cases: {
              '@keywords': 'keyword',
              '@default': 'identifier'
            }
          }],
          [/"[^"]*"/, 'string'],
          [/'[^']*'/, 'string'],
          [/[0-9]+(\.[0-9]+)?/, 'number'],
          [/\/\/.*$/, 'comment'],
          [/\/\*[\s\S]*?\*\//, 'comment'],
          [/[+\-*/%=<>!&|]+/, 'operator'],
          [/[()\[\]{}.,;:]/, 'delimiter'],
          [/\s+/, 'white']
        ]
      },
      keywords: ['for', 'if', 'else', 'end', 'in', 'with', 'while', 'break', 'continue', 'ret', 'func', 'import', 'include', 'with', 'tablerow', 'raw', 'wrap', 'case', 'when', 'default']
    });

    // Register completion provider (auto-complete)
    monaco.languages.registerCompletionItemProvider('scriban', {
      triggerCharacters: ['(', '.'],
      provideCompletionItems: (model, position) => {
        const word = model.getWordUntilPosition(position);
        const range = {
          startLineNumber: position.lineNumber,
          endLineNumber: position.lineNumber,
          startColumn: word.startColumn,
          endColumn: word.endColumn
        };

        const suggestions = CUSTOM_FUNCTIONS.map(func => {
          const pathParams = func.parameters.filter(p => p.type === 'path');
          const nonPathParams = func.parameters.filter(p => p.type !== 'path');

          let insertText = `${func.name}(`;
          const parts: string[] = [];

          pathParams.forEach(() => {
            parts.push(`"${MARKER_TOKEN}"`);
          });

          nonPathParams.forEach(() => {
            parts.push(MARKER_TOKEN);
          });

          insertText += parts.join(', ') + ')';

          return {
            label: func.name,
            kind: monaco.languages.CompletionItemKind.Function,
            documentation: {
              value: func.doc,
              isTrusted: true
            },
            detail: func.signature,
            insertText,
            range,
            insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet
          } as monaco.languages.CompletionItem;
        });

        const builtInKeywords = [
          { label: 'for', kind: monaco.languages.CompletionItemKind.Keyword, insertText: 'for ${1:item} in ${2:collection}\n  $0\nend', insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet },
          { label: 'if', kind: monaco.languages.CompletionItemKind.Keyword, insertText: 'if ${1:condition}\n  $0\nend', insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet },
          { label: 'while', kind: monaco.languages.CompletionItemKind.Keyword, insertText: 'while ${1:condition}\n  $0\nend', insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet }
        ];

        return {
          suggestions: [...suggestions, ...builtInKeywords.map(k => ({ ...k, range }))]
        };
      }
    });

    // Register signature help provider (parameter hints)
    monaco.languages.registerSignatureHelpProvider('scriban', {
      signatureHelpTriggerCharacters: ['(', ','],
      provideSignatureHelp: (model, position) => {
        const textUntilPosition = model.getValueInRange({
          startLineNumber: 1,
          startColumn: 1,
          endLineNumber: position.lineNumber,
          endColumn: position.column
        });

        const match = textUntilPosition.match(/([a-zA-Z_]\w*)\s*\([^)]*$/);
        if (!match) return null;

        const funcName = match[1];
        const func = CUSTOM_FUNCTIONS.find(f => f.name === funcName);
        if (!func) return null;

        const commaCount = (textUntilPosition.match(/,/g) || []).length;
        const activeParameter = commaCount;

        return {
          value: {
            signatures: [{
              label: func.signature,
              documentation: { value: func.doc, isTrusted: true },
              parameters: func.parameters.map(p => ({
                label: p.name,
                documentation: { value: p.description || '', isTrusted: true }
              }))
            }],
            activeSignature: 0,
            activeParameter: Math.min(activeParameter, func.parameters.length - 1)
          },
          dispose: () => {}
        };
      }
    });

    // Register hover provider (documentation on hover)
    monaco.languages.registerHoverProvider('scriban', {
      provideHover: (model, position) => {
        const word = model.getWordAtPosition(position);
        if (!word) return null;

        const func = CUSTOM_FUNCTIONS.find(f => f.name === word.word);
        if (!func) return null;

        return {
          range: new monaco.Range(
            position.lineNumber,
            word.startColumn,
            position.lineNumber,
            word.endColumn
          ),
          contents: [
            { value: `**${func.name}**`, isTrusted: true },
            { value: func.signature, isTrusted: true },
            { value: func.doc, isTrusted: true }
          ]
        };
      }
    });
  }, []); // Empty dependency array - only run once on mount
}
