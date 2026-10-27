/**
 * Issue #575: cevap kutusu normalleştirmesi — sunucudaki `SlotAnswerNormalizer` (C#) ile birebir ikiz. Kurallar ortak
 * fixture'la (`shared/fixtures/slot-normalization.v1.json`) iki tarafta da sınanır; fixture doğruluk kaynağıdır.
 * İstemcide yalnız örnek sorunun anlık geri bildirimi ve yazarlık önizlemesi için kullanılır; asıl kontrol sunucuda.
 *
 * - **Sayı** (TR, yalnız ASCII rakam): tam sayı `12`, negatif `-3`, ondalık virgül `0,75`, binlik nokta `1.250` / `1.250,5`,
 *   tek noktalı ondalık `0.75` (noktadan sonra TAM 3 hane değilse; 3 hane = binlik), kesir `a/b`, tam sayılı kesir `2 3/5` /
 *   `2 tam 3/5` (kesir kısmı basit kesir olmalı), yüzde `%75` / `75%` / `% 75` (yüzdeye kesir / birim yazılmaz). BigInt ile
 *   kesin rasyonel. Birim = sayıdan sonraki harf dizisi (`5cm` == `5 CM`).
 * - **Metin**: NFC, Türkçe küçük harf (İ→i, I→ı), kesme işaretleri birleşir, boşluk teke iner, sondaki `.,;:!?` atılır.
 *   Aksan katlaması yok (`ılık` ≠ `ilik`). İsteğe bağlı ek: cevap = kabul + " " + ek.
 * - **Palet**: NFC, tüm boşluklar silinir, büyük/küçük harf duyarlı birebir.
 */
import {
  SLOT_WHITESPACE_CLASS,
  SlotKind,
  SlotNumberSolution,
  SlotPaletteSolution,
  SlotSelectSolution,
  SlotSolution,
  SlotTextSolution,
  isBlankSlotValue,
} from '../../models/slot-fill';

export type NumberForm = 'integer' | 'decimal' | 'fraction' | 'mixed' | 'percent';

export interface ParsedNumber {
  /** İşaretli, indirgenmiş pay. */
  num: bigint;
  /** İndirgenmiş payda (> 0). */
  den: bigint;
  form: NumberForm;
  /** Kesir / tam sayılı kesir en sade mi (diğer biçimlerde true). */
  simplest: boolean;
  /** Normalize birim; yoksa null. */
  unit: string | null;
}

/** Ayrıştırmaya girecek en uzun girdi (dev BigInt'e karşı; sunucuyla aynı). */
export const MAX_NUMBER_INPUT_LENGTH = 64;

/** Sunucunun açık boşluk sınıfı (bkz. `SLOT_WHITESPACE_CLASS`); JS `\s` U+0085'i kapsamaz. */
const WHITESPACE = new RegExp(`${SLOT_WHITESPACE_CLASS}+`, 'gu');
const WHITESPACE_CHAR = new RegExp(`^${SLOT_WHITESPACE_CLASS}$`, 'u');
/** Sunucudaki `NumberPatternSource` (birim: harf, °, para birimi `\p{Sc}` ile başlar). */
const NUMBER_PATTERN =
  /^(?:(?<neg>-) ?)?(?:(?<whole>[0-9]+) (?:tam )?(?<mn>[0-9]+) ?\/ ?(?<md>[0-9]+)|(?<fn>[0-9]+) ?\/ ?(?<fd>[0-9]+)|(?<dec>[0-9]+(?:[.,][0-9]+)*))(?: ?(?<unit>[\p{L}°\p{Sc}][\p{L}\p{M}°²³ ]*(?:\/ ?[\p{L}][\p{L}\p{M}²³ ]*)?))?$/u;
const NEGATIVE_PERCENT = /^- ?%/u;
/**
 * .NET regex'i UTF-16 kod birimiyle çalışır: astral (BMP dışı) bir harf `\p{L}` ile eşleşmez ve sayı geçersiz olur. JS `u`
 * kipi astral harfi tek kod noktası sayıp eşleştirirdi; aynı sonuç için birimde vekil (surrogate) kod birimi reddedilir.
 */
const SURROGATE = /[\uD800-\uDFFF]/;
const GROUPED_INTEGER = /^[1-9][0-9]{0,2}(?:\.[0-9]{3})+$/u;
const COMMA_DECIMAL = /^(?<int>[1-9][0-9]{0,2}(?:\.[0-9]{3})+|[0-9]+),(?<frac>[0-9]+)$/u;
const DOT_DECIMAL = /^(?<int>[0-9]+)\.(?<frac>[0-9]+)$/u;
const DIGITS = /^[0-9]+$/u;
const TRAILING = new Set(['.', ',', ';', ':', '!', '?']);

/** Türkçe küçük harf (yerelden bağımsız, sunucuyla aynı): İ→i, I→ı, diğerleri karakter karakter; i + U+0307 → i. */
export function turkishLower(value: string): string {
  let out = '';
  for (const ch of value) {
    out += ch === 'İ' ? 'i' : ch === 'I' ? 'ı' : ch.toLowerCase();
  }
  return out.replaceAll('i̇', 'i');
}

/** Boşluk dizileri tek boşluğa, sonra yalnız U+0020 kırpılır (sunucu `Trim(' ')`). */
function collapseWhitespace(value: string): string {
  return value.replace(WHITESPACE, ' ').replace(/^ +| +$/g, '');
}

function isWhitespaceChar(ch: string): boolean {
  return WHITESPACE_CHAR.test(ch);
}

function gcd(a: bigint, b: bigint): bigint {
  let x = a < 0n ? -a : a;
  let y = b < 0n ? -b : b;
  while (y !== 0n) [x, y] = [y, x % y];
  return x;
}

/**
 * Kabul edilen birim yazılabilir mi (sunucu `IsParseableUnit`): `"1 " + birim` ayrıştırılınca tam sayı ve birim aynen geri
 * gelmeli (ör. `²`, `%`, `5cm`, astral harf geçersiz).
 */
export function isParseableUnit(unit: string): boolean {
  const parsed = parseTrNumber('1 ' + unit);
  return !!parsed && parsed.form === 'integer' && parsed.unit !== null && parsed.unit === normalizeUnit(unit);
}

/** Birim karşılaştırma anahtarı: NFC, Türkçe küçük harf, tüm boşluklar silinir. */
export function normalizeUnit(unit: string): string {
  return turkishLower(unit.normalize('NFC')).replace(WHITESPACE, '');
}

function parseDecimal(s: string): { num: bigint; den: bigint; isInteger: boolean } | null {
  if (DIGITS.test(s)) return { num: BigInt(s), den: 1n, isInteger: true };
  const compose = (intPart: string, frac: string) => {
    const den = 10n ** BigInt(frac.length);
    return { num: BigInt(intPart) * den + BigInt(frac), den, isInteger: false };
  };
  if (s.includes(',')) {
    const m = COMMA_DECIMAL.exec(s);
    return m?.groups ? compose(m.groups['int'].replaceAll('.', ''), m.groups['frac']) : null;
  }
  if (GROUPED_INTEGER.test(s)) return { num: BigInt(s.replaceAll('.', '')), den: 1n, isInteger: true };
  const m = DOT_DECIMAL.exec(s);
  if (!m?.groups || m.groups['frac'].length === 3) return null;
  return compose(m.groups['int'], m.groups['frac']);
}

/** Sayı cevabını ayrıştırır; geçersizse (sıfır payda, tanınmayan biçim, yüzdeye birim / kesir, tanınmayan kuyruk) null. */
export function parseTrNumber(input: string | null | undefined): ParsedNumber | null {
  if (input == null || isBlankSlotValue(input) || input.length > MAX_NUMBER_INPUT_LENGTH) return null;

  let s = collapseWhitespace(turkishLower(input.normalize('NFC')).replaceAll('\u2212', '-'));
  let percent = false;
  const negativePercent = NEGATIVE_PERCENT.exec(s);
  if (negativePercent) {
    // "-%75" / "- %75" = "%-75"
    percent = true;
    s = '-' + s.slice(negativePercent[0].length).replace(/^ +/, '');
  } else if (s.startsWith('%')) {
    percent = true;
    s = s.slice(1).replace(/^ +/, '');
  } else if (s.endsWith('%')) {
    percent = true;
    s = s.slice(0, -1).replace(/ +$/, '');
  }
  if (s.length === 0 || s.includes('%')) return null;

  const g = NUMBER_PATTERN.exec(s)?.groups;
  if (!g) return null;
  if (g['unit'] !== undefined && SURROGATE.test(g['unit'])) return null;

  const unitRaw = g['unit'] !== undefined ? normalizeUnit(g['unit']) : null;
  const unit = unitRaw ? unitRaw : null;
  let num: bigint;
  let den: bigint;
  let form: NumberForm;
  let simplest = true;

  if (g['whole'] !== undefined) {
    const whole = BigInt(g['whole']);
    const mn = BigInt(g['mn']);
    const md = BigInt(g['md']);
    // Tam sayılı kesrin kesir kısmı basit kesir olmalı (0 < pay < payda).
    if (md === 0n || mn === 0n || mn >= md) return null;
    simplest = gcd(mn, md) === 1n;
    num = whole * md + mn;
    den = md;
    form = 'mixed';
  } else if (g['fn'] !== undefined) {
    num = BigInt(g['fn']);
    den = BigInt(g['fd']);
    if (den === 0n) return null;
    simplest = gcd(num, den) === 1n;
    form = 'fraction';
  } else {
    const decimal = parseDecimal(g['dec']);
    if (!decimal) return null;
    ({ num, den } = decimal);
    form = decimal.isInteger ? 'integer' : 'decimal';
  }

  if (percent) {
    if (form === 'fraction' || form === 'mixed' || unit !== null) return null;
    den *= 100n;
    form = 'percent';
  }

  if (g['neg'] !== undefined) num = -num;
  const divisor = gcd(num, den);
  if (divisor !== 0n && divisor !== 1n) {
    num /= divisor;
    den /= divisor;
  }
  return { num, den, form, simplest, unit };
}

/**
 * Sayı cevabı doğru mu? Çözüm değeri birimsiz geçerli bir sayı olmalı. `equivalence` açık: değer eşitliği yeter; kapalı:
 * biçim de aynı (tam sayı ≠ ondalık). `requireSimplest`: kesir / tam sayılı kesir en sade olmalı. Birim: `none` → birim
 * yazılmamalı; `optional` → yoksa olur, varsa listede; `required` → olmalı ve listede.
 */
export function isNumberAnswerCorrect(solution: SlotNumberSolution, answer: string | null | undefined): boolean {
  const expected = parseTrNumber(solution.value);
  if (!expected || expected.unit !== null) return false;
  const actual = parseTrNumber(answer);
  if (!actual) return false;
  if (actual.num !== expected.num || actual.den !== expected.den) return false;
  if (!solution.equivalence && actual.form !== expected.form) return false;
  if (solution.requireSimplest && (actual.form === 'fraction' || actual.form === 'mixed') && !actual.simplest) return false;

  const units = new Set((solution.units ?? []).map(normalizeUnit).filter((u) => u.length > 0));
  switch (solution.unitMode) {
    case 'optional':
      return actual.unit === null || units.has(actual.unit);
    case 'required':
      return actual.unit !== null && units.has(actual.unit);
    default:
      return actual.unit === null;
  }
}

/** Metin karşılaştırma anahtarı (aksanlar korunur). */
export function normalizeSlotText(value: string | null | undefined): string {
  if (value == null || isBlankSlotValue(value)) return '';
  let s = turkishLower(value.normalize('NFC')).replace(/[’‘ʼ`´]/gu, "'");
  s = collapseWhitespace(s);
  while (s.length > 0 && (TRAILING.has(s[s.length - 1]) || isWhitespaceChar(s[s.length - 1]))) s = s.slice(0, -1);
  return s;
}

/** Metin cevabı: normalize cevap == normalize kabul, ya da kabul + " " + normalize isteğe bağlı ek. */
export function isTextAnswerCorrect(solution: SlotTextSolution, answer: string | null | undefined): boolean {
  const a = normalizeSlotText(answer);
  if (!a) return false;
  const suffixes = (solution.optionalSuffixes ?? []).map(normalizeSlotText).filter((x) => x.length > 0);
  return (solution.accept ?? [])
    .map(normalizeSlotText)
    .filter((x) => x.length > 0)
    .some((candidate) => a === candidate || suffixes.some((suffix) => a === `${candidate} ${suffix}`));
}

/** Palet karşılaştırma anahtarı: NFC, tüm boşluklar silinir; büyük/küçük harf korunur. */
export function normalizePaletteText(value: string | null | undefined): string {
  return value ? value.normalize('NFC').replace(WHITESPACE, '') : '';
}

export function isPaletteAnswerCorrect(solution: SlotPaletteSolution, answer: string | null | undefined): boolean {
  const a = normalizePaletteText(answer);
  return a.length > 0 && (solution.accept ?? []).some((x) => normalizePaletteText(x) === a);
}

/** Seçim: seçenek id'si çözümdeki id'lerden biri (birebir). */
export function isSelectAnswerCorrect(solution: SlotSelectSolution, answer: string | null | undefined): boolean {
  return !!answer && (solution.optionIds ?? []).includes(answer);
}

/** Kutunun cevabı doğru mu (boş cevap her zaman yanlış). */
export function isSlotAnswerCorrect(kind: SlotKind, solution: SlotSolution, answer: string | null | undefined): boolean {
  switch (kind) {
    case 'number':
      return isNumberAnswerCorrect(solution as SlotNumberSolution, answer);
    case 'text':
      return isTextAnswerCorrect(solution as SlotTextSolution, answer);
    case 'select':
      return isSelectAnswerCorrect(solution as SlotSelectSolution, answer);
    case 'palette':
      return isPaletteAnswerCorrect(solution as SlotPaletteSolution, answer);
    default:
      return false;
  }
}
