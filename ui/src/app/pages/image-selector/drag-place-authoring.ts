/**
 * Issue #576: sürükle-yerleştir (dragPlace) yazarlığının saf kısmı — durum, plan / çözüm üreticileri ve doğrulama. Tuval
 * (`ImageSelectorComponent`) yalnız çizim / seçim / müfettiş köprüsünü tutar. Kurallar backend `DragPlaceGrader`
 * (`ValidateDefinition`) ile aynı: 1..30 hedef, 1..60 kart, kapasite 1..30 (sıra modunda 1), kartta etiket ya da kutu,
 * çözümde en az bir yerleşim, hedef ve kart id'leri çakışmaz (`t*` / `c*`).
 */
import { DRAG_PLACE, DRAG_PLACE_LIMITS, DragPlaceMode, DragPlacePlanV1 } from '../../models/drag-place';
import { InteractionBox, copyBox, isValidInteractionId } from '../../models/interaction-ids';

export interface PlaceTargetAuthoring {
  id: string;
  label: string;
  /** Kutulu hedef görsel üstünde bırakma alanı, kutusuz hedef görselin altında kova. */
  box: InteractionBox | null;
  capacity: number;
}

export interface PlaceCardAuthoring {
  id: string;
  label: string;
  /** Kırpma kartı (soru görselinin bölgesi). */
  box: InteractionBox | null;
}

export interface DragPlaceAuthoringState {
  mode: DragPlaceMode;
  reuse: boolean;
  targets: PlaceTargetAuthoring[];
  cards: PlaceCardAuthoring[];
  /** Hedef id → doğru kart id'leri (prototipsiz). */
  solution: Record<string, string[]>;
  /** Çözüm atamasında seçili hedef (tuvalde kart kutusuna tık = bu hedefe ata / kaldır). */
  selectedTargetId: string | null;
}

export type DragPlaceIssueCode =
  | 'noTargets'
  | 'tooManyTargets'
  | 'noCards'
  | 'tooManyCards'
  | 'targetNoLabel'
  | 'cardEmpty'
  | 'tooLong'
  | 'overCapacity'
  | 'noSolution'
  | 'orderMissing';

export interface DragPlaceIssue {
  code: DragPlaceIssueCode;
  /** Hedef / kart numarası (1-tabanlı). */
  no?: number;
}

export interface DragPlaceInspector {
  mode: DragPlaceMode;
  reuse: boolean;
  targets: Array<PlaceTargetAuthoring & { no: number; cardIds: string[]; full: boolean }>;
  cards: Array<PlaceCardAuthoring & { no: number; targetNos: number[] }>;
  selected: (PlaceTargetAuthoring & { no: number; cardIds: string[]; full: boolean }) | null;
  /** Çözümde hiçbir hedefe atanmamış kart (çeldirici) sayısı. */
  distractorCount: number;
  issues: DragPlaceIssue[];
  limits: typeof DRAG_PLACE_LIMITS;
}

function emptySolution(): Record<string, string[]> {
  return Object.create(null) as Record<string, string[]>;
}

function assigned(state: DragPlaceAuthoringState, targetId: string): string[] {
  return Object.hasOwn(state.solution, targetId) ? state.solution[targetId] : [];
}

function withAssigned(state: DragPlaceAuthoringState, targetId: string, cards: string[]): Record<string, string[]> {
  const next = emptySolution();
  Object.keys(state.solution).forEach((key) => {
    if (key !== targetId) next[key] = state.solution[key];
  });
  if (cards.length > 0) next[targetId] = cards;
  return next;
}

export function createDragPlaceState(): DragPlaceAuthoringState {
  return { mode: 'place', reuse: false, targets: [], cards: [], solution: emptySolution(), selectedTargetId: null };
}

/**
 * Issue #579: kayıtlı / taslak plandan yazarlık durumu (çözümsüz). Plan `parseDragPlacePlan` ile temizlenmiş olmalı.
 */
export function dragPlaceStateFromPlan(plan: DragPlacePlanV1): DragPlaceAuthoringState {
  return {
    mode: plan.mode,
    reuse: plan.reuse,
    targets: plan.targets.map((t) => ({
      id: t.id,
      label: t.label ?? '',
      box: t.box ? copyBox(t.box) : null,
      capacity: plan.mode === 'order' ? 1 : clampCapacity(t.capacity),
    })),
    cards: plan.cards.map((c) => ({ id: c.id, label: c.label ?? '', box: c.box ? copyBox(c.box) : null })),
    solution: emptySolution(),
    selectedTargetId: null,
  };
}

/** `prefix` + ilk boş numara; hedef `t`, kart `c` öneki aldığı için iki uzay çakışmaz. */
export function nextInteractionId(prefix: string, used: string[]): string {
  const taken = new Set(used);
  for (let i = 1; ; i++) {
    const id = `${prefix}${i}`;
    if (!taken.has(id) && isValidInteractionId(id)) return id;
  }
}

function allIds(state: DragPlaceAuthoringState): string[] {
  return [...state.targets.map((t) => t.id), ...state.cards.map((c) => c.id)];
}

function clampCapacity(value: number): number {
  const n = Number.isFinite(value) ? Math.trunc(value) : 1;
  return Math.min(DRAG_PLACE_LIMITS.maxCapacity, Math.max(DRAG_PLACE_LIMITS.minCapacity, n));
}

/** Yeni hedef (kutulu bırakma alanı ya da kutusuz kova); seçili hedef olur. Sınırdaysa durum değişmez. */
export function addPlaceTarget(state: DragPlaceAuthoringState, box: InteractionBox | null, label = ''): DragPlaceAuthoringState {
  if (state.targets.length >= DRAG_PLACE_LIMITS.maxTargets) return state;
  const id = nextInteractionId('t', allIds(state));
  return {
    ...state,
    targets: [...state.targets, { id, label: label.slice(0, DRAG_PLACE_LIMITS.maxLabelLength), box: box ? copyBox(box) : null, capacity: 1 }],
    selectedTargetId: id,
  };
}

/** Metinden kartlar: her satır bir kart (boş satırlar atlanır, sınırda kesilir). */
export function addPlaceCardsFromText(state: DragPlaceAuthoringState, text: string): DragPlaceAuthoringState {
  const labels = (text ?? '')
    .split(/\r?\n/)
    .map((line) => line.trim().slice(0, DRAG_PLACE_LIMITS.maxLabelLength))
    .filter((line) => !!line);
  let next = state;
  for (const label of labels) {
    if (next.cards.length >= DRAG_PLACE_LIMITS.maxCards) break;
    const id = nextInteractionId('c', allIds(next));
    next = { ...next, cards: [...next.cards, { id, label, box: null }] };
  }
  return next;
}

/** Kırpma kartı (tuvalde çizilen kutu). */
export function addPlaceCardBox(state: DragPlaceAuthoringState, box: InteractionBox): DragPlaceAuthoringState {
  if (state.cards.length >= DRAG_PLACE_LIMITS.maxCards) return state;
  const id = nextInteractionId('c', allIds(state));
  return { ...state, cards: [...state.cards, { id, label: '', box: copyBox(box) }] };
}

export function removePlaceTarget(state: DragPlaceAuthoringState, targetId: string): DragPlaceAuthoringState {
  const targets = state.targets.filter((t) => t.id !== targetId);
  return {
    ...state,
    targets,
    solution: withAssigned(state, targetId, []),
    selectedTargetId: state.selectedTargetId === targetId ? targets.at(-1)?.id ?? null : state.selectedTargetId,
  };
}

export function removePlaceCard(state: DragPlaceAuthoringState, cardId: string): DragPlaceAuthoringState {
  const solution = emptySolution();
  Object.keys(state.solution).forEach((key) => {
    const cards = state.solution[key].filter((id) => id !== cardId);
    if (cards.length > 0) solution[key] = cards;
  });
  return { ...state, cards: state.cards.filter((c) => c.id !== cardId), solution };
}

export function setPlaceTargetLabel(state: DragPlaceAuthoringState, targetId: string, label: string): DragPlaceAuthoringState {
  const value = (label ?? '').slice(0, DRAG_PLACE_LIMITS.maxLabelLength);
  return { ...state, targets: state.targets.map((t) => (t.id === targetId ? { ...t, label: value } : t)) };
}

export function setPlaceCardLabel(state: DragPlaceAuthoringState, cardId: string, label: string): DragPlaceAuthoringState {
  const value = (label ?? '').slice(0, DRAG_PLACE_LIMITS.maxLabelLength);
  return { ...state, cards: state.cards.map((c) => (c.id === cardId ? { ...c, label: value } : c)) };
}

/** Kapasite (1..30; sıra modunda 1). Azalınca fazla doğru kartlar sondan düşer. */
export function setPlaceTargetCapacity(state: DragPlaceAuthoringState, targetId: string, capacity: number): DragPlaceAuthoringState {
  const value = state.mode === 'order' ? 1 : clampCapacity(capacity);
  const next = { ...state, targets: state.targets.map((t) => (t.id === targetId ? { ...t, capacity: value } : t)) };
  return { ...next, solution: withAssigned(next, targetId, assigned(next, targetId).slice(0, value)) };
}

/** Sıra modunda tüm kapasiteler 1 olur ve fazla doğru kartlar düşer. */
export function setDragPlaceMode(state: DragPlaceAuthoringState, mode: DragPlaceMode): DragPlaceAuthoringState {
  if (mode !== 'order') return { ...state, mode };
  const solution = emptySolution();
  Object.keys(state.solution).forEach((key) => (solution[key] = state.solution[key].slice(0, 1)));
  return { ...state, mode, targets: state.targets.map((t) => ({ ...t, capacity: 1 })), solution };
}

/** Tekrar kullanım kapanınca bir kart yalnız ilk hedefinde kalır. */
export function setDragPlaceReuse(state: DragPlaceAuthoringState, reuse: boolean): DragPlaceAuthoringState {
  if (reuse) return { ...state, reuse };
  const used = new Set<string>();
  const solution = emptySolution();
  state.targets.forEach((t) => {
    const cards = assigned(state, t.id).filter((id) => !used.has(id));
    cards.forEach((id) => used.add(id));
    if (cards.length > 0) solution[t.id] = cards;
  });
  return { ...state, reuse, solution };
}

export function selectPlaceTarget(state: DragPlaceAuthoringState, targetId: string | null): DragPlaceAuthoringState {
  if (targetId !== null && !state.targets.some((t) => t.id === targetId)) return state;
  return { ...state, selectedTargetId: targetId };
}

/**
 * Kartı hedefin doğru kartı yapar / kaldırır. Tekrar kullanım kapalıyken kart başka hedeften alınır; tek kartlık hedefte
 * eski kartın yerine geçer; çok kartlık dolu hedefe eklenmez (durum aynen döner).
 */
export function togglePlaceAssignment(state: DragPlaceAuthoringState, targetId: string, cardId: string): DragPlaceAuthoringState {
  const target = state.targets.find((t) => t.id === targetId);
  if (!target || !state.cards.some((c) => c.id === cardId)) return state;
  const current = assigned(state, targetId);
  if (current.includes(cardId)) return { ...state, solution: withAssigned(state, targetId, current.filter((id) => id !== cardId)) };
  let next = current;
  if (current.length >= target.capacity) {
    if (target.capacity !== 1) return state;
    next = [];
  }
  let base: DragPlaceAuthoringState = state;
  if (!state.reuse) {
    const solution = emptySolution();
    Object.keys(state.solution).forEach((key) => {
      const cards = key === targetId ? state.solution[key] : state.solution[key].filter((id) => id !== cardId);
      if (cards.length > 0) solution[key] = cards;
    });
    base = { ...state, solution };
  }
  return { ...base, solution: withAssigned(base, targetId, [...next, cardId]) };
}

/** Sıra modu kısayolu: kart sayısı kadar konum (eksik konum eklenir) ve doğru sıra = kartların listedeki sırası. */
export function fillOrderFromCards(state: DragPlaceAuthoringState): DragPlaceAuthoringState {
  let next: DragPlaceAuthoringState = { ...setDragPlaceMode(state, 'order') };
  const want = Math.min(next.cards.length, DRAG_PLACE_LIMITS.maxTargets);
  while (next.targets.length < want) next = addPlaceTarget(next, null);
  const solution = emptySolution();
  next.targets.forEach((t, i) => {
    if (next.cards[i]) solution[t.id] = [next.cards[i].id];
  });
  return { ...next, solution, selectedTargetId: next.targets[0]?.id ?? null };
}

/** Öğrenciye giden plan: çözüm içermez (#570); yalnız izinli anahtarlar, boş etiket gönderilmez. */
export function buildDragPlacePlan(state: DragPlaceAuthoringState): DragPlacePlanV1 {
  return {
    version: 1,
    type: DRAG_PLACE,
    mode: state.mode,
    reuse: state.reuse,
    targets: state.targets.map((t) => ({
      id: t.id,
      ...(t.label.trim() ? { label: t.label.trim() } : {}),
      ...(t.box ? { box: copyBox(t.box) } : {}),
      capacity: state.mode === 'order' ? 1 : t.capacity,
    })),
    cards: state.cards.map((c) => ({
      id: c.id,
      ...(c.label.trim() ? { label: c.label.trim() } : {}),
      ...(c.box ? { box: copyBox(c.box) } : {}),
    })),
  };
}

/** Çözüm `{hedef: [kartlar]}` her hedef için (boş dizi dahil); hiç yerleşim yoksa null (taslak). */
export function buildDragPlaceSolution(state: DragPlaceAuthoringState): Record<string, string[]> | null {
  const cardIds = new Set(state.cards.map((c) => c.id));
  const result: Record<string, string[]> = {};
  let total = 0;
  state.targets.forEach((t) => {
    const cards = assigned(state, t.id).filter((id) => cardIds.has(id));
    total += cards.length;
    result[t.id] = cards;
  });
  return total > 0 ? result : null;
}

export function dragPlaceIssues(state: DragPlaceAuthoringState): DragPlaceIssue[] {
  const limits = DRAG_PLACE_LIMITS;
  const issues: DragPlaceIssue[] = [];
  if (state.targets.length < limits.minTargets) issues.push({ code: 'noTargets' });
  if (state.targets.length > limits.maxTargets) issues.push({ code: 'tooManyTargets' });
  if (state.cards.length < limits.minCards) issues.push({ code: 'noCards' });
  if (state.cards.length > limits.maxCards) issues.push({ code: 'tooManyCards' });
  state.targets.forEach((t, i) => {
    if (state.mode === 'place' && !t.box && !t.label.trim()) issues.push({ code: 'targetNoLabel', no: i + 1 });
    if (t.label.trim().length > limits.maxLabelLength) issues.push({ code: 'tooLong', no: i + 1 });
    if (assigned(state, t.id).length > t.capacity) issues.push({ code: 'overCapacity', no: i + 1 });
    if (state.mode === 'order' && assigned(state, t.id).length === 0) issues.push({ code: 'orderMissing', no: i + 1 });
  });
  state.cards.forEach((c, i) => {
    if (!c.box && !c.label.trim()) issues.push({ code: 'cardEmpty', no: i + 1 });
  });
  if (state.targets.length > 0 && state.cards.length > 0 && !buildDragPlaceSolution(state)) issues.push({ code: 'noSolution' });
  return issues;
}

export function buildDragPlaceInspector(state: DragPlaceAuthoringState): DragPlaceInspector {
  const targets = state.targets.map((t, i) => {
    const cardIds = assigned(state, t.id);
    return { ...t, no: i + 1, cardIds, full: cardIds.length >= t.capacity };
  });
  const targetNos = new Map<string, number[]>();
  targets.forEach((t) => t.cardIds.forEach((id) => targetNos.set(id, [...(targetNos.get(id) ?? []), t.no])));
  const cards = state.cards.map((c, i) => ({ ...c, no: i + 1, targetNos: targetNos.get(c.id) ?? [] }));
  return {
    mode: state.mode,
    reuse: state.reuse,
    targets,
    cards,
    selected: targets.find((t) => t.id === state.selectedTargetId) ?? null,
    distractorCount: cards.filter((c) => c.targetNos.length === 0).length,
    issues: dragPlaceIssues(state),
    limits: DRAG_PLACE_LIMITS,
  };
}
