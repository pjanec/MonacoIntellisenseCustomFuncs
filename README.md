# Scriban IntelliSense Demo

A demonstration of Monaco Editor with full IntelliSense support for Scriban scripts in **script mode** (without template braces `{{ }}`), including custom function completion, parameter pickers, syntax validation, and file system integration.

## Features

- ✅ **Syntax Highlighting**: Custom Monarch tokenizer for Scriban syntax
- ✅ **Auto-completion**: IntelliSense for custom functions (copy, move, delete, read, write)
- ✅ **Parameter Pickers**: Rich dialog-based parameter selection with file system navigation
- ✅ **File System Integration**: Browse available files and folders from a snapshot
- ✅ **Globbing Support**: Enable glob patterns for file operations
- ✅ **Syntax Validation**: Real-time syntax checking with error/warning markers
- ✅ **Signature Help**: Parameter hints when typing function calls
- ✅ **Hover Documentation**: Function documentation on hover

## Getting Started

### Prerequisites

- Node.js 18+ and npm

### Installation

```bash
npm install
```

### Development

```bash
npm run dev
```

The app will open at `http://localhost:3000`

### Build

```bash
npm run build
```

## Usage

1. Type a function name (e.g., `copy`, `move`, `delete`)
2. Press `Ctrl+Space` or wait for auto-completion suggestions
3. Select a function to insert it with a parameter marker
4. A parameter picker dialog will appear automatically
5. Browse files, enable globbing if needed, and select a path
6. The marker will be replaced with your selection

## Architecture

- **Monaco Editor**: Embedded code editor with custom language support
- **Custom Language**: Scriban language registration with syntax highlighting
- **Completion Provider**: Custom IntelliSense for functions
- **Parameter Picker**: React component for rich parameter selection
- **Mock Backend**: Emulated Scriban parser for syntax validation
- **File System Snapshot**: Static snapshot of available files/folders

## Customization

### Adding New Functions

Edit `src/config.ts` to add new custom functions:

```typescript
export const CUSTOM_FUNCTIONS: CustomFunction[] = [
  // Add your functions here
];
```

### Updating File System Snapshot

Edit `FILE_SYSTEM_SNAPSHOT` in `src/config.ts`:

```typescript
export const FILE_SYSTEM_SNAPSHOT: FileSystemSnapshot = {
  files: [...],
  folders: [...]
};
```

### Backend Integration

Replace `src/services/mockBackend.ts` with your actual backend API:

```typescript
export async function validateScriban(script: string): Promise<Diagnostic[]> {
  const response = await fetch('/api/validate-scriban', {
    method: 'POST',
    body: JSON.stringify({ script })
  });
  return response.json();
}
```

## Technologies

- React 18
- TypeScript
- Monaco Editor
- Vite

