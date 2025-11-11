# Scriban IntelliSense Specification

## Overview

This document describes the specifications for the Scriban IntelliSense feature implemented in the Monaco Editor. The feature provides intelligent code completion, syntax checking, parameter pickers, and contextual assistance for Scriban scripts in script mode (without `{{ }}` templating braces).

## Core Features

### 1. Syntax Highlighting
- **Language**: Scriban script mode (direct function calls without templating braces)
- **Token Types**: Keywords, identifiers, strings, numbers, comments, operators, delimiters
- **Keywords**: `for`, `if`, `else`, `end`, `in`, `with`, `while`, `break`, `continue`, `ret`, `func`, `import`, `include`, `with`, `tablerow`, `raw`, `wrap`, `case`, `when`, `default`

### 2. Auto-Completion
- **Trigger Characters**: `(`, `.`
- **Function Suggestions**: Custom functions appear in completion list with:
  - Function name
  - Signature
  - Documentation
  - Parameter placeholders (markers) for path parameters
- **Keyword Suggestions**: Built-in Scriban keywords with snippet templates
- **Insertion Behavior**: 
  - Path parameters are inserted as `"__PARAM__MARKER__"` (quoted)
  - Non-path parameters are inserted as `__PARAM__MARKER__` (unquoted)
  - All parameters are comma-separated

### 3. Signature Help
- **Trigger Characters**: `(`, `,`
- **Display**: Function signature with active parameter highlighting
- **Information**: Parameter names and descriptions
- **Active Parameter**: Automatically determined by comma count

### 4. Hover Documentation
- **Trigger**: Mouse hover over function names
- **Content**: 
  - Function name (bold)
  - Function signature
  - Full documentation

### 5. Syntax Validation
- **Backend**: Mock backend emulates Scriban parsing
- **Debounce**: 350ms delay before validation
- **Diagnostics**: 
  - Errors and warnings displayed as Monaco markers
  - Marker token detection (`__PARAM__MARKER__`)
  - Unknown function detection
  - Unclosed string detection
  - Unclosed comment detection

## Custom Functions

### Function Definitions

All custom functions support the following parameter metadata:

- **Type**: `'path' | 'string' | 'number' | 'boolean'`
- **Optional**: Boolean flag
- **Description**: Human-readable description
- **pathType**: For path parameters: `'file' | 'folder' | 'both'`
- **isSource**: Boolean flag indicating if parameter is a source path (supports globbing/whole folder option)

### Available Functions

#### `copy(source, dest)`
- **Description**: Copies a file or directory from source to destination. Supports glob patterns for source.
- **Parameters**:
  - `source` (path, both, isSource: true): Source file or directory path (supports globbing)
  - `dest` (path, both, isSource: false): Destination file or folder (folder must end with `/`)

#### `move(source, dest)`
- **Description**: Moves a file or directory from source to destination. Supports glob patterns for source.
- **Parameters**:
  - `source` (path, both, isSource: true): Source file or directory path (supports globbing)
  - `dest` (path, both, isSource: false): Destination file or folder (folder must end with `/`)

#### `delete(path)`
- **Description**: Deletes a file or directory. Supports glob patterns for bulk deletion.
- **Parameters**:
  - `path` (path, both): File or directory path to delete

#### `read(path)`
- **Description**: Reads the contents of a file as a string.
- **Parameters**:
  - `path` (path, file): File path to read

#### `write(path, content)`
- **Description**: Writes content to a file. Creates the file if it does not exist.
- **Parameters**:
  - `path` (path, file): File path to write to
  - `content` (string): Content to write

## Parameter Picker

### Trigger Conditions

The parameter picker opens automatically in the following scenarios:

1. **Function Completion**: When a function is selected from the auto-completion list, placeholders are inserted and the picker opens for the first path parameter
2. **Opening Parenthesis**: When user types `functionName(`, placeholders are inserted and picker opens for the first path parameter (after debounce delay)
3. **Comma**: When user types a comma after a parameter, if more path parameters are available, the picker opens for the next path parameter
4. **Click**: When user clicks on an existing path parameter
5. **Ctrl+Space**: When user presses `Ctrl+Space` (or `Cmd+Space` on Mac) while cursor is:
   - Within an existing path parameter, OR
   - In a function call that needs a new path parameter (e.g., empty brackets `copy()`)

### Picker Behavior

#### Source Parameters (`isSource: true`)
- **Whole Folder Checkbox**: 
  - Visible only for source parameters
  - Only shown if `pathType !== 'file'`
  - Pre-checked if:
    - Current parameter value ends with `/`, OR
    - `pathType === 'folder'`
  - When checked: Only folders are shown in the list
  - When unchecked: Both files and folders are shown
- **Folder Handling**: When a folder is selected, a trailing slash `/` is automatically added (indicates copying whole folder as subfolder)

#### Destination Parameters (`isSource: false`)
- **No Whole Folder Checkbox**: Checkbox is not shown for destination parameters
- **Folder Handling**: When a folder is selected, a trailing slash `/` is automatically added (required for folder destinations)
- **Info Message**: Displays hint about automatic trailing slash

#### List Display
- **Sorting**: 
  - Folders appear before files
  - Each group (folders, files) is sorted alphabetically
- **Icons**: 
  - 📁 for folders
  - 📄 for files
- **Filtering**: Real-time filtering as user types in search field
- **Selection**: 
  - Current parameter value is pre-selected if it exists in the list
  - Arrow keys (Up/Down) navigate the list
  - Focus remains in the filter input field during navigation
  - Enter key selects the highlighted item

#### Keyboard Shortcuts
- **Escape**: Closes picker without changing code, restores cursor position
- **Enter**: Selects the highlighted item
- **Arrow Up/Down**: Navigates the list (focus stays in filter field)
- **Typing**: Filters the list

### Picker Positioning
- Positioned below the parameter in the editor
- Uses Monaco's `getScrolledVisiblePosition` for accurate placement
- Floats above editor content

## Auto-Insertion Behavior

### Opening Parenthesis `(`
- **Trigger**: User types `functionName(`
- **Debounce**: 200ms delay to avoid triggering during rapid typing
- **Condition**: Only triggers if:
  - Function has path parameters
  - No markers already exist in the function call
  - No content after the opening parenthesis (or just whitespace/closing paren)
- **Action**: Inserts placeholders for all parameters, opens picker for first path parameter

### Comma `,`
- **Trigger**: User types comma after a parameter
- **Condition**: Only triggers if:
  - Function supports more parameters than currently present
  - Next parameter is a path parameter
  - Text after comma is empty, whitespace, or closing parenthesis
- **Action**: Inserts marker for next path parameter after comma (with leading space), opens picker

### Manual Typing
- **Behavior**: Picker closes when user types manually (not from auto-insertion)
- **Detection**: Distinguishes between auto-insertion and manual typing

## File System Integration

### File System Snapshot
The application maintains a snapshot of the file system structure:

- **Files**: Array of file paths
- **Folders**: Array of folder paths

### Path Filtering
- **File-only parameters** (`pathType: 'file'`): Only files are shown
- **Folder-only parameters** (`pathType: 'folder'`): Only folders are shown
- **Both** (`pathType: 'both'` or undefined): Both files and folders are shown (subject to "Whole folder" checkbox for source parameters)

## Technical Implementation

### Marker Token
- **Token**: `__PARAM__MARKER__`
- **Format**: Quoted for path parameters: `"__PARAM__MARKER__"`
- **Purpose**: Temporary placeholder that triggers parameter picker
- **Detection**: Automatically detected after insertion with 50ms delay
- **Replacement**: Replaced with actual value when user selects from picker

### Timing Constants
- **MARKER_DETECTION_DELAY**: 50ms - Delay before detecting marker after insertion
- **PICKER_OPEN_DELAY**: 100ms - Delay before opening picker after marker detection
- **SEQUENTIAL_PICKER_DELAY**: 300ms - Delay before opening next picker in sequential parameter selection
- **VALIDATION_DEBOUNCE**: 350ms - Debounce interval for validation calls
- **CLICK_PROCESSING_DELAY**: 100ms - Delay before processing click events
- **AUTO_INSERT_DEBOUNCE**: 200ms - Debounce delay before auto-inserting markers

### Range Handling
- **Quoted Parameters**: Range includes quotes for replacement operations
- **Content Range**: For editing, range is content between quotes
- **Replacement Range**: For replacement, range includes quotes
- **Consistency**: All range calculations use centralized `RangeUtils` service

### Cursor Management
- **After Selection**: Cursor is positioned after the inserted parameter value
- **On Cancel (ESC)**: Cursor position is restored to original position before picker opened
- **Deselection**: When ESC is pressed, any selected text is deselected and cursor becomes visible

## User Experience Guidelines

### Non-Intrusive Behavior
- Picker only opens when explicitly triggered (completion, `(`, `,`, click, `Ctrl+Space`)
- Does not open during normal typing or deletion
- Closes automatically when user types manually

### Accessibility
- Keyboard navigation fully supported
- Focus management ensures filter field is always accessible
- Clear visual feedback for selected items

### Performance
- Debouncing prevents excessive validation calls
- Efficient filtering of file system items
- Memoized calculations for filtered lists

## Error Handling

### Parser Errors
- Graceful handling of malformed function calls
- Fallback to direct model search if parser fails
- Range validation before replacement operations

### Edge Cases
- Empty function calls: `copy()`
- Trailing commas: `copy("first",)`
- Nested function calls
- Quoted strings with commas
- Escaped quotes in strings

## Future Enhancements (Not Implemented)

- Real backend integration for Scriban parsing
- Dynamic file system updates
- Custom function discovery from backend
- Multi-file support
- Variable completion
- Template completion
- Error recovery suggestions

