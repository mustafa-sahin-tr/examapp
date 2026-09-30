// Issue #98: gerçek paketin Excalidraw tip bildirimlerinde kullanılan yüzeyi.
import type { MermaidToExcalidrawResult } from './dist/interfaces';

export interface MermaidConfig {
  startOnLoad?: boolean;
  flowchart?: { curve?: 'linear' | 'basis' };
  themeVariables?: { fontSize?: string };
  maxEdges?: number;
  maxTextSize?: number;
}

export declare const parseMermaidToExcalidraw: (
  definition: string,
  config?: MermaidConfig
) => Promise<MermaidToExcalidrawResult>;
