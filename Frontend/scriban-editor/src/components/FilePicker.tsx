import React, { useState, useEffect, useRef } from 'react';
import { HubConnection } from '@microsoft/signalr';
import './FilePicker.css';

export interface FilePickerProps {
  hubConnection: HubConnection | null;
  functionName: string;
  parameterIndex: number;
  currentValue: string | null;
  position: { x: number; y: number };
  onSelect: (value: string) => void;
  onCancel: () => void;
}

export const FilePicker: React.FC<FilePickerProps> = ({
  hubConnection,
  functionName,
  parameterIndex,
  currentValue,
  position,
  onSelect,
  onCancel,
}) => {
  const [suggestions, setSuggestions] = useState<string[]>([]);
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [isLoading, setIsLoading] = useState(true);
  const [mouseHasMoved, setMouseHasMoved] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  // Focus the container when mounted
  useEffect(() => {
    if (containerRef.current) {
      containerRef.current.focus();
    }
  }, []);

  // Track mouse movement to prevent accidental hover selection
  useEffect(() => {
    const handleMouseMove = () => {
      setMouseHasMoved(true);
    };

    // Add listener after a tiny delay to ignore the initial mouse position
    const timer = setTimeout(() => {
      document.addEventListener('mousemove', handleMouseMove);
    }, 50);

    return () => {
      clearTimeout(timer);
      document.removeEventListener('mousemove', handleMouseMove);
    };
  }, []);

  // Fetch path suggestions from the server
  useEffect(() => {
    if (!hubConnection || !functionName) return;

    setIsLoading(true);
    hubConnection
      .invoke('GetPathSuggestions', functionName, parameterIndex, currentValue || '')
      .then((paths: string[]) => {
        setSuggestions(paths || []);
        setIsLoading(false);
      })
      .catch((err) => {
        console.error('Failed to fetch path suggestions:', err);
        setSuggestions([]);
        setIsLoading(false);
      });
  }, [hubConnection, functionName, parameterIndex, currentValue]);

  // Pre-select the current value if it exists in suggestions
  useEffect(() => {
    if (!isLoading && currentValue && suggestions.length > 0) {
      const index = suggestions.findIndex(
        (path) => path.toLowerCase() === currentValue.toLowerCase()
      );
      if (index >= 0) {
        setSelectedIndex(index);
      }
    }
  }, [suggestions, currentValue, isLoading]);

  // Handle keyboard navigation
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        onCancel();
      } else if (e.key === 'ArrowDown') {
        e.preventDefault();
        setSelectedIndex((prev) => Math.min(prev + 1, suggestions.length - 1));
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        setSelectedIndex((prev) => Math.max(prev - 1, 0));
      } else if (e.key === 'Enter') {
        e.preventDefault();
        if (suggestions[selectedIndex]) {
          onSelect(suggestions[selectedIndex]);
        }
      }
    };

    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [suggestions, selectedIndex, onSelect, onCancel]);

  // Close on outside click
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        onCancel();
      }
    };

    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [onCancel]);

  return (
    <div
      ref={containerRef}
      className="file-picker"
      tabIndex={0}
      style={{
        position: 'fixed',
        left: `${position.x}px`,
        top: `${position.y}px`,
        outline: 'none',
      }}
    >
      <div className="picker-header">
        <span>Select File Path</span>
        <span className="picker-hint">↑↓ Navigate • Enter Select • Esc Cancel</span>
      </div>

      {isLoading ? (
        <div className="picker-loading">Loading paths...</div>
      ) : suggestions.length === 0 ? (
        <div className="picker-empty">No paths available</div>
      ) : (
        <ul className="picker-list">
          {suggestions.map((path, index) => (
            <li
              key={path}
              className={`picker-item ${index === selectedIndex ? 'selected' : ''}`}
              onClick={() => onSelect(path)}
              onMouseEnter={() => {
                // Only allow hover selection after mouse has actually moved
                if (mouseHasMoved) {
                  setSelectedIndex(index);
                }
              }}
            >
              <span className="picker-icon">📁</span>
              <span className="picker-path">{path}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
};
