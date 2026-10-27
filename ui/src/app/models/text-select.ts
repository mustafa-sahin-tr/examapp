/**
 * Issue #582: metin üzerinde işaretleme (`InteractionType = textSelect`, bileşen D) — metin token'larına dokunarak altını çiz /
 * yuvarlak içine al / yanlışı bul (`select`), `( )` boşluklarına paletten noktalama koy (`punctuation`), yanlışı seçip
 * düzeltmesini yaz (`correct`). Plan öğrenciye gider, çözüm gitmez (#570). Kurallar sunucudaki `TextSelectGrader` ile aynı;
 * satır → token bölme `TextSelectTokenizer` ile birebir (ortak fixture `shared/fixtures/text-tokenize.v1.json`).
 * Puansız: kontrol seçili token başına doğru / fazla / yanlış düzeltme ve bulunan / kalan / fazla sayıları döner.
 */
import { InteractionBox, copyBox, isInteractionBox, isInteractionType, isValidInteractionId } from './interaction-ids';
import { SLOT_WHITESPACE_CLASS, isBlankSlotValue } from './slot-fill';
import { isTextAnswerCorrect } from '../shared/utils/slot-normalizer';

export const TEXT_SELECT = 'textSelect' as const;

export type TextSelectMode = 'select' | 'punctuation' | 'correct';
export type TextSelectStyle = 'underline' | 'circle' | 'highlight';

export const TEXT_SELECT_MODES: readonly TextSelectMode[] = ['select', 'punctuation', 'correct'];
export const TEXT_SELECT_STYLES: readonly TextSelectStyle[] = ['underline', 'circle', 'highlight'];

/** Backend `TextSelectGrader` sınırları. */
export const TEXT_SELECT_LIMITS = {
  minTokens: 1,
  maxTokens: 600,
  maxTokenTextLength: 64,
  maxLines: 300,
  maxBoxes: 3,
  minGaps: 1,
  maxGaps: 100,
  minSymbols: 1,
  maxSymbols: 20,
  maxSymbolLength: 8,
  minTargets: 1,
  maxTargets: 100,
  maxTargetTokens: 8,
  maxAcceptEntries: 20,
  maxAcceptLength: 100,
  maxSuffixEntries: 10,
  /** Öğrencinin tek token'a yazabileceği en uzun düzeltme / sembol. */
  maxValueLength: 200,
} as const;

/** Gap token'ın metni (tokenizer `( )` / `()` / `(   )` hepsini buna çevirir). */
export const TEXT_SELECT_GAP_TEXT = '( )';

/**
 * Fazla seçim eşiği (sunucu `TextSelectGrader.TooManyExtraMin`): fazla seçim `max(3, hedef sayısı)`nı aşarsa geri bildirim satır
 * içermez (`rows: []`, `tooMany: true`) — her kelimeyi seçip hangisinin doğru olduğunu öğrenmek işe yaramasın; sayılar kalır.
 */
export const TOO_MANY_EXTRA_MIN = 3;

/** Varsayılan noktalama paleti (Claude palet vermezse sunucunun koyduğu; yazarlıkta da bu). */
export const DEFAULT_TEXT_SELECT_PALETTE: readonly string[] = ['.', ',', '?', '!', ':', ';', '...', '-'];

export interface TextSelectToken {
  id: string;
  text: string;
  /** 0 tabanlı satır (plan sırasıyla azalmaz). */
  line: number;
  /** 1..3 kutu (satır sonunda tirelenen kelime birden çok kutu). Soru bölgesine göre. */
  boxes: InteractionBox[];
  gap: boolean;
}

export interface TextSelectPlanV1 {
  version: 1;
  type: typeof TEXT_SELECT;
  mode: TextSelectMode;
  /** Yalnız görünüm; planda yoksa `highlight`. */
  style: TextSelectStyle;
  /** Yalnız `punctuation`'da dolu. */
  palette: string[];
  tokens: TextSelectToken[];
}

export interface TextSelectTarget {
  tokenIds: string[];
  /** Yalnız `correct`. */
  accept: string[];
  optionalSuffixes: string[];
}

/** Çözüm: select / correct hedefler, punctuation gap → kabul edilen semboller. */
export interface TextSelectSolution {
  targets: TextSelectTarget[];
  gaps: Record<string, string[]>;
}

/** Öğrencinin cevabı: seçili token'lar (plan sırası) ve token / gap → değer (prototipsiz). */
export interface TextSelectAnswer {
  selected: string[];
  values: Record<string, string>;
}

export interface TextSelectAnswerPayload {
  version: 1;
  type: typeof TEXT_SELECT;
  selected: string[];
  values: Record<string, string>;
}

export type TextSelectReason = 'extra' | 'value';

export interface TextSelectFeedbackRow {
  id: string;
  correct: boolean;
  reason?: TextSelectReason | null;
}

/** Kontrol sonucu (sunucunun `feedback`'i ya da örnek soruda istemci kontrolü). Sayılar yalnız select / correct'te. */
export interface TextSelectFeedback {
  rows: TextSelectFeedbackRow[];
  found: number;
  missing: number;
  extra: number;
  /** Fazla seçim eşiği aşıldı: satırlar gönderilmedi, işaretler gösterilmez. */
  tooMany: boolean;
}

export function isTextSelectType(type: unknown): boolean {
  return isInteractionType(type, TEXT_SELECT);
}

function emptyRecord<T>(): Record<string, T> {
  return Object.create(null) as Record<string, T>;
}

export function emptyTextSelectAnswer(): TextSelectAnswer {
  return { selected: [], values: emptyRecord<string>() };
}

/** Token / gap değeri (yalnız kendi özelliği; yoksa boş). */
export function textValueOf(answer: TextSelectAnswer, id: string): string {
  return Object.hasOwn(answer.values, id) && typeof answer.values[id] === 'string' ? answer.values[id] : '';
}

/** Gap'in kabul edilen sembolleri (yalnız kendi özelliği). */
export function gapAcceptOf(solution: TextSelectSolution, gapId: string): string[] {
  return Object.hasOwn(solution.gaps, gapId) ? solution.gaps[gapId] : [];
}

function stringList(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((v): v is string => typeof v === 'string') : [];
}

function isMode(value: unknown): value is TextSelectMode {
  return typeof value === 'string' && (TEXT_SELECT_MODES as readonly string[]).includes(value);
}

function isStyle(value: unknown): value is TextSelectStyle {
  return typeof value === 'string' && (TEXT_SELECT_STYLES as readonly string[]).includes(value);
}

/** Plan JSON'unu güvenli ayrıştırır; tür / mod farklı, bozuk ya da token yoksa null. Bozuk token atlanır. */
export function parseTextSelectPlan(json: string | null | undefined): TextSelectPlanV1 | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isTextSelectType(parsed['type']) || !isMode(parsed['mode'])) return null;
    const mode = parsed['mode'];
    const seen = new Set<string>();
    const tokens: TextSelectToken[] = [];
    for (const raw of Array.isArray(parsed['tokens']) ? (parsed['tokens'] as unknown[]) : []) {
      if (!raw || typeof raw !== 'object') continue;
      const t = raw as Record<string, unknown>;
      const id = t['id'];
      if (!isValidInteractionId(id) || seen.has(id)) continue;
      const boxes = (Array.isArray(t['boxes']) ? (t['boxes'] as unknown[]) : []).filter(isInteractionBox).map(copyBox);
      if (boxes.length === 0) continue;
      const gap = t['gap'] === true;
      const text = typeof t['text'] === 'string' ? t['text'] : '';
      if (!gap && !text.trim()) continue;
      const line = typeof t['line'] === 'number' && Number.isFinite(t['line']) ? Math.max(0, Math.floor(t['line'])) : 0;
      seen.add(id);
      tokens.push({ id, text: gap ? text || TEXT_SELECT_GAP_TEXT : text, line, boxes, gap });
    }
    if (tokens.length === 0) return null;
    const palette =
      mode === 'punctuation' ? [...new Set(stringList(parsed['palette']).map((s) => s.trim()).filter((s) => !!s))] : [];
    return {
      version: 1,
      type: TEXT_SELECT,
      mode,
      style: isStyle(parsed['style']) ? parsed['style'] : 'highlight',
      palette,
      tokens,
    };
  } catch {
    return null;
  }
}

/**
 * Çözüm JSON'u — bozuk / boşsa boş çözüm. Plan verilirse sunucunun toleranslı okuması: plan dışı / gap token'lar ve başka
 * hedefte kullanılmış token'lar atlanır; correct'te kabulsüz hedef atlanır; gap'ler yalnız plandaki gap'ler.
 */
export function parseTextSelectSolution(json: string | null | undefined, plan?: TextSelectPlanV1 | null): TextSelectSolution {
  const result: TextSelectSolution = { targets: [], gaps: emptyRecord<string[]>() };
  if (!json) return result;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return result;
    const words = plan ? new Set(plan.tokens.filter((t) => !t.gap).map((t) => t.id)) : null;
    const gapIds = plan ? new Set(plan.tokens.filter((t) => t.gap).map((t) => t.id)) : null;
    const used = new Set<string>();
    for (const raw of Array.isArray(parsed['targets']) ? (parsed['targets'] as unknown[]) : []) {
      if (!raw || typeof raw !== 'object') continue;
      const target = raw as Record<string, unknown>;
      const tokenIds = stringList(target['tokenIds']).filter(
        (id) => isValidInteractionId(id) && (!words || words.has(id)) && !used.has(id) && (used.add(id), true)
      );
      if (tokenIds.length === 0) continue;
      const accept = stringList(target['accept']);
      if (plan?.mode === 'correct' && accept.length === 0) continue;
      result.targets.push({ tokenIds, accept, optionalSuffixes: stringList(target['optionalSuffixes']) });
    }
    const gaps = parsed['gaps'];
    if (gaps && typeof gaps === 'object' && !Array.isArray(gaps)) {
      Object.entries(gaps as Record<string, unknown>).forEach(([gapId, entry]) => {
        if (!isValidInteractionId(gapId) || (gapIds && !gapIds.has(gapId))) return;
        if (!entry || typeof entry !== 'object' || Array.isArray(entry)) return;
        result.gaps[gapId] = stringList((entry as Record<string, unknown>)['accept']);
      });
    }
    return result;
  } catch {
    return result;
  }
}

/**
 * Cevabı plana göre temizler: select / correct'te seçim yalnız plandaki gap olmayan token'lar (plan sırasıyla, tekrarsız);
 * correct'te değerler yalnız seçili token'lar; punctuation'da seçim yok, değerler yalnız gap'ler ve palet sembolleri.
 * Boş değer yazılmaz. Plan yoksa yalnız id deseni ve tekrar denetlenir.
 */
export function normalizeTextSelectAnswer(
  selected: unknown,
  values: unknown,
  plan?: TextSelectPlanV1 | null
): TextSelectAnswer {
  const answer = emptyTextSelectAnswer();
  const mode = plan?.mode ?? null;
  const rawSelected = stringList(selected).filter(isValidInteractionId);
  if (mode !== 'punctuation') {
    if (plan) {
      const chosen = new Set(rawSelected);
      answer.selected = plan.tokens.filter((t) => !t.gap && chosen.has(t.id)).map((t) => t.id);
    } else {
      answer.selected = [...new Set(rawSelected)];
    }
  }
  if (values && typeof values === 'object' && !Array.isArray(values)) {
    const selectedSet = new Set(answer.selected);
    const gapIds = plan ? new Set(plan.tokens.filter((t) => t.gap).map((t) => t.id)) : null;
    const palette = plan ? new Set(plan.palette) : null;
    Object.entries(values as Record<string, unknown>).forEach(([id, value]) => {
      if (!isValidInteractionId(id) || typeof value !== 'string' || isBlankSlotValue(value)) return;
      const clipped = clipText(value, TEXT_SELECT_LIMITS.maxValueLength);
      if (mode === 'select') return;
      if (mode === 'correct' && !selectedSet.has(id)) return;
      if (mode === 'punctuation' && (!gapIds?.has(id) || !palette?.has(clipped.trim()))) return;
      answer.values[id] = mode === 'punctuation' ? clipped.trim() : clipped;
    });
  }
  return answer;
}

/** Öğrenci cevabı (`{"type":"textSelect","selected":[...],"values":{...}}`) — başka tür / bozuksa boş. */
export function parseTextSelectAnswer(json: string | null | undefined, plan?: TextSelectPlanV1 | null): TextSelectAnswer {
  if (!json) return emptyTextSelectAnswer();
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isTextSelectType(parsed['type'])) return emptyTextSelectAnswer();
    return normalizeTextSelectAnswer(parsed['selected'], parsed['values'], plan);
  } catch {
    return emptyTextSelectAnswer();
  }
}

/** Seçim ve değer yok = boş cevap (sunucu da `Normalize` ile null sayar). */
export function isEmptyTextSelectAnswer(answer: TextSelectAnswer): boolean {
  return answer.selected.length === 0 && Object.keys(answer.values).every((id) => isBlankSlotValue(answer.values[id]));
}

/** Payload: değerler kırpılmaz (düzeltme olduğu gibi gider), boş değer yazılmaz. */
export function buildTextSelectPayload(answer: TextSelectAnswer): string {
  const values = emptyRecord<string>();
  Object.keys(answer.values).forEach((id) => {
    const value = answer.values[id];
    if (typeof value === 'string' && !isBlankSlotValue(value)) values[id] = value;
  });
  const payload: TextSelectAnswerPayload = { version: 1, type: TEXT_SELECT, selected: [...answer.selected], values };
  return JSON.stringify(payload);
}

/** Seçimsiz ve değersiz cevap: sunucu boş cevap sayar, ekranlar da cevapsız saymalı. */
export function isBlankTextSelectPayload(json: string | null | undefined): boolean {
  if (!json) return true;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isTextSelectType(parsed['type'])) return false;
    return isEmptyTextSelectAnswer(normalizeTextSelectAnswer(parsed['selected'], parsed['values']));
  } catch {
    return false;
  }
}

/** Seçimi değiştirilmiş yeni cevap (plan sırası korunur); seçimden çıkan token'ın düzeltmesi silinir. */
export function withSelection(answer: TextSelectAnswer, plan: TextSelectPlanV1, tokenId: string, on: boolean): TextSelectAnswer {
  const chosen = new Set(answer.selected);
  if (on) chosen.add(tokenId);
  else chosen.delete(tokenId);
  const values = emptyRecord<string>();
  Object.keys(answer.values).forEach((id) => {
    if (on || id !== tokenId) values[id] = answer.values[id];
  });
  return { selected: plan.tokens.filter((t) => !t.gap && chosen.has(t.id)).map((t) => t.id), values };
}

/** Değeri değiştirilmiş yeni cevap; boş değer girişi siler. */
export function withTextValue(answer: TextSelectAnswer, id: string, value: string): TextSelectAnswer {
  const values = emptyRecord<string>();
  Object.keys(answer.values).forEach((key) => {
    if (key !== id) values[key] = answer.values[key];
  });
  if (!isBlankSlotValue(value)) values[id] = clipText(value, TEXT_SELECT_LIMITS.maxValueLength);
  return { selected: [...answer.selected], values };
}

/** Sunucu `feedback` JSON'u (`{"rows":[...],"found","missing","extra"}`) — bozuksa null. */
export function parseTextSelectFeedback(json: string | null | undefined): TextSelectFeedback | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !Array.isArray(parsed['rows'])) return null;
    const count = (key: string) => (typeof parsed[key] === 'number' ? Math.max(0, parsed[key] as number) : 0);
    const tooMany = parsed['tooMany'] === true;
    return {
      rows: tooMany ? [] : textSelectRows(parsed['rows']),
      found: count('found'),
      missing: count('missing'),
      extra: count('extra'),
      tooMany,
    };
  } catch {
    return null;
  }
}

/** Satır listesini (id, correct, reason) temizler. */
export function textSelectRows(value: unknown): TextSelectFeedbackRow[] {
  if (!Array.isArray(value)) return [];
  return (value as unknown[]).flatMap((raw) => {
    if (!raw || typeof raw !== 'object') return [];
    const row = raw as Record<string, unknown>;
    if (typeof row['id'] !== 'string' || typeof row['correct'] !== 'boolean') return [];
    const reason = row['reason'] === 'extra' || row['reason'] === 'value' ? row['reason'] : null;
    return [{ id: row['id'], correct: row['correct'], reason }];
  });
}

/**
 * Etkinlik oynatıcısının sakladığı satırlardan (`ActivityFeedbackRow`, `reason` korunur) ve kontrol sayılarından geri
 * bildirim: `found` = doğru sayısı, `extra` = `reason: extra` satırları, `missing` = toplam − bulunan − fazla (sunucuda
 * toplam = hedef + fazla).
 */
export function textSelectFeedbackFromCounts(rows: unknown, correctCount: number, totalCount: number): TextSelectFeedback {
  const clean = textSelectRows(rows);
  const extra = clean.filter((r) => r.reason === 'extra').length;
  return { rows: clean, found: correctCount, missing: Math.max(0, totalCount - correctCount - extra), extra, tooMany: false };
}

/**
 * Sunucu `TextSelectGrader.Grade`'in istemci ikizi (örnek sorunun anlık geri bildirimi, yazarlık / testler). Çözüm boşsa null
 * (puanlanamaz). punctuation: her gap için satır (plan sırası), sayılar 0.
 */
export function gradeTextSelect(
  plan: TextSelectPlanV1,
  solution: TextSelectSolution,
  answer: TextSelectAnswer
): (TextSelectFeedback & { correctCount: number; totalCount: number }) | null {
  if (plan.mode === 'punctuation') {
    const gaps = plan.tokens.filter((t) => t.gap);
    if (gaps.length === 0 || Object.keys(solution.gaps).length === 0) return null;
    const rows = gaps.map((gap) => {
      const value = textValueOf(answer, gap.id).trim();
      const accept = gapAcceptOf(solution, gap.id);
      return { id: gap.id, correct: !!value && accept.some((a) => a.trim() === value), reason: null };
    });
    const correctCount = rows.filter((r) => r.correct).length;
    return { rows, found: 0, missing: 0, extra: 0, tooMany: false, correctCount, totalCount: rows.length };
  }
  const targets = solution.targets;
  if (targets.length === 0) return null;
  const targetOf = new Map<string, number>();
  targets.forEach((target, index) => target.tokenIds.forEach((id) => targetOf.set(id, index)));
  const selected = new Set(answer.selected);
  const found = targets.map(() => false);
  for (const id of answer.selected) {
    const index = targetOf.get(id);
    if (index === undefined) continue;
    if (plan.mode === 'select') found[index] = true;
    else if (isTextAnswerCorrect({ accept: targets[index].accept, optionalSuffixes: targets[index].optionalSuffixes }, textValueOf(answer, id))) {
      found[index] = true;
    }
  }
  const rows: TextSelectFeedbackRow[] = [];
  let extra = 0;
  for (const token of plan.tokens) {
    if (token.gap || !selected.has(token.id)) continue;
    const index = targetOf.get(token.id);
    if (index === undefined) {
      extra++;
      rows.push({ id: token.id, correct: false, reason: 'extra' });
    } else {
      rows.push(found[index] ? { id: token.id, correct: true, reason: null } : { id: token.id, correct: false, reason: 'value' });
    }
  }
  const foundCount = found.filter((f) => f).length;
  const tooMany = extra > Math.max(TOO_MANY_EXTRA_MIN, targets.length);
  return {
    rows: tooMany ? [] : rows,
    found: foundCount,
    missing: targets.length - foundCount,
    extra,
    tooMany,
    correctCount: foundCount,
    totalCount: targets.length + extra,
  };
}

/**
 * Metni en çok `max` UTF-16 kod birimine keser (sunucu uzunluğu böyle sayar) ama vekil (surrogate) çiftini bölmez: kesim bir
 * çiftin ortasına düşerse yarım karakter atılır.
 */
export function clipText(value: string, max: number): string {
  if (value.length <= max) return value;
  const cut = value.slice(0, max);
  const last = cut.charCodeAt(cut.length - 1);
  return last >= 0xd800 && last <= 0xdbff ? cut.slice(0, -1) : cut;
}

/** Token'ları satır numarasına göre gruplar (metin görünümü; plan sırası korunur). */
export function tokensByLine(tokens: readonly TextSelectToken[]): Array<{ line: number; tokens: TextSelectToken[] }> {
  const lines: Array<{ line: number; tokens: TextSelectToken[] }> = [];
  for (const token of tokens) {
    const last = lines[lines.length - 1];
    if (last && last.line === token.line) last.tokens.push(token);
    else lines.push({ line: token.line, tokens: [token] });
  }
  return lines;
}

/**
 * Hedef gruplama (yazarlık): verilen token'lar plan sırasıyla dizilir; aralarında başka (gap olmayan) token olmayan ardışık
 * token'lar tek hedef olur ("Sonun da" → bir hedef). Gap token'lar sırayı bozmaz ama hedefe girmez.
 */
export function groupConsecutiveTokens(tokenIds: Iterable<string>, tokens: readonly TextSelectToken[]): string[][] {
  const chosen = new Set(tokenIds);
  const groups: string[][] = [];
  let current: string[] | null = null;
  for (const token of tokens) {
    if (token.gap) continue;
    if (chosen.has(token.id)) {
      if (!current) {
        current = [];
        groups.push(current);
      }
      current.push(token.id);
    } else {
      current = null;
    }
  }
  return groups;
}

// ---------------------------------------------------------------------------------------------------------------------------
// Satır → token bölme (sunucu `TextSelectTokenizer` ikizi; fixture ile kilitli)
// ---------------------------------------------------------------------------------------------------------------------------

export interface TextLineInput {
  text: string | null | undefined;
  /** Satır kutusu (soru bölgesine göre); önce tam sayıya yuvarlanır. */
  box: InteractionBox;
}

const WHITESPACE_RUN = new RegExp(`${SLOT_WHITESPACE_CLASS}+`, 'gu');
/** Sunucudaki `PiecePatternSource` (UTF-16 kod birimiyle; `u` kipi yok — .NET ile aynı indeksler). */
export const TEXT_PIECE_PATTERN_SOURCE = '\\( ?\\)|(?:(?!\\( ?\\))[^ ])+';
const GAP_PIECE = /^\( ?\)$/;
const LETTER = /^\p{L}$/u;

/** Tokenizer'ın metin ön işlemesi: NFC, boşluk dizileri tek boşluk, baş / son boşluk kırpılır. */
export function normalizeTextLine(text: string | null | undefined): string {
  if (!text) return '';
  return text.normalize('NFC').replace(WHITESPACE_RUN, ' ').replace(/^ +| +$/g, '');
}

function roundLineBox(box: InteractionBox): InteractionBox {
  return { x: Math.round(box.x), y: Math.round(box.y), width: Math.round(box.width), height: Math.round(box.height) };
}

function slice(line: InteractionBox, length: number, start: number, end: number): InteractionBox {
  if (length <= 0) return { ...line };
  const left = line.x + Math.floor((line.width * start) / length);
  const right = line.x + Math.floor((line.width * end) / length);
  return { x: left, y: line.y, width: Math.max(1, right - left), height: line.height };
}

function endsWithLineBreakHyphen(text: string): boolean {
  return text.length >= 2 && text[text.length - 1] === '-' && LETTER.test(text[text.length - 2]);
}

/**
 * Satırları token'lara böler: kelimeler `t1..`, gap'ler `g1..`; `line` = girdi satırının sırası (boş satır sırayı tüketir).
 * Satır sonunda harf + `-` ile biten kelime, sonraki satırın ilk parçasıyla (gap değilse) tiresiz birleşir (en çok 3 kutu).
 * Sınır uygulanmaz (çağıran keser).
 */
export function tokenizeTextLines(lines: readonly TextLineInput[]): TextSelectToken[] {
  const drafts: Array<{ text: string; line: number; gap: boolean; boxes: InteractionBox[] }> = [];
  let open: (typeof drafts)[number] | null = null;
  lines.forEach((input, lineIndex) => {
    const text = normalizeTextLine(input.text);
    const box = roundLineBox(input.box);
    const pieces = [...text.matchAll(new RegExp(TEXT_PIECE_PATTERN_SOURCE, 'g'))];
    pieces.forEach((piece, j) => {
      const value = piece[0];
      const start = piece.index ?? 0;
      const gap = GAP_PIECE.test(value);
      const tokenBox = slice(box, text.length, start, start + value.length);
      if (j === 0 && open && !gap) {
        open.text = open.text.slice(0, -1) + value;
        open.boxes.push(tokenBox);
        return;
      }
      drafts.push({ text: gap ? TEXT_SELECT_GAP_TEXT : value, line: lineIndex, gap, boxes: [tokenBox] });
    });
    open = null;
    const last = drafts[drafts.length - 1];
    if (
      pieces.length > 0 &&
      last &&
      !last.gap &&
      last.boxes.length < TEXT_SELECT_LIMITS.maxBoxes &&
      endsWithLineBreakHyphen(last.text)
    ) {
      open = last;
    }
  });
  let words = 0;
  let gaps = 0;
  return drafts.map((d) => ({ id: d.gap ? `g${++gaps}` : `t${++words}`, text: d.text, line: d.line, gap: d.gap, boxes: d.boxes }));
}
