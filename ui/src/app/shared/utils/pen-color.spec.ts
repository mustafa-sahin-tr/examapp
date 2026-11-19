import { afterEach, describe, expect, it } from 'vitest';
import { SCRATCH_STROKE_PATTERN, colorStroke, penColor, selectPenColor, strokeCss, strokePath } from './pen-color';

describe('pen colour (#1241)', () => {
  afterEach(() => {
    selectPenColor('default');
    sessionStorage.clear();
  });

  it('keeps the default colour as a bare path and prefixes the others', () => {
    expect(colorStroke('M1 2L3 4', 'default')).toBe('M1 2L3 4');
    expect(colorStroke('M1 2L3 4', 'red')).toBe('red:M1 2L3 4');
  });

  it('reads the path and the colour back', () => {
    expect(strokePath('red:M1 2L3 4')).toBe('M1 2L3 4');
    expect(strokePath('M1 2L3 4')).toBe('M1 2L3 4');
    expect(strokeCss('red:M1 2L3 4')).toMatch(/^#[0-9a-f]{6}$/);
    expect(strokeCss('M1 2L3 4')).toBeNull();
  });

  it('accepts only strokes in a known colour', () => {
    expect(SCRATCH_STROKE_PATTERN.test('M1 2L3 4')).toBe(true);
    expect(SCRATCH_STROKE_PATTERN.test('blue:M1 2L3 4')).toBe(true);
    expect(SCRATCH_STROKE_PATTERN.test('pink:M1 2L3 4')).toBe(false);
    expect(SCRATCH_STROKE_PATTERN.test('red:M1 2Z"><script>')).toBe(false);
  });

  it('remembers the selected colour for the session', () => {
    selectPenColor('green');
    expect(penColor()).toBe('green');
    expect(sessionStorage.getItem('testSolve.penColor')).toBe('green');
  });
});
