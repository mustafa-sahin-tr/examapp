/**
 * Issue #575: cevap kutusu etkileşimi (`InteractionType = slotFill`) — kısa sayısal / yazılı cevap, cümle içi boşluk,
 * tablo doldurma, harita/şema etiketleme. Görsel aynen kalır, üstüne giriş kutuları (`slots[]`) konur. Plan öğrenciye
 * gider, çözüm gitmez (#570): çözüm ayrı alanda (`interactionSolution`) yalnız yetkili/izinli durumda gelir. Kutu
 * koordinatları soru bölgesine göredir (matris / sürükle-bırak ile aynı kural). Puansız: kontrol kutu başına doğru/yanlış.
 */
export const SLOT_FILL = 'slotFill' as const;

/** Backend sözleşmesinin sınırları (`SlotFillGrader` plan / çözüm / payload doğrulaması). */
export const SLOT_FILL_LIMITS = {
  minSlots: 1,
  maxSlots: 80,
  maxIdLength: 32,
  maxLabelLength: 200,
  minSelectOptions: 2,
  maxSelectOptions: 12,
  minPaletteSymbols: 1,
  maxPaletteSymbols: 20,
  maxSymbolLength: 8,
  maxUnitHintLength: 20,
  maxAcceptEntries: 20,
  maxAcceptLength: 100,
  maxSuffixEntries: 10,
  maxUnits: 10,
  maxUnitLength: 20,
  /** Öğrencinin tek kutuya yazabileceği en uzun değer. */
  maxValueLength: 200,
} as const;

/** Plan / seçenek id deseni (backend ile aynı). */
export const SLOT_ID_PATTERN = /^[A-Za-z0-9_-]{1,32}$/;

/**
 * Boşluk sınıfı — sunucudaki `SlotAnswerNormalizer.WhitespaceClass` ile birebir (.NET `\s` U+0085'i, JS `\s` U+FEFF'i
 * içerir; burada ikisi de var). Boş kutu, kırpma ve normalleştirme hep bu sınıfı kullanır.
 */
export const SLOT_WHITESPACE_CLASS = '[\\t\\n\\v\\f\\r \\u0085\\u00A0\\u1680\\u2000-\\u200A\\u2028\\u2029\\u202F\\u205F\\u3000\\uFEFF]';
const BLANK = new RegExp(`^${SLOT_WHITESPACE_CLASS}*$`, 'u');
const EDGE_WHITESPACE = new RegExp(`^${SLOT_WHITESPACE_CLASS}+|${SLOT_WHITESPACE_CLASS}+$`, 'gu');

/** null, boş ya da yalnız boşluk sınıfı = cevapsız kutu (sunucu `IsBlank`). */
export function isBlankSlotValue(value: string | null | undefined): boolean {
  return value == null || BLANK.test(value);
}

/** Boşluk sınıfına göre kırpar. */
export function trimSlotValue(value: string): string {
  return value.replace(EDGE_WHITESPACE, '');
}

/** Sunucunun reddettiği id'ler (prototip adları); üretilen id'ler bunlarla çakışmaz, gelen planda bu kutular atlanır. */
export const RESERVED_SLOT_IDS: ReadonlySet<string> = new Set(['__proto__', 'constructor', 'prototype']);

export function isValidSlotId(id: unknown): id is string {
  return typeof id === 'string' && SLOT_ID_PATTERN.test(id) && !RESERVED_SLOT_IDS.has(id);
}

export interface SlotBox {
  x: number;
  y: number;
  width: number;
  height: number;
}

export type SlotNumberEntry = 'plain' | 'fraction' | 'mixed';
export type SlotUnitMode = 'none' | 'optional' | 'required';
export type SlotKind = 'number' | 'text' | 'select' | 'palette';

export interface SlotSelectOption {
  id: string;
  label: string;
}

export interface SlotNumberInput {
  kind: 'number';
  entry: SlotNumberEntry;
  /** Kutunun yanında gösterilen birim (ör. "cm"); öğrencinin yazması gerekmez. */
  unit?: string;
}

export interface SlotTextInput {
  kind: 'text';
}

export interface SlotSelectInput {
  kind: 'select';
  options: SlotSelectOption[];
}

export interface SlotPaletteInput {
  kind: 'palette';
  symbols: string[];
}

export type SlotInput = SlotNumberInput | SlotTextInput | SlotSelectInput | SlotPaletteInput;

export interface SlotFillSlot {
  id: string;
  label?: string | null;
  box: SlotBox;
  input: SlotInput;
}

export interface SlotFillPlanV1 {
  version: 1;
  type: typeof SLOT_FILL;
  slots: SlotFillSlot[];
}

export interface SlotNumberSolution {
  value: string;
  equivalence: boolean;
  requireSimplest: boolean;
  units: string[];
  unitMode: SlotUnitMode;
}

export interface SlotTextSolution {
  accept: string[];
  optionalSuffixes: string[];
}

export interface SlotSelectSolution {
  optionIds: string[];
}

export interface SlotPaletteSolution {
  accept: string[];
}

export type SlotSolution = SlotNumberSolution | SlotTextSolution | SlotSelectSolution | SlotPaletteSolution;

/** Kutu id → çözüm (her kutu için bir giriş). */
export type SlotFillSolutionMap = Record<string, SlotSolution>;

/** Kutu id → öğrencinin yazdığı değer (boş kutu yazılmaz). */
export type SlotValues = Record<string, string>;

/** Öğrenci cevabı (`answerPayload`). Boş `values` boş cevap sayılır. */
export interface SlotFillAnswerPayload {
  version: 1;
  type: typeof SLOT_FILL;
  values: SlotValues;
}

/** Yazarlıkta palet hazır ayarları. */
export type SlotPalettePreset = 'geometry' | 'punctuation';

export const SLOT_PALETTE_PRESETS: Record<SlotPalettePreset, string[]> = {
  geometry: ['∠', '⊥', '//', '[AB]', '(AB)', '|AB|'],
  punctuation: ['.', ',', ';', ':', '!', '?', "'", '"', '-'],
};

/**
 * Güvenlik (#574 L2 ile aynı): kutu id'leri öğrenciye giden plandan/payload'dan gelir; `constructor`, `__proto__` gibi
 * id'ler düz nesnede prototipe çarpmasın diye eşlemeler prototipsiz kurulur ve okuma yalnız kendi özelliğinden yapılır.
 */
export function emptySlotValues(): SlotValues {
  return Object.create(null) as SlotValues;
}

export function emptySlotSolutions(): SlotFillSolutionMap {
  return Object.create(null) as SlotFillSolutionMap;
}

/** Kutunun değeri (yalnız kendi özelliği; yoksa boş metin). */
export function valueOf(values: SlotValues, slotId: string): string {
  return Object.hasOwn(values, slotId) && typeof values[slotId] === 'string' ? values[slotId] : '';
}

/** Kutunun çözümü (yalnız kendi özelliği). */
export function solutionOf(solutions: SlotFillSolutionMap, slotId: string): SlotSolution | null {
  return Object.hasOwn(solutions, slotId) ? solutions[slotId] : null;
}

/** Kutunun değeri değiştirilmiş yeni (prototipsiz) eşleme; boş / yalnız boşluk değer kutuyu siler. */
export function withValue(values: SlotValues, slotId: string, value: string): SlotValues {
  const next = emptySlotValues();
  Object.keys(values).forEach((key) => {
    if (key !== slotId) next[key] = values[key];
  });
  if (!isBlankSlotValue(value)) next[slotId] = value;
  return next;
}

function isBox(value: unknown): value is SlotBox {
  if (!value || typeof value !== 'object') return false;
  const b = value as Record<string, unknown>;
  return ['x', 'y', 'width', 'height'].every((k) => typeof b[k] === 'number' && Number.isFinite(b[k] as number));
}

function stringList(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((v): v is string => typeof v === 'string') : [];
}

function parseInput(value: unknown): SlotInput | null {
  if (!value || typeof value !== 'object') return null;
  const input = value as Record<string, unknown>;
  switch (input['kind']) {
    case 'number': {
      const entry = input['entry'] === 'fraction' || input['entry'] === 'mixed' ? input['entry'] : 'plain';
      const unit = typeof input['unit'] === 'string' && input['unit'].trim() ? input['unit'].trim() : undefined;
      return unit ? { kind: 'number', entry, unit } : { kind: 'number', entry };
    }
    case 'text':
      return { kind: 'text' };
    case 'select': {
      const options = (Array.isArray(input['options']) ? input['options'] : [])
        .filter((o): o is SlotSelectOption => !!o && typeof o.id === 'string' && !!o.id)
        .map((o) => ({ id: o.id, label: typeof o.label === 'string' ? o.label : o.id }));
      return options.length > 0 ? { kind: 'select', options } : null;
    }
    case 'palette': {
      const symbols = stringList(input['symbols']).filter((s) => !!s);
      return { kind: 'palette', symbols };
    }
    default:
      return null;
  }
}

/** Plan JSON'unu güvenli ayrıştırır; tür farklı / bozuk / kutu yoksa null. Bozuk kutu atlanır. */
export function parseSlotFillPlan(json: string | null | undefined): SlotFillPlanV1 | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Partial<SlotFillPlanV1> | null;
    if (!parsed || !isSlotFillType(parsed.type) || !Array.isArray(parsed.slots)) return null;
    const seen = new Set<string>();
    const slots: SlotFillSlot[] = [];
    for (const raw of parsed.slots as unknown[]) {
      if (!raw || typeof raw !== 'object') continue;
      const s = raw as Record<string, unknown>;
      const id = s['id'];
      if (typeof id !== 'string' || !id || RESERVED_SLOT_IDS.has(id) || seen.has(id) || !isBox(s['box'])) continue;
      const input = parseInput(s['input']);
      if (!input) continue;
      seen.add(id);
      const label = typeof s['label'] === 'string' && s['label'].trim() ? s['label'] : null;
      slots.push({ id, label, box: s['box'], input });
    }
    if (slots.length === 0) return null;
    return { version: 1, type: SLOT_FILL, slots };
  } catch {
    return null;
  }
}

function parseSolutionEntry(value: unknown, input: SlotInput | null): SlotSolution | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;
  const v = value as Record<string, unknown>;
  const kind = input?.kind ?? (typeof v['value'] === 'string' ? 'number' : Array.isArray(v['optionIds']) ? 'select' : Array.isArray(v['optionalSuffixes']) ? 'text' : 'palette');
  switch (kind) {
    case 'number': {
      if (typeof v['value'] !== 'string') return null;
      const unitMode = v['unitMode'] === 'optional' || v['unitMode'] === 'required' ? v['unitMode'] : 'none';
      return {
        value: v['value'],
        // Sunucuyla aynı varsayılanlar: eşdeğerlik açık, en sade kapalı, birim yok.
        equivalence: v['equivalence'] !== false,
        requireSimplest: v['requireSimplest'] === true,
        units: stringList(v['units']),
        unitMode,
      };
    }
    case 'text':
      return { accept: stringList(v['accept']), optionalSuffixes: stringList(v['optionalSuffixes']) };
    case 'select':
      return { optionIds: stringList(v['optionIds']) };
    case 'palette':
      return { accept: stringList(v['accept']) };
  }
}

/** Çözüm JSON'u (`{"s1":{"value":"3/4",...}}`) — bozuk / boşsa `{}`. Plan verilirse yalnız plandaki kutular, türüne göre. */
export function parseSlotFillSolution(json: string | null | undefined, plan?: SlotFillPlanV1 | null): SlotFillSolutionMap {
  const result = emptySlotSolutions();
  if (!json) return result;
  try {
    const parsed = JSON.parse(json) as unknown;
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return result;
    const inputs = plan ? new Map(plan.slots.map((s) => [s.id, s.input])) : null;
    Object.entries(parsed as Record<string, unknown>).forEach(([slotId, entry]) => {
      if (inputs && !inputs.has(slotId)) return;
      const solution = parseSolutionEntry(entry, inputs?.get(slotId) ?? null);
      if (solution) result[slotId] = solution;
    });
    return result;
  } catch {
    return result;
  }
}

/** `{slotId: "değer"}` nesnesini temizler (yalnız dolu metinler; plan verilirse plandaki kutular). */
export function normalizeSlotValues(value: unknown, plan?: SlotFillPlanV1 | null): SlotValues {
  const result = emptySlotValues();
  if (!value || typeof value !== 'object' || Array.isArray(value)) return result;
  const slotIds = plan ? new Set(plan.slots.map((s) => s.id)) : null;
  Object.entries(value as Record<string, unknown>).forEach(([slotId, v]) => {
    if (RESERVED_SLOT_IDS.has(slotId) || (slotIds && !slotIds.has(slotId))) return;
    if (typeof v === 'string' && !isBlankSlotValue(v)) result[slotId] = v;
  });
  return result;
}

/** Öğrenci cevabından değerler (`{"type":"slotFill","values":{...}}`) — başka tür / bozuksa `{}`. */
export function parseSlotFillValues(json: string | null | undefined, plan?: SlotFillPlanV1 | null): SlotValues {
  if (!json) return emptySlotValues();
  try {
    const parsed = JSON.parse(json) as Partial<SlotFillAnswerPayload> | null;
    if (!parsed || !isSlotFillType(parsed.type)) return emptySlotValues();
    return normalizeSlotValues(parsed.values, plan);
  } catch {
    return emptySlotValues();
  }
}

/** Payload: boş kutular yazılmaz, değerler kırpılır. */
export function buildSlotFillPayload(values: SlotValues): string {
  const clean = emptySlotValues();
  Object.keys(values).forEach((key) => {
    const v = trimSlotValue(values[key] ?? '');
    if (v) clean[key] = v;
  });
  const payload: SlotFillAnswerPayload = { version: 1, type: SLOT_FILL, values: clean };
  return JSON.stringify(payload);
}

/** Tür karşılaştırması büyük/küçük harf duyarsız (#574 L3 ile aynı): `SlotFill` de cevap kutusu sayılır. */
export function isSlotFillType(type: unknown): boolean {
  return typeof type === 'string' && type.toLowerCase() === SLOT_FILL.toLowerCase();
}

/** Tüm kutuları boş cevap (Temizle / son değeri silme): sunucu boş cevap sayar, ekranlar da cevapsız saymalı. */
export function isBlankSlotFillPayload(json: string | null | undefined): boolean {
  if (!json) return true;
  try {
    const parsed = JSON.parse(json) as Partial<SlotFillAnswerPayload> | null;
    if (!parsed || !isSlotFillType(parsed.type)) return false;
    return Object.keys(normalizeSlotValues(parsed.values)).length === 0;
  } catch {
    return false;
  }
}

/** Tam sayılı kesir parçaları (pay/payda bileşeni). */
export interface FractionParts {
  whole: string;
  numerator: string;
  denominator: string;
}

/** Pay/payda bileşeninin değeri: "3/4", "2 3/5", yalnız tam "2"; hepsi boşsa "". Eksik payda olduğu gibi gider ("3/"). */
export function composeFraction(parts: FractionParts): string {
  const whole = parts.whole.trim();
  const numerator = parts.numerator.trim();
  const denominator = parts.denominator.trim();
  const fraction = numerator || denominator ? `${numerator}/${denominator}` : '';
  if (whole && fraction) return `${whole} ${fraction}`;
  return whole || fraction;
}

/** `composeFraction`'ın tersi (kayıtlı cevabı bileşene geri yükler). Kesir değilse tam kısma konur. */
export function splitFraction(value: string | null | undefined): FractionParts {
  const text = (value ?? '').trim();
  const mixed = /^(\S+)\s+(?:tam\s+)?([^/\s]*)\s*\/\s*(\S*)$/iu.exec(text);
  if (mixed) return { whole: mixed[1], numerator: mixed[2], denominator: mixed[3] };
  const fraction = /^([^/\s]*)\s*\/\s*(\S*)$/u.exec(text);
  if (fraction) return { whole: '', numerator: fraction[1], denominator: fraction[2] };
  return { whole: text, numerator: '', denominator: '' };
}

/** Çözümün öğrenciye gösterilecek kabul edilen değeri: ilk kabul / değer + birim / seçenek adı. */
export function solutionDisplay(input: SlotInput, solution: SlotSolution | null): string {
  if (!solution) return '';
  switch (input.kind) {
    case 'number': {
      const s = solution as SlotNumberSolution;
      const unit = s.unitMode !== 'none' ? s.units[0] ?? input.unit ?? '' : '';
      return unit ? `${s.value} ${unit}` : s.value ?? '';
    }
    case 'select': {
      const ids = (solution as SlotSelectSolution).optionIds ?? [];
      return ids.map((id) => input.options.find((o) => o.id === id)?.label ?? id).join(' / ');
    }
    case 'text':
    case 'palette':
      return (solution as SlotTextSolution | SlotPaletteSolution).accept?.[0] ?? '';
  }
}

/** Öğrencinin değerinin gösterimi (seçenek id'si → seçenek adı). */
export function valueDisplay(input: SlotInput, value: string): string {
  if (input.kind === 'select') return input.options.find((o) => o.id === value)?.label ?? value;
  return value;
}
