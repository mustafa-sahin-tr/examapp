import { signal } from '@angular/core';

/**
 * Issue #1241: karalama alanı (#782) ve soru üstü çizimde (#1175) kalem rengi. `default` yüzeyin kendi mürekkep rengidir
 * (tema ile değişir); diğerleri açık ve koyu temada ayırt edilen sabit orta tonlardır.
 */
export const PEN_COLORS = ['default', 'red', 'green', 'blue', 'orange'] as const;
export type PenColor = (typeof PEN_COLORS)[number];

const PEN_COLOR_CSS: Record<Exclude<PenColor, 'default'>, string> = {
  red: '#e53935',
  green: '#2e9e4f',
  blue: '#1e88e5',
  orange: '#ef6c00',
};

/**
 * Kayıtlı çizgi: SVG yolu, renkliyse başında `renk:` (ör. `red:M1 2L3 4`). Öneksiz çizgi (#1241 öncesi kayıtlar dahil)
 * varsayılan renktedir.
 */
export const SCRATCH_STROKE_PATTERN = new RegExp(`^(?:(?:${Object.keys(PEN_COLOR_CSS).join('|')}):)?M[\\d L]+$`);

export function colorStroke(path: string, color: PenColor): string {
  return color === 'default' ? path : `${color}:${path}`;
}

export function strokePath(stroke: string): string {
  return stroke.slice(stroke.indexOf(':') + 1);
}

/** Çizginin CSS rengi; varsayılan renkte null (yüzeyin `currentColor`'ı geçerli olur). */
export function strokeCss(stroke: string): string | null {
  const separator = stroke.indexOf(':');
  return separator < 0 ? null : penColorCss(stroke.slice(0, separator) as PenColor);
}

export function penColorCss(color: PenColor): string | null {
  return color === 'default' ? null : (PEN_COLOR_CSS[color] ?? null);
}

const SESSION_KEY = 'testSolve.penColor';

function readPenColor(): PenColor {
  try {
    const stored = sessionStorage.getItem(SESSION_KEY) as PenColor | null;
    return stored && PEN_COLORS.includes(stored) ? stored : 'default';
  } catch {
    return 'default';
  }
}

const selected = signal<PenColor>(readPenColor());

/** Seçili kalem rengi; karalama alanı ve soru üstü çizim aynı rengi kullanır, oturum boyunca hatırlanır. */
export const penColor = selected.asReadonly();

export function selectPenColor(color: PenColor): void {
  selected.set(color);
  try {
    sessionStorage.setItem(SESSION_KEY, color);
  } catch {
    // Depolama kapalıysa renk yalnız sayfa açıkken kalır.
  }
}
