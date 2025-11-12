import React, { useState, useEffect, useRef } from 'react';
import './FilePicker.css'; // Reuse the same styles

export interface EnumPickerProps {
  options: string[];
  functionName: string;
  parameterIndex: number;
  currentValue: string | null;
  position: { x: number; y: number };
  onSelect: (value: string) => void;
  onCancel: () => void;
}

export const EnumPicker: React.FC<EnumPickerProps> = ({
  options,
  functionName,
  parameterIndex,
  currentValue,
  position,
  onSelect,
  onCancel,
}) => {
  const [selectedIndex, setSelectedIndex] = useState(0);
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

  // Pre-select the current value if it exists in options
  useEffect(() => {
    if (currentValue) {
      const index = options.findIndex(
        (opt) => opt.toLowerCase() === currentValue.toLowerCase()
      );
      if (index >= 0) {
        setSelectedIndex(index);
      }
    }
  }, [options, currentValue]);

  // Handle keyboard navigation
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        onCancel();
      } else if (e.key === 'ArrowDown') {
        e.preventDefault();
        setSelectedIndex((prev) => Math.min(prev + 1, options.length - 1));
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        setSelectedIndex((prev) => Math.max(prev - 1, 0));
      } else if (e.key === 'Enter') {
        e.preventDefault();
        if (options[selectedIndex]) {
          onSelect(options[selectedIndex]);
        }
      }
    };

    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [options, selectedIndex, onSelect, onCancel]);

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
        <span>
          Select Value ({functionName} - param {parameterIndex})
        </span>
        <span className="picker-hint">↑↓ Navigate • Enter Select • Esc Cancel</span>
      </div>

      {options.length === 0 ? (
        <div className="picker-empty">No options available</div>
      ) : (
        <ul className="picker-list">
          {options.map((option, index) => (
            <li
              key={option}
              className={`picker-item ${index === selectedIndex ? 'selected' : ''}`}
              onClick={() => onSelect(option)}
              onMouseEnter={() => {
                // Only allow hover selection after mouse has actually moved
                if (mouseHasMoved) {
                  setSelectedIndex(index);
                }
              }}
            >
              <span className="picker-icon">📌</span>
              <span className="picker-path">{option}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
};
