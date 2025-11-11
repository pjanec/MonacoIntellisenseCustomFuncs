/**
 * Centralized debug logging utility.
 *
 * Provides categorized logging methods for different parts of the application.
 * Can be easily disabled for production builds by setting DEBUG_ENABLED to false.
 */

const DEBUG_ENABLED = true;

/**
 * Debug logger with categorized logging methods.
 * All methods accept any number of arguments like console.log.
 */
export const DebugLogger = {
  /**
   * Log content change events in the editor.
   */
  contentChange: (message: string, ...args: any[]) => {
    if (!DEBUG_ENABLED) return;
    console.log(`[CONTENT_CHANGE] ${message}`, ...args);
  },

  /**
   * Log auto-insertion events (after '(' or ',').
   */
  autoInsert: (message: string, ...args: any[]) => {
    if (!DEBUG_ENABLED) return;
    console.log(`[AUTO_INSERT] ${message}`, ...args);
  },

  /**
   * Log comma detection and insertion events.
   */
  comma: (message: string, ...args: any[]) => {
    if (!DEBUG_ENABLED) return;
    console.log(`[COMMA] ${message}`, ...args);
  },

  /**
   * Log marker detection events.
   */
  markerDetection: (message: string, ...args: any[]) => {
    if (!DEBUG_ENABLED) return;
    console.log(`[MARKER_DETECTION] ${message}`, ...args);
  },

  /**
   * Log parameter replacement events.
   */
  replaceParameter: (message: string, ...args: any[]) => {
    if (!DEBUG_ENABLED) return;
    console.log(`[REPLACE_PARAMETER] ${message}`, ...args);
  },

  /**
   * Log general events that don't fit other categories.
   */
  general: (message: string, ...args: any[]) => {
    if (!DEBUG_ENABLED) return;
    console.log(message, ...args);
  },

  /**
   * Log events related to marker detection and picker opening.
   */
  handleMarkerDetected: (message: string, ...args: any[]) => {
    if (!DEBUG_ENABLED) return;
    console.log(`[HANDLE_MARKER_DETECTED] ${message}`, ...args);
  }
};
