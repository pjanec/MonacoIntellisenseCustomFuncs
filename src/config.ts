import { CustomFunction, FileSystemSnapshot } from './types';

export const LANGUAGE_ID = 'scriban';

export const CUSTOM_FUNCTIONS: CustomFunction[] = [
  {
    name: 'copy',
    doc: 'Copies a file or directory from source to destination. Supports glob patterns for source.',
    signature: 'copy(source, dest)',
    parameters: [
      { name: 'source', type: 'path', pathType: 'both', isSource: true, description: 'Source file or directory path (supports globbing)' },
      { name: 'dest', type: 'path', pathType: 'both', isSource: false, description: 'Destination file or folder (folder must end with /)' }
    ]
  },
  {
    name: 'move',
    doc: 'Moves a file or directory from source to destination. Supports glob patterns for source.',
    signature: 'move(source, dest)',
    parameters: [
      { name: 'source', type: 'path', pathType: 'both', isSource: true, description: 'Source file or directory path (supports globbing)' },
      { name: 'dest', type: 'path', pathType: 'both', isSource: false, description: 'Destination file or folder (folder must end with /)' }
    ]
  },
  {
    name: 'delete',
    doc: 'Deletes a file or directory. Supports glob patterns for bulk deletion.',
    signature: 'delete(path)',
    parameters: [
      { name: 'path', type: 'path', pathType: 'both', description: 'File or directory path to delete' }
    ]
  },
  {
    name: 'read',
    doc: 'Reads the contents of a file as a string.',
    signature: 'read(path)',
    parameters: [
      { name: 'path', type: 'path', pathType: 'file', description: 'File path to read' }
    ]
  },
  {
    name: 'write',
    doc: 'Writes content to a file. Creates the file if it does not exist.',
    signature: 'write(path, content)',
    parameters: [
      { name: 'path', type: 'path', pathType: 'file', description: 'File path to write to' },
      { name: 'content', type: 'string', description: 'Content to write' }
    ]
  }
];

export const FILE_SYSTEM_SNAPSHOT: FileSystemSnapshot = {
  files: [
    'src/index.js',
    'src/app.js',
    'src/components/button.js',
    'src/components/header.js',
    'src/components/footer.js',
    'src/utils/helpers.js',
    'src/utils/validators.js',
    'static/logo.png',
    'static/favicon.ico',
    'static/images/banner.jpg',
    'static/images/hero.png',
    'README.md',
    'package.json',
    'package-lock.json',
    'assets/icons/close.svg',
    'assets/icons/menu.svg',
    'assets/icons/search.svg',
    'assets/styles/main.css',
    'assets/styles/theme.css',
    'docs/usage.md',
    'docs/api.md',
    'docs/guide.md',
    'tests/unit/test-helpers.js',
    'tests/integration/test-api.js',
    'config/webpack.config.js',
    'config/babel.config.js'
  ],
  folders: [
    'src',
    'src/components',
    'src/utils',
    'static',
    'static/images',
    'assets',
    'assets/icons',
    'assets/styles',
    'docs',
    'tests',
    'tests/unit',
    'tests/integration',
    'config'
  ]
};

export const MARKER_TOKEN = '__PARAM__MARKER__';

