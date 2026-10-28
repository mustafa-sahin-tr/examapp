/**
 * Issue #590: görselsiz (form tabanlı) etkinlik yazarlığı — öğretmenin yazdığı içerik ↔ etkileşim planı + çözüm. Görselli
 * (kitaptan) etkinliklerle aynı tipler ve kontrolcüler kullanılır; ayrı veri modeli yok. Plan kutusuzdur: matris satırları
 * etiketli liste, sürükle-yerleştir hedefleri kova / sıralı konum, eşleştirme öğeleri metin. Çözüm ayrı alanda gider (#570).
 *
 * Dilim b: boşluk türleri (boşluk doldurma, soru + kısa cevap, tablo → `slotFill` düzenli plan; kelime havuzu → `dragPlace`
 * boşluklu metin) `activity-authoring-blanks.ts`'te. Issue #585: yol oyunları (`mazeTree` şablonları)
 * `activity-authoring-maze.ts`'te.
 *
 * Kayıtta backend sürükle-yerleştir kartlarını ve eşleştirme sağ öğelerini karışık sıraya dizip opak id verir (#576
 * H1/M1); düzenlemede formu plan + çözümden geri kurarken id'lere değil, plan sırası ve çözüm eşlemesine bakılır.
 */
import {
  BlankForm,
  BlankKind,
  buildBlankDefinition,
  emptyBlankForm,
  parseSlotFillBlanks,
  parseWordPool,
  validateBlankForm,
} from './activity-authoring-blanks';
import {
  ExitName,
  MAZE_KINDS,
  MazeForm,
  MazeKind,
  buildMazeDefinition,
  emptyMazeForm,
  isMazeForm,
  isMazeKind,
  parseMazeDefinition,
  validateMazeForm,
} from './activity-authoring-maze';
import { DRAG_PLACE, DRAG_PLACE_LIMITS, DragPlacePlanV1, parseDragPlacePlan, parseDragPlaceSolution, cardsIn } from './drag-place';
import { MATCH, MATCH_LIMITS, MatchPlanV1, parseMatchPlan, parseMatchSolution, rightOf } from './match';
import {
  MATRIX_CHOICE,
  MATRIX_CHOICE_LIMITS,
  MatrixChoicePlanV1,
  parseMatrixChoicePlan,
  parseMatrixChoiceSolution,
  selectionOf,
} from './matrix-choice';
import { MAZE_TREE } from './maze-tree';
import { SLOT_FILL, parseSlotFillPlan } from './slot-fill';

export type AuthoringKind = 'matrix' | 'classify' | 'order' | 'match' | BlankKind | MazeKind;

export const AUTHORING_KINDS: readonly AuthoringKind[] = [
  'matrix',
  'classify',
  'order',
  'match',
  'fillText',
  'shortAnswer',
  'fillTable',
  'wordPool',
  ...MAZE_KINDS,
];

export const AUTHORING_LABEL_MAX = MATRIX_CHOICE_LIMITS.maxLabelLength;

export interface MatrixRowForm {
  text: string;
  /** Doğru kolonların sırası (`columns` dizinine göre). */
  correct: number[];
}

export interface MatrixForm {
  kind: 'matrix';
  columns: string[];
  multiPerRow: boolean;
  shuffleRows: boolean;
  rows: MatrixRowForm[];
}

export interface ClassifyCardForm {
  text: string;
  /** Kartın doğru kovası (`buckets` dizinine göre); null = çeldirici (hiçbir kovaya ait değil). */
  bucket: number | null;
}

export interface ClassifyForm {
  kind: 'classify';
  buckets: string[];
  cards: ClassifyCardForm[];
}

export interface OrderForm {
  kind: 'order';
  /** Doğru sırayla; öğrenciye karışık gelir. */
  items: string[];
}

export interface MatchPairForm {
  left: string;
  right: string;
}

export interface MatchForm {
  kind: 'match';
  pairs: MatchPairForm[];
  /** Hiçbir sol öğeye ait olmayan sağ öğeler. */
  distractors: string[];
}

export type AuthoringForm = MatrixForm | ClassifyForm | OrderForm | MatchForm | BlankForm | MazeForm;

export interface AuthoringDefinition {
  interactionType: typeof MATRIX_CHOICE | typeof DRAG_PLACE | typeof MATCH | typeof SLOT_FILL | typeof MAZE_TREE;
  plan: string;
  solution: string;
}

/** Doğrulama hatası: i18n anahtarı (`activityEditor.errors.<key>`) + parametreler. */
export interface AuthoringError {
  key: string;
  params?: Record<string, unknown>;
}

/** Yol oyunu şablonlarının çevrilen varsayılanları (dal etiketleri, çıkış adı). */
export interface MazeDefaults {
  branches: [string, string];
  exitName: ExitName;
}

const DEFAULT_MAZE: MazeDefaults = { branches: ['D', 'Y'], exitName: (n) => `${n}.` };

export function emptyForm(
  kind: AuthoringKind,
  trueFalseLabels: readonly [string, string],
  maze: MazeDefaults = DEFAULT_MAZE
): AuthoringForm {
  if (isMazeKind(kind)) return emptyMazeForm(kind, maze.branches, maze.exitName);
  switch (kind) {
    case 'matrix':
      return {
        kind,
        columns: [...trueFalseLabels],
        multiPerRow: false,
        shuffleRows: false,
        rows: [{ text: '', correct: [] }],
      };
    case 'classify':
      return { kind, buckets: ['', ''], cards: [{ text: '', bucket: 0 }, { text: '', bucket: 1 }] };
    case 'order':
      return { kind, items: ['', '', ''] };
    case 'match':
      return { kind, pairs: [{ left: '', right: '' }, { left: '', right: '' }], distractors: [] };
    default:
      return emptyBlankForm(kind);
  }
}

/** Kovanın kapasitesi: kova sayısı ipucu vermesin diye her kova tüm kartları alabilir (backend üst sınırına kadar). */
function bucketCapacity(cardCount: number): number {
  return Math.min(DRAG_PLACE_LIMITS.maxCapacity, Math.max(DRAG_PLACE_LIMITS.minCapacity, cardCount));
}

export function validateForm(form: AuthoringForm): AuthoringError[] {
  if (isMazeForm(form)) return validateMazeForm(form);
  const errors: AuthoringError[] = [];
  const blank = (values: string[]) => values.some((v) => !v.trim());
  const tooLong = (values: string[]) => values.some((v) => v.trim().length > AUTHORING_LABEL_MAX);
  const texts = (values: string[], blankKey: string) => {
    if (blank(values)) errors.push({ key: blankKey });
    if (tooLong(values)) errors.push({ key: 'labelTooLong', params: { max: AUTHORING_LABEL_MAX } });
  };

  switch (form.kind) {
    case 'matrix': {
      const { minColumns, maxColumns, minRows, maxRows } = MATRIX_CHOICE_LIMITS;
      if (form.columns.length < minColumns || form.columns.length > maxColumns)
        errors.push({ key: 'columnCount', params: { min: minColumns, max: maxColumns } });
      if (form.rows.length < minRows || form.rows.length > maxRows)
        errors.push({ key: 'rowCount', params: { min: minRows, max: maxRows } });
      texts(form.columns, 'blankColumn');
      texts(
        form.rows.map((r) => r.text),
        'blankRow'
      );
      if (form.rows.some((r) => r.correct.length === 0 || (!form.multiPerRow && r.correct.length !== 1)))
        errors.push({ key: 'rowWithoutAnswer' });
      break;
    }
    case 'classify': {
      const { minTargets, maxTargets, minCards, maxCards, maxCapacity } = DRAG_PLACE_LIMITS;
      if (form.buckets.length < minTargets || form.buckets.length > maxTargets)
        errors.push({ key: 'bucketCount', params: { min: minTargets, max: maxTargets } });
      if (form.cards.length < minCards || form.cards.length > maxCards)
        errors.push({ key: 'cardCount', params: { min: minCards, max: maxCards } });
      texts(form.buckets, 'blankBucket');
      texts(
        form.cards.map((c) => c.text),
        'blankCard'
      );
      if (!form.cards.some((c) => c.bucket !== null)) errors.push({ key: 'noPlacedCard' });
      const capacity = bucketCapacity(form.cards.length);
      if (form.buckets.some((_, i) => form.cards.filter((c) => c.bucket === i).length > capacity))
        errors.push({ key: 'bucketOverCapacity', params: { max: maxCapacity } });
      break;
    }
    case 'order': {
      const { maxTargets } = DRAG_PLACE_LIMITS;
      if (form.items.length < 2 || form.items.length > maxTargets)
        errors.push({ key: 'orderCount', params: { min: 2, max: maxTargets } });
      texts(form.items, 'blankItem');
      break;
    }
    case 'match': {
      const { minLeft, maxLeft, maxRight } = MATCH_LIMITS;
      if (form.pairs.length < minLeft || form.pairs.length > maxLeft)
        errors.push({ key: 'pairCount', params: { min: minLeft, max: maxLeft } });
      const rights = [...form.pairs.map((p) => p.right), ...form.distractors];
      if (rights.length > maxRight) errors.push({ key: 'rightCount', params: { max: maxRight } });
      texts(
        form.pairs.flatMap((p) => [p.left, p.right]),
        'blankPair'
      );
      texts(form.distractors, 'blankDistractor');
      const normalized = rights.map((r) => r.trim().toLocaleLowerCase('tr'));
      if (new Set(normalized).size !== normalized.length) errors.push({ key: 'duplicateRight' });
      break;
    }
    default:
      errors.push(...validateBlankForm(form));
  }
  return errors;
}

/** Form → plan + çözüm (JSON). Önce {@link validateForm} geçmeli. */
export function buildDefinition(form: AuthoringForm): AuthoringDefinition {
  if (isMazeForm(form)) return buildMazeDefinition(form);
  switch (form.kind) {
    case 'matrix': {
      const columns = form.columns.map((label, i) => ({ id: `c${i + 1}`, label: label.trim() }));
      const rows = form.rows.map((row, i) => ({ id: `r${i + 1}`, label: row.text.trim() }));
      const solution: Record<string, string[]> = {};
      form.rows.forEach((row, i) => {
        solution[`r${i + 1}`] = [...row.correct].sort((a, b) => a - b).map((c) => `c${c + 1}`);
      });
      const plan = {
        version: 1,
        type: MATRIX_CHOICE,
        multiPerRow: form.multiPerRow,
        ...(form.shuffleRows ? { shuffleRows: true } : {}),
        columns,
        rows,
      };
      return { interactionType: MATRIX_CHOICE, plan: JSON.stringify(plan), solution: JSON.stringify(solution) };
    }
    case 'classify': {
      const capacity = bucketCapacity(form.cards.length);
      const targets = form.buckets.map((label, i) => ({ id: `t${i + 1}`, label: label.trim(), capacity }));
      const cards = form.cards.map((card, i) => ({ id: `k${i + 1}`, label: card.text.trim() }));
      const solution: Record<string, string[]> = {};
      targets.forEach((target, ti) => {
        solution[target.id] = form.cards.flatMap((card, ci) => (card.bucket === ti ? [`k${ci + 1}`] : []));
      });
      const plan = { version: 1, type: DRAG_PLACE, mode: 'place', reuse: false, targets, cards };
      return { interactionType: DRAG_PLACE, plan: JSON.stringify(plan), solution: JSON.stringify(solution) };
    }
    case 'order': {
      const targets = form.items.map((_, i) => ({ id: `t${i + 1}`, label: `${i + 1}`, capacity: 1 }));
      const cards = form.items.map((text, i) => ({ id: `k${i + 1}`, label: text.trim() }));
      const solution: Record<string, string[]> = {};
      targets.forEach((target, i) => (solution[target.id] = [`k${i + 1}`]));
      const plan = { version: 1, type: DRAG_PLACE, mode: 'order', reuse: false, targets, cards };
      return { interactionType: DRAG_PLACE, plan: JSON.stringify(plan), solution: JSON.stringify(solution) };
    }
    case 'match': {
      const left = form.pairs.map((pair, i) => ({ id: `l${i + 1}`, label: pair.left.trim() }));
      const right = [...form.pairs.map((p) => p.right), ...form.distractors].map((label, i) => ({
        id: `r${i + 1}`,
        label: label.trim(),
      }));
      const solution: Record<string, string> = {};
      form.pairs.forEach((_, i) => (solution[`l${i + 1}`] = `r${i + 1}`));
      const plan = { version: 1, type: MATCH, left, right, prelinked: [] };
      return { interactionType: MATCH, plan: JSON.stringify(plan), solution: JSON.stringify(solution) };
    }
    default:
      return buildBlankDefinition(form);
  }
}

/**
 * Kayıtlı plan + çözüm → form (düzenleme). Görselli (kutulu) ya da formun ifade edemediği plan (örnek çiftli eşleştirme,
 * tekrar kullanımlı sürükle-yerleştir, etiketsiz öğe) null döner: o madde tuvalde düzenlenir.
 */
export function parseDefinition(
  interactionType: string | null | undefined,
  planJson: string | null | undefined,
  solutionJson: string | null | undefined
): AuthoringForm | null {
  const type = (interactionType ?? '').toLowerCase();
  if (type === MATRIX_CHOICE.toLowerCase()) return parseMatrix(parseMatrixChoicePlan(planJson), solutionJson);
  if (type === DRAG_PLACE.toLowerCase()) return parseDragPlace(parseDragPlacePlan(planJson), solutionJson);
  if (type === MATCH.toLowerCase()) return parseMatch(parseMatchPlan(planJson), solutionJson);
  if (type === SLOT_FILL.toLowerCase()) return parseSlotFillBlanks(parseSlotFillPlan(planJson), solutionJson);
  if (type === MAZE_TREE.toLowerCase()) return parseMazeDefinition(planJson, solutionJson);
  return null;
}

function parseMatrix(plan: MatrixChoicePlanV1 | null, solutionJson: string | null | undefined): MatrixForm | null {
  if (!plan || plan.rows.some((r) => r.box || !r.label || (r.cells && Object.keys(r.cells).length > 0))) return null;
  const solution = parseMatrixChoiceSolution(solutionJson, plan);
  const columnIndex = new Map(plan.columns.map((c, i) => [c.id, i]));
  return {
    kind: 'matrix',
    columns: plan.columns.map((c) => c.label),
    multiPerRow: plan.multiPerRow,
    shuffleRows: plan.shuffleRows === true,
    rows: plan.rows.map((row) => ({
      text: row.label ?? '',
      correct: selectionOf(solution, row.id)
        .map((id) => columnIndex.get(id))
        .filter((i): i is number => i !== undefined),
    })),
  };
}

function parseDragPlace(
  plan: DragPlacePlanV1 | null,
  solutionJson: string | null | undefined
): ClassifyForm | OrderForm | BlankForm | null {
  if (plan?.layout) return parseWordPool(plan, solutionJson);
  if (!plan || plan.reuse) return null;
  if (plan.targets.some((t) => t.box) || plan.cards.some((c) => c.box || !c.label)) return null;
  const solution = parseDragPlaceSolution(solutionJson, plan);
  const cardLabel = new Map(plan.cards.map((c) => [c.id, c.label ?? '']));

  if (plan.mode === 'order') {
    const items = plan.targets.map((t) => cardLabel.get(cardsIn(solution, t.id)[0] ?? '') ?? '');
    return { kind: 'order', items };
  }

  const bucketOf = new Map<string, number>();
  plan.targets.forEach((t, ti) => cardsIn(solution, t.id).forEach((cardId) => bucketOf.set(cardId, ti)));
  if (plan.targets.some((t) => !t.label)) return null;
  return {
    kind: 'classify',
    buckets: plan.targets.map((t) => t.label ?? ''),
    cards: plan.cards.map((c) => ({ text: c.label ?? '', bucket: bucketOf.get(c.id) ?? null })),
  };
}

function parseMatch(plan: MatchPlanV1 | null, solutionJson: string | null | undefined): MatchForm | null {
  if (!plan || plan.prelinked.length > 0) return null;
  if ([...plan.left, ...plan.right].some((item) => item.box || !item.label)) return null;
  const solution = parseMatchSolution(solutionJson, plan);
  const rightLabel = new Map(plan.right.map((r) => [r.id, r.label ?? '']));
  const used = new Set<string>();
  const pairs = plan.left.map((l) => {
    const rightId = rightOf(solution, l.id);
    if (rightId) used.add(rightId);
    return { left: l.label ?? '', right: rightId ? (rightLabel.get(rightId) ?? '') : '' };
  });
  return {
    kind: 'match',
    pairs,
    distractors: plan.right.filter((r) => !used.has(r.id)).map((r) => r.label ?? ''),
  };
}
