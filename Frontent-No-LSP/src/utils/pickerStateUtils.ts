import * as monaco from 'monaco-editor';

/**
 * Represents the state of the parameter picker component
 */
export interface PickerState {
  visible: boolean;
  position: { x: number; y: number };
  range: monaco.Range | null;
  parameterIndex: number;
  functionName: string;
  currentValue: string | null;
  cursorPosition: monaco.Position | null;
}

/**
 * Default picker state when closed or reset
 */
export const DEFAULT_PICKER_STATE: PickerState = {
  visible: false,
  position: { x: 0, y: 0 },
  range: null,
  parameterIndex: 0,
  functionName: '',
  currentValue: null,
  cursorPosition: null,
};

/**
 * Creates a closed picker state (same as DEFAULT_PICKER_STATE)
 * Use this when explicitly closing the picker
 */
export const closePickerState = (): PickerState => DEFAULT_PICKER_STATE;

/**
 * Creates an open picker state with the provided options
 * Merges provided options with DEFAULT_PICKER_STATE and sets visible to true
 */
export const createPickerState = (
  options: Partial<Omit<PickerState, 'visible'>>
): PickerState => ({
  ...DEFAULT_PICKER_STATE,
  ...options,
  visible: true,
});
