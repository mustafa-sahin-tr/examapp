import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { QuestionRegion } from '../../../models/draws';
import {
  PaintCells,
  PaintColor,
  RegionPaintFeedback,
  RegionPaintPlanV1,
  buildRegionPaintPayload,
  countPainted,
  emptyCells,
  isEmptyPaint,
  paintCss,
  parseRegionPaintAnswer,
  parseRegionPaintPlan,
  parseRegionPaintSolution,
  solutionCells,
} from '../../../models/region-paint';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';

/**
 * Issue #587: boyama oynatıcısı (bileşen E, `regionPaint`), dilim a: ızgara modu. Paletten renk seçilir, hücreye dokunmak / üzerinde
 * sürüklemek boyar; seçili renkle boyalı hücreden başlayan sürükleme siler (silgi de var). Renk başına boyanan hücre sayısı canlı
 * gösterilir (öğrenci oranı kendisi hesaplar; hedef sayı gösterilmez). Kontrol sonrası palet satırları yeşil / kırmızı; çözüm açıkken
 * hedef sayılar satır öncelikli örnek dolgu ile çizilir. Sürükleme bitince bir kez emit edilir. OnPush: tüm durum signal.
 */
@Component({
  selector: 'app-question-canvas-region-paint',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective, StorageImageRetryDirective],
  templateUrl: './question-canvas-region-paint.component.html',
  styleUrl: './question-canvas-region-paint.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionCanvasRegionPaintComponent {
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    const changed = value !== this._region();
    this._region.set(value);
    if (!changed) return;
    this.lastEmitted = null;
    this.restore();
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this._plan.set(parseRegionPaintPlan(value));
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

  @Input() set feedback(value: RegionPaintFeedback | null | undefined) {
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
  /** Süren sürüklemenin yazdığı değer (palet dizini ya da silme için null); sürükleme yoksa undefined. */
  private stroke: number | null | undefined = undefined;
  private strokeChanged = false;

  private readonly _region = signal<QuestionRegion | null>(null);
  private readonly _plan = signal<RegionPaintPlanV1 | null>(null);
  private readonly _solutionJson = signal<string | null>(null);
  private readonly _answerPayloadJson = signal<string | null>(null);
  private readonly _feedback = signal<RegionPaintFeedback | null>(null);
  private readonly _showSolution = signal(false);
  private readonly _activityMode = signal(false);
  private readonly _disabled = signal(false);

  readonly cells = signal<PaintCells>([]);
  /** Seçili palet dizini; null = silgi. */
  readonly selected = signal<number | null>(0);

  readonly plan = this._plan.asReadonly();
  readonly solutionShown = this._showSolution.asReadonly();
  readonly activity = this._activityMode.asReadonly();
  readonly imageUrl = computed(() => this._region()?.imageUrl || null);
  readonly passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);
  readonly passageTitle = computed(() => this._region()?.passage?.title ?? null);
  readonly readOnly = computed(() => this._showSolution() || this._disabled());
  readonly feedbackState = this._feedback.asReadonly();

  /** Çizilen ızgara: çözüm açıkken örnek dolgu, değilse öğrencinin boyası. */
  readonly shown = computed<PaintCells>(() => {
    const plan = this._plan();
    if (!plan) return [];
    return this._showSolution() ? solutionCells(plan, parseRegionPaintSolution(this._solutionJson(), plan)) : this.cells();
  });
  readonly counts = computed(() => countPainted(this.shown(), this._plan()?.palette.length ?? 0));
  readonly total = computed(() => {
    const plan = this._plan();
    return plan ? plan.rows * plan.cols : 0;
  });
  readonly rowMarks = computed(() => new Map((this._feedback()?.rows ?? []).map((r) => [r.id, r.correct])));
  readonly correctCount = computed(() => [...this.rowMarks().values()].filter(Boolean).length);
  readonly canReset = computed(() => !this.readOnly() && !isEmptyPaint(this.cells()));

  /**
   * Kare boyu: dar ekranda ızgara sayfaya sığsın (sürükleme `touch-action: none` olduğundan ızgara üzerinde kaydırılamaz); 14–32 px.
   */
  cellSize(cols: number): string {
    return `clamp(14px, calc((100vw - 64px) / ${cols}), 32px)`;
  }

  css(color: PaintColor): string {
    return paintCss(color);
  }

  cellColor(value: number | null): string | null {
    const plan = this._plan();
    return value === null || !plan || !plan.palette[value] ? null : paintCss(plan.palette[value].color);
  }

  entryState(id: string): 'ok' | 'bad' | null {
    const mark = this.rowMarks().get(id);
    return mark === undefined ? null : mark ? 'ok' : 'bad';
  }

  select(index: number | null): void {
    if (this.readOnly()) return;
    this.selected.set(index);
  }

  /** Sürüklemeyi başlatır: seçili renkle boyalı hücrede silme, değilse seçili renk (silgide hep silme). */
  startStroke(event: PointerEvent, r: number, c: number): void {
    if (this.readOnly() || !this._plan()) return;
    event.preventDefault();
    const selected = this.selected();
    const current = this.cells()[r]?.[c] ?? null;
    this.stroke = selected === null || current === selected ? null : selected;
    this.strokeChanged = false;
    this.apply(r, c);
  }

  /** Sürükleme sırasında parmağın / farenin altındaki hücreyi boyar (pointer yakalaması olmadan `elementFromPoint`). */
  moveStroke(event: PointerEvent): void {
    if (this.stroke === undefined) return;
    const target = document.elementFromPoint(event.clientX, event.clientY)?.closest<HTMLElement>('[data-cell]');
    if (!target) return;
    const r = Number(target.dataset['r']);
    const c = Number(target.dataset['c']);
    if (Number.isInteger(r) && Number.isInteger(c)) this.apply(r, c);
  }

  endStroke(): void {
    if (this.stroke === undefined) return;
    this.stroke = undefined;
    if (this.strokeChanged) this.emit();
    this.strokeChanged = false;
  }

  /** Klavye: Enter / Space hücreyi seçili renkle boyar ya da (aynı renkse) siler. */
  toggle(r: number, c: number): void {
    if (this.readOnly() || !this._plan()) return;
    const selected = this.selected();
    const current = this.cells()[r]?.[c] ?? null;
    this.stroke = selected === null || current === selected ? null : selected;
    this.strokeChanged = false;
    this.apply(r, c);
    this.endStroke();
  }

  reset(): void {
    const plan = this._plan();
    if (!plan || !this.canReset()) return;
    this.cells.set(emptyCells(plan));
    this.emit();
  }

  cellAria(r: number, c: number, value: number | null): string {
    const plan = this._plan();
    const entry = value === null || !plan ? null : plan.palette[value];
    return `${r + 1}, ${c + 1}: ${entry ? entry.label || entry.color : '—'}`;
  }

  private apply(r: number, c: number): void {
    const value = this.stroke;
    if (value === undefined) return;
    const cells = this.cells();
    if (!cells[r] || c < 0 || c >= cells[r].length || cells[r][c] === value) return;
    const next = cells.map((row, i) => (i === r ? row.map((v, j) => (j === c ? value : v)) : row));
    this.cells.set(next);
    this.strokeChanged = true;
  }

  private emit(): void {
    if (!this._plan()) return;
    const payload = buildRegionPaintPayload(this.cells());
    this.lastEmitted = payload;
    this.answerPayloadChange.emit(payload);
    this.answered.emit();
  }

  private restore(): void {
    const json = this._answerPayloadJson();
    if (this.lastEmitted !== null && json === this.lastEmitted) return;
    const plan = this._plan();
    this.cells.set(parseRegionPaintAnswer(json, plan));
    if (plan && (this.selected() ?? 0) >= plan.palette.length) this.selected.set(0);
  }
}
