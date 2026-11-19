import { PenColor } from '../shared/utils/pen-color';

/**
 * Issue #1226: öğretmenin bir soruya kaydettiği çözüm. Çizgiler veridir (video değil); oynatıcı ses çalarken çizgileri zamanı
 * gelince çizer. Sunucudaki karşılığı `SolutionStrokeDocument`.
 */
export interface SolutionStroke {
  c: PenColor;
  /** Çizginin başladığı an (ms, kaydın başından). */
  t: number;
  /** Çizginin silindiği an (geri al / temizle); silinmediyse yok. */
  e?: number;
  /** Düz dizi: `x, y, dt, x, y, dt, ...`; x, y sayfa ölçeğinde, dt çizginin başından ms. */
  p: number[];
}

export interface SolutionStrokeDocument {
  v: 1;
  width: number;
  height: number;
  strokes: SolutionStroke[];
}

export interface SolutionRecording {
  questionId: number;
  durationMs: number;
  audioContentType: string;
  audioSizeBytes: number;
  schemaRevision: number;
  strokes: SolutionStrokeDocument;
  updatedAt: string;
}

/** Çözüm sayfası: soru görseli üstte, altı yazı alanı. Koordinatlar bu ölçekte tutulur, her ekranda aynı yere düşer. */
export const SOLUTION_PAGE_WIDTH = 1000;
export const SOLUTION_PAGE_HEIGHT = 750;
export const SOLUTION_MAX_DURATION_MS = 5 * 60 * 1000;
