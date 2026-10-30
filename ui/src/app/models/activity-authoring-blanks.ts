/**
 * Issue #590 (dilim b): görselsiz boşluk türlerinin yazarlığı — öğretmenin yazdığı içerik ↔ plan + çözüm.
 *
 * - Boşluk doldurma (`fillText`), tablo (`fillTable`): metinde `[[cevap]]` boşluğun yeridir; `|` ile alternatif kabul edilen
 *   cevaplar yazılır (`[[Ankara|ankara'dır]]`). Tek cevap sayıysa kutu sayı kutusudur (kesir / tam sayılı kesir dahil,
 *   eşdeğerlik açık), değilse metin kutusu (#575 normalleştirmesi). Planda yer tutucu `[[s1]]` olur, cevap çözüme gider.
 * - Soru + kısa cevap (`shortAnswer`): her satır soru metni (kutunun etiketi) + kabul edilen cevaplar. Issue #586 (dilim d):
 *   isteğe bağlı şifre — gizli kelime + satır başına harf; kelimenin her harfi o harfi taşıyan bir soruya bağlanır (aynı harf
 *   birden çok soruda varsa sırayla dağıtılır), boşluk kelime arasıdır. Doğru cevaplanan sorunun harfi şifrede açılır.
 * - Kelime havuzu (`wordPool`): `dragPlace` boşluklu metin; her boşluk tek kartlık hedef, havuz = boşluk kelimeleri (aynı
 *   kelime tek kart, tekrar kullanımlı) + çeldiriciler.
 */
import { DRAG_PLACE, DRAG_PLACE_LIMITS, DragPlacePlanV1, cardsIn, parseDragPlaceSolution } from './drag-place';
import { LAYOUT_LIMITS, LAYOUT_PLACEHOLDER } from './interaction-layout';
import {
  SLOT_FILL,
  SLOT_FILL_LIMITS,
  SlotFillPlanV1,
  SlotFillSlot,
  SlotInput,
  SlotNumberSolution,
  SlotSolution,
  SlotTextSolution,
  isValidCipherLetter,
  parseSlotFillLetters,
  parseSlotFillSolution,
  solutionOf,
} from './slot-fill';
import { parseTrNumber } from '../shared/utils/slot-normalizer';

export type BlankKind = 'fillText' | 'shortAnswer' | 'fillTable' | 'wordPool';

export interface FillTextForm {
  kind: 'fillText';
  /** `[[cevap|alternatif]]` boşluklu metin. */
  text: string;
}

export interface ShortAnswerRowForm {
  question: string;
  /** Kabul edilen cevaplar, `|` ile ayrılmış. */
  answers: string;
  /** Issue #586: şifre harfi (tek harf / rakam; şifresizse boş). */
  letter?: string;
}

export interface ShortAnswerForm {
  kind: 'shortAnswer';
  rows: ShortAnswerRowForm[];
  /** Issue #586: gizli kelime (boş = şifre yok). */
  cipher?: string;
}

export interface FillTableForm {
  kind: 'fillTable';
  /** İlk satır başlık mı (başlıkta boşluk olmaz). */
  hasHeader: boolean;
  /** Hücreler (satır × kolon); `[[cevap]]` içeren hücre cevap kutusudur. */
  cells: string[][];
}

export interface WordPoolForm {
  kind: 'wordPool';
  /** `[[kelime]]` boşluklu metin (alternatif yok). */
  text: string;
  /** Havuza eklenen, hiçbir boşluğa ait olmayan kelimeler. */
  distractors: string[];
}

export type BlankForm = FillTextForm | ShortAnswerForm | FillTableForm | WordPoolForm;

export interface BlankError {
  key: string;
  params?: Record<string, unknown>;
}

export interface BlankDefinition {
  interactionType: typeof SLOT_FILL | typeof DRAG_PLACE;
  plan: string;
  solution: string;
}

/** Yazarlıktaki boşluk: `[[...]]` (içinde köşeli parantez yok). */
const AUTHOR_BLANK = /\[\[([^[\]]*)\]\]/g;
const ALTERNATIVE_SEPARATOR = '|';

/** Tablo hücresinde en az bir `[[...]]` boşluğu var mı. */
export function cellHasBlank(cell: string): boolean {
  return new RegExp(AUTHOR_BLANK.source).test(cell);
}

/**
 * Hücreyi boşluk yapar: zaten boşluk içeriyorsa dokunmaz, yoksa kırpılmış metnin tamamı cevap olur (`7` → `[[7]]`, boş
 * hücre → `[[]]`). Metnin kenarındaki boşluklar dışarıda kalır.
 */
export function cellToBlank(cell: string): string {
  if (cellHasBlank(cell)) return cell;
  const inner = cell.trim();
  const lead = cell.slice(0, cell.length - cell.trimStart().length);
  const trail = cell.slice(cell.trimEnd().length);
  return `${lead}[[${inner}]]${trail}`;
}

/** Hücredeki boşlukları düz metne çevirir; alternatifli boşlukta ilk cevap kalır (`[[bir|1]] metre` → `bir metre`). */
export function cellFromBlank(cell: string): string {
  return cell.replace(AUTHOR_BLANK, (_, inner: string) => inner.split(ALTERNATIVE_SEPARATOR)[0].trim());
}

export const BLANK_TABLE_LIMITS = { maxRows: LAYOUT_LIMITS.maxTableRows, maxColumns: LAYOUT_LIMITS.maxTableColumns } as const;

export function emptyBlankForm(kind: BlankKind): BlankForm {
  switch (kind) {
    case 'fillText':
      return { kind, text: '' };
    case 'shortAnswer':
      return { kind, rows: [{ question: '', answers: '' }] };
    case 'fillTable':
      return { kind, hasHeader: true, cells: [['', ''], ['', '']] };
    case 'wordPool':
      return { kind, text: '', distractors: [] };
  }
}

/** Boşluk içeriği → kabul edilen cevaplar (kırpılmış, boşlar atılır). */
export function blankAlternatives(raw: string): string[] {
  return raw
    .split(ALTERNATIVE_SEPARATOR)
    .map((a) => a.trim())
    .filter((a) => a.length > 0);
}

/** Metindeki boşlukların içerikleri (sırasıyla). */
export function authorBlanks(text: string): string[] {
  return [...text.matchAll(AUTHOR_BLANK)].map((m) => m[1]);
}

// ---------------------------------------------------------------- doğrulama

export function validateBlankForm(form: BlankForm): BlankError[] {
  const errors: BlankError[] = [];
  const add = (key: string, params?: Record<string, unknown>) => {
    if (!errors.some((e) => e.key === key)) errors.push({ key, params });
  };
  const checkAnswers = (raws: string[]) => {
    raws.forEach((raw) => {
      const alternatives = blankAlternatives(raw);
      if (alternatives.length === 0) add('blankAnswer');
      if (alternatives.length > SLOT_FILL_LIMITS.maxAcceptEntries)
        add('tooManyAlternatives', { max: SLOT_FILL_LIMITS.maxAcceptEntries });
      if (alternatives.some((a) => a.length > SLOT_FILL_LIMITS.maxAcceptLength))
        add('answerTooLong', { max: SLOT_FILL_LIMITS.maxAcceptLength });
    });
  };
  const checkSlotCount = (count: number) => {
    if (count === 0) add('noBlank');
    if (count > SLOT_FILL_LIMITS.maxSlots) add('blankCount', { max: SLOT_FILL_LIMITS.maxSlots });
  };

  switch (form.kind) {
    case 'fillText': {
      const blanks = authorBlanks(form.text);
      checkSlotCount(blanks.length);
      checkAnswers(blanks);
      if (layoutText(form.text, 's').length > LAYOUT_LIMITS.maxTextLength)
        add('textTooLong', { max: LAYOUT_LIMITS.maxTextLength });
      break;
    }
    case 'shortAnswer': {
      if (form.rows.length < 1 || form.rows.length > SLOT_FILL_LIMITS.maxSlots)
        add('questionCount', { min: 1, max: SLOT_FILL_LIMITS.maxSlots });
      if (form.rows.some((r) => !r.question.trim())) add('blankQuestion');
      if (form.rows.some((r) => r.question.trim().length > SLOT_FILL_LIMITS.maxLabelLength))
        add('labelTooLong', { max: SLOT_FILL_LIMITS.maxLabelLength });
      checkAnswers(form.rows.map((r) => r.answers));
      validateCipher(form, add);
      break;
    }
    case 'fillTable': {
      const { maxRows, maxColumns } = BLANK_TABLE_LIMITS;
      const bodyRows = form.hasHeader ? form.cells.slice(1) : form.cells;
      const columns = form.cells[0]?.length ?? 0;
      if (bodyRows.length < 1 || bodyRows.length > maxRows) add('tableRows', { min: 1, max: maxRows });
      if (columns < 1 || columns > maxColumns) add('tableColumns', { min: 1, max: maxColumns });
      if (form.hasHeader && authorBlanks(form.cells[0]?.join(' ') ?? '').length > 0) add('blankInHeader');
      const blanks = bodyRows.flatMap((row) => row.flatMap(authorBlanks));
      checkSlotCount(blanks.length);
      checkAnswers(blanks);
      let n = 0;
      const tooLong = form.cells.some((row) =>
        row.some((cell) => {
          const stored = cell.trim().replace(AUTHOR_BLANK, () => `[[s${++n}]]`);
          return stored.length > LAYOUT_LIMITS.maxCellLength;
        })
      );
      if (tooLong) add('cellTooLong', { max: LAYOUT_LIMITS.maxCellLength });
      break;
    }
    case 'wordPool': {
      const blanks = authorBlanks(form.text);
      if (blanks.length === 0) add('noBlank');
      if (blanks.length > DRAG_PLACE_LIMITS.maxTargets) add('poolBlankCount', { max: DRAG_PLACE_LIMITS.maxTargets });
      const words = blanks.map((b) => b.trim());
      if (words.some((w) => !w)) add('blankAnswer');
      if (words.some((w) => w.includes(ALTERNATIVE_SEPARATOR))) add('poolAlternatives');
      if (form.distractors.some((d) => !d.trim())) add('blankDistractor');
      const pool = [...words, ...form.distractors.map((d) => d.trim())];
      if (pool.some((w) => w.length > DRAG_PLACE_LIMITS.maxLabelLength))
        add('labelTooLong', { max: DRAG_PLACE_LIMITS.maxLabelLength });
      const unique = new Set(words.map(poolKey));
      if (unique.size + form.distractors.length > DRAG_PLACE_LIMITS.maxCards)
        add('poolCardCount', { max: DRAG_PLACE_LIMITS.maxCards });
      if (form.distractors.some((d) => unique.has(poolKey(d)))) add('distractorIsAnswer');
      if (layoutText(form.text, 't').length > LAYOUT_LIMITS.maxTextLength)
        add('textTooLong', { max: LAYOUT_LIMITS.maxTextLength });
      break;
    }
  }
  return errors;
}

/** Gizli kelimenin normal biçimi: kırpılmış, iç boşluklar teke inmiş, tr-TR büyük harf. */
export function cipherWord(raw: string | null | undefined): string {
  return (raw ?? '').trim().replace(/\s+/gu, ' ').toLocaleUpperCase('tr-TR');
}

function rowLetter(row: ShortAnswerRowForm): string {
  return (row.letter ?? '').trim().toLocaleUpperCase('tr-TR');
}

function validateCipher(form: ShortAnswerForm, add: (key: string, params?: Record<string, unknown>) => void): void {
  const word = cipherWord(form.cipher);
  const lettered = form.rows.filter((r) => rowLetter(r));
  if (!word) {
    if (lettered.length > 0) add('cipherWordMissing');
    return;
  }
  const chars = [...word];
  if (chars.length > SLOT_FILL_LIMITS.maxCipherCells) add('cipherTooLong', { max: SLOT_FILL_LIMITS.maxCipherCells });
  if (chars.some((c) => c !== ' ' && !isValidCipherLetter(c))) add('cipherWordChars');
  if (chars.filter((c) => c !== ' ').length < SLOT_FILL_LIMITS.minCipherLetters)
    add('cipherWordShort', { min: SLOT_FILL_LIMITS.minCipherLetters });
  if (form.rows.some((r) => rowLetter(r) && !isValidCipherLetter(rowLetter(r)))) add('cipherLetterInvalid');
  const available = new Set(lettered.map(rowLetter));
  const missing = chars.find((c) => c !== ' ' && !available.has(c));
  if (missing) add('cipherLetterMissing', { letter: missing });
  const inWord = new Set(chars);
  const unused = lettered.map(rowLetter).find((l) => !inWord.has(l));
  if (unused) add('cipherLetterUnused', { letter: unused });
}

/**
 * Gizli kelimenin hücreleri: her harf o harfi taşıyan sorulardan birine (aynı harf birden çok soruda varsa sırayla) bağlanır,
 * boşluk `null`. Önce {@link validateCipher} geçmeli.
 */
function cipherCells(form: ShortAnswerForm): (string | null)[] {
  const byLetter = new Map<string, number[]>();
  form.rows.forEach((row, i) => {
    const letter = rowLetter(row);
    if (letter) byLetter.set(letter, [...(byLetter.get(letter) ?? []), i]);
  });
  const used = new Map<string, number>();
  return [...cipherWord(form.cipher)].map((c) => {
    if (c === ' ') return null;
    const rows = byLetter.get(c) ?? [];
    const n = used.get(c) ?? 0;
    used.set(c, n + 1);
    return rows.length ? `s${rows[n % rows.length] + 1}` : null;
  });
}

// ---------------------------------------------------------------- form → plan + çözüm

/** Önce {@link validateBlankForm} geçmeli. */
export function buildBlankDefinition(form: BlankForm): BlankDefinition {
  switch (form.kind) {
    case 'fillText': {
      const blanks = authorBlanks(form.text);
      const built = blanks.map((raw) => slotFromAnswers(blankAlternatives(raw)));
      const layout = { kind: 'text', text: layoutText(form.text, 's') };
      return slotFillDefinition(layout, built, () => null);
    }
    case 'shortAnswer': {
      const built = form.rows.map((row) => slotFromAnswers(blankAlternatives(row.answers)));
      if (!cipherWord(form.cipher)) return slotFillDefinition({ kind: 'list' }, built, (i) => form.rows[i].question.trim());
      return slotFillDefinition({ kind: 'list' }, built, (i) => form.rows[i].question.trim(), {
        cells: cipherCells(form),
        letters: form.rows.map((row) => rowLetter(row) || null),
      });
    }
    case 'fillTable': {
      const cells = form.cells.map((row) => row.map((cell) => cell.trim()));
      const header = form.hasHeader ? cells[0] : null;
      const body = form.hasHeader ? cells.slice(1) : cells;
      const raws: string[] = [];
      let n = 0;
      const rows = body.map((row) =>
        row.map((cell) =>
          cell.replace(AUTHOR_BLANK, (_, raw: string) => {
            raws.push(raw);
            return `[[s${++n}]]`;
          })
        )
      );
      const built = raws.map((raw) => slotFromAnswers(blankAlternatives(raw)));
      const layout = header ? { kind: 'table', header, rows } : { kind: 'table', rows };
      return slotFillDefinition(layout, built, () => null);
    }
    case 'wordPool':
      return wordPoolDefinition(form);
  }
}

interface BuiltSlot {
  input: SlotInput;
  solution: SlotSolution;
}

/** Tek cevap birimsiz sayıysa sayı kutusu (biçim cevaptan), değilse metin kutusu. */
function slotFromAnswers(answers: string[]): BuiltSlot {
  const parsed = answers.length === 1 ? parseTrNumber(answers[0]) : null;
  if (parsed && parsed.unit === null) {
    const entry = parsed.form === 'fraction' ? 'fraction' : parsed.form === 'mixed' ? 'mixed' : 'plain';
    const solution: SlotNumberSolution = {
      value: answers[0],
      equivalence: true,
      requireSimplest: false,
      units: [],
      unitMode: 'none',
    };
    return { input: { kind: 'number', entry }, solution };
  }
  const solution: SlotTextSolution = { accept: answers, optionalSuffixes: [] };
  return { input: { kind: 'text' }, solution };
}

function slotFillDefinition(
  layout: object,
  built: BuiltSlot[],
  label: (index: number) => string | null,
  cipher?: { cells: (string | null)[]; letters: (string | null)[] }
): BlankDefinition {
  const slots = built.map((slot, i) => {
    const text = label(i);
    return { id: `s${i + 1}`, ...(text ? { label: text } : {}), input: slot.input };
  });
  const solution: Record<string, SlotSolution & { letter?: string }> = {};
  // Sunucu kuralı: harf yalnız şifrede geçen kutuda (doğrulama kullanılmayan harfi zaten reddeder).
  const inCipher = new Set(cipher?.cells.filter((c): c is string => !!c));
  built.forEach((slot, i) => {
    const id = `s${i + 1}`;
    const letter = cipher?.letters[i];
    solution[id] = letter && inCipher.has(id) ? { ...slot.solution, letter } : slot.solution;
  });
  const plan = cipher ? { version: 1, type: SLOT_FILL, layout, slots, cipher: { cells: cipher.cells } } : { version: 1, type: SLOT_FILL, layout, slots };
  return { interactionType: SLOT_FILL, plan: JSON.stringify(plan), solution: JSON.stringify(solution) };
}

function wordPoolDefinition(form: WordPoolForm): BlankDefinition {
  const words = authorBlanks(form.text).map((w) => w.trim());
  const cardOf = new Map<string, string>();
  const cards: { id: string; label: string }[] = [];
  words.forEach((word) => {
    const key = poolKey(word);
    if (cardOf.has(key)) return;
    const id = `k${cards.length + 1}`;
    cardOf.set(key, id);
    cards.push({ id, label: word });
  });
  form.distractors.forEach((d) => cards.push({ id: `k${cards.length + 1}`, label: d.trim() }));
  const targets = words.map((_, i) => ({ id: `t${i + 1}`, capacity: 1 }));
  const solution: Record<string, string[]> = {};
  words.forEach((word, i) => (solution[`t${i + 1}`] = [cardOf.get(poolKey(word))!]));
  // Aynı kelime birden çok boşluğa aitse tek kart tekrar kullanılır (aynı yazılı iki kart yer değiştirince yanlış sayılmasın).
  const reuse = cardOf.size < words.length;
  const plan = {
    version: 1,
    type: DRAG_PLACE,
    mode: 'place',
    reuse,
    layout: { kind: 'text', text: layoutText(form.text, 't') },
    targets,
    cards,
  };
  return { interactionType: DRAG_PLACE, plan: JSON.stringify(plan), solution: JSON.stringify(solution) };
}

/** Yazarlık metni → düzen metni: n. boşluk `[[<önek>n]]`. */
function layoutText(text: string, prefix: string): string {
  let n = 0;
  return text.trim().replace(AUTHOR_BLANK, () => `[[${prefix}${++n}]]`);
}

function poolKey(word: string): string {
  return word.trim().toLocaleLowerCase('tr');
}

// ---------------------------------------------------------------- plan + çözüm → form (düzenleme)

/** Kayıtlı slotFill planı → form; düzensiz (görselli) ya da formun ifade edemediği plan null. */
export function parseSlotFillBlanks(plan: SlotFillPlanV1 | null, solutionJson: string | null | undefined): BlankForm | null {
  if (!plan?.layout || plan.slots.some((s) => s.box)) return null;
  const solution = parseSlotFillSolution(solutionJson, plan);
  const answers = new Map<string, string>();
  for (const slot of plan.slots) {
    const text = answerText(slot, solutionOf(solution, slot.id));
    if (text === null) return null;
    answers.set(slot.id, text);
  }
  const fill = (value: string) => value.replace(LAYOUT_PLACEHOLDER, (_, id: string) => `[[${answers.get(id) ?? ''}]]`);

  switch (plan.layout.kind) {
    case 'text':
      return { kind: 'fillText', text: fill(plan.layout.text) };
    case 'list': {
      const letters = parseSlotFillLetters(solutionJson);
      const cells = plan.cipher?.cells;
      const rows = plan.slots.map((s) => {
        const row = { question: s.label ?? '', answers: answers.get(s.id) ?? '' };
        return cells ? { ...row, letter: letters.get(s.id) ?? '' } : row;
      });
      if (!cells) return { kind: 'shortAnswer', rows };
      const word = cells.map((c) => (c ? (letters.get(c) ?? '') : ' ')).join('');
      return { kind: 'shortAnswer', rows, cipher: word };
    }
    case 'table': {
      const header = plan.layout.header ?? null;
      const rows = plan.layout.rows.map((row) => row.map(fill));
      return { kind: 'fillTable', hasHeader: !!header, cells: header ? [[...header], ...rows] : rows };
    }
  }
}

/** Kutunun çözümü yazarlık sözdizimiyle; form karşılığı yoksa (seçim, palet, birim, sonek, eşdeğerlik kapalı) null. */
function answerText(slot: SlotFillSlot, solution: SlotSolution | null): string | null {
  if (!solution) return '';
  if (slot.input.kind === 'number') {
    const s = solution as SlotNumberSolution;
    if (slot.input.unit || !s.equivalence || s.requireSimplest || s.unitMode !== 'none' || s.units.length > 0) return null;
    return s.value;
  }
  if (slot.input.kind === 'text') {
    const s = solution as SlotTextSolution;
    if (s.optionalSuffixes.length > 0) return null;
    return s.accept.join(ALTERNATIVE_SEPARATOR);
  }
  return null;
}

/** Kayıtlı boşluklu metin (dragPlace) → kelime havuzu formu; düzensiz plan null. */
export function parseWordPool(plan: DragPlacePlanV1 | null, solutionJson: string | null | undefined): WordPoolForm | null {
  if (plan?.layout?.kind !== 'text' || plan.mode !== 'place') return null;
  if (plan.targets.some((t) => t.box || t.capacity !== 1) || plan.cards.some((c) => c.box || !c.label)) return null;
  const solution = parseDragPlaceSolution(solutionJson, plan);
  const label = new Map(plan.cards.map((c) => [c.id, c.label ?? '']));
  const used = new Set<string>();
  const text = plan.layout.text.replace(LAYOUT_PLACEHOLDER, (_, id: string) => {
    const cardId = cardsIn(solution, id)[0];
    if (cardId) used.add(cardId);
    return `[[${cardId ? (label.get(cardId) ?? '') : ''}]]`;
  });
  return { kind: 'wordPool', text, distractors: plan.cards.filter((c) => !used.has(c.id)).map((c) => c.label ?? '') };
}
