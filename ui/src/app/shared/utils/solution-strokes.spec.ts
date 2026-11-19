import { describe, expect, it } from 'vitest';
import { SolutionStrokeDocument } from '../../models/solution-recording';
import { drawnStrokesAt, formatSolutionTime, strokePathAt } from './solution-strokes';

const DOCUMENT: SolutionStrokeDocument = {
  v: 1,
  width: 1000,
  height: 750,
  strokes: [
    { c: 'default', t: 1000, e: 4000, p: [10, 10, 0, 20, 20, 100, 30, 30, 200] },
    { c: 'red', t: 2000, p: [50, 50, 0] },
  ],
};

describe('solution strokes (#1226)', () => {
  it('draws a stroke point by point as time passes', () => {
    const [stroke] = DOCUMENT.strokes;
    expect(strokePathAt(stroke, 999)).toBeNull();
    expect(strokePathAt(stroke, 1000)).toBe('M10 10L10 10');
    expect(strokePathAt(stroke, 1100)).toBe('M10 10L20 20');
    expect(strokePathAt(stroke, 1500)).toBe('M10 10L20 20L30 30');
  });

  it('hides a stroke from the moment it was undone or cleared', () => {
    expect(strokePathAt(DOCUMENT.strokes[0], 3999)).not.toBeNull();
    expect(strokePathAt(DOCUMENT.strokes[0], 4000)).toBeNull();
  });

  it('lists what is on the page at a given time, with colours', () => {
    expect(drawnStrokesAt(DOCUMENT, 0)).toEqual([]);
    expect(drawnStrokesAt(DOCUMENT, 2500).map((s) => s.css)).toEqual([null, '#e53935']);
    expect(drawnStrokesAt(DOCUMENT, 5000)).toEqual([{ d: 'M50 50L50 50', css: '#e53935' }]);
  });

  it('formats time as m:ss', () => {
    expect(formatSolutionTime(0)).toBe('0:00');
    expect(formatSolutionTime(65_400)).toBe('1:05');
  });
});
