import * as monaco from 'monaco-editor';

/**
 * Safely gets the model from an editor
 * Returns null if editor is null or model is not available
 */
export const getModelSafely = (
  editor: monaco.editor.IStandaloneCodeEditor | null
): monaco.editor.ITextModel | null => {
  return editor?.getModel() || null;
};

/**
 * Safely gets the current cursor position from an editor
 * Returns null if editor is null or position is not available
 */
export const getPositionSafely = (
  editor: monaco.editor.IStandaloneCodeEditor | null
): monaco.Position | null => {
  return editor?.getPosition() || null;
};

/**
 * Executes a callback with the editor's model if it exists
 * Returns null if editor or model is not available, otherwise returns the callback result
 *
 * @example
 * const text = withModel(editor, (model) => model.getValue());
 * if (text) {
 *   console.log('Model text:', text);
 * }
 */
export const withModel = <T>(
  editor: monaco.editor.IStandaloneCodeEditor | null,
  callback: (model: monaco.editor.ITextModel) => T
): T | null => {
  const model = editor?.getModel();
  if (!model) return null;
  return callback(model);
};

/**
 * Executes a callback with both the editor's model and position if they exist
 * Returns null if editor, model, or position is not available, otherwise returns the callback result
 *
 * @example
 * const result = withModelAndPosition(editor, (model, position) => {
 *   return model.getLineContent(position.lineNumber);
 * });
 */
export const withModelAndPosition = <T>(
  editor: monaco.editor.IStandaloneCodeEditor | null,
  callback: (model: monaco.editor.ITextModel, position: monaco.Position) => T
): T | null => {
  const model = editor?.getModel();
  const position = editor?.getPosition();
  if (!model || !position) return null;
  return callback(model, position);
};

/**
 * Executes a callback with the model if it exists
 * Returns undefined (void) - useful for operations that don't return a value
 *
 * @example
 * withModelVoid(editor, (model) => {
 *   model.pushEditOperations([], edits, () => null);
 * });
 */
export const withModelVoid = (
  editor: monaco.editor.IStandaloneCodeEditor | null,
  callback: (model: monaco.editor.ITextModel) => void
): void => {
  const model = editor?.getModel();
  if (!model) return;
  callback(model);
};

/**
 * Checks if editor has a valid model
 * Useful for early returns or conditional checks
 *
 * @example
 * if (!hasModel(editor)) return;
 * // Safe to use editor.getModel()!
 */
export const hasModel = (
  editor: monaco.editor.IStandaloneCodeEditor | null
): editor is monaco.editor.IStandaloneCodeEditor => {
  return editor !== null && editor.getModel() !== null;
};

/**
 * Checks if editor has both a valid model and position
 * Useful for early returns or conditional checks
 */
export const hasModelAndPosition = (
  editor: monaco.editor.IStandaloneCodeEditor | null
): boolean => {
  return hasModel(editor) && editor.getPosition() !== null;
};
