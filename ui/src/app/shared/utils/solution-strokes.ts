import { SolutionStroke, SolutionStrokeDocument } from '../../models/solution-recording';
import { PenColor, penColorCss } from './pen-color';

export interface DrawnStroke {
  d: string;
  /** CSS rengi; varsayılan renkte null (sayfanın mürekkep rengi). */
  css: string | null;
}

/** Issue #1226: çizginin `timeMs` anına kadar çizilmiş kısmı; henüz başlamadıysa ya da silindiyse null. */
export function strokePathAt(stroke: SolutionStroke, timeMs: number): string | null {
  if (timeMs < stroke.t || (stroke.e !== undefined && timeMs >= stroke.e)) return null;
  const points = stroke.p;
  let d = `M${points[0]} ${points[1]}`;
  let last = 0;
  for (let i = 3; i < points.length; i += 3) {
    if (stroke.t + points[i + 2] > timeMs) break;
    d += `L${points[i]} ${points[i + 1]}`;
    last = i;
  }
  // Tek nokta da görünsün (yuvarlak uçla nokta olur).
  return last === 0 ? `${d}L${points[0]} ${points[1]}` : d;
}

/** `timeMs` anında sayfada görünen çizgiler, çizim sırasıyla. */
export function drawnStrokesAt(document: SolutionStrokeDocument, timeMs: number): DrawnStroke[] {
  const drawn: DrawnStroke[] = [];
  for (const stroke of document.strokes) {
    const d = strokePathAt(stroke, timeMs);
    if (d !== null) drawn.push({ d, css: penColorCss(stroke.c as PenColor) });
  }
  return drawn;
}

/** `m:ss`. */
export function formatSolutionTime(ms: number): string {
  const seconds = Math.max(0, Math.floor(ms / 1000));
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}
