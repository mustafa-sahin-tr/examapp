/**
 * Issue #575: cevap kutusu (slotFill) yazarlığının saf kısmı — durum, plan / çözüm üreticileri, doğrulama ve "tüm kutulara
 * uygula". Tuval (`ImageSelectorComponent`) yalnız çizim / seçim / müfettiş köprüsünü tutar. Kurallar backend
 * `SlotFillGrader.ValidateDefinition` ile aynı: kayıtta engelleyici sorunlar burada üretilir.
 */
import {
  SLOT_FILL,
  SLOT_FILL_LIMITS,
  SlotBox,
  SlotFillPlanV1,
  SlotFillSolutionMap,
  SlotFillSlot,
  SlotInput,
  SlotKind,
  SlotNumberEntry,
  SlotNumberSolution,
  SlotPaletteSolution,
  SlotSelectOption,
  SlotSelectSolution,
  SlotSolution,
  SlotTextSolution,
  SlotUnitMode,
  emptySlotSolutions,
  isValidSlotId,
} from '../../models/slot-fill';
import {
  isParseableUnit,
  normalizePaletteText,
  normalizeSlotText,
  normalizeUnit,
  parseTrNumber,
} from '../../shared/utils/slot-normalizer';

/** Yazarlıkta tek kutu: tüm türlerin alanları birlikte tutulur (tür değişince girilenler kaybolmasın). */
export interface SlotAuthoring {
  id: string;
  label: string;
  box: SlotBox;
  kind: SlotKind;
  // number
  entry: SlotNumberEntry;
  /** Kutunun yanında gösterilen birim (plan `input.unit`). */
  unitHint: string;
  value: string;
  equivalence: boolean;
  requireSimplest: boolean;
  unitMode: SlotUnitMode;
  units: string[];
  // text
  accept: string[];
  optionalSuffixes: string[];
  // select
  options: SlotSelectOption[];
  correctOptionIds: string[];
  // palette
  symbols: string[];
  paletteAccept: string[];
}

export interface SlotFillAuthoringState {
  slots: SlotAuthoring[];
  /** Müfettişte düzenlenen kutu. */
  selectedSlotId: string | null;
}

export type SlotIssueCode =
  | 'missingNumber'
  | 'invalidNumber'
  | 'numberHasUnit'
  | 'unitsMissing'
  | 'percentWithUnit'
  | 'notSimplest'
  | 'entryFormMismatch'
  | 'entryWithRequiredUnit'
  | 'invalidUnit'
  | 'missingAccept'
  | 'optionCount'
  | 'emptyOptionLabel'
  | 'missingCorrectOption'
  | 'symbolCount'
  | 'symbolTooLong'
  | 'missingPaletteAccept'
  | 'tooLong';

/** Müfettişin cevap kutusu bölümü (`ActiveInspector.slotFill`). */
export interface SlotFillInspector {
  slots: Array<SlotAuthoring & { no: number; issues: SlotIssueCode[] }>;
  selected: (SlotAuthoring & { no: number; issues: SlotIssueCode[] }) | null;
  missingCount: number;
  limits: typeof SLOT_FILL_LIMITS;
}

/** Ayar kopyalamada / yeni kutuda taşınan alanlar (doğru değerler hariç). */
const SETTING_KEYS = [
  'kind',
  'entry',
  'unitHint',
  'equivalence',
  'requireSimplest',
  'unitMode',
  'units',
  'optionalSuffixes',
  'options',
  'symbols',
] as const;

export function createSlotFillState(): SlotFillAuthoringState {
  return { slots: [], selectedSlotId: null };
}

/**
 * Issue #579: kayıtlı / taslak plandan yazarlık durumu (çözümsüz: doğru değerler boş). Plan `parseSlotFillPlan` ile
 * temizlenmiş olmalı.
 */
export function slotFillStateFromPlan(plan: SlotFillPlanV1): SlotFillAuthoringState {
  return { slots: plan.slots.map(slotFromPlan), selectedSlotId: null };
}

function slotFromPlan(slot: SlotFillSlot): SlotAuthoring {
  const base = newSlot(slot.id, { ...slot.box });
  const next: SlotAuthoring = { ...base, label: slot.label ?? '', kind: slot.input.kind };
  switch (slot.input.kind) {
    case 'number':
      return { ...next, entry: slot.input.entry, unitHint: slot.input.unit ?? '' };
    case 'select':
      return { ...next, options: slot.input.options.map((o) => ({ ...o })) };
    case 'palette':
      return { ...next, symbols: [...slot.input.symbols] };
    default:
      return next;
  }
}

/** Yeni kutu: şablon verilirse onun ayarları (tür, giriş, birim, seçenekler, semboller), doğru değerler boş. */
export function newSlot(id: string, box: SlotBox, template?: SlotAuthoring | null): SlotAuthoring {
  const base: SlotAuthoring = {
    id,
    label: '',
    box,
    kind: 'number',
    entry: 'plain',
    unitHint: '',
    value: '',
    equivalence: true,
    requireSimplest: false,
    unitMode: 'none',
    units: [],
    accept: [],
    optionalSuffixes: [],
    options: [
      { id: 'o1', label: '' },
      { id: 'o2', label: '' },
    ],
    correctOptionIds: [],
    symbols: [],
    paletteAccept: [],
  };
  return template ? copySlotSettings(template, base) : base;
}

/** Kaynağın ayarlarını hedefe uygular; hedefin doğru değerleri korunur (seçim türünde kalmayan seçenekler düşer). */
export function copySlotSettings(source: SlotAuthoring, target: SlotAuthoring): SlotAuthoring {
  const next: SlotAuthoring = { ...target };
  for (const key of SETTING_KEYS) {
    const value = source[key];
    (next as unknown as Record<string, unknown>)[key] = Array.isArray(value) ? value.map((v) => (typeof v === 'object' ? { ...v } : v)) : value;
  }
  const optionIds = new Set(next.options.map((o) => o.id));
  next.correctOptionIds = next.correctOptionIds.filter((id) => optionIds.has(id));
  return next;
}

/** `prefix` + ilk boş numara (s1, s2, …; o1, o2, …). */
export function nextSlotId(prefix: string, used: string[]): string {
  const taken = new Set(used);
  for (let i = 1; ; i++) {
    const id = `${prefix}${i}`;
    if (!taken.has(id) && isValidSlotId(id)) return id;
  }
}

/** Liste alanı metni → öğeler (kırpılmış, boşlar atılmış, `key`e göre yinelenmez). */
export function parseList(text: string, separator: RegExp, key: (value: string) => string = (v) => v): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  for (const part of (text ?? '').split(separator)) {
    const value = part.trim();
    const k = key(value);
    if (!value || !k || seen.has(k)) continue;
    seen.add(k);
    result.push(value);
  }
  return result;
}

export const LINE_SEPARATOR = /\r?\n/;
export const COMMA_SEPARATOR = /[,;\n]/;
export const SPACE_SEPARATOR = /\s+/;

function clean(list: string[], key: (value: string) => string): string[] {
  return parseList(list.join('\n'), LINE_SEPARATOR, key);
}

function slotInput(slot: SlotAuthoring): SlotInput {
  switch (slot.kind) {
    case 'number': {
      const unit = slot.unitHint.trim();
      return unit ? { kind: 'number', entry: slot.entry, unit } : { kind: 'number', entry: slot.entry };
    }
    case 'text':
      return { kind: 'text' };
    case 'select':
      return { kind: 'select', options: slot.options.map((o) => ({ id: o.id, label: o.label.trim() })) };
    case 'palette':
      return { kind: 'palette', symbols: clean(slot.symbols, (s) => s) };
  }
}

/** Öğrenciye giden plan: çözüm içermez (#570). Boş kutu adı gönderilmez; yalnız izinli alanlar. */
export function buildSlotFillPlan(state: SlotFillAuthoringState): SlotFillPlanV1 {
  return {
    version: 1,
    type: SLOT_FILL,
    slots: state.slots.map((slot) => ({
      id: slot.id,
      ...(slot.label.trim() ? { label: slot.label.trim() } : {}),
      box: { ...slot.box },
      input: slotInput(slot),
    })),
  };
}

/** Tek kutunun çözümü (kayıt ve "öğrenci şunu yazarsa" önizlemesi aynı nesneyi kullanır). */
export function slotSolution(slot: SlotAuthoring): SlotSolution {
  switch (slot.kind) {
    case 'number':
      return {
        value: slot.value.trim(),
        equivalence: slot.equivalence,
        requireSimplest: slot.requireSimplest,
        // Birim kipi "yok" iken liste gönderilmez: gizli kalan eski birimler kaydı reddettirmesin.
        units: slot.unitMode === 'none' ? [] : clean(slot.units, normalizeUnit),
        unitMode: slot.unitMode,
      };
    case 'text':
      return { accept: clean(slot.accept, normalizeSlotText), optionalSuffixes: clean(slot.optionalSuffixes, normalizeSlotText) };
    case 'select':
      return { optionIds: slot.correctOptionIds.filter((id) => slot.options.some((o) => o.id === id)) };
    case 'palette':
      return { accept: clean(slot.paletteAccept, normalizePaletteText) };
  }
}

/**
 * Issue #580: kayıtlı / taslak çözümü kutuya yazar (kutunun türüne göre; tür ayarları — giriş, birim ipucu, seçenekler,
 * semboller — değişmez). Çözüm `parseSlotFillSolution` ile kutunun planına göre temizlenmiş olmalı.
 */
export function slotWithSolution(slot: SlotAuthoring, solution: SlotSolution): SlotAuthoring {
  switch (slot.kind) {
    case 'number': {
      const s = solution as Partial<SlotNumberSolution>;
      if (typeof s.value !== 'string') return slot;
      return {
        ...slot,
        value: s.value,
        equivalence: s.equivalence !== false,
        requireSimplest: s.requireSimplest === true,
        unitMode: s.unitMode ?? 'none',
        units: [...(s.units ?? [])],
      };
    }
    case 'text': {
      const s = solution as Partial<SlotTextSolution>;
      return { ...slot, accept: [...(s.accept ?? [])], optionalSuffixes: [...(s.optionalSuffixes ?? [])] };
    }
    case 'select': {
      const ids = new Set(slot.options.map((o) => o.id));
      const s = solution as Partial<SlotSelectSolution>;
      return { ...slot, correctOptionIds: (s.optionIds ?? []).filter((id) => ids.has(id)) };
    }
    case 'palette': {
      const s = solution as Partial<SlotPaletteSolution>;
      return { ...slot, paletteAccept: [...(s.accept ?? [])] };
    }
  }
}

/**
 * Issue #580: kutunun doğru değerlerini siler (tür ayarları kalır) — çözümde yer almayan kutu eski değerini taşımasın.
 */
export function slotWithoutSolution(slot: SlotAuthoring): SlotAuthoring {
  return {
    ...slot,
    value: '',
    equivalence: true,
    requireSimplest: false,
    unitMode: 'none',
    units: [],
    accept: [],
    optionalSuffixes: [],
    correctOptionIds: [],
    paletteAccept: [],
  };
}

/** Kutunun doğru değeri girilmiş mi (en az bir değer; ayrıntılı geçerlilik `slotIssues`). */
export function slotHasCorrectValue(slot: SlotAuthoring): boolean {
  switch (slot.kind) {
    case 'number':
      return !!slot.value.trim();
    case 'text':
      return clean(slot.accept, normalizeSlotText).length > 0;
    case 'select':
      return slot.correctOptionIds.some((id) => slot.options.some((o) => o.id === id));
    case 'palette':
      return clean(slot.paletteAccept, normalizePaletteText).length > 0;
  }
}

/**
 * Çözüm `{slotId: {...}}` (plan kutu sırasıyla, her kutu için). Hiçbir kutunun doğru değeri yoksa null (taslak); kısmi
 * çözümü kayıt doğrulaması zaten engeller (backend her kutu için girdi ister).
 */
export function buildSlotFillSolution(state: SlotFillAuthoringState): SlotFillSolutionMap | null {
  if (!state.slots.some(slotHasCorrectValue)) return null;
  const result = emptySlotSolutions();
  state.slots.forEach((slot) => {
    result[slot.id] = slotSolution(slot);
  });
  return result;
}

const tooLong = (list: string[], max: number) => list.some((v) => v.trim().length > max);

/** Kutunun kayıt engelleyen sorunları (backend `ValidateDefinition` kuralları). */
export function slotIssues(slot: SlotAuthoring): SlotIssueCode[] {
  const limits = SLOT_FILL_LIMITS;
  const issues: SlotIssueCode[] = [];
  if (slot.label.trim().length > limits.maxLabelLength) issues.push('tooLong');
  switch (slot.kind) {
    case 'number': {
      const value = slot.value.trim();
      if (!value) {
        issues.push('missingNumber');
        break;
      }
      const parsed = parseTrNumber(value);
      if (!parsed) {
        issues.push('invalidNumber');
        break;
      }
      if (parsed.unit !== null) issues.push('numberHasUnit');
      const units = clean(slot.units, normalizeUnit);
      if (slot.unitMode !== 'none' && units.length === 0) issues.push('unitsMissing');
      // Her kabul edilen birim öğrencinin yazabileceği bir birim olmalı (sunucu `IsParseableUnit`: ör. "²", "5cm" olmaz).
      if (slot.unitMode !== 'none' && units.some((u) => !isParseableUnit(u))) issues.push('invalidUnit');
      if (slot.unitMode === 'required' && parsed.form === 'percent') issues.push('percentWithUnit');
      // Kesir bileşeninde birim yazılacak yer yok: birim zorunlu kesir kutusu çözülemez (sunucu da reddeder).
      if (slot.unitMode === 'required' && slot.entry !== 'plain') issues.push('entryWithRequiredUnit');
      if (
        !slot.equivalence &&
        slot.requireSimplest &&
        (parsed.form === 'fraction' || parsed.form === 'mixed') &&
        !parsed.simplest
      ) {
        issues.push('notSimplest');
      }
      // Kesir girişinde öğrenci yalnız "a/b" (tam sayılı kesirde ayrıca "w a/b" ya da "w") yazabilir: eşdeğerlik kapalıyken
      // çözüm başka biçimdeyse hiçbir cevap doğru olmaz.
      if (!slot.equivalence && slot.entry !== 'plain') {
        const reachable = slot.entry === 'fraction' ? ['fraction'] : ['mixed', 'fraction', 'integer'];
        if (!reachable.includes(parsed.form)) issues.push('entryFormMismatch');
      }
      if ((slot.unitMode !== 'none' && (units.length > limits.maxUnits || tooLong(units, limits.maxUnitLength))) || slot.unitHint.trim().length > limits.maxUnitHintLength) {
        issues.push('tooLong');
      }
      break;
    }
    case 'text': {
      const accept = clean(slot.accept, normalizeSlotText);
      const suffixes = clean(slot.optionalSuffixes, normalizeSlotText);
      if (accept.length === 0) issues.push('missingAccept');
      if (
        accept.length > limits.maxAcceptEntries ||
        suffixes.length > limits.maxSuffixEntries ||
        tooLong(accept, limits.maxAcceptLength) ||
        tooLong(suffixes, limits.maxAcceptLength)
      ) {
        issues.push('tooLong');
      }
      break;
    }
    case 'select': {
      if (slot.options.length < limits.minSelectOptions || slot.options.length > limits.maxSelectOptions) {
        issues.push('optionCount');
      }
      if (slot.options.some((o) => !o.label.trim())) issues.push('emptyOptionLabel');
      if (slot.options.some((o) => o.label.trim().length > limits.maxLabelLength || !isValidSlotId(o.id))) {
        issues.push('tooLong');
      }
      if (!slotHasCorrectValue(slot)) issues.push('missingCorrectOption');
      break;
    }
    case 'palette': {
      const symbols = clean(slot.symbols, (s) => s);
      const accept = clean(slot.paletteAccept, normalizePaletteText);
      if (symbols.length < limits.minPaletteSymbols || symbols.length > limits.maxPaletteSymbols) issues.push('symbolCount');
      if (symbols.some((s) => s.length > limits.maxSymbolLength)) issues.push('symbolTooLong');
      if (accept.length === 0) issues.push('missingPaletteAccept');
      if (accept.length > limits.maxAcceptEntries || tooLong(accept, limits.maxAcceptLength)) issues.push('tooLong');
      break;
    }
  }
  return issues;
}

/** Soru düzeyindeki sorun anahtarları (kutu yok / çok kutu) + kutu sorunları (kutu numarasıyla). */
export function slotFillIssues(state: SlotFillAuthoringState): Array<{ code: SlotIssueCode | 'noSlots' | 'tooManySlots'; no?: number }> {
  const result: Array<{ code: SlotIssueCode | 'noSlots' | 'tooManySlots'; no?: number }> = [];
  if (state.slots.length < SLOT_FILL_LIMITS.minSlots) result.push({ code: 'noSlots' });
  if (state.slots.length > SLOT_FILL_LIMITS.maxSlots) result.push({ code: 'tooManySlots' });
  state.slots.forEach((slot, index) => slotIssues(slot).forEach((code) => result.push({ code, no: index + 1 })));
  return result;
}

export function buildSlotFillInspector(state: SlotFillAuthoringState): SlotFillInspector {
  const slots = state.slots.map((slot, index) => ({ ...slot, no: index + 1, issues: slotIssues(slot) }));
  return {
    slots,
    selected: slots.find((s) => s.id === state.selectedSlotId) ?? null,
    missingCount: slots.filter((s) => s.issues.length > 0).length,
    limits: SLOT_FILL_LIMITS,
  };
}
