import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { QuestionRegion } from '../../../models/draws';
import {
  GRID_DRAW_LIMITS,
  GridDrawFeedback,
  GridDrawPlanV1,
  GridDrawShapeStatus,
  GridRect,
  buildGridDrawPayload,
  exampleLayout,
  sizeKey,
  sizeOf,
  gridDrawTargets,
  overlaps,
  parseGridDrawAnswer,
  parseGridDrawPlan,
  parseGridDrawSolution,
} from '../../../models/grid-draw';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';

interface Cell {
  row: number;
  col: number;
}

interface DrawnShape extends GridRect {
  state: GridDrawShapeStatus | null;
}

/**
 * Issue #588 (dilim c): kareli / noktalı zeminde çizim oynatıcısı (`gridDraw`). Öğrenci dikdörtgeni köşeden köşeye sürükleyerek ya
 * da iki dokunuşla (ilk köşe → karşı köşe) çizer; çizili bir dikdörtgene dokunmak onu siler. Çizimler üst üste binemez. Klavyede ok
 * tuşları imleci taşır, Enter / Boşluk köşe koyar, Delete imlecin altındaki çizimi siler, Escape yarım çizimi bırakır. Kontrol sonrası
 * çizimler yeşil (kurala uyuyor) / turuncu (aynı boyut zaten var) / kırmızı (uymuyor) + "Bulunan · Kalan · Fazla". Çözüm açıkken kurala
 * uyan boyutlar örnek yerleşimle çizilir. Her değişiklikte bir kez emit edilir. OnPush: tüm durum signal.
 */
@Component({
  selector: 'app-question-canvas-grid-draw',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective, StorageImageRetryDirective],
  templateUrl: './question-canvas-grid-draw.component.html',
  styleUrl: './question-canvas-grid-draw.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionCanvasGridDrawComponent {
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    const changed = value !== this._region();
    this._region.set(value);
    if (!changed) return;
    this.lastEmitted = null;
    this.anchor.set(null);
    this.restore();
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this._plan.set(parseGridDrawPlan(value));
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

  @Input() set feedback(value: GridDrawFeedback | null | undefined) {
    this._feedback.set(value ?? null);
  }

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

  private lastEmitted: string | null = null;
  /** Sürükleme: başladığı hücre ve dokunuşun bir çizimin üstünde başlayıp başlamadığı. */
  private drag: { start: Cell; onShape: number; moved: boolean; fresh: boolean } | null = null;

  private readonly _region = signal<QuestionRegion | null>(null);
  private readonly _plan = signal<GridDrawPlanV1 | null>(null);
  private readonly _solutionJson = signal<string | null>(null);
  private readonly _answerPayloadJson = signal<string | null>(null);
  private readonly _feedback = signal<GridDrawFeedback | null>(null);
  private readonly _showSolution = signal(false);
  private readonly _activityMode = signal(false);
  private readonly _disabled = signal(false);

  readonly shapes = signal<GridRect[]>([]);
  /** İki dokunuşla çizimde ilk köşe; sürüklemede başlangıç. */
  readonly anchor = signal<Cell | null>(null);
  /** Sürüklenen / imlecin gösterdiği karşı köşe. */
  readonly hover = signal<Cell | null>(null);
  readonly cursor = signal<Cell>({ row: 0, col: 0 });
  readonly focused = signal(false);
  /** Son reddedilen çizimin nedeni (üst üste / çok çizim); bir sonraki değişiklikte silinir. */
  readonly notice = signal<'overlap' | 'tooMany' | null>(null);

  readonly plan = this._plan.asReadonly();
  readonly solutionShown = this._showSolution.asReadonly();
  readonly activity = this._activityMode.asReadonly();
  readonly imageUrl = computed(() => this._region()?.imageUrl || null);
  readonly passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);
  readonly passageTitle = computed(() => this._region()?.passage?.title ?? null);
  readonly readOnly = computed(() => this._showSolution() || this._disabled());
  readonly feedbackState = this._feedback.asReadonly();

  readonly rule = computed(() => parseGridDrawSolution(this._solutionJson()));

  /** Kurala uyan boyutlar (çözüm açıkken listelenir). */
  readonly targets = computed(() => {
    const plan = this._plan();
    const rule = this.rule();
    return plan && rule ? gridDrawTargets(plan, rule) : [];
  });

  /** #679: çözüm açık ve öğrenci çizmiş — öğrencinin dikdörtgenleri yeşil / kırmızı / turuncu (tekrar), eksik boyutlar altta. */
  readonly compare = computed(() => this._showSolution() && !!this.rule() && this.shapes().length > 0);

  /** Karşılaştırma: kontrolcü gibi — geçerli boyutun ilk çizimi doğru, tekrarı `duplicate`, kurala uymayan yanlış. */
  private readonly compareStates = computed(() => {
    const valid = new Set(this.targets().map(sizeKey));
    const seen = new Set<string>();
    return this.shapes().map((shape) => {
      const key = sizeKey(sizeOf(shape));
      if (!valid.has(key)) return 'wrong' as const;
      if (seen.has(key)) return 'duplicate' as const;
      seen.add(key);
      return 'ok' as const;
    });
  });

  /** Karşılaştırmada eksik kalan boyutlar ("3 × 4"); "biri yeterdi"de geçerli çizim varsa boş. */
  readonly missingSizes = computed(() => {
    if (!this.compare()) return '';
    const found = new Set(this.shapes().map((shape) => sizeKey(sizeOf(shape))));
    const targets = this.targets();
    const missing = this.rule()?.all ? targets.filter((t) => !found.has(sizeKey(t))) : targets.some((t) => found.has(sizeKey(t))) ? [] : targets;
    return missing.map((t) => `${t.short} × ${t.long}`).join(', ');
  });

  /** Gösterilen çizimler: öğrencinin (karşılaştırmada da; kontrol sonucu sayısı tutuyorsa işaretli); çözüm açık ama çizim yoksa örnek yerleşim. */
  readonly drawn = computed<DrawnShape[]>(() => {
    const plan = this._plan();
    if (!plan) return [];
    if (this.compare()) return this.shapes().map((s, i) => ({ ...s, state: this.compareStates()[i] }));
    if (this._showSolution()) return exampleLayout(plan, this.targets()).map((r) => ({ ...r, state: 'ok' as const }));
    const marks = this._feedback()?.shapes;
    const shapes = this.shapes();
    return shapes.map((s, i) => ({ ...s, state: marks && marks.length === shapes.length ? marks[i] : null }));
  });

  /** Çizilmekte olan dikdörtgen (köşe + karşı köşe). */
  readonly preview = computed<GridRect | null>(() => {
    const a = this.anchor();
    const b = this.hover();
    return a && b && !this.readOnly() ? rectBetween(a, b) : null;
  });

  readonly previewBlocked = computed(() => {
    const p = this.preview();
    return !!p && this.shapes().some((s) => overlaps(s, p));
  });

  /** Arka plan: kareli çizgiler ya da kesişim noktaları (viewBox birimi = kare). */
  readonly gridLines = computed(() => {
    const plan = this._plan();
    if (!plan) return { rows: [] as number[], cols: [] as number[] };
    return {
      rows: Array.from({ length: plan.rows + 1 }, (_, i) => i),
      cols: Array.from({ length: plan.cols + 1 }, (_, i) => i),
    };
  });

  readonly sizeList = computed(() => this.targets().map((t) => `${t.short} × ${t.long}`).join(', '));

  readonly canReset = computed(() => !this.readOnly() && this.shapes().length > 0);

  readonly cursorLabel = computed(() => {
    const c = this.cursor();
    return { row: c.row + 1, col: c.col + 1 };
  });

  // ---- İşaretçi ----

  onPointerDown(event: PointerEvent): void {
    if (this.readOnly() || event.button > 0) return;
    const cell = this.cellAt(event);
    if (!cell) return;
    event.preventDefault();
    try {
      (event.currentTarget as Element | null)?.setPointerCapture?.(event.pointerId);
    } catch {
      // Sentetik / bitmiş işaretçi: yakalama olmadan da çalışır.
    }
    this.notice.set(null);
    this.cursor.set(cell);
    const onShape = this.anchor() ? -1 : this.shapeIndexAt(cell);
    const fresh = onShape < 0 && !this.anchor();
    this.drag = { start: cell, onShape, moved: false, fresh };
    if (fresh) {
      this.anchor.set(cell);
      this.hover.set(cell);
    }
  }

  onPointerMove(event: PointerEvent): void {
    if (!this.drag) return;
    const cell = this.cellAt(event);
    if (!cell) return;
    if (cell.row !== this.drag.start.row || cell.col !== this.drag.start.col) this.drag.moved = true;
    if (this.drag.onShape < 0) this.hover.set(cell);
  }

  onPointerUp(event: PointerEvent): void {
    const drag = this.drag;
    this.drag = null;
    if (!drag || this.readOnly()) return;
    const cell = this.cellAt(event) ?? this.hover() ?? drag.start;
    if (drag.onShape >= 0) {
      // Çizimin üstüne dokunuş siler (sürüklemeyle silinmez).
      if (!drag.moved) this.removeAt(drag.onShape);
      return;
    }
    const anchor = this.anchor();
    if (!anchor) return;
    if (drag.moved || !drag.fresh) {
      // Sürükleme ya da ikinci dokunuş (aynı kareye de: 1 × 1): dikdörtgeni bitir.
      this.commit(rectBetween(anchor, cell));
      return;
    }
    // İlk dokunuş: köşe kaldı, ikinci dokunuşu bekle.
    this.hover.set(cell);
  }

  onPointerCancel(): void {
    this.drag = null;
  }

  // ---- Klavye ----

  onKeydown(event: KeyboardEvent): void {
    const plan = this._plan();
    if (!plan || this.readOnly()) return;
    const c = this.cursor();
    let next: Cell | null = null;
    switch (event.key) {
      case 'ArrowUp':
        next = { row: Math.max(0, c.row - 1), col: c.col };
        break;
      case 'ArrowDown':
        next = { row: Math.min(plan.rows - 1, c.row + 1), col: c.col };
        break;
      case 'ArrowLeft':
        next = { row: c.row, col: Math.max(0, c.col - 1) };
        break;
      case 'ArrowRight':
        next = { row: c.row, col: Math.min(plan.cols - 1, c.col + 1) };
        break;
      case 'Enter':
      case ' ': {
        event.preventDefault();
        this.notice.set(null);
        const anchor = this.anchor();
        if (anchor) this.commit(rectBetween(anchor, c));
        else {
          this.anchor.set(c);
          this.hover.set(c);
        }
        return;
      }
      case 'Delete':
      case 'Backspace': {
        event.preventDefault();
        const index = this.shapeIndexAt(c);
        if (index >= 0) this.removeAt(index);
        return;
      }
      case 'Escape':
        if (this.anchor()) {
          event.preventDefault();
          this.cancel();
        }
        return;
      default:
        return;
    }
    event.preventDefault();
    this.cursor.set(next);
    if (this.anchor()) this.hover.set(next);
  }

  cancel(): void {
    this.anchor.set(null);
    this.hover.set(null);
  }

  reset(): void {
    if (!this.canReset()) return;
    this.cancel();
    this.notice.set(null);
    this.shapes.set([]);
    this.emit();
  }

  isCursor(row: number, col: number): boolean {
    const c = this.cursor();
    return this.focused() && c.row === row && c.col === col;
  }

  // ---- İç ----

  private commit(rect: GridRect): void {
    this.cancel();
    const shapes = this.shapes();
    if (shapes.some((s) => overlaps(s, rect))) {
      this.notice.set('overlap');
      return;
    }
    if (shapes.length >= GRID_DRAW_LIMITS.maxShapes) {
      this.notice.set('tooMany');
      return;
    }
    this.shapes.set([...shapes, rect]);
    this.emit();
  }

  private removeAt(index: number): void {
    this.notice.set(null);
    this.shapes.set(this.shapes().filter((_, i) => i !== index));
    this.emit();
  }

  private shapeIndexAt(cell: Cell): number {
    return this.shapes().findIndex((s) => overlaps(s, { row: cell.row, col: cell.col, width: 1, height: 1 }));
  }

  /** İşaretçinin altındaki hücre (SVG'nin kutusuna göre); zemin dışında kenara kırpılır. */
  private cellAt(event: PointerEvent): Cell | null {
    const plan = this._plan();
    const el = event.currentTarget as Element | null;
    if (!plan || !el) return null;
    const rect = el.getBoundingClientRect();
    if (rect.width <= 0 || rect.height <= 0) return null;
    const clamp = (v: number, n: number) => Math.min(n - 1, Math.max(0, Math.floor(v)));
    return {
      row: clamp(((event.clientY - rect.top) / rect.height) * plan.rows, plan.rows),
      col: clamp(((event.clientX - rect.left) / rect.width) * plan.cols, plan.cols),
    };
  }

  private emit(): void {
    const payload = buildGridDrawPayload(this.shapes());
    this.lastEmitted = payload;
    this.answerPayloadChange.emit(payload);
    this.answered.emit();
  }

  private restore(): void {
    const json = this._answerPayloadJson();
    if (this.lastEmitted !== null && json === this.lastEmitted) return;
    this.shapes.set(parseGridDrawAnswer(json, this._plan()));
  }
}

function rectBetween(a: Cell, b: Cell): GridRect {
  return {
    row: Math.min(a.row, b.row),
    col: Math.min(a.col, b.col),
    width: Math.abs(a.col - b.col) + 1,
    height: Math.abs(a.row - b.row) + 1,
  };
}
