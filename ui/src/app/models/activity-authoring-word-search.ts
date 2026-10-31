/**
 * Issue #586: kelime avının (`wordSearch`) görselsiz formu — öğretmen kelimeleri, ızgara boyutunu, izinli yönleri (çapraz, ters)
 * ve listede ne görüneceğini seçer: ya kelimelerin kendisi ya da (her kelime için zorunlu) ipuçları — ikisi karışık değil. Plan
 * yalnız görünen metni (`label`) taşır; düzenlemede kip etiketlerden çıkarılır (hepsi kelimenin kendisi = kelime kipi); ızgara bunların ve bir tohumun saf fonksiyonudur ({@link generateWordSearch}):
 * aynı girdi hep aynı ızgarayı verir, **Karıştır** tohumu değiştirir. Kayıtlı madde düzenlenirken kelimeler / boyut / yönler
 * değişmedikçe kaydedilmiş ızgara korunur (`fixed`).
 *
 * Kurallar backend `WordSearchGrader.ValidateDefinition` ile aynı: ızgara 4..20, 1..40 kelime, etiket ≤ 100, kelime çizgileri düz
 * ve tekil.
 */
import { WORD_SEARCH, WORD_SEARCH_LIMITS, WordSearchLine, parseWordSearchPlan, parseWordSearchSolution, spell } from './word-search';

/** Boş hücreleri dolduran harfler (Türk alfabesi). */
export const FILL_ALPHABET = [...'ABCÇDEFGĞHIİJKLMNOÖPRSŞTUÜVYZ'];

export interface WordSearchEntryForm {
  word: string;
  /** İpucu kipinde listede görünen metin; kelime kipinde kullanılmaz (kip değişince kaybolmasın diye tutulur). */
  label: string;
}

export interface WordSearchFixedGrid {
  /** Bu ızgaranın üretildiği girdinin imzası (kelimeler, boyut, yönler). */
  key: string;
  letters: string[];
  lines: WordSearchLine[];
}

/** Listede ne görünür: kelimelerin kendisi ya da ipuçları. */
export type WordSearchShow = 'words' | 'clues';

export interface WordSearchForm {
  kind: 'wordSearch';
  show: WordSearchShow;
  rows: number;
  cols: number;
  diagonal: boolean;
  reverse: boolean;
  seed: number;
  words: WordSearchEntryForm[];
  /** Kayıttan açılan madde: içerik değişmedikçe bu ızgara kullanılır. */
  fixed: WordSearchFixedGrid | null;
}

export interface WordSearchError {
  key: string;
  params?: Record<string, unknown>;
}

export interface WordSearchDefinition {
  interactionType: typeof WORD_SEARCH;
  plan: string;
  solution: string;
}

export interface WordSearchGrid {
  letters: string[];
  /** Kelime başına çizgi; yerleştirilemeyen kelimede null. */
  lines: (WordSearchLine | null)[];
}

export const WORD_SEARCH_FORM_LIMITS = {
  minSize: WORD_SEARCH_LIMITS.minSize,
  maxSize: WORD_SEARCH_LIMITS.maxSize,
  maxWords: WORD_SEARCH_LIMITS.maxWords,
  minLength: WORD_SEARCH_LIMITS.minWordLength,
  maxLabel: WORD_SEARCH_LIMITS.maxLabel,
} as const;

export function isWordSearchForm(form: { kind: string }): form is WordSearchForm {
  return form.kind === 'wordSearch';
}

export function emptyWordSearchForm(): WordSearchForm {
  return {
    kind: 'wordSearch',
    show: 'words',
    rows: 8,
    cols: 8,
    diagonal: false,
    reverse: false,
    seed: 1,
    words: [
      { word: '', label: '' },
      { word: '', label: '' },
      { word: '', label: '' },
    ],
    fixed: null,
  };
}

/** Kelimenin hücre harfleri: boşluklar atılır, tr-TR büyük harf. */
export function wordLetters(word: string): string[] {
  return [...word.replace(/\s+/gu, '')].map((ch) => ch.toLocaleUpperCase('tr-TR'));
}

function signature(form: WordSearchForm): string {
  return JSON.stringify([form.rows, form.cols, form.diagonal, form.reverse, form.words.map((w) => wordLetters(w.word).join(''))]);
}

/**
 * Izgara üretimi: kelimeler uzundan kısaya, izinli yönlerde (yatay, dikey; isteğe bağlı çapraz ve ters) tohumlu rastgele bir
 * konuma yerleşir; ortak hücrede harf aynıysa kesişebilir. Kalan hücreler alfabeden doldurulur. Yer bulunamayan kelime null.
 */
export function generateWordSearch(form: WordSearchForm): WordSearchGrid {
  const rows = clamp(form.rows);
  const cols = clamp(form.cols);
  const random = seededRandom(form.seed * 7919 + 17);
  const grid: (string | null)[][] = Array.from({ length: rows }, () => Array.from({ length: cols }, () => null));
  const dirs: [number, number][] = [
    [0, 1],
    [1, 0],
  ];
  if (form.diagonal) dirs.push([1, 1], [-1, 1]);
  if (form.reverse) dirs.push(...dirs.map(([dr, dc]) => [-dr, -dc] as [number, number]));

  const words = form.words.map((w) => wordLetters(w.word));
  const lines: (WordSearchLine | null)[] = words.map(() => null);
  const order = words.map((w, i) => ({ w, i })).sort((a, b) => b.w.length - a.w.length || a.i - b.i);
  for (const { w, i } of order) {
    if (w.length < WORD_SEARCH_LIMITS.minWordLength || w.length > Math.max(rows, cols)) continue;
    const candidates: { r: number; c: number; dr: number; dc: number; overlap: number }[] = [];
    for (const [dr, dc] of dirs) {
      for (let r = 0; r < rows; r++) {
        for (let c = 0; c < cols; c++) {
          const er = r + dr * (w.length - 1);
          const ec = c + dc * (w.length - 1);
          if (er < 0 || er >= rows || ec < 0 || ec >= cols) continue;
          let overlap = 0;
          let ok = true;
          for (let k = 0; k < w.length && ok; k++) {
            const cell = grid[r + dr * k][c + dc * k];
            if (cell === null) continue;
            if (cell === w[k]) overlap++;
            else ok = false;
          }
          // Bir kelime öbürünün tamamen üstüne oturmasın (aynı çizgi iki kelime olamaz).
          if (ok && overlap < w.length) candidates.push({ r, c, dr, dc, overlap });
        }
      }
    }
    if (candidates.length === 0) continue;
    const pick = candidates[Math.floor(random() * candidates.length)];
    for (let k = 0; k < w.length; k++) grid[pick.r + pick.dr * k][pick.c + pick.dc * k] = w[k];
    lines[i] = { from: [pick.r, pick.c], to: [pick.r + pick.dr * (w.length - 1), pick.c + pick.dc * (w.length - 1)] };
  }
  const letters = grid.map((row) => row.map((ch) => ch ?? FILL_ALPHABET[Math.floor(random() * FILL_ALPHABET.length)]).join(''));
  return { letters, lines };
}

/** Formun ızgarası: kayıtlı ızgara (içerik değişmediyse) ya da üretilen. */
export function wordSearchGrid(form: WordSearchForm): WordSearchGrid {
  return form.fixed && form.fixed.key === signature(form) ? { letters: form.fixed.letters, lines: form.fixed.lines } : generateWordSearch(form);
}

export function validateWordSearchForm(form: WordSearchForm): WordSearchError[] {
  const errors: WordSearchError[] = [];
  const { minSize, maxSize, maxWords, minLength, maxLabel } = WORD_SEARCH_FORM_LIMITS;
  const push = (key: string, params?: Record<string, unknown>) => {
    if (!errors.some((e) => e.key === key)) errors.push(params ? { key, params } : { key });
  };
  if (form.rows < minSize || form.rows > maxSize || form.cols < minSize || form.cols > maxSize)
    push('wordSearchGridSize', { min: minSize, max: maxSize });
  if (form.words.length < 1 || form.words.length > maxWords) push('wordSearchWordCount', { min: 1, max: maxWords });
  const seen = new Set<string>();
  for (const entry of form.words) {
    const letters = wordLetters(entry.word);
    if (letters.length === 0) push('wordSearchWordRequired');
    else if (letters.length < minLength) push('wordSearchWordLength', { min: minLength });
    if (letters.some((ch) => !/^[\p{L}\p{N}]$/u.test(ch))) push('wordSearchWordChars');
    if (form.show === 'clues') {
      const clue = entry.label.trim();
      if (!clue) push('wordSearchClueRequired');
      else if (clue.length > maxLabel) push('labelTooLong', { max: maxLabel });
      else if (letters.length && wordLetters(clue).join('') === letters.join('')) push('wordSearchClueIsWord');
    } else if (letters.length > maxLabel) push('labelTooLong', { max: maxLabel });
    const key = letters.join('');
    if (key && seen.has(key)) push('wordSearchDuplicate');
    seen.add(key);
  }
  if (errors.length === 0) {
    const unplaced = wordSearchGrid(form)
      .lines.map((line, i) => (line ? 0 : i + 1))
      .filter((n) => n > 0);
    if (unplaced.length) push('wordSearchUnplaced', { list: unplaced.join(', ') });
  }
  return errors;
}

/** Form → plan + çözüm. Önce {@link validateWordSearchForm} geçmeli; yerleşmeyen kelime planda kalır, çözümde yer almaz. */
export function buildWordSearchDefinition(form: WordSearchForm): WordSearchDefinition {
  const grid = wordSearchGrid(form);
  const words = form.words.map((entry, i) => ({
    id: `w${i + 1}`,
    label: form.show === 'clues' ? entry.label.trim() : wordLetters(entry.word).join(''),
  }));
  const lines: Record<string, WordSearchLine> = {};
  grid.lines.forEach((line, i) => {
    if (line) lines[`w${i + 1}`] = { from: [...line.from], to: [...line.to] };
  });
  return {
    interactionType: WORD_SEARCH,
    plan: JSON.stringify({ version: 1, type: WORD_SEARCH, rows: grid.letters.length, cols: [...(grid.letters[0] ?? '')].length, letters: grid.letters, words }),
    solution: JSON.stringify({ lines }),
  };
}

/** Kayıtlı plan + çözüm → form (düzenleme); ızgara `fixed` olarak korunur. Çözümsüz kelime varsa null (tuvalde düzenlenir). */
export function parseWordSearchDefinition(
  planJson: string | null | undefined,
  solutionJson: string | null | undefined
): WordSearchForm | null {
  const plan = parseWordSearchPlan(planJson);
  if (!plan || plan.words.length === 0) return null;
  const solution = parseWordSearchSolution(solutionJson, plan);
  if (plan.words.some((w) => !solution[w.id])) return null;
  const words = plan.words.map((w) => {
    const word = spell(plan, solution[w.id]);
    const label = w.label && wordLetters(w.label).join('') !== word ? w.label : '';
    return { word, label };
  });
  // Eski (#586) kayıtlarda kelime ile ipucu karışık olabilir: tek ipucu bile varsa ipucu kipi, eksikler doğrulamada istenir.
  const show: WordSearchShow = words.some((w) => w.label) ? 'clues' : 'words';
  const lines = plan.words.map((w) => solution[w.id]);
  const isDiagonal = (l: WordSearchLine) => l.from[0] !== l.to[0] && l.from[1] !== l.to[1];
  const isReverse = (l: WordSearchLine) => l.to[1] < l.from[1] || (l.to[1] === l.from[1] && l.to[0] < l.from[0]);
  const form: WordSearchForm = {
    kind: 'wordSearch',
    show,
    rows: plan.rows,
    cols: plan.cols,
    diagonal: lines.some(isDiagonal),
    reverse: lines.some(isReverse),
    seed: 1,
    words,
    fixed: null,
  };
  form.fixed = { key: signature(form), letters: plan.letters.map((row) => row.join('')), lines };
  return form;
}

function clamp(value: number): number {
  const { minSize, maxSize } = WORD_SEARCH_LIMITS;
  return Math.min(maxSize, Math.max(minSize, Math.trunc(Number.isFinite(value) ? value : minSize)));
}

/** Deterministik sözde rastgele (mulberry32). */
function seededRandom(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
