export interface CustomFunction {
  name: string;
  doc: string;
  signature: string;
  parameters: FunctionParameter[];
}

export interface FunctionParameter {
  name: string;
  type: 'path' | 'string' | 'number' | 'boolean';
  optional?: boolean;
  description?: string;
  pathType?: 'file' | 'folder' | 'both'; // For path parameters: file only, folder only, or both
  isSource?: boolean; // True if this is a source parameter (supports globbing, whole folder option)
}

export interface Diagnostic {
  startLine: number;
  startCol: number;
  endLine: number;
  endCol: number;
  message: string;
  severity: 'error' | 'warning';
}

export interface FileSystemSnapshot {
  files: string[];
  folders: string[];
}

