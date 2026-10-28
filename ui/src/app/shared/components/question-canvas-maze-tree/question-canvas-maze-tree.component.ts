import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { QuestionRegion } from '../../../models/draws';
import {
  MAZE_LIMITS,
  MazeEdge,
  MazeFeedback,
  MazeNode,
  MazeTreePlanV1,
  boardRows,
  buildMazeMarkedPayload,
  buildMazePathPayload,
  edgeTarget,
  mazeRoute,
  nodeOf,
  parseMazeAnswer,
  parseMazeSolution,
  parseMazeTreePlan,
  roomsGrid,
  treeLayout,
  usableEdges,
  walkMaze,
} from '../../../models/maze-tree';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';

/** Ağaç düzeyi / oda satırı yüksekliği (px); kartlar ve dallar bu ızgaraya yerleşir. */
export const MAZE_TREE_ROW_PX = 150;
export const MAZE_ROOM_ROW_PX = 168;

interface PositionedNode {
  node: MazeNode;
  /** Sahne genişliğine göre yüzde. */
  left: number;
  top: number;
  width: number;
}

interface PositionedEdge {
  edge: MazeEdge;
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  /** Etiket düğmesinin yeri: yüzde (yatay) ve px (dikey). */
  left: number;
  top: number;
}

/**
 * Issue #585: yol oyunları oynatıcısı (bileşen G, `mazeTree`). Görsel (varsa) etkileşimsiz üstte, oyun HTML olarak çizilir:
 * - ağaç: düzeyler halinde ifade kartları, kartları bağlayan çizgilerin ortasında D/Y düğmeleri; seçilen dal yolu boyar,
 *   işaretçi yeni düğüme kayar;
 * - odalar: ızgarada oda kartları, iki oda arasındaki duvarda (çapraz dahil) kapı düğmeleri; kapılar iki yönlü;
 * - pano: alttan üste satırlar, yalnız sıradaki satırın kutuları seçilebilir, seçilen kutu yanar;
 * - taşlar: taşlara dokunarak "basılmaz" işareti.
 *
 * Yol adım adım kurulur (Geri al / Baştan); her adım cevap olarak kaydedilir. Kontrol sonrası sapma düğümü ("Burada yanlış
 * yöne saptın") ve sapılan dal / kırılan kutu gösterilir; çözüm açıkken doğru yol yeşil. Geri bildirim yalnız öğrencinin kendi
 * yolunu anlatır (rota sızmaz). OnPush: tüm durum signal.
 */
@Component({
  selector: 'app-question-canvas-maze-tree',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective, StorageImageRetryDirective],
  templateUrl: './question-canvas-maze-tree.component.html',
  styleUrl: './question-canvas-maze-tree.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionCanvasMazeTreeComponent {
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    const changed = value !== this._region();
    this._region.set(value);
    if (!changed) return;
    this.lastEmitted = null;
    this.restore();
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this._plan.set(parseMazeTreePlan(value));
    this.restore();
  }

  @Input() set interactionSolutionJson(value: string | null | undefined) {
    this._solutionJson.set(value ?? null);
  }

  @Input() imageRefresh: StorageImageResolver | null = null;

  @Input() hidePassage = false;

  @Input() set answerPayloadJson(value: string | null | undefined) {
    this._answerPayloadJson.set(value ?? null);
    this.restore();
  }

  /** Kontrol sonucu (sunucunun `feedback`'i); cevap değişince ebeveyn null verir. */
  @Input() set feedback(value: MazeFeedback | null | undefined) {
    this._feedback.set(value ?? null);
  }

  /** true iken yol salt okunur, doğru yol gösterilir. */
  @Input() set showSolution(value: boolean) {
    this._showSolution.set(!!value);
  }

  @Input() set activityMode(value: boolean) {
    this._activityMode.set(!!value);
  }

  @Input() set disabled(value: boolean) {
    this._disabled.set(!!value);
  }

  @Output() answerPayloadChange = new EventEmitter<string>();
  @Output() answered = new EventEmitter<void>();

  /** Son yayınlanan payload: ebeveynin geri verdiği yankı ekrandaki yolu ezmez. */
  private lastEmitted: string | null = null;

  private readonly _region = signal<QuestionRegion | null>(null);
  private readonly _plan = signal<MazeTreePlanV1 | null>(null);
  private readonly _solutionJson = signal<string | null>(null);
  private readonly _answerPayloadJson = signal<string | null>(null);
  private readonly _feedback = signal<MazeFeedback | null>(null);
  private readonly _showSolution = signal(false);
  private readonly _activityMode = signal(false);
  private readonly _disabled = signal(false);

  /** Seçilen kenarlar (yol varyantları). */
  readonly path = signal<string[]>([]);
  /** İşaretli taşlar. */
  readonly marked = signal<string[]>([]);

  readonly rowPx = MAZE_TREE_ROW_PX;
  readonly roomRowPx = MAZE_ROOM_ROW_PX;
  readonly plan = this._plan.asReadonly();
  readonly variant = computed(() => this._plan()?.variant ?? null);
  readonly solutionShown = this._showSolution.asReadonly();
  readonly activity = this._activityMode.asReadonly();
  readonly imageUrl = computed(() => this._region()?.imageUrl || null);
  readonly passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);
  readonly passageTitle = computed(() => this._region()?.passage?.title ?? null);
  readonly readOnly = computed(() => this._showSolution() || this._disabled());

  readonly walk = computed(() => {
    const plan = this._plan();
    return plan && plan.variant !== 'stones' ? walkMaze(plan, this.path()) : null;
  });
  readonly current = computed(() => this.walk()?.current ?? null);
  readonly finished = computed(() => this.walk()?.finished ?? false);
  readonly currentNode = computed(() => {
    const plan = this._plan();
    return plan ? nodeOf(plan, this.current()) : null;
  });
  readonly visitedNodes = computed(() => new Set(this.walk()?.nodes ?? []));
  readonly chosenEdges = computed(() => new Set(this.walk()?.edges ?? []));
  readonly availableEdges = computed(() => {
    const plan = this._plan();
    const current = this.current();
    // Odalarda ileri-geri gidip gelmek yolu uzatır; sınırda yeni adım alınmaz (sunucu daha uzun yolu reddeder).
    if (!plan || !current || this.finished() || this.readOnly() || this.path().length >= MAZE_LIMITS.maxPathLength) {
      return new Set<string>();
    }
    return new Set(usableEdges(plan, current).map((e) => e.id));
  });

  readonly solution = computed(() => parseMazeSolution(this._solutionJson(), this._plan()));
  /** Çözüm açıkken doğru yolun kenarları ve düğümleri. */
  readonly routeEdges = computed(() => {
    const plan = this._plan();
    if (!plan || !this._showSolution()) return new Set<string>();
    return new Set(mazeRoute(plan, this.solution().choices) ?? []);
  });
  readonly routeNodes = computed(() => {
    const plan = this._plan();
    const nodes = new Set<string>();
    if (!plan?.start || this.routeEdges().size === 0) return nodes;
    let current = plan.start;
    nodes.add(current);
    for (const edgeId of mazeRoute(plan, this.solution().choices) ?? []) {
      const edge = plan.edges.find((e) => e.id === edgeId);
      const next = edge ? edgeTarget(plan, edge, current) : null;
      if (!next) break;
      nodes.add(next);
      current = next;
    }
    return nodes;
  });

  readonly feedbackState = this._feedback.asReadonly();
  /** Sapma düğümü (kontrol sonucu yanlış yön). */
  readonly deviatedAt = computed(() => {
    const fb = this._feedback();
    return fb?.status === 'deviated' ? fb.at : null;
  });
  /** Sapılan kenar: sapma satırı yolun son değerlendirilen adımıdır. */
  readonly wrongEdge = computed(() => {
    const fb = this._feedback();
    if (fb?.status !== 'deviated' || this._plan()?.variant === 'stones') return null;
    return this.path()[fb.rows.length - 1] ?? null;
  });
  readonly okNodes = computed(() => new Set((this._feedback()?.rows ?? []).filter((r) => r.correct).map((r) => r.id)));
  readonly stoneMarks = computed(() => new Map((this._feedback()?.rows ?? []).map((r) => [r.id, r.correct])));

  // ---- Ağaç ----

  readonly tree = computed(() => {
    const plan = this._plan();
    if (!plan || plan.variant !== 'tree') return null;
    const layout = treeLayout(plan);
    const pos = new Map(layout.nodes.map((n) => [n.id, n]));
    const width = layout.width;
    const nodes: PositionedNode[] = layout.nodes
      .map((n) => ({
        node: nodeOf(plan, n.id)!,
        left: (n.x / width) * 100,
        top: (n.y + 0.5) * MAZE_TREE_ROW_PX,
        width: (n.span / width) * 100,
      }))
      .filter((n) => !!n.node);
    const edges: PositionedEdge[] = plan.edges.flatMap((edge) => {
      const a = pos.get(edge.from);
      const b = pos.get(edge.to);
      if (!a || !b) return [];
      return [
        {
          edge,
          x1: a.x,
          y1: a.y + 0.5,
          x2: b.x,
          y2: b.y + 0.5,
          left: ((a.x + b.x) / 2 / width) * 100,
          top: (a.y + 1) * MAZE_TREE_ROW_PX,
        },
      ];
    });
    return { nodes, edges, width, height: (layout.depth + 1) * MAZE_TREE_ROW_PX, levels: layout.depth + 1 };
  });

  // ---- Odalar ----

  readonly rooms = computed(() => {
    const plan = this._plan();
    if (!plan || plan.variant !== 'rooms') return null;
    const grid = roomsGrid(plan);
    const center = (n: MazeNode) => ({ x: (n.col ?? 0) + 0.5, y: (n.row ?? 0) + 0.5 });
    const nodes: PositionedNode[] = plan.nodes.map((node) => ({
      node,
      left: (center(node).x / grid.cols) * 100,
      top: center(node).y * MAZE_ROOM_ROW_PX,
      width: 100 / grid.cols,
    }));
    const edges: PositionedEdge[] = plan.edges.flatMap((edge) => {
      const a = nodeOf(plan, edge.from);
      const b = nodeOf(plan, edge.to);
      if (!a || !b) return [];
      const ca = center(a);
      const cb = center(b);
      return [
        {
          edge,
          x1: ca.x,
          y1: ca.y,
          x2: cb.x,
          y2: cb.y,
          left: ((ca.x + cb.x) / 2 / grid.cols) * 100,
          top: ((ca.y + cb.y) / 2) * MAZE_ROOM_ROW_PX,
        },
      ];
    });
    return { nodes, edges, cols: grid.cols, rows: grid.rows, height: grid.rows * MAZE_ROOM_ROW_PX };
  });

  /** Yol işaretçisi: bulunulan kartın sol kenarında; yeni düğüme geçişte kayar (ağaç / odalar). */
  readonly token = computed(() => {
    const layout = this.tree() ?? this.rooms();
    const current = this.current();
    const n = layout?.nodes.find((p) => p.node.id === current);
    return n ? { left: n.left - n.width / 2, top: n.top } : null;
  });

  // ---- Pano ----

  /** Pano satırları üstten alta (bitiş en üstte); her satırda öğrencinin o satırdaki seçimi. */
  readonly board = computed(() => {
    const plan = this._plan();
    if (!plan || plan.variant !== 'board') return null;
    const { rows, finish } = boardRows(plan);
    const chosen = this.walk()?.edges ?? [];
    const view = rows.map((row, index) => ({
      ...row,
      index,
      chosen: chosen[index] ?? null,
      active: index === chosen.length && !this.readOnly(),
    }));
    return { rows: [...view].reverse(), finish };
  });

  // ---- Taşlar ----

  readonly stones = computed(() => {
    const plan = this._plan();
    return plan && plan.variant === 'stones' ? plan.nodes : [];
  });
  readonly markedSet = computed(() => new Set(this.marked()));
  readonly expectedMarked = computed(() => new Set(this._showSolution() ? this.solution().marked : []));

  readonly canUndo = computed(() => !this.readOnly() && this.path().length > 0);

  /** Kenar seç: bulunulan düğümden kullanılabiliyorsa yola eklenir. */
  choose(edgeId: string): void {
    if (!this.availableEdges().has(edgeId)) return;
    this.path.update((path) => [...path, edgeId]);
    this.emit();
  }

  undo(): void {
    if (!this.canUndo()) return;
    this.path.update((path) => path.slice(0, -1));
    this.emit();
  }

  reset(): void {
    if (!this.canUndo()) return;
    this.path.set([]);
    this.emit();
  }

  toggleStone(id: string): void {
    if (this.readOnly()) return;
    this.marked.update((marked) => (marked.includes(id) ? marked.filter((m) => m !== id) : [...marked, id]));
    this.emit();
  }

  nodeById(id: string): MazeNode | null {
    const plan = this._plan();
    return plan ? nodeOf(plan, id) : null;
  }

  nodeLabel(node: MazeNode | null | undefined): string {
    return node?.label ?? '';
  }

  private emit(): void {
    const plan = this._plan();
    if (!plan) return;
    const payload = plan.variant === 'stones' ? buildMazeMarkedPayload(this.marked()) : buildMazePathPayload(this.path());
    this.lastEmitted = payload;
    this.answerPayloadChange.emit(payload);
    this.answered.emit();
  }

  private restore(): void {
    const json = this._answerPayloadJson();
    if (this.lastEmitted !== null && json === this.lastEmitted) return;
    const answer = parseMazeAnswer(json, this._plan());
    this.path.set(answer.path);
    this.marked.set(answer.marked);
  }
}
