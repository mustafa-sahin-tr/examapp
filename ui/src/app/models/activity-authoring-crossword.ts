/**
 * Issue #586: çengel bulmacanın (`crossword`) görselsiz formu — öğretmen 10×10'luk ({@link CROSSWORD_EDITOR}) ızgarada bir hücre
 * seçer, yön + ipucu + cevap yazar; cevap harfleri hücrelere yazılır. Yer yetmezse dört kenara satır / sütun eklenir
 * ({@link insertCrosswordLine}); kayıtta kelimelerin kapladığı alana kırpılır ({@link trimCrosswordForm}), öğrenci yalnız o alanı
 * görür. **Otomatik yerleştir** ({@link autoLayout}) kelimeleri kesiştirerek yeniden dizer. Numaralar kayıtta başlangıç
 * hücrelerine göre verilir (aynı hücreden başlayanlar aynı numara).
 *
 * Kurallar backend `CrosswordGrader.ValidateDefinition` ile aynı: kırpılmış ızgara 2..20 (editör ızgarası en çok 30), 1..80 kelime, kelime 2..20 harf/rakam, ipucu
 * zorunlu, aynı yönde iki kelime hücre paylaşmaz ve uç uca değmez, kesişen hücrelerde harfler aynı.
 */
import { CROSSWORD, CROSSWORD_LIMITS, CrosswordDir, clueCells, clueNumbers, isCellChar, parseCrosswordPlan, parseCrosswordSolution } from './crossword';

export interface CrosswordEntryForm {
  answer: string;
  clue: string;
  dir: CrosswordDir;
  /** 0 tabanlı başlangıç hücresi. */
  row: number;
  col: number;
  /**
   * Issue #586 (dilim e): görselden çıkarılan kelime yerinin uzunluğu — cevap yazılana dek ızgarada bu kadar hücre tutar
   * (cevap yazılınca uzunluk cevaptan gelir).
   */
  length?: number;
}

export interface CrosswordForm {
  kind: 'crossword';
  rows: number;
  cols: number;
  entries: CrosswordEntryForm[];
}

export interface CrosswordError {
  key: string;
  params?: Record<string, unknown>;
}

export interface CrosswordDefinition {
  interactionType: typeof CROSSWORD;
  plan: string;
  solution: string;
}

export const CROSSWORD_FORM_LIMITS = {
  minSize: CROSSWORD_LIMITS.minSize,
  maxSize: CROSSWORD_LIMITS.maxSize,
  maxEntries: CROSSWORD_LIMITS.maxClues,
  minLength: CROSSWORD_LIMITS.minLength,
  maxClue: CROSSWORD_LIMITS.maxClueText,
} as const;

/** Editör ızgarası: varsayılan boyut ve kenarlara eklemeyle varılabilecek en büyük boyut (kayıtta kırpılan alan yine ≤ 20). */
export const CROSSWORD_EDITOR = { defaultSize: 10, maxSize: 30 } as const;

export type CrosswordSide = 'top' | 'bottom' | 'left' | 'right';

export function isCrosswordForm(form: { kind: string }): form is CrosswordForm {
  return form.kind === 'crossword';
}

export function emptyCrosswordForm(): CrosswordForm {
  return {
    kind: 'crossword',
    rows: CROSSWORD_EDITOR.defaultSize,
    cols: CROSSWORD_EDITOR.defaultSize,
    entries: [],
  };
}

/** Kenara bir satır / sütun ekler; üste / sola eklenince kelimeler kayar. Editör sınırındaysa form değişmez. */
export function insertCrosswordLine(form: CrosswordForm, side: CrosswordSide): CrosswordForm {
  const vertical = side === 'top' || side === 'bottom';
  if ((vertical ? form.rows : form.cols) >= CROSSWORD_EDITOR.maxSize) return form;
  const dr = side === 'top' ? 1 : 0;
  const dc = side === 'left' ? 1 : 0;
  return {
    ...form,
    rows: form.rows + (vertical ? 1 : 0),
    cols: form.cols + (vertical ? 0 : 1),
    entries: form.entries.map((e) => ({ ...e, row: e.row + dr, col: e.col + dc })),
  };
}

/**
 * Kelimelerin kapladığı alan (en az 2×2); yalnız ızgaraya sığan, uzunluğu olan kelimeler sayılır. Kelime yoksa null.
 */
function usedArea(form: CrosswordForm): { top: number; left: number; rows: number; cols: number } | null {
  const cells = form.entries.flatMap(entryCells);
  if (cells.length === 0) return null;
  const top = Math.min(...cells.map(([r]) => r));
  const left = Math.min(...cells.map(([, c]) => c));
  return {
    top,
    left,
    rows: Math.max(CROSSWORD_LIMITS.minSize, Math.max(...cells.map(([r]) => r)) - top + 1),
    cols: Math.max(CROSSWORD_LIMITS.minSize, Math.max(...cells.map(([, c]) => c)) - left + 1),
  };
}

/** Tamamen boş üst / alt satırları ve sol / sağ sütunları atar (kayıtta; öğrenci yalnız kelimelerin olduğu alanı görür). */
export function trimCrosswordForm(form: CrosswordForm): CrosswordForm {
  const area = usedArea(form);
  if (!area) return form;
  return {
    ...form,
    rows: area.rows,
    cols: area.cols,
    entries: form.entries.map((e) => ({ ...e, row: e.row - area.top, col: e.col - area.left })),
  };
}

/** Kayıtlı / okunan bulmacayı editörde en az varsayılan boyuta (sağa ve alta boş hücre) genişletir. */
export function padCrosswordForm(form: CrosswordForm): CrosswordForm {
  return {
    ...form,
    rows: Math.max(form.rows, CROSSWORD_EDITOR.defaultSize),
    cols: Math.max(form.cols, CROSSWORD_EDITOR.defaultSize),
  };
}

/** Cevabın hücre harfleri: boşluklar atılır, tr-TR büyük harf. */
export function answerLetters(answer: string): string[] {
  return [...answer.replace(/\s+/gu, '')].map((ch) => ch.toLocaleUpperCase('tr-TR'));
}

/** Kelimenin hücre sayısı: cevap yazıldıysa cevaptan, yoksa görselden çıkarılan uzunluk (yoksa 0). */
export function entryLength(entry: CrosswordEntryForm): number {
  return answerLetters(entry.answer).length || entry.length || 0;
}

function entryCells(entry: CrosswordEntryForm): [number, number][] {
  return clueCells({ dir: entry.dir, row: entry.row, col: entry.col, length: entryLength(entry) });
}

/** Editör ızgarasında bir hücre: yazılan harf (boş kelime yerinde ''), çakışma, başlangıç numarası, geçen kelimeler. */
export interface CrosswordBoardCell {
  letter: string;
  conflict: boolean;
  number: number | null;
  entries: number[];
}

/**
 * Editör ızgarasının dolu hücreleri (anahtar `satır,sütun`) ve kelime başına numara (form sırasıyla; kayıttaki numaralandırmayla
 * aynı kural: başlangıç hücreleri satır, sonra sütun sırasıyla, aynı hücreden başlayanlar aynı numara).
 */
export function crosswordBoard(form: CrosswordForm): { cells: Map<string, CrosswordBoardCell>; numbers: number[] } {
  const cells = new Map<string, CrosswordBoardCell>();
  form.entries.forEach((entry, index) => {
    const word = answerLetters(entry.answer);
    entryCells(entry).forEach(([r, c], i) => {
      const key = `${r},${c}`;
      const cell = cells.get(key) ?? { letter: '', conflict: false, number: null, entries: [] };
      const ch = word[i] ?? '';
      if (ch && cell.letter && cell.letter !== ch) cell.conflict = true;
      if (ch && !cell.letter) cell.letter = ch;
      cell.entries.push(index);
      cells.set(key, cell);
    });
  });
  const starts = [...new Set(form.entries.filter((e) => entryLength(e) > 0).map((e) => `${e.row},${e.col}`))].sort((a, b) => {
    const [ar, ac] = a.split(',').map(Number);
    const [br, bc] = b.split(',').map(Number);
    return ar - br || ac - bc;
  });
  const byStart = new Map(starts.map((key, i) => [key, i + 1]));
  for (const [key, n] of byStart) {
    const cell = cells.get(key);
    if (cell) cell.number = n;
  }
  return { cells, numbers: form.entries.map((e) => byStart.get(`${e.row},${e.col}`) ?? 0) };
}

export function validateCrosswordForm(form: CrosswordForm): CrosswordError[] {
  const errors: CrosswordError[] = [];
  const { minSize, maxSize, maxEntries, minLength, maxClue } = CROSSWORD_FORM_LIMITS;
  const push = (key: string, params?: Record<string, unknown>) => {
    if (!errors.some((e) => e.key === key)) errors.push(params ? { key, params } : { key });
  };

  const editorMax = CROSSWORD_EDITOR.maxSize;
  if (form.rows < minSize || form.rows > editorMax || form.cols < minSize || form.cols > editorMax)
    push('crosswordGridSize', { min: minSize, max: editorMax });
  const area = usedArea(form);
  if (area && (area.rows > maxSize || area.cols > maxSize)) push('crosswordAreaTooLarge', { max: maxSize });
  if (form.entries.length < 1 || form.entries.length > maxEntries) push('crosswordEntryCount', { min: 1, max: maxEntries });

  const letters = new Map<string, string>();
  const owners: Record<CrosswordDir, Map<string, number>> = { across: new Map(), down: new Map() };
  form.entries.forEach((entry, index) => {
    const word = answerLetters(entry.answer);
    if (word.length === 0) push('crosswordAnswerRequired');
    else if (word.length < minLength || word.length > maxSize) push('crosswordAnswerLength', { min: minLength, max: maxSize });
    if (word.some((ch) => !isCellChar(ch))) push('crosswordAnswerChars');
    if (!entry.clue.trim()) push('crosswordClueRequired');
    if (entry.clue.trim().length > maxClue) push('labelTooLong', { max: maxClue });

    const cells = entryCells(entry);
    if (cells.some(([r, c]) => r < 0 || c < 0 || r >= form.rows || c >= form.cols)) {
      push('crosswordOutOfGrid', { n: index + 1 });
      return;
    }
    cells.forEach(([r, c], i) => {
      const key = `${r},${c}`;
      const previous = letters.get(key);
      if (previous !== undefined && word[i] && previous !== word[i]) push('crosswordConflict', { n: index + 1 });
      if (word[i]) letters.set(key, word[i]);
      if (owners[entry.dir].has(key)) push('crosswordOverlap', { n: index + 1 });
      owners[entry.dir].set(key, index);
    });
  });

  // Aynı yönde uç uca değen kelimeler öğrenciye tek kelime gibi görünür.
  form.entries.forEach((entry, index) => {
    const length = entryLength(entry);
    if (length === 0) return;
    const [br, bc] = entry.dir === 'across' ? [entry.row, entry.col - 1] : [entry.row - 1, entry.col];
    const [ar, ac] = entry.dir === 'across' ? [entry.row, entry.col + length] : [entry.row + length, entry.col];
    const own = owners[entry.dir];
    if (own.has(`${br},${bc}`) || own.has(`${ar},${ac}`)) push('crosswordTouching', { n: index + 1 });
  });
  return errors;
}

/**
 * Form → plan + çözüm; ızgara kelimelerin kapladığı alana kırpılır. Önce {@link validateCrosswordForm} geçmeli; geçersiz formda da çalışır (önizleme ayrıştırıcısı ızgaraya
 * sığmayan kelimeyi atlar). Numara yalnız ızgaraya sığan kelimelere verilir.
 */
export function buildCrosswordDefinition(editorForm: CrosswordForm): CrosswordDefinition {
  const form = trimCrosswordForm(editorForm);
  const rows = clamp(form.rows, CROSSWORD_LIMITS.minSize, CROSSWORD_LIMITS.maxSize);
  const cols = clamp(form.cols, CROSSWORD_LIMITS.minSize, CROSSWORD_LIMITS.maxSize);
  const clues = form.entries.map((entry, i) => ({
    id: `k${i + 1}`,
    dir: entry.dir,
    row: entry.row,
    col: entry.col,
    length: entryLength(entry),
    text: entry.clue.trim(),
  }));
  const numbered = parseCrosswordPlan(JSON.stringify({ version: 1, type: CROSSWORD, rows, cols, clues }));
  const numbers = numbered ? clueNumbers(numbered) : new Map<string, number>();
  const planClues = clues.map((clue) => {
    const number = numbers.get(clue.id);
    return number === undefined ? clue : { ...clue, number };
  });
  const answers: Record<string, string> = {};
  form.entries.forEach((entry, i) => (answers[`k${i + 1}`] = answerLetters(entry.answer).join('')));
  return {
    interactionType: CROSSWORD,
    plan: JSON.stringify({ version: 1, type: CROSSWORD, rows, cols, clues: planClues }),
    solution: JSON.stringify({ answers }),
  };
}

/**
 * Issue #586 (dilim e): Claude'un görselden çıkardığı plan (`activity-structure`, çözümsüz) → form: ızgara boyutu, kelime yerleri
 * ve ipuçları gelir, cevaplar boş kalır (öğretmen yazar); her kelimenin uzunluğu cevap yazılana dek `length`'te tutulur.
 * Kelime yeri yoksa null.
 */
export function crosswordFormFromDraft(planJson: string | null | undefined): CrosswordForm | null {
  const plan = parseCrosswordPlan(planJson);
  if (!plan || plan.clues.length === 0) return null;
  return padCrosswordForm({
    kind: 'crossword',
    rows: plan.rows,
    cols: plan.cols,
    entries: plan.clues.slice(0, CROSSWORD_FORM_LIMITS.maxEntries).map((clue) => ({
      answer: '',
      clue: clue.text ?? '',
      dir: clue.dir,
      row: clue.row,
      col: clue.col,
      length: clue.length,
    })),
  });
}

/** Kayıtlı plan + çözüm → form (düzenleme). Bozuk plan null. */
export function parseCrosswordDefinition(
  planJson: string | null | undefined,
  solutionJson: string | null | undefined
): CrosswordForm | null {
  const plan = parseCrosswordPlan(planJson);
  if (!plan || plan.clues.length === 0) return null;
  const answers = parseCrosswordSolution(solutionJson, plan);
  return padCrosswordForm({
    kind: 'crossword',
    rows: plan.rows,
    cols: plan.cols,
    entries: plan.clues.map((clue) => ({
      answer: Object.hasOwn(answers, clue.id) ? answers[clue.id] : '',
      clue: clue.text ?? '',
      dir: clue.dir,
      row: clue.row,
      col: clue.col,
    })),
  });
}

export interface AutoLayoutResult {
  form: CrosswordForm;
  /** Kesiştirilemeyen kelimelerin form sırası (1 tabanlı); bunlar ızgaranın altına ayrı satıra konur. */
  unplaced: number[];
}

/** Otomatik yerleştirmede denenen tohum sayısı (ilk deneme en çok kesişmeyi seçer, diğerleri adaylardan tohumlu rastgele). */
const AUTO_LAYOUT_ATTEMPTS = 40;

/**
 * Otomatik yerleştirme: en uzun kelime yatay başlar, sıradaki her kelime yerleşmiş bir harfle kesişecek bir yere konur. Uygun
 * yer: kesişme dışındaki hücrelerin yanları boş, kelimenin önü ve arkası boş, ortak hücrelerde harf aynı. İlk turda yer
 * bulamayan kelimeler sonda bir kez daha denenir. Açgözlü seçim sonraki kelimelerin yolunu kapatabildiği için
 * {@link AUTO_LAYOUT_ATTEMPTS} deneme yapılır (deterministik tohumlar); en az kelimeyi dışarıda bırakan, sonra en küçük ızgaralı
 * sonuç seçilir. Hiçbir kelimeyle kesişemeyen kelime ızgaranın altına (araya bir boş satır bırakarak) yatay eklenir. Izgara
 * kelimelerin kapladığı alana kırpılır; 20'yi aşarsa form değişmeden döner (`unplaced` tüm kelimeler).
 */
export function autoLayout(form: CrosswordForm): AutoLayoutResult {
  const words = form.entries.map((e) => answerLetters(e.answer));
  const order = words
    .map((w, i) => ({ w, i }))
    .filter((x) => x.w.length >= CROSSWORD_LIMITS.minLength)
    .sort((a, b) => b.w.length - a.w.length || a.i - b.i)
    .map((x) => x.i);
  if (order.length === 0) return { form, unplaced: [] };

  let best: LayoutAttempt | null = null;
  for (let attempt = 0; attempt < AUTO_LAYOUT_ATTEMPTS; attempt++) {
    const result = layoutOnce(words, order, attempt === 0 ? null : seededRandom(attempt));
    if (!best || result.unplaced.length < best.unplaced.length
      || (result.unplaced.length === best.unplaced.length && result.size < best.size)) {
      best = result;
    }
    if (best.unplaced.length === 0 && attempt >= 8) break;
  }

  const { placed, unplaced } = best!;
  // Kesişemeyenler: mevcut alanın altına, araya boş satır bırakarak.
  const positions = [...placed.values()];
  let bottom = Math.max(...positions.map((p) => p.dir === 'down' ? p.row + words[p.index].length - 1 : p.row));
  const left = Math.min(...positions.map((p) => p.col));
  for (const i of unplaced) {
    bottom += 2;
    placed.set(i, { index: i, dir: 'across', row: bottom, col: left });
  }

  const cells = [...placed.values()].flatMap((p) => clueCells({ dir: p.dir, row: p.row, col: p.col, length: words[p.index].length }));
  const minR = Math.min(...cells.map(([r]) => r));
  const minC = Math.min(...cells.map(([, c]) => c));
  const rows = Math.max(...cells.map(([r]) => r)) - minR + 1;
  const cols = Math.max(...cells.map(([, c]) => c)) - minC + 1;
  if (rows > CROSSWORD_LIMITS.maxSize || cols > CROSSWORD_LIMITS.maxSize) {
    return { form, unplaced: order.map((i) => i + 1) };
  }
  return {
    form: {
      ...form,
      rows: Math.max(CROSSWORD_LIMITS.minSize, rows),
      cols: Math.max(CROSSWORD_LIMITS.minSize, cols),
      entries: form.entries.map((entry, i) => {
        const p = placed.get(i);
        return p ? { ...entry, dir: p.dir, row: p.row - minR, col: p.col - minC } : entry;
      }),
    },
    unplaced: unplaced.map((i) => i + 1),
  };
}

interface Placement {
  index: number;
  dir: CrosswordDir;
  row: number;
  col: number;
}

interface LayoutAttempt {
  placed: Map<number, Placement>;
  unplaced: number[];
  /** Kesişen kelimelerin kapladığı alanın uzun kenarı (küçük daha iyi). */
  size: number;
}

/** Tek yerleştirme denemesi; `random` null ise her kelime için en çok kesişen ilk aday seçilir. */
function layoutOnce(words: string[][], order: number[], random: (() => number) | null): LayoutAttempt {
  const cells = new Map<string, { ch: string; dirs: Set<CrosswordDir> }>();
  const placed = new Map<number, Placement>();
  const at = (r: number, c: number) => cells.get(`${r},${c}`);

  const fits = (w: string[], dir: CrosswordDir, row: number, col: number): number => {
    const [dr, dc] = dir === 'across' ? [0, 1] : [1, 0];
    if (at(row - dr, col - dc) || at(row + dr * w.length, col + dc * w.length)) return -1;
    let crossings = 0;
    for (let i = 0; i < w.length; i++) {
      const r = row + dr * i;
      const c = col + dc * i;
      const cell = at(r, c);
      if (cell) {
        if (cell.ch !== w[i] || cell.dirs.has(dir)) return -1;
        crossings++;
      } else if (at(r + dc, c + dr) || at(r - dc, c - dr)) {
        return -1;
      }
    }
    return crossings;
  };
  const put = (index: number, dir: CrosswordDir, row: number, col: number) => {
    const [dr, dc] = dir === 'across' ? [0, 1] : [1, 0];
    words[index].forEach((ch, i) => {
      const key = `${row + dr * i},${col + dc * i}`;
      const cell = cells.get(key) ?? { ch, dirs: new Set<CrosswordDir>() };
      cell.dirs.add(dir);
      cells.set(key, cell);
    });
    placed.set(index, { index, dir, row, col });
  };
  const tryPlace = (index: number): boolean => {
    const w = words[index];
    const candidates: { dir: CrosswordDir; row: number; col: number; crossings: number }[] = [];
    for (const [key, cell] of cells) {
      const [r, c] = key.split(',').map(Number);
      for (let k = 0; k < w.length; k++) {
        if (w[k] !== cell.ch) continue;
        for (const dir of ['across', 'down'] as CrosswordDir[]) {
          if (cell.dirs.has(dir)) continue;
          const row = dir === 'across' ? r : r - k;
          const col = dir === 'across' ? c - k : c;
          const crossings = fits(w, dir, row, col);
          if (crossings > 0) candidates.push({ dir, row, col, crossings });
        }
      }
    }
    if (candidates.length === 0) return false;
    const pick = random
      ? candidates[Math.floor(random() * candidates.length)]
      : candidates.reduce((a, b) => (b.crossings > a.crossings ? b : a));
    put(index, pick.dir, pick.row, pick.col);
    return true;
  };

  put(order[0], 'across', 0, 0);
  let pending = order.slice(1).filter((i) => !tryPlace(i));
  pending = pending.filter((i) => !tryPlace(i));

  const keys = [...cells.keys()].map((k) => k.split(',').map(Number));
  const height = Math.max(...keys.map(([r]) => r)) - Math.min(...keys.map(([r]) => r)) + 1;
  const width = Math.max(...keys.map(([, c]) => c)) - Math.min(...keys.map(([, c]) => c)) + 1;
  return { placed, unplaced: pending, size: Math.max(height, width) };
}

/** Deterministik sözde rastgele (mulberry32): aynı form her seferinde aynı yerleşimi verir. */
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

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, Math.trunc(Number.isFinite(value) ? value : min)));
}
