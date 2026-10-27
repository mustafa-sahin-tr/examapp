/**
 * Issue #582: metin üzerinde işaretleme (textSelect) yazarlığının saf kısmı. Kurallar backend `TextSelectGrader`
 * (`ValidateDefinition`) ile aynı: 1..600 token, metin ≤ 64, satır 0..299 azalmaz, kutu 1..3; punctuation'da 1..100 gap ve
 * 1..20 sembollük palet, select / correct'te gap yok; select / correct çözümü 1..100 hedef (hedef 1..8 token, token tek
 * hedefte), correct'te her hedefin kabul edilen düzeltmesi; punctuation'da her gap için paletten en az bir sembol.
 * Cevaplar yalnız elle girilir (Claude yalnız metni ve token kutularını çıkarır).
 */
import { InteractionBox, copyBox } from '../../models/interaction-ids';
import {
  DEFAULT_TEXT_SELECT_PALETTE,
  TEXT_SELECT,
  TEXT_SELECT_LIMITS,
  TextSelectMode,
  TextSelectPlanV1,
  TextSelectStyle,
  TextSelectTarget,
  TextSelectToken,
  clipText,
  normalizeTextLine,
  tokenizeTextLines,
} from '../../models/text-select';
import { normalizeSlotText } from '../../shared/utils/slot-normalizer';
import { nextInteractionId } from './drag-place-authoring';

export interface TextSelectAuthoringState {
  mode: TextSelectMode;
  style: TextSelectStyle;
  /** Mod punctuation değilken de tutulur (mod değişince kaybolmasın); plana yalnız punctuation'da gider. */
  palette: string[];
  tokens: TextSelectToken[];
  /** select / correct hedefleri (plan sırasıyla token'lar); accept / ek yalnız correct'te kullanılır. */
  targets: TextSelectTarget[];
  /** Gap id → doğru sembol(ler) (prototipsiz). */
  gapAccept: Record<string, string[]>;
  /** Müfettişte düzenlenen token. */
  selectedTokenId: string | null;
  /** "Satır ekle" için tuvalde çizilen satır kutusu (soru bölgesine göre); metin müfettişte yazılır. */
  pendingLine: InteractionBox | null;
}

export type TextSelectIssueCode =
  | 'noText'
  | 'tooManyTokens'
  | 'tooManyLines'
  | 'tokenTooLong'
  | 'noGaps'
  | 'tooManyGaps'
  | 'gapsNeedPunctuation'
  | 'paletteCount'
  | 'symbolTooLong'
  | 'noTargets'
  | 'tooManyTargets'
  | 'targetTooLong'
  | 'missingAccept'
  | 'tooLong'
  | 'missingGapAccept';

export interface TextSelectIssue {
  code: TextSelectIssueCode;
  no?: number;
}

export interface TextSelectTokenInspector extends TextSelectToken {
  /** Token'ın hedef numarası (1'den; hedefte değilse null). */
  targetNo: number | null;
  /** Gap numarası (1'den; gap değilse null). */
  gapNo: number | null;
  /** Gap'in doğru sembolleri. */
  accept: string[];
  selected: boolean;
}

export interface TextSelectTargetInspector extends TextSelectTarget {
  no: number;
  text: string;
  missing: boolean;
}

export interface TextSelectInspector {
  mode: TextSelectMode;
  style: TextSelectStyle;
  palette: string[];
  lines: Array<{ line: number; tokens: TextSelectTokenInspector[] }>;
  selected: TextSelectTokenInspector | null;
  targets: TextSelectTargetInspector[];
  tokenCount: number;
  gapCount: number;
  hasPendingLine: boolean;
  issues: TextSelectIssue[];
  limits: typeof TEXT_SELECT_LIMITS;
}

function emptyGapAccept(): Record<string, string[]> {
  return Object.create(null) as Record<string, string[]>;
}

function gapAcceptOf(state: TextSelectAuthoringState, gapId: string): string[] {
  return Object.hasOwn(state.gapAccept, gapId) ? state.gapAccept[gapId] : [];
}

export function createTextSelectState(): TextSelectAuthoringState {
  return {
    mode: 'select',
    style: 'highlight',
    palette: [...DEFAULT_TEXT_SELECT_PALETTE],
    tokens: [],
    targets: [],
    gapAccept: emptyGapAccept(),
    selectedTokenId: null,
    pendingLine: null,
  };
}

/** Kayıtlı / taslak plandan yazarlık durumu (çözümsüz). Plan `parseTextSelectPlan` ile temizlenmiş olmalı. */
export function textSelectStateFromPlan(plan: TextSelectPlanV1): TextSelectAuthoringState {
  return {
    ...createTextSelectState(),
    mode: plan.mode,
    style: plan.style,
    palette: plan.palette.length > 0 ? [...plan.palette] : [...DEFAULT_TEXT_SELECT_PALETTE],
    tokens: plan.tokens.map((t) => ({ ...t, boxes: t.boxes.map(copyBox) })),
  };
}

export function setTextSelectMode(state: TextSelectAuthoringState, mode: TextSelectMode): TextSelectAuthoringState {
  return { ...state, mode, palette: state.palette.length > 0 ? state.palette : [...DEFAULT_TEXT_SELECT_PALETTE] };
}

export function setTextSelectStyle(state: TextSelectAuthoringState, style: TextSelectStyle): TextSelectAuthoringState {
  return { ...state, style };
}

/** Palet metni (boşluk / virgül değil, satır ya da boşlukla ayrılır: "," de sembol olabilir) → semboller. */
export function parsePaletteText(text: string): string[] {
  const seen = new Set<string>();
  return (text ?? '')
    .split(/\s+/)
    .map((s) => s.trim())
    .filter((s) => !!s && !seen.has(s) && (seen.add(s), true));
}

/** Palet değişince paletten çıkan semboller gap çözümlerinden de düşer. */
export function setTextSelectPalette(state: TextSelectAuthoringState, symbols: string[]): TextSelectAuthoringState {
  const palette = [...new Set(symbols.map((s) => s.trim()).filter((s) => !!s))];
  const allowed = new Set(palette);
  const gapAccept = emptyGapAccept();
  Object.keys(state.gapAccept).forEach((id) => {
    const kept = state.gapAccept[id].filter((s) => allowed.has(s));
    if (kept.length > 0) gapAccept[id] = kept;
  });
  return { ...state, palette, gapAccept };
}

export function selectTextToken(state: TextSelectAuthoringState, tokenId: string | null): TextSelectAuthoringState {
  return { ...state, selectedTokenId: tokenId && state.tokens.some((t) => t.id === tokenId) ? tokenId : null };
}

/** Kelime token'ının metnini düzeltir (gap metni sabit); boş metin değişiklik yapmaz (silmek için `removeTextToken`). */
export function setTextTokenText(state: TextSelectAuthoringState, tokenId: string, text: string): TextSelectAuthoringState {
  const value = clipText(normalizeTextLine(text), TEXT_SELECT_LIMITS.maxTokenTextLength);
  if (!value) return state;
  return { ...state, tokens: state.tokens.map((t) => (t.id === tokenId && !t.gap ? { ...t, text: value } : t)) };
}

/** Token'ı siler; hedeflerden ve gap çözümünden de düşer (boş kalan hedef silinir). */
export function removeTextToken(state: TextSelectAuthoringState, tokenId: string): TextSelectAuthoringState {
  const gapAccept = emptyGapAccept();
  Object.keys(state.gapAccept).forEach((id) => {
    if (id !== tokenId) gapAccept[id] = state.gapAccept[id];
  });
  return {
    ...state,
    tokens: state.tokens.filter((t) => t.id !== tokenId),
    targets: state.targets
      .map((target) => ({ ...target, tokenIds: target.tokenIds.filter((id) => id !== tokenId) }))
      .filter((target) => target.tokenIds.length > 0),
    gapAccept,
    selectedTokenId: state.selectedTokenId === tokenId ? null : state.selectedTokenId,
  };
}

export function setPendingTextLine(state: TextSelectAuthoringState, box: InteractionBox | null): TextSelectAuthoringState {
  return { ...state, pendingLine: box ? copyBox(box) : null };
}

/**
 * "Satır ekle": yazılan metin TS tokenizer'la (sunucuyla aynı kural) satır kutusu içinde token'lara bölünür ve satır, kutunun
 * dikey konumuna göre okuma sırasına yerleşir; satır numaraları 0'dan yeniden verilir. Yeni token'lar boş `t` / `g` id'lerini
 * alır (mevcut id'ler ve hedefler değişmez). Metinde token yoksa ya da token / satır sınırı aşılırsa durum değişmez.
 * Sıra yalnız y'ye göredir: iki sütunlu metinde okuma sırasını yazar kontrol eder (müfettiş ipucu).
 */
export function addTextLine(state: TextSelectAuthoringState, box: InteractionBox, text: string): TextSelectAuthoringState {
  const fresh = tokenizeTextLines([{ text, box }]);
  if (fresh.length === 0 || state.tokens.length + fresh.length > TEXT_SELECT_LIMITS.maxTokens) return state;
  const used = state.tokens.map((t) => t.id);
  const added = fresh.map((t) => {
    const id = nextInteractionId(t.gap ? 'g' : 't', used);
    used.push(id);
    return { ...t, id };
  });
  const groups: Array<{ y: number; tokens: TextSelectToken[] }> = [];
  for (const token of state.tokens) {
    const last = groups[groups.length - 1];
    if (last && last.tokens[0].line === token.line) last.tokens.push(token);
    else groups.push({ y: token.boxes[0]?.y ?? 0, tokens: [token] });
  }
  if (groups.length + 1 > TEXT_SELECT_LIMITS.maxLines) return state;
  const y = Math.round(box.y);
  const at = groups.findIndex((g) => g.y > y);
  groups.splice(at < 0 ? groups.length : at, 0, { y, tokens: added });
  const tokens = groups.flatMap((g, line) => g.tokens.map((t) => ({ ...t, line })));
  return { ...state, tokens, pendingLine: null, selectedTokenId: added[0].id };
}

function ordered(state: TextSelectAuthoringState, ids: Iterable<string>): string[] {
  const set = new Set(ids);
  return state.tokens.filter((t) => set.has(t.id)).map((t) => t.id);
}

function targetIndexOf(state: TextSelectAuthoringState, tokenId: string): number {
  return state.targets.findIndex((t) => t.tokenIds.includes(tokenId));
}

/**
 * Hedef işaretle / kaldır (select / correct). Hedefteki token'a tık onu hedeften çıkarır (boş kalan hedef silinir). Hedefte
 * olmayan token, önündeki ya da ardındaki (gap olmayan) komşusu bir hedefteyse ona katılır — ardışık token'lar tek hedeftir
 * ("Sonun da"); iki yanı da farklı hedefteyse ikisi birleşir. Hedef en çok 8 token; sığmazsa yeni hedef açılır.
 */
export function toggleTextTarget(state: TextSelectAuthoringState, tokenId: string): TextSelectAuthoringState {
  const token = state.tokens.find((t) => t.id === tokenId);
  if (!token || token.gap) return state;
  const current = targetIndexOf(state, tokenId);
  if (current >= 0) {
    const targets = state.targets
      .map((t, i) => (i === current ? { ...t, tokenIds: t.tokenIds.filter((id) => id !== tokenId) } : t))
      .filter((t) => t.tokenIds.length > 0);
    return { ...state, targets };
  }
  const words = state.tokens.filter((t) => !t.gap);
  const position = words.findIndex((t) => t.id === tokenId);
  const before = position > 0 ? targetIndexOf(state, words[position - 1].id) : -1;
  const after = position < words.length - 1 ? targetIndexOf(state, words[position + 1].id) : -1;
  const max = TEXT_SELECT_LIMITS.maxTargetTokens;
  const fits = (index: number, extra = 0) => index >= 0 && state.targets[index].tokenIds.length + 1 + extra <= max;
  const targets = state.targets.map((t) => ({ ...t, tokenIds: [...t.tokenIds], accept: [...t.accept], optionalSuffixes: [...t.optionalSuffixes] }));
  if (before >= 0 && after >= 0 && before !== after && fits(before, targets[after].tokenIds.length)) {
    const merged = targets[before];
    const other = targets[after];
    merged.tokenIds = ordered(state, [...merged.tokenIds, tokenId, ...other.tokenIds]);
    merged.accept = [...merged.accept, ...other.accept.filter((a) => !merged.accept.includes(a))];
    merged.optionalSuffixes = [...merged.optionalSuffixes, ...other.optionalSuffixes.filter((s) => !merged.optionalSuffixes.includes(s))];
    return { ...state, targets: targets.filter((_, i) => i !== after) };
  }
  const join = fits(before) ? before : fits(after) ? after : -1;
  if (join >= 0) {
    targets[join].tokenIds = ordered(state, [...targets[join].tokenIds, tokenId]);
    return { ...state, targets };
  }
  // Yeni hedef plan sırasındaki yerine konur (hedef numaraları metindeki sırayı izler).
  const firstIndex = (t: TextSelectTarget) => state.tokens.findIndex((x) => x.id === t.tokenIds[0]);
  const index = state.tokens.findIndex((t) => t.id === tokenId);
  const insertAt = targets.findIndex((t) => firstIndex(t) > index);
  targets.splice(insertAt < 0 ? targets.length : insertAt, 0, { tokenIds: [tokenId], accept: [], optionalSuffixes: [] });
  return { ...state, targets };
}

/** "Ayır": çok token'lı hedef token başına ayrı hedeflere bölünür (ilk hedef düzeltmeleri korur). */
export function splitTextTarget(state: TextSelectAuthoringState, index: number): TextSelectAuthoringState {
  const target = state.targets[index];
  if (!target || target.tokenIds.length < 2) return state;
  const parts: TextSelectTarget[] = target.tokenIds.map((id, i) => ({
    tokenIds: [id],
    accept: i === 0 ? [...target.accept] : [],
    optionalSuffixes: i === 0 ? [...target.optionalSuffixes] : [],
  }));
  return { ...state, targets: [...state.targets.slice(0, index), ...parts, ...state.targets.slice(index + 1)] };
}

export function removeTextTarget(state: TextSelectAuthoringState, index: number): TextSelectAuthoringState {
  if (index < 0 || index >= state.targets.length) return state;
  return { ...state, targets: state.targets.filter((_, i) => i !== index) };
}

/** Liste metni (satır, virgül ya da noktalı virgülle) → kabul edilen düzeltmeler (normalize hâline göre tekrarsız). */
export function parseAcceptText(text: string): string[] {
  const seen = new Set<string>();
  return (text ?? '')
    .split(/[\n,;]/)
    .map((s) => s.trim())
    .filter((s) => {
      const key = normalizeSlotText(s);
      if (!s || !key || seen.has(key)) return false;
      seen.add(key);
      return true;
    });
}

export function setTextTargetAccept(state: TextSelectAuthoringState, index: number, accept: string[]): TextSelectAuthoringState {
  if (!state.targets[index]) return state;
  return { ...state, targets: state.targets.map((t, i) => (i === index ? { ...t, accept: [...accept] } : t)) };
}

export function setTextTargetSuffixes(state: TextSelectAuthoringState, index: number, suffixes: string[]): TextSelectAuthoringState {
  if (!state.targets[index]) return state;
  return { ...state, targets: state.targets.map((t, i) => (i === index ? { ...t, optionalSuffixes: [...suffixes] } : t)) };
}

/** Gap'in doğru sembolü: paletteki sembolü ekler / çıkarır (birden çok sembol kabul edilebilir). */
export function toggleGapSymbol(state: TextSelectAuthoringState, gapId: string, symbol: string): TextSelectAuthoringState {
  const token = state.tokens.find((t) => t.id === gapId);
  if (!token?.gap || !state.palette.includes(symbol)) return state;
  const current = gapAcceptOf(state, gapId);
  const next = current.includes(symbol) ? current.filter((s) => s !== symbol) : [...current, symbol];
  const gapAccept = emptyGapAccept();
  Object.keys(state.gapAccept).forEach((id) => {
    if (id !== gapId) gapAccept[id] = state.gapAccept[id];
  });
  if (next.length > 0) gapAccept[gapId] = state.palette.filter((s) => next.includes(s));
  return { ...state, gapAccept };
}

/** Öğrenciye giden plan: çözüm içermez (#570); stil punctuation'da, palet diğer modlarda gönderilmez. */
export function buildTextSelectPlan(state: TextSelectAuthoringState): TextSelectPlanV1 {
  const punctuation = state.mode === 'punctuation';
  const plan: Record<string, unknown> = {
    version: 1,
    type: TEXT_SELECT,
    mode: state.mode,
    ...(punctuation ? { palette: [...state.palette] } : { style: state.style }),
    tokens: state.tokens.map((t) => ({
      id: t.id,
      text: t.text,
      line: t.line,
      boxes: t.boxes.map(copyBox),
      ...(t.gap ? { gap: true } : {}),
    })),
  };
  return plan as unknown as TextSelectPlanV1;
}

function cleanTargets(state: TextSelectAuthoringState): TextSelectTarget[] {
  const words = new Set(state.tokens.filter((t) => !t.gap).map((t) => t.id));
  return state.targets
    .map((t) => ({ ...t, tokenIds: t.tokenIds.filter((id) => words.has(id)) }))
    .filter((t) => t.tokenIds.length > 0);
}

/**
 * Çözüm. select `{targets:[{tokenIds}]}`; correct `{targets:[{tokenIds, accept, optionalSuffixes?}]}`; punctuation
 * `{gaps:{g1:{accept}}}` (her gap). Hiçbir cevap girilmediyse null (taslak); eksik çözümü kayıt doğrulaması engeller.
 */
export function buildTextSelectSolution(state: TextSelectAuthoringState): Record<string, unknown> | null {
  if (state.mode === 'punctuation') {
    const gaps = state.tokens.filter((t) => t.gap);
    if (!gaps.some((g) => gapAcceptOf(state, g.id).length > 0)) return null;
    const result: Record<string, { accept: string[] }> = {};
    gaps.forEach((g) => (result[g.id] = { accept: [...gapAcceptOf(state, g.id)] }));
    return { gaps: result };
  }
  const targets = cleanTargets(state);
  if (targets.length === 0) return null;
  if (state.mode === 'select') return { targets: targets.map((t) => ({ tokenIds: [...t.tokenIds] })) };
  return {
    targets: targets.map((t) => ({
      tokenIds: [...t.tokenIds],
      accept: parseAcceptText(t.accept.join('\n')),
      ...(t.optionalSuffixes.length > 0 ? { optionalSuffixes: parseAcceptText(t.optionalSuffixes.join('\n')) } : {}),
    })),
  };
}

/** Kayıt engelleyen sorunlar (backend `ValidateDefinition` kuralları). */
export function textSelectIssues(state: TextSelectAuthoringState): TextSelectIssue[] {
  const limits = TEXT_SELECT_LIMITS;
  const issues: TextSelectIssue[] = [];
  const tokens = state.tokens;
  if (tokens.length < limits.minTokens) issues.push({ code: 'noText' });
  if (tokens.length > limits.maxTokens) issues.push({ code: 'tooManyTokens' });
  if (tokens.some((t) => t.line >= limits.maxLines)) issues.push({ code: 'tooManyLines' });
  tokens.forEach((t, i) => {
    if (t.text.length > limits.maxTokenTextLength || (!t.gap && !t.text.trim())) issues.push({ code: 'tokenTooLong', no: i + 1 });
  });
  const gaps = tokens.filter((t) => t.gap);
  if (state.mode === 'punctuation') {
    if (tokens.length > 0 && gaps.length < limits.minGaps) issues.push({ code: 'noGaps' });
    if (gaps.length > limits.maxGaps) issues.push({ code: 'tooManyGaps' });
    if (state.palette.length < limits.minSymbols || state.palette.length > limits.maxSymbols) issues.push({ code: 'paletteCount' });
    if (state.palette.some((s) => s.length > limits.maxSymbolLength)) issues.push({ code: 'symbolTooLong' });
    gaps.forEach((g, i) => {
      if (gapAcceptOf(state, g.id).length === 0) issues.push({ code: 'missingGapAccept', no: i + 1 });
    });
    return issues;
  }
  if (gaps.length > 0) issues.push({ code: 'gapsNeedPunctuation' });
  const targets = cleanTargets(state);
  if (tokens.length > 0 && targets.length < limits.minTargets) issues.push({ code: 'noTargets' });
  if (targets.length > limits.maxTargets) issues.push({ code: 'tooManyTargets' });
  targets.forEach((t, i) => {
    if (t.tokenIds.length > limits.maxTargetTokens) issues.push({ code: 'targetTooLong', no: i + 1 });
    if (state.mode !== 'correct') return;
    const accept = parseAcceptText(t.accept.join('\n'));
    const suffixes = parseAcceptText(t.optionalSuffixes.join('\n'));
    if (accept.length === 0) issues.push({ code: 'missingAccept', no: i + 1 });
    if (
      accept.length > limits.maxAcceptEntries ||
      suffixes.length > limits.maxSuffixEntries ||
      [...accept, ...suffixes].some((s) => s.length > limits.maxAcceptLength)
    ) {
      issues.push({ code: 'tooLong', no: i + 1 });
    }
  });
  return issues;
}

export function buildTextSelectInspector(state: TextSelectAuthoringState): TextSelectInspector {
  const targets = cleanTargets(state);
  const targetNo = new Map<string, number>();
  targets.forEach((t, i) => t.tokenIds.forEach((id) => targetNo.set(id, i + 1)));
  const gapNo = new Map(state.tokens.filter((t) => t.gap).map((t, i) => [t.id, i + 1]));
  const textOf = new Map(state.tokens.map((t) => [t.id, t.text]));
  const views: TextSelectTokenInspector[] = state.tokens.map((t) => ({
    ...t,
    targetNo: targetNo.get(t.id) ?? null,
    gapNo: gapNo.get(t.id) ?? null,
    accept: t.gap ? gapAcceptOf(state, t.id) : [],
    selected: state.selectedTokenId === t.id,
  }));
  const lines: TextSelectInspector['lines'] = [];
  for (const view of views) {
    const last = lines[lines.length - 1];
    if (last && last.line === view.line) last.tokens.push(view);
    else lines.push({ line: view.line, tokens: [view] });
  }
  return {
    mode: state.mode,
    style: state.style,
    palette: [...state.palette],
    lines,
    selected: views.find((v) => v.selected) ?? null,
    targets: targets.map((t, i) => ({
      ...t,
      no: i + 1,
      text: t.tokenIds.map((id) => textOf.get(id) ?? id).join(' '),
      missing: state.mode === 'correct' && parseAcceptText(t.accept.join('\n')).length === 0,
    })),
    tokenCount: state.tokens.length,
    gapCount: gapNo.size,
    hasPendingLine: !!state.pendingLine,
    issues: textSelectIssues(state),
    limits: TEXT_SELECT_LIMITS,
  };
}
