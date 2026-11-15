/**
 * Issue #782: soru başına karalama notları. Yalnız bu cihazda durur, sunucuya gitmez: öğretmen göremez.
 * Test bitince silinir; bu cihazda bitmeyen testlerin notları {@link SCRATCH_MAX_AGE_MS} sonra temizlenir.
 */
export interface ScratchNote {
  /** SVG yol verisi; koordinatlar alan genişliği {@link SCRATCH_VIEW_WIDTH} sayılarak tutulur. */
  strokes: string[];
  text: string;
}

export type ScratchNotes = Record<number, ScratchNote>;

export const SCRATCH_VIEW_WIDTH = 1000;
export const SCRATCH_TEXT_MAX_LENGTH = 2000;
export const SCRATCH_MAX_AGE_MS = 30 * 24 * 60 * 60 * 1000;

const KEY_PREFIX = 'testSolve.scratch.';
const scratchKey = (instanceId: number) => `${KEY_PREFIX}${instanceId}`;
/** #1175: soru görselinin üstüne çizilenler; karalama notlarıyla aynı kurallar, ayrı anahtar. */
const INK_KEY_PREFIX = 'testSolve.ink.';
const inkKey = (instanceId: number) => `${INK_KEY_PREFIX}${instanceId}`;
/** Eldeki yol yalnız bizim ürettiğimiz biçimde olabilir: M ile başlar, sayı ve L taşır. */
const STROKE_PATTERN = /^M[\d L]+$/;

function sanitizeStrokes(strokes: unknown): string[] {
  return Array.isArray(strokes)
    ? strokes.filter((stroke): stroke is string => typeof stroke === 'string' && STROKE_PATTERN.test(stroke))
    : [];
}

function sanitizeNote(value: unknown): ScratchNote | null {
  if (!value || typeof value !== 'object') return null;
  const { strokes, text } = value as Partial<ScratchNote>;
  return {
    strokes: sanitizeStrokes(strokes),
    text: typeof text === 'string' ? text.slice(0, SCRATCH_TEXT_MAX_LENGTH) : '',
  };
}

export function readScratchNotes(instanceId: number): ScratchNotes {
  try {
    const parsed = JSON.parse(localStorage.getItem(scratchKey(instanceId)) ?? 'null');
    const notes: ScratchNotes = {};
    for (const [questionId, value] of Object.entries(parsed?.notes ?? {})) {
      const note = sanitizeNote(value);
      if (note) notes[Number(questionId)] = note;
    }
    return notes;
  } catch {
    return {};
  }
}

/** Yazılamazsa (depolama kapalı ya da dolu) false; notlar yalnız sayfa açıkken kalır. */
export function writeScratchNotes(instanceId: number, notes: ScratchNotes, now = Date.now()): boolean {
  try {
    localStorage.setItem(scratchKey(instanceId), JSON.stringify({ updatedAt: now, notes }));
    return true;
  } catch {
    return false;
  }
}

/**
 * Issue #1175: bir sorunun görseline çizilenler. Koordinatlar görsel genişliği {@link SCRATCH_VIEW_WIDTH} sayılarak tutulur.
 */
export function readInkStrokes(instanceId: number, questionId: number): string[] {
  try {
    const parsed = JSON.parse(localStorage.getItem(inkKey(instanceId)) ?? 'null');
    return sanitizeStrokes(parsed?.strokes?.[questionId]);
  } catch {
    return [];
  }
}

/**
 * Yalnız bu sorunun kaydını değiştirir: aynı anda görünen iki sorunun katmanı birbirinin çizgisini ezmez.
 * Yazılamazsa false (bkz. {@link writeScratchNotes}).
 */
export function writeInkStrokes(instanceId: number, questionId: number, strokes: string[], now = Date.now()): boolean {
  try {
    let all: Record<string, unknown> = {};
    try {
      const parsed = JSON.parse(localStorage.getItem(inkKey(instanceId)) ?? 'null');
      if (parsed?.strokes && typeof parsed.strokes === 'object') all = parsed.strokes;
    } catch {
      // Okunamayan kayıt yeniden yazılır.
    }
    if (strokes.length) {
      all[questionId] = strokes;
    } else {
      delete all[questionId];
    }
    localStorage.setItem(inkKey(instanceId), JSON.stringify({ updatedAt: now, strokes: all }));
    return true;
  } catch {
    return false;
  }
}

/** Testin karalama notlarını ve görsel üstü çizimlerini siler. */
export function clearScratchNotes(instanceId: number): void {
  try {
    localStorage.removeItem(scratchKey(instanceId));
    localStorage.removeItem(inkKey(instanceId));
  } catch {
    // Bkz. writeScratchNotes.
  }
}

export function purgeOldScratchNotes(now = Date.now()): void {
  try {
    for (const key of Object.keys(localStorage)) {
      if (!key.startsWith(KEY_PREFIX) && !key.startsWith(INK_KEY_PREFIX)) continue;
      let updatedAt = 0;
      try {
        updatedAt = Number(JSON.parse(localStorage.getItem(key) ?? 'null')?.updatedAt) || 0;
      } catch {
        // Okunamayan kayıt eski sayılır.
      }
      if (now - updatedAt > SCRATCH_MAX_AGE_MS) {
        localStorage.removeItem(key);
      }
    }
  } catch {
    // Bkz. writeScratchNotes.
  }
}
