/**
 * Timing constants for editor operations.
 * These values control delays and debounce intervals for various async operations.
 */
export const TIMING = {
  /** Delay before detecting marker after insertion (allows Monaco to update) */
  MARKER_DETECTION_DELAY: 50,
  
  /** Delay before opening picker after marker detection (allows UI to settle) */
  PICKER_OPEN_DELAY: 100,
  
  /** Delay before opening next picker in sequential parameter selection */
  SEQUENTIAL_PICKER_DELAY: 300,
  
  /** Debounce interval for validation calls */
  VALIDATION_DEBOUNCE: 350,
  
  /** Delay before processing click events (allows Monaco to set cursor position) */
  CLICK_PROCESSING_DELAY: 100,
  
  /** Debounce delay before auto-inserting markers (prevents triggering during rapid typing) */
  AUTO_INSERT_DEBOUNCE: 200,
} as const;

