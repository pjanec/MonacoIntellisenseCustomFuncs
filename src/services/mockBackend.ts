import { Diagnostic } from '../types';
import { ScribanParser } from './scribanParser';

export async function validateScriban(script: string): Promise<Diagnostic[]> {
  await new Promise(resolve => setTimeout(resolve, 100));
  
  return ScribanParser.validate(script);
}

