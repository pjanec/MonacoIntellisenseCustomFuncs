import * as monaco from 'monaco-editor';

/**
 * Scriban Language Definition for Monaco Editor
 *
 * Defines syntax highlighting, auto-indentation, and other language features
 * for the Scriban templating language.
 */

export const SCRIBAN_LANGUAGE_ID = 'scriban';

export function registerScribanLanguage() {
  // Register the language
  monaco.languages.register({ id: SCRIBAN_LANGUAGE_ID });

  // Set the monarch tokenizer for syntax highlighting
  monaco.languages.setMonarchTokensProvider(SCRIBAN_LANGUAGE_ID, {
    // Keywords
    keywords: [
      'for', 'in', 'end', 'if', 'else', 'elsif', 'case', 'when',
      'while', 'break', 'continue', 'func', 'ret', 'capture',
      'readonly', 'import', 'with', 'wrap', 'include', 'tablerow'
    ],

    // Operators
    operators: [
      '=', '>', '<', '!', '~', '?', ':', '==', '<=', '>=', '!=',
      '&&', '||', '++', '--', '+', '-', '*', '/', '&', '|', '^', '%',
      '<<', '>>', '>>>', '+=', '-=', '*=', '/=', '&=', '|=', '^=',
      '%=', '<<=', '>>=', '>>>='
    ],

    // Delimiters and special characters
    symbols: /[=><!~?:&|+\-*\/\^%]+/,
    escapes: /\\(?:[abfnrtv\\"']|x[0-9A-Fa-f]{1,4}|u[0-9A-Fa-f]{4}|U[0-9A-Fa-f]{8})/,

    // Tokenizer rules
    tokenizer: {
      root: [
        // Comments
        [/#.*$/, 'comment'],

        // Keywords
        [/[a-z_][\w$]*/, {
          cases: {
            '@keywords': 'keyword',
            '@default': 'identifier'
          }
        }],

        // Strings
        [/"([^"\\]|\\.)*$/, 'string.invalid'],
        [/'([^'\\]|\\.)*$/, 'string.invalid'],
        [/"/, 'string', '@string_double'],
        [/'/, 'string', '@string_single'],

        // Numbers
        [/\d+\.\d+([eE][\-+]?\d+)?/, 'number.float'],
        [/\d+/, 'number'],

        // Operators
        [/@symbols/, {
          cases: {
            '@operators': 'operator',
            '@default': ''
          }
        }],

        // Delimiters
        [/[{}()\[\]]/, '@brackets'],
        [/,/, 'delimiter.comma'],
        [/\./, 'delimiter.dot'],
        [/;/, 'delimiter.semicolon'],

        // Whitespace
        [/\s+/, 'white'],
      ],

      codeBlock: [
        [/%?\}\}/, { token: 'delimiter.bracket', next: '@pop' }],
        [/[a-z_$][\w$]*/, {
          cases: {
            '@keywords': 'keyword',
            '@default': 'identifier'
          }
        }],
        [/"([^"\\]|\\.)*$/, 'string.invalid'],
        [/'([^'\\]|\\.)*$/, 'string.invalid'],
        [/"/, 'string', '@string_double'],
        [/'/, 'string', '@string_single'],
        [/\d+\.\d+([eE][\-+]?\d+)?/, 'number.float'],
        [/\d+/, 'number'],
        [/@symbols/, {
          cases: {
            '@operators': 'operator',
            '@default': ''
          }
        }],
        [/[{}()\[\]]/, '@brackets'],
        [/,/, 'delimiter.comma'],
        [/\./, 'delimiter.dot'],
        [/\s+/, 'white'],
      ],

      rawCodeBlock: [
        [/%\}\}/, { token: 'delimiter.bracket', next: '@pop' }],
        [/./, 'string.raw'],
      ],

      outputBlock: [
        [/\}\}/, { token: 'delimiter.bracket', next: '@pop' }],
        [/[a-z_$][\w$]*/, 'identifier'],
        [/"([^"\\]|\\.)*$/, 'string.invalid'],
        [/'([^'\\]|\\.)*$/, 'string.invalid'],
        [/"/, 'string', '@string_double'],
        [/'/, 'string', '@string_single'],
        [/\d+\.\d+([eE][\-+]?\d+)?/, 'number.float'],
        [/\d+/, 'number'],
        [/@symbols/, {
          cases: {
            '@operators': 'operator',
            '@default': ''
          }
        }],
        [/[{}()\[\]]/, '@brackets'],
        [/,/, 'delimiter.comma'],
        [/\./, 'delimiter.dot'],
        [/\s+/, 'white'],
      ],

      escapeBlock: [
        [/\\\}\}/, { token: 'delimiter.bracket', next: '@pop' }],
        [/./, 'string.escape'],
      ],

      string_double: [
        [/[^\\"]+/, 'string'],
        [/@escapes/, 'string.escape'],
        [/\\./, 'string.escape.invalid'],
        [/"/, 'string', '@pop']
      ],

      string_single: [
        [/[^\\']+/, 'string'],
        [/@escapes/, 'string.escape'],
        [/\\./, 'string.escape.invalid'],
        [/'/, 'string', '@pop']
      ],
    },
  });

  // Set language configuration for auto-indentation, brackets, etc.
  monaco.languages.setLanguageConfiguration(SCRIBAN_LANGUAGE_ID, {
    comments: {
      lineComment: '//',
      blockComment: ['/*', '*/']
    },
    brackets: [
      ['{{', '}}'],
      ['{', '}'],
      ['[', ']'],
      ['(', ')']
    ],
    autoClosingPairs: [
      { open: '{{', close: '}}' },
      { open: '{', close: '}' },
      { open: '[', close: ']' },
      { open: '(', close: ')' },
      { open: '"', close: '"' },
      { open: "'", close: "'" }
    ],
    surroundingPairs: [
      { open: '{{', close: '}}' },
      { open: '{', close: '}' },
      { open: '[', close: ']' },
      { open: '(', close: ')' },
      { open: '"', close: '"' },
      { open: "'", close: "'" }
    ],
    folding: {
      markers: {
        start: new RegExp('^\\s*//\\s*#?region\\b'),
        end: new RegExp('^\\s*//\\s*#?endregion\\b')
      }
    }
  });

  console.log('Scriban language registered successfully');
}
