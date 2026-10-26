import { CommonModule } from '@angular/common';
import {
  Component,
  DestroyRef,
  EventEmitter,
  Input,
  Output,
  computed,
  inject,
  signal,
  ChangeDetectionStrategy,
} from '@angular/core';
import { DragDropModule, CdkDragDrop, transferArrayItem } from '@angular/cdk/drag-drop';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { TranslocoDirective, TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { QuestionRegion } from '../../../models/draws';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { ActivityFeedbackRow } from '../../../models/activity';

/** Issue #573: dokunmatik / dar ekranda sürükleme kapalı, yalnız dokun-dokun yerleştirme. */
const TAP_ONLY_QUERY = '(pointer: coarse), (max-width: 640px)';

/** Dokun-dokun / klavye seçimi: seçilen etiket ve nereden alındığı (`pool` ya da hedef id'si). */
interface ChipSelection {
  id: string;
  from: string;
}

export interface DragDropLabelingPlanV1 {
  version: 1;
  type: 'dragDropLabeling';
  dropZones: Array<{ id: string; x: number; y: number; width: number; height: number }>;
  draggables: Array<{ id: string; label: string }>;
}

/** issue #570: doğru yerleşimler plandan ayrı gelir (`QuestionDto.interactionSolution`). */
export interface DragDropLabelingSolution {
  placements: DragDropLabelingPlacement[];
}

export interface DragDropLabelingPlacement {
  draggableId: string;
  dropZoneId: string;
}

@Component({
  selector: 'app-question-canvas-dragdrop-labeling',
  standalone: true,
  imports: [
    CommonModule,
    DragDropModule,
    MatIconModule,
    MatButtonModule,
    TranslocoDirective,
    TranslocoPipe,
    StorageImageRetryDirective,
  ],
  templateUrl: './question-canvas-dragdrop-labeling.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./question-canvas-dragdrop-labeling.component.scss'],
})
export class QuestionCanvasDragDropLabelingComponent {
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    this._region.set(value);
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this._planJson.set(value ?? null);
    this._plan.set(this.parsePlan(value));
    this.resetStateFromPlan();
  }

  /**
   * issue #570: doğru yerleşimler (JSON, `{"placements":[...]}`). Backend yalnız örnek soruda, biten test sonucunda
   * ya da sahip/admin için doldurur; plan artık çözüm taşımaz.
   */
  @Input() set interactionSolutionJson(value: string | null | undefined) {
    this._solution.set(this.parseSolution(value));
  }

  /** issue #365 (S3): görsel yüklenemezse taze imzalı URL'yi bulan fonksiyon (sahip sayfa verir). */
  @Input() imageRefresh: StorageImageResolver | null = null;

  // If true, show immediate feedback (IsExample)
  @Input() practiceFeedback = false;

  // Used by solve 'passage first' mode: when true, passage should not be rendered in the question view.
  @Input() hidePassage: boolean = false;

  // Current answer payload (persisted)
  @Input() set answerPayloadJson(value: string | null | undefined) {
    this._answerPayloadJson.set(value ?? null);
    this.restoreFromAnswerPayload();
  }

  /**
   * Issue #573: sunucu kontrolünün satır bazlı sonucu (`feedback.rows`, satır id = hedef id). Verilirse hedefler ✓/✗ + metinle
   * işaretlenir (renk tek sinyal değil); `practiceFeedback` işaretlerinin önüne geçer. null = işaret yok.
   */
  @Input() set feedbackRows(value: ActivityFeedbackRow[] | null | undefined) {
    if (!value) {
      this._feedback.set(null);
      return;
    }
    const map = new Map<string, boolean>();
    value.forEach((row) => map.set(row.id, row.correct));
    this._feedback.set(map);
  }

  /**
   * Issue #573: true iken öğrencinin yerleşimi yerine `interactionSolutionJson`'daki doğru yerleşim gösterilir (salt okunur;
   * sürükleme ve dokun-dokun kapalı). Öğrencinin cevabı korunur, false olunca geri gelir.
   */
  @Input() set showSolution(value: boolean) {
    this._showSolution.set(!!value);
    if (value) {
      this.selected.set(null);
    }
  }

  /**
   * Issue #573 (S3): etkinlik oynatıcısı modu — dokun-dokun / klavye yerleştirme, `aria-live` ipucu ve dokunmatikte
   * sürüklemesiz kullanım yalnız burada açılır. Varsayılan false: test ekranı eski davranışıyla (yalnız sürükle-bırak) kalır.
   */
  @Input() set activityMode(value: boolean) {
    this._activityMode.set(!!value);
    if (!value) this.selected.set(null);
  }

  @Output() answerPayloadChange = new EventEmitter<string>();
  @Output() answered = new EventEmitter<void>();

  private readonly transloco = inject(TranslocoService);
  private readonly _activityMode = signal(false);
  public readonly activity = this._activityMode.asReadonly();
  /** Cihaz dokunmatik ya da ekran dar (yalnız etkinlik modunda sürüklemeyi kapatır). */
  private readonly coarsePointer = signal(false);
  /** S2: dil değişince / sözlük yüklenince ipucu ve etiketler yeniden hesaplansın. */
  private readonly i18nTick = toSignal(
    this.transloco.events$.pipe(
      filter((e) => e.type === 'langChanged' || e.type === 'translationLoadSuccess'),
      map(() => Date.now())
    ),
    { initialValue: 0 }
  );
  private readonly _feedback = signal<Map<string, boolean> | null>(null);
  private readonly _showSolution = signal(false);

  /** Dokun-dokun / klavye ile seçilmiş etiket. */
  public readonly selected = signal<ChipSelection | null>(null);
  /** Etkinlik modunda dokunmatik ya da dar ekran: sürükleme kapalı (yalnız dokun-dokun). Klavye her yerde açık. */
  public readonly tapOnly = computed(() => this._activityMode() && this.coarsePointer());
  public readonly solutionShown = this._showSolution.asReadonly();
  /** Sürükleme bırakıldıktan hemen sonra gelen `click` seçimi tetiklemesin. */
  private suppressNextClick = false;

  constructor() {
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      const query = window.matchMedia(TAP_ONLY_QUERY);
      this.coarsePointer.set(query.matches);
      const listener = (event: MediaQueryListEvent) => this.coarsePointer.set(event.matches);
      query.addEventListener('change', listener);
      inject(DestroyRef).onDestroy(() => query.removeEventListener('change', listener));
    }
  }

  private _region = signal<QuestionRegion | null>(null);
  private _planJson = signal<string | null>(null);
  private _plan = signal<DragDropLabelingPlanV1 | null>(null);
  private _answerPayloadJson = signal<string | null>(null);
  private _solution = signal<DragDropLabelingPlacement[]>([]);

  // pool holds draggable ids not placed
  public pool = signal<string[]>([]);

  // per dropZone list holds 0..1 draggable id
  public zoneState = signal<Record<string, string[]>>({});

  public plan = computed(() => this._plan());

  public imageUrl = computed(() => this._region()?.imageUrl ?? null);

  public passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);

  public passageTitle = computed(() => this._region()?.passage?.title ?? null);

  public zones = computed(() => this._plan()?.dropZones ?? []);

  // Angular templates don't support inline arrow functions like `.map(z => ...)`.
  // Precompute connected drop-list ids here.
  public zoneDropListIds = computed(() => this.zones().map((z) => `zone-${z.id}`));

  public allDropListIds = computed(() => ['pool', ...this.zoneDropListIds()]);

  public draggableLabel = computed(() => {
    const plan = this._plan();
    const map = new Map<string, string>();
    plan?.draggables?.forEach((d) => map.set(d.id, d.label));
    return (id: string) => map.get(id) ?? id;
  });

  public zoneStyle = computed(() => {
    const region = this._region();
    const plan = this._plan();
    if (!region || !plan) return () => ({ display: 'none' });

    // Coordinates in plan are in original question image pixels (same space as region.width/height).
    // We map them to overlay container which scales with the rendered image.
    return (z: DragDropLabelingPlanV1['dropZones'][number]) => {
      return {
        left: `${(z.x / region.width) * 100}%`,
        top: `${(z.y / region.height) * 100}%`,
        width: `${(z.width / region.width) * 100}%`,
        height: `${(z.height / region.height) * 100}%`,
      } as const;
    };
  });

  /** Ekranda gösterilen hedef içerikleri: çözüm açıkken doğru yerleşim, değilse öğrencinin yerleşimi. */
  public displayZones = computed<Record<string, string[]>>(() => {
    if (!this._showSolution()) return this.zoneState();
    const zones: Record<string, string[]> = {};
    this.zones().forEach((z) => (zones[z.id] = []));
    this._solution().forEach((p) => {
      if (zones[p.dropZoneId]) zones[p.dropZoneId] = [p.draggableId];
    });
    return zones;
  });

  /** Ekranda gösterilen havuz: çözüm açıkken yerleşmeyen etiketler, değilse öğrencinin havuzu. */
  public displayPool = computed<string[]>(() => {
    if (!this._showSolution()) return this.pool();
    const used = new Set(this._solution().map((p) => p.draggableId));
    return (this._plan()?.draggables ?? []).map((d) => d.id).filter((id) => !used.has(id));
  });

  /** Sürükleme kapalı mı: dokunmatik/dar ekran ya da çözüm gösteriliyor. */
  public dragDisabled = computed(() => this.tapOnly() || this._showSolution());

  /** Hedefin geri bildirim işareti: `correct` / `wrong` / null (işaret yok). */
  public zoneMark = computed(() => {
    const feedback = this._feedback();
    return (zoneId: string): 'correct' | 'wrong' | null => {
      if (this._showSolution() || !feedback || !feedback.has(zoneId)) return null;
      return feedback.get(zoneId) ? 'correct' : 'wrong';
    };
  });

  /** `aria-live` ipucu: seçim yokken nasıl yerleştirileceği, seçimdeyken seçilen etiket. */
  public hint = computed(() => {
    this.i18nTick();
    const selection = this.selected();
    if (this._showSolution()) return this.t('solutionShownHint');
    if (!selection) return this.t(this.tapOnly() ? 'tapHintIdle' : 'keyboardHint');
    const label = this.draggableLabel()(selection.id);
    return this.t(selection.from === 'pool' ? 'tapHintSelected' : 'tapHintSelectedPlaced', { label });
  });

  public zoneClass = computed(() => {
    const feedback = this._feedback();
    return (zoneId: string) => {
      if (this._showSolution()) return 'is-solution';
      // Issue #573: sunucu kontrolü (etkinlik) varsa yalnız ondan.
      if (feedback) {
        if (!feedback.has(zoneId)) return '';
        return feedback.get(zoneId) ? 'is-correct' : 'is-incorrect';
      }
      // İstemci tarafı kontrol yalnız örnek sorularda (practiceFeedback); çözüm girdisi yoksa işaret yok.
      if (!this.practiceFeedback) return '';
      const solution = this._solution();
      if (solution.length === 0) return '';
      const current = this.buildPlacements();
      const expected = solution.find((p) => p.dropZoneId === zoneId)?.draggableId ?? null;
      const actual = current.find((p) => p.dropZoneId === zoneId)?.draggableId ?? null;
      if (!expected || !actual) return '';
      return expected === actual ? 'is-correct' : 'is-incorrect';
    };
  });

  /** Hedefin ekran okuyucu etiketi: sıra no + içerik (+ kontrol sonucu). */
  zoneAria(zoneId: string, index: number): string {
    const item = this.displayZones()[zoneId]?.[0];
    const content = item ? this.draggableLabel()(item) : this.t('zoneEmpty');
    const mark = this.zoneMark()(zoneId);
    const base = this.t('zoneAria', { n: index + 1, content });
    return mark ? `${base}, ${this.t(mark === 'correct' ? 'markCorrect' : 'markWrong')}` : base;
  }

  isSelected(id: string): boolean {
    return this.selected()?.id === id;
  }

  onDragStarted(): void {
    this.suppressNextClick = true;
    this.selected.set(null);
  }

  /** Bırakmadan sonra tarayıcı `click` üretmezse bayrak sonraki gerçek dokunuşu yutmasın. */
  onDragEnded(): void {
    setTimeout(() => (this.suppressNextClick = false));
  }

  /** Etikete dokunma / Enter: seç, aynı etikete tekrar dokununca iptal; hedefteki etikete başka seçimle dokununca o hedefe yerleştir. */
  onChipActivate(id: string, from: string, event: Event): void {
    if (!this._activityMode()) return;
    event.stopPropagation();
    if (this.suppressNextClick) {
      this.suppressNextClick = false;
      return;
    }
    if (this._showSolution()) return;
    const selection = this.selected();
    if (selection?.id === id) {
      this.selected.set(null);
      return;
    }
    if (selection && from !== 'pool') {
      this.placeInZone(selection.id, from);
      return;
    }
    this.selected.set({ id, from });
  }

  /** Hedefe dokunma / Enter: seçili etiketi buraya bırakır (dolu hedefteki etiket havuza döner). */
  onZoneActivate(zoneId: string, event?: Event): void {
    event?.preventDefault();
    if (!this._activityMode()) return;
    if (this.suppressNextClick) {
      this.suppressNextClick = false;
      return;
    }
    const selection = this.selected();
    if (!selection || this._showSolution()) return;
    this.placeInZone(selection.id, zoneId);
  }

  /** Havuza dokunma / Enter: hedeften seçilmiş etiketi havuza geri koyar. */
  onPoolActivate(event?: Event): void {
    event?.preventDefault();
    const selection = this.selected();
    if (!this._activityMode() || !selection || selection.from === 'pool' || this._showSolution()) return;
    this.returnToPool(selection.id);
  }

  cancelSelection(): void {
    this.selected.set(null);
  }

  private placeInZone(draggableId: string, zoneId: string): void {
    const current = this.zoneState();
    if (!current[zoneId]) return;
    const zones: Record<string, string[]> = {};
    Object.entries(current).forEach(([id, list]) => (zones[id] = (list ?? []).filter((x) => x !== draggableId)));
    const pool = this.pool().filter((x) => x !== draggableId);
    const occupant = zones[zoneId][0];
    if (occupant) pool.push(occupant);
    zones[zoneId] = [draggableId];
    this.zoneState.set(zones);
    this.pool.set(pool);
    this.selected.set(null);
    this.emitAnswerPayload();
    this.answered.emit();
  }

  private returnToPool(draggableId: string): void {
    const zones: Record<string, string[]> = {};
    Object.entries(this.zoneState()).forEach(([id, list]) => (zones[id] = (list ?? []).filter((x) => x !== draggableId)));
    this.zoneState.set(zones);
    this.pool.set([...this.pool().filter((x) => x !== draggableId), draggableId]);
    this.selected.set(null);
    this.emitAnswerPayload();
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`shared.questionCanvas.${key}`, params) ?? '';
  }

  dropToPool(event: CdkDragDrop<string[]>) {
    this.selected.set(null);
    if (event.previousContainer === event.container) return;
    transferArrayItem(event.previousContainer.data, event.container.data, event.previousIndex, event.currentIndex);
    this.emitAnswerPayload();
  }

  dropToZone(zoneId: string, event: CdkDragDrop<string[]>) {
    this.selected.set(null);
    const zones = this.zoneState();
    const zoneList = zones[zoneId] ?? [];

    // Enforce single item per zone; if occupied, swap back to pool.
    if (zoneList.length >= 1 && event.previousContainer !== event.container) {
      const removed = zoneList.splice(0, 1);
      this.pool.set([...this.pool(), ...removed]);
    }

    if (event.previousContainer === event.container) return;
    transferArrayItem(event.previousContainer.data, zoneList, event.previousIndex, 0);

    this.zoneState.set({ ...zones, [zoneId]: zoneList });
    this.emitAnswerPayload();
    this.answered.emit();
  }

  clearAll() {
    this.selected.set(null);
    // Girdi olarak gelen eski cevap yeniden uygulanmaz; "Temizle" tüm yerleştirmeleri havuza döndürür.
    this.resetStateFromPlan({ restoreAnswer: false });
    this.emitAnswerPayload();
  }

  private parsePlan(json: string | null | undefined): DragDropLabelingPlanV1 | null {
    if (!json) return null;
    try {
      const parsed = JSON.parse(json) as Partial<DragDropLabelingPlanV1>;
      if (parsed?.type !== 'dragDropLabeling') return null;
      if (!Array.isArray(parsed.dropZones) || !Array.isArray(parsed.draggables)) return null;
      return {
        version: 1,
        type: 'dragDropLabeling',
        dropZones: parsed.dropZones as DragDropLabelingPlanV1['dropZones'],
        draggables: parsed.draggables as DragDropLabelingPlanV1['draggables'],
      };
    } catch {
      return null;
    }
  }

  private parseSolution(json: string | null | undefined): DragDropLabelingPlacement[] {
    if (!json) return [];
    try {
      const parsed = JSON.parse(json) as Partial<DragDropLabelingSolution> | null;
      if (!parsed || !Array.isArray(parsed.placements)) return [];
      return parsed.placements.filter(
        (p): p is DragDropLabelingPlacement =>
          !!p && typeof p.draggableId === 'string' && typeof p.dropZoneId === 'string',
      );
    } catch {
      return [];
    }
  }

  private resetStateFromPlan({ restoreAnswer = true }: { restoreAnswer?: boolean } = {}) {
    const plan = this._plan();
    if (!plan) {
      this.pool.set([]);
      this.zoneState.set({});
      return;
    }

    this.pool.set(plan.draggables.map((d) => d.id));
    const zones: Record<string, string[]> = {};
    plan.dropZones.forEach((z) => (zones[z.id] = []));
    this.zoneState.set(zones);

    // If an answer payload is already provided, restore after initializing.
    if (restoreAnswer) {
      this.restoreFromAnswerPayload();
    }
  }

  private restoreFromAnswerPayload() {
    const payload = this._answerPayloadJson();
    const plan = this._plan();
    if (!payload || !plan) return;

    try {
      const parsed = JSON.parse(payload) as { type?: string; placements?: DragDropLabelingPlacement[] };
      if (parsed?.type !== 'dragDropLabeling' || !Array.isArray(parsed.placements)) return;

      const zones: Record<string, string[]> = {};
      plan.dropZones.forEach((z) => (zones[z.id] = []));

      const used = new Set<string>();
      parsed.placements.forEach((p) => {
        if (!p?.dropZoneId || !p?.draggableId) return;
        if (!zones[p.dropZoneId]) return;
        if (used.has(p.draggableId)) return;
        zones[p.dropZoneId] = [p.draggableId];
        used.add(p.draggableId);
      });

      const remaining = plan.draggables.map((d) => d.id).filter((id) => !used.has(id));
      this.zoneState.set(zones);
      this.pool.set(remaining);
    } catch {
      // ignore
    }
  }

  private buildPlacements(): DragDropLabelingPlacement[] {
    const zones = this.zoneState();
    return Object.entries(zones)
      .map(([dropZoneId, list]) => {
        const draggableId = list?.[0];
        return draggableId ? { dropZoneId, draggableId } : null;
      })
      .filter((p): p is DragDropLabelingPlacement => !!p);
  }

  private emitAnswerPayload() {
    const payload = {
      version: 1,
      type: 'dragDropLabeling' as const,
      placements: this.buildPlacements(),
    };
    this.answerPayloadChange.emit(JSON.stringify(payload));
  }
}
