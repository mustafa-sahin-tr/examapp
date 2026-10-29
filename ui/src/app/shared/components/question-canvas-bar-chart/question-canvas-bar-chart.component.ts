import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { QuestionRegion } from '../../../models/draws';
import {
  BarChartFeedback,
  BarChartPlanV1,
  BarChartValues,
  buildBarChartPayload,
  isFreeCategory,
  parseBarChartAnswer,
  parseBarChartPlan,
  parseBarChartSolution,
} from '../../../models/bar-chart';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';

/** Çizilen bir sütun. */
interface Column {
  id: string;
  label: string;
  fixed: boolean;
  /** Çizilen değer; çizilmemişse null. */
  value: number | null;
  state: 'ok' | 'bad' | null;
}

/**
 * Issue #588 (dilim b): grafik kurma oynatıcısı (`barChart`). Serbest sütunun içine dokunmak değeri o seviyeye getirir (en yakın
 * `unit` katı); aynı seviyeye yeniden dokunmak sütunu siler. Sütun klavyeyle bir kaydırıcıdır: ↑ / ↓ bir adım, PageUp / PageDown bir
 * etiket aralığı, Home 0, End en büyük, Delete siler. Hazır (sabit) sütunlar gri çizilir, değiştirilemez. Çizgi görünümünde değerler
 * nokta + çizgiyle gösterilir. Kontrol sonrası sütunlar yeşil / kırmızı; çözüm açıkken hedef değerler. Her değişiklikte bir kez emit
 * edilir. OnPush: tüm durum signal.
 */
@Component({
  selector: 'app-question-canvas-bar-chart',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective, StorageImageRetryDirective],
  templateUrl: './question-canvas-bar-chart.component.html',
  styleUrl: './question-canvas-bar-chart.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionCanvasBarChartComponent {
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    const changed = value !== this._region();
    this._region.set(value);
    if (!changed) return;
    this.lastEmitted = null;
    this.restore();
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this._plan.set(parseBarChartPlan(value));
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

  @Input() set feedback(value: BarChartFeedback | null | undefined) {
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

  private readonly _region = signal<QuestionRegion | null>(null);
  private readonly _plan = signal<BarChartPlanV1 | null>(null);
  private readonly _solutionJson = signal<string | null>(null);
  private readonly _answerPayloadJson = signal<string | null>(null);
  private readonly _feedback = signal<BarChartFeedback | null>(null);
  private readonly _showSolution = signal(false);
  private readonly _activityMode = signal(false);
  private readonly _disabled = signal(false);

  readonly values = signal<BarChartValues>({});

  readonly plan = this._plan.asReadonly();
  readonly solutionShown = this._showSolution.asReadonly();
  readonly activity = this._activityMode.asReadonly();
  readonly imageUrl = computed(() => this._region()?.imageUrl || null);
  readonly passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);
  readonly passageTitle = computed(() => this._region()?.passage?.title ?? null);
  readonly readOnly = computed(() => this._showSolution() || this._disabled());
  readonly feedbackState = this._feedback.asReadonly();

  readonly rowMarks = computed(() => new Map((this._feedback()?.rows ?? []).map((r) => [r.id, r.correct])));
  readonly correctCount = computed(() => [...this.rowMarks().values()].filter(Boolean).length);

  /** Çizilen değerler: çözüm açıkken hedefler, değilse öğrencinin; sabit sütunlar her zaman. */
  readonly columns = computed<Column[]>(() => {
    const plan = this._plan();
    if (!plan) return [];
    const shown = this._showSolution() ? parseBarChartSolution(this._solutionJson(), plan) : this.values();
    return plan.categories.map((c) => {
      const fixed = Object.hasOwn(plan.fixed, c.id);
      const value = fixed ? plan.fixed[c.id] : Object.hasOwn(shown, c.id) ? shown[c.id] : null;
      const mark = this.rowMarks().get(c.id);
      return { id: c.id, label: c.label, fixed, value, state: mark === undefined ? null : mark ? 'ok' : 'bad' };
    });
  });

  /** Ince ızgara çizgileri (her `unit`) ve etiketli çizgiler (her `step`), 0 hariç, alttan üste. */
  readonly gridLines = computed(() => {
    const plan = this._plan();
    if (!plan) return [];
    const lines: { value: number; major: boolean }[] = [];
    for (let v = plan.unit; v <= plan.max; v += plan.unit) lines.push({ value: v, major: v % plan.step === 0 });
    return lines;
  });

  readonly axisLabels = computed(() => {
    const plan = this._plan();
    if (!plan) return [];
    return Array.from({ length: plan.max / plan.step + 1 }, (_, i) => i * plan.step);
  });

  /** Çizgi görünümü: değeri olan sütunların noktalarını birleştiren çizgi (viewBox: kategori × değer). */
  readonly linePoints = computed(() => {
    const plan = this._plan();
    if (!plan || plan.style !== 'line') return '';
    return this.columns()
      .flatMap((c, i) => (c.value === null ? [] : [`${i + 0.5},${plan.max - c.value}`]))
      .join(' ');
  });

  readonly canReset = computed(() => !this.readOnly() && Object.keys(this.values()).length > 0);

  /** Değerin dikey konumu (alttan yüzde). */
  percent(value: number): string {
    const plan = this._plan();
    return plan ? `${(value / plan.max) * 100}%` : '0%';
  }

  /** Sütunun içine dokunma: dokunulan yüksekliğe en yakın `unit` katı; aynı değere yeniden dokunmak siler. */
  setFromPointer(event: MouseEvent, column: Column): void {
    const plan = this._plan();
    const el = event.currentTarget as HTMLElement | null;
    if (!plan || !el || column.fixed || this.readOnly()) return;
    const rect = el.getBoundingClientRect();
    if (rect.height <= 0) return;
    const ratio = Math.min(1, Math.max(0, (rect.bottom - event.clientY) / rect.height));
    const value = Math.round((ratio * plan.max) / plan.unit) * plan.unit;
    this.set(column.id, column.value === value ? null : value);
  }

  onColumnKey(event: KeyboardEvent, column: Column): void {
    const plan = this._plan();
    if (!plan || column.fixed || this.readOnly()) return;
    const current = column.value ?? 0;
    let next: number | null | undefined;
    switch (event.key) {
      case 'ArrowUp':
      case 'ArrowRight':
        next = current + plan.unit;
        break;
      case 'ArrowDown':
      case 'ArrowLeft':
        next = current - plan.unit;
        break;
      case 'PageUp':
        next = current + plan.step;
        break;
      case 'PageDown':
        next = current - plan.step;
        break;
      case 'Home':
        next = 0;
        break;
      case 'End':
        next = plan.max;
        break;
      case 'Delete':
      case 'Backspace':
        next = null;
        break;
      default:
        return;
    }
    event.preventDefault();
    if (next !== null) next = Math.min(plan.max, Math.max(0, Math.round(next / plan.unit) * plan.unit));
    this.set(column.id, next);
  }

  reset(): void {
    if (!this.canReset()) return;
    this.values.set({});
    this.emit();
  }

  /** Değeri yazar (null = siler); değişmediyse emit etmez. */
  private set(id: string, value: number | null): void {
    const plan = this._plan();
    if (!plan || !isFreeCategory(plan, id)) return;
    const current = Object.hasOwn(this.values(), id) ? this.values()[id] : null;
    if (current === value) return;
    const next = { ...this.values() };
    if (value === null) delete next[id];
    else next[id] = value;
    this.values.set(next);
    this.emit();
  }

  private emit(): void {
    const plan = this._plan();
    if (!plan) return;
    const payload = buildBarChartPayload(plan, this.values());
    this.lastEmitted = payload;
    this.answerPayloadChange.emit(payload);
    this.answered.emit();
  }

  private restore(): void {
    const json = this._answerPayloadJson();
    if (this.lastEmitted !== null && json === this.lastEmitted) return;
    this.values.set({ ...parseBarChartAnswer(json, this._plan()) });
  }
}
