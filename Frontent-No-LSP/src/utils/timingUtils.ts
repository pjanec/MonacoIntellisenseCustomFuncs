import { TIMING } from '../constants/timing';

/**
 * Type for delayed callback handles (for cleanup/cancellation)
 */
export type DelayedCallback = ReturnType<typeof setTimeout>;

/**
 * Schedules a callback to run after the marker detection delay
 * Use this after inserting markers to allow Monaco to update before detection
 * @param callback Function to execute after delay
 * @returns Handle that can be used to cancel the timeout
 */
export const delayedMarkerDetection = (callback: () => void): DelayedCallback => {
  return setTimeout(callback, TIMING.MARKER_DETECTION_DELAY);
};

/**
 * Schedules a callback to run after the picker open delay
 * Use this to allow UI to settle before opening picker
 * @param callback Function to execute after delay
 * @returns Handle that can be used to cancel the timeout
 */
export const delayedPickerOpen = (callback: () => void): DelayedCallback => {
  return setTimeout(callback, TIMING.PICKER_OPEN_DELAY);
};

/**
 * Schedules a callback to run after the sequential picker delay
 * Use this when opening the next picker in sequential parameter selection
 * @param callback Function to execute after delay
 * @returns Handle that can be used to cancel the timeout
 */
export const delayedSequentialPicker = (callback: () => void): DelayedCallback => {
  return setTimeout(callback, TIMING.SEQUENTIAL_PICKER_DELAY);
};

/**
 * Schedules a callback to run after the click processing delay
 * Use this to allow Monaco to set cursor position after a click
 * @param callback Function to execute after delay
 * @returns Handle that can be used to cancel the timeout
 */
export const delayedClickProcessing = (callback: () => void): DelayedCallback => {
  return setTimeout(callback, TIMING.CLICK_PROCESSING_DELAY);
};

/**
 * Schedules a callback with auto-insert debounce delay
 * Use this to prevent triggering auto-insertion during rapid typing
 * @param callback Function to execute after delay
 * @returns Handle that can be used to cancel the timeout
 */
export const debouncedAutoInsert = (callback: () => void): DelayedCallback => {
  return setTimeout(callback, TIMING.AUTO_INSERT_DEBOUNCE);
};

/**
 * Schedules a callback with custom delay
 * Use this for delays that don't fit standard timing patterns
 * @param callback Function to execute after delay
 * @param delay Delay in milliseconds
 * @returns Handle that can be used to cancel the timeout
 */
export const delayedCallback = (callback: () => void, delay: number): DelayedCallback => {
  return setTimeout(callback, delay);
};

/**
 * Cancels a delayed callback if it hasn't executed yet
 * @param handle Handle returned from any delayed* function
 */
export const cancelDelayedCallback = (handle: DelayedCallback | null): void => {
  if (handle !== null) {
    clearTimeout(handle);
  }
};
