import React, { useState, useEffect, useRef, useMemo } from 'react';
import * as monaco from 'monaco-editor';
import { FILE_SYSTEM_SNAPSHOT } from '../config';
import { FunctionParameter } from '../types';
import { delayedCallback } from '../utils/timingUtils';
import './ParameterPicker.css';

// Extend window type to include monaco
declare global {
  interface Window {
    monaco?: typeof monaco;
  }
}

interface ParameterPickerProps {
  position: { x: number; y: number };
  parameter: FunctionParameter;
  parameterIndex: number;
  isSource: boolean;
  currentValue?: string | null; // Current parameter value (without quotes)
  autoFocus?: boolean; // Whether to auto-focus the search input
  onSelect: (value: string) => void;
  onCancel: () => void;
}

export const ParameterPicker: React.FC<ParameterPickerProps> = ({
  position,
  parameter,
  parameterIndex: _parameterIndex,
  isSource,
  currentValue,
  autoFocus: _autoFocus = false, // Keep for backward compatibility but always focus
  onSelect,
  onCancel
}) => {
  const [searchTerm, setSearchTerm] = useState('');
  // Pre-check whole folder if: current value ends with /, or pathType is 'folder'
  const initialWholeFolder = currentValue?.endsWith('/') || parameter.pathType === 'folder';
  const [wholeFolder, setWholeFolder] = useState(initialWholeFolder);
  const containerRef = useRef<HTMLDivElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);
  
  // Filter items based on pathType and wholeFolder - memoize to prevent unnecessary recalculations
  const baseItems = useMemo(() => {
    if (parameter.pathType === 'file') {
      return FILE_SYSTEM_SNAPSHOT.files;
    } else if (parameter.pathType === 'folder') {
      return FILE_SYSTEM_SNAPSHOT.folders;
    } else {
      // 'both' or undefined - show both files and folders
      return wholeFolder 
        ? FILE_SYSTEM_SNAPSHOT.folders 
        : [...FILE_SYSTEM_SNAPSHOT.files, ...FILE_SYSTEM_SNAPSHOT.folders];
    }
  }, [parameter.pathType, wholeFolder]);
  
  // Sort items: folders first (sorted), then files (sorted), then filter - memoize to prevent unnecessary recalculations
  const filteredItems = useMemo(() => {
    // Separate folders and files
    const folders: string[] = [];
    const files: string[] = [];
    
    for (const item of baseItems) {
      if (FILE_SYSTEM_SNAPSHOT.folders.includes(item)) {
        folders.push(item);
      } else {
        files.push(item);
      }
    }
    
    // Sort each group alphabetically
    folders.sort((a, b) => a.localeCompare(b));
    files.sort((a, b) => a.localeCompare(b));
    
    // Combine: folders first, then files
    const sortedBaseItems = [...folders, ...files];
    
    // Filter by search term
    return sortedBaseItems.filter(item =>
      item.toLowerCase().includes(searchTerm.toLowerCase())
    );
  }, [baseItems, searchTerm]);

  // Define handleSelect first so it can be used in useEffect
  const handleSelect = React.useCallback((item: string) => {
    let value = item;
    const isFolder = FILE_SYSTEM_SNAPSHOT.folders.includes(item);
    
    if (isSource) {
      // Source: if folder, add trailing slash (means copy whole folder)
      if (isFolder) {
        value = `${item}/`;
      }
      // Files stay as-is
    } else {
      // Destination: folders must end with /
      if (isFolder) {
        value = `${item}/`;
      }
      // Files stay as-is
    }
    
    onSelect(`"${value}"`);
  }, [isSource, onSelect]);

  // Initialize selected index based on current value
  const [selectedIndex, setSelectedIndex] = useState(() => {
    if (!currentValue) return 0;
    // Remove trailing slash for comparison
    const valueWithoutSlash = currentValue.endsWith('/') ? currentValue.slice(0, -1) : currentValue;
    // Use same sorting logic as filteredItems: folders first, then files
    const folders: string[] = [];
    const files: string[] = [];
    for (const item of baseItems) {
      if (FILE_SYSTEM_SNAPSHOT.folders.includes(item)) {
        folders.push(item);
      } else {
        files.push(item);
      }
    }
    folders.sort((a, b) => a.localeCompare(b));
    files.sort((a, b) => a.localeCompare(b));
    const sortedBaseItems = [...folders, ...files];
    const index = sortedBaseItems.findIndex(item => item === valueWithoutSlash);
    return index >= 0 ? index : 0;
  });
  
  // Track if we're navigating with arrow keys (to prevent useEffect from resetting selection)
  const isNavigatingRef = React.useRef(false);
  
  // Update selected index when picker opens (initial mount) or when current value changes
  useEffect(() => {
    if (!isNavigatingRef.current && filteredItems.length > 0) {
      if (!currentValue) {
        setSelectedIndex(0);
        return;
      }
      // Remove trailing slash for comparison
      const valueWithoutSlash = currentValue.endsWith('/') ? currentValue.slice(0, -1) : currentValue;
      const index = filteredItems.findIndex(item => item === valueWithoutSlash);
      setSelectedIndex(index >= 0 ? index : 0);
    }
  }, [currentValue, filteredItems]); // Update when currentValue changes (picker opens with new value)
  
  // Reset selection when search term changes (user is typing)
  // But if current value matches, keep it selected
  useEffect(() => {
    if (isNavigatingRef.current) {
      // Don't reset selection if we're navigating with arrow keys
      isNavigatingRef.current = false;
      return;
    }
    
    if (searchTerm === '') {
      // Search cleared - try to restore selection to current value
      if (!currentValue) {
        setSelectedIndex(0);
        return;
      }
      const valueWithoutSlash = currentValue.endsWith('/') ? currentValue.slice(0, -1) : currentValue;
      const index = filteredItems.findIndex(item => item === valueWithoutSlash);
      setSelectedIndex(index >= 0 ? index : 0);
    } else {
      // User is searching - try to find current value in filtered results
      if (!currentValue) {
        setSelectedIndex(0);
        return;
      }
      const valueWithoutSlash = currentValue.endsWith('/') ? currentValue.slice(0, -1) : currentValue;
      const index = filteredItems.findIndex(item => item === valueWithoutSlash);
      if (index >= 0) {
        setSelectedIndex(index);
      } else {
        // Current value not in filtered results, reset to first item
        setSelectedIndex(0);
      }
    }
  }, [searchTerm, currentValue, filteredItems]);

  // Auto-focus search input when picker opens and scroll selected item into view
  useEffect(() => {
    // Always focus the input field when picker opens (for both click and auto-open)
    if (searchInputRef.current) {
      searchInputRef.current.focus();
    }
    // Scroll selected item into view when picker opens
    if (listRef.current && selectedIndex >= 0 && selectedIndex < filteredItems.length) {
      const selectedElement = listRef.current.children[selectedIndex] as HTMLElement;
      if (selectedElement) {
        // Use delayedCallback to ensure DOM is ready
        delayedCallback(() => {
          selectedElement.scrollIntoView({ block: 'nearest', behavior: 'auto' });
        }, 0);
      }
    }
  }, [selectedIndex, filteredItems.length]); // Run when picker opens or selection changes

  const handleInputKeyDown = React.useCallback((e: React.KeyboardEvent<HTMLInputElement>) => {
    // Handle arrow keys and Enter even when input has focus
    if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation();
      onCancel();
      return;
    }
    
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      e.stopPropagation();
      if (filteredItems.length === 0) return;
      // Mark that we're navigating to prevent useEffect from resetting selection
      isNavigatingRef.current = true;
      setSelectedIndex(prev => {
        const newIndex = Math.min(prev + 1, filteredItems.length - 1);
        return newIndex;
      });
      // Keep focus in input field
      delayedCallback(() => {
        if (searchInputRef.current) {
          searchInputRef.current.focus();
        }
      }, 0);
      return;
    }

    if (e.key === 'ArrowUp') {
      e.preventDefault();
      e.stopPropagation();
      if (filteredItems.length === 0) return;
      // Mark that we're navigating to prevent useEffect from resetting selection
      isNavigatingRef.current = true;
      setSelectedIndex(prev => {
        const newIndex = Math.max(prev - 1, 0);
        return newIndex;
      });
      // Keep focus in input field
      delayedCallback(() => {
        if (searchInputRef.current) {
          searchInputRef.current.focus();
        }
      }, 0);
      return;
    }
    
    if (e.key === 'Enter') {
      // If there are no filtered items, close the picker and insert newline in editor
      if (filteredItems.length === 0) {
        e.preventDefault();
        e.stopPropagation();
        // Get cursor position before closing picker
        const editor = window.monaco?.editor?.getEditors()[0];
        let cursorPosition: monaco.Position | null = null;
        if (editor) {
          cursorPosition = editor.getPosition();
        }
        onCancel();
        // Return focus to editor and insert newline after a delay to allow cursor restoration
        delayedCallback(() => {
          const editor = window.monaco?.editor?.getEditors()[0];
          if (editor) {
            editor.focus();
            // Use the saved cursor position, or get current position
            const position = cursorPosition || editor.getPosition();
            if (position) {
              const model = editor.getModel();
              if (model) {
                // Get the end of the line to insert newline there
                const lineContent = model.getLineContent(position.lineNumber);
                const endOfLine = lineContent.length + 1;
                editor.executeEdits('insert-newline', [{
                  range: new monaco.Range(position.lineNumber, endOfLine, position.lineNumber, endOfLine),
                  text: '\n'
                }]);
                // Move cursor to new line
                editor.setPosition(new monaco.Position(position.lineNumber + 1, 1));
              }
            }
          }
        }, 50);
        return;
      }
      
      // If there are items, select the selected one
      if (filteredItems[selectedIndex]) {
        e.preventDefault();
        e.stopPropagation();
        handleSelect(filteredItems[selectedIndex]);
      }
      return;
    }
  }, [filteredItems, selectedIndex, handleSelect, onCancel]);

  useEffect(() => {
    // Also handle Escape on document level
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onCancel();
      }
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onCancel]);

  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        onCancel();
      }
    };

    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [onCancel]);

  // Scroll selected item into view when selection changes (for arrow key navigation)
  useEffect(() => {
    if (listRef.current && selectedIndex >= 0 && selectedIndex < filteredItems.length) {
      const selectedElement = listRef.current.children[selectedIndex] as HTMLElement;
      if (selectedElement) {
        selectedElement.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
      }
    }
  }, [selectedIndex, filteredItems.length]);

  return (
    <div
      ref={containerRef}
      className="parameter-picker"
      style={{
        left: `${position.x}px`,
        top: `${position.y}px`
      }}
    >
      <div className="parameter-picker-header">
        <div className="parameter-picker-title">
          {parameter.name} {parameter.description && `- ${parameter.description}`}
        </div>
        <input
          ref={searchInputRef}
          type="text"
          className="parameter-picker-search"
          placeholder="Filter files and folders..."
          value={searchTerm}
          onChange={(e) => {
            setSearchTerm(e.target.value);
            // Don't reset selection here - let the useEffect handle it
          }}
          onKeyDown={handleInputKeyDown}
        />
      </div>
      
      {isSource && parameter.pathType !== 'file' && (
        <div className="parameter-picker-options">
          <label className="parameter-picker-checkbox">
            <input
              type="checkbox"
              checked={wholeFolder}
              onChange={(e) => setWholeFolder(e.target.checked)}
            />
            <span>Whole folder</span>
          </label>
        </div>
      )}
      
      {!isSource && (
        <div className="parameter-picker-info">
          <span className="parameter-picker-info-text">
            💡 Folders will automatically end with <code>/</code> to indicate folder destination
          </span>
        </div>
      )}
      
      {isSource && (
        <div className="parameter-picker-info">
          <span className="parameter-picker-info-text">
            💡 Folders will end with <code>/</code> (copies whole folder as subfolder)
          </span>
        </div>
      )}

      <div ref={listRef} className="parameter-picker-list">
        {filteredItems.length === 0 ? (
          <div className="parameter-picker-item parameter-picker-item-empty">
            No matches found
          </div>
        ) : (
          filteredItems.map((item, index) => (
            <div
              key={item}
              className={`parameter-picker-item ${
                index === selectedIndex ? 'parameter-picker-item-selected' : ''
              }`}
              onClick={() => handleSelect(item)}
              onMouseEnter={() => setSelectedIndex(index)}
            >
              <span className="parameter-picker-item-icon">
                {FILE_SYSTEM_SNAPSHOT.folders.includes(item) ? '📁' : '📄'}
              </span>
              <span className="parameter-picker-item-text">{item}</span>
            </div>
          ))
        )}
      </div>

      <div className="parameter-picker-footer">
        <span className="parameter-picker-hint">
          {wholeFolder ? 'Showing folders only' : 'Press Enter to select'}
        </span>
      </div>
    </div>
  );
};

