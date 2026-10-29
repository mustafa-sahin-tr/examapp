import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { QuestionRegion } from '../../../models/draws';
import {
  NumberLineCard,
  NumberLineFeedback,
  NumberLinePlacement,
  NumberLinePlanV1,
  buildNumberLinePayload,
  parseNumberLineAnswer,
  parseNumberLinePlan,
  parseNumberLineSolution,
  solutionPlacement,
  tickName,
} from '../../../models/number-line';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';

/** Doğrunun üstündeki bir çentikte duran kartlar. */
interface TickStack {
  at: number;
  cards: NumberLineCard[];
}

/**
 * Issue #588: sayı doğrusu oynatıcısı (`numberLine`). Kart seçilir (havuzdan ya da doğrudan), sonra çentiğe ya da doğrunun üstüne
 * dokunulur: kart en yakın çentiğe oturur. Doğrudaki seçili kart ok tuşlarıyla bir çentik kayar; ikinci dokunuş ya da "Havuza geri"
 * kartı kaldırır. Bir çentikte birden çok kart durabilir (üst üste). Kontrol sonrası kartlar yeşil / kırmızı; çözüm açıkken hedefli
 * kartlar hedef çentikte, çeldiriciler havuzda. Her değişiklikte bir kez emit edilir. OnPush: tüm durum signal.
 */
@Component({
  selector: 'app-question-canvas-number-line',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective, StorageImageRetryDirective],
  templateUrl: './question-canvas-number-line.component.html',
  styleUrl: './question-canvas-number-line.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionCanvasNumberLineComponent {
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    const changed = value !== this._region();
    this._region.set(value);
    if (!changed) return;
    this.lastEmitted = null;
    this.selected.set(null);
    this.restore();
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this._plan.set(parseNumberLinePlan(value));
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

  @Input() set feedback(value: NumberLineFeedback | null | undefined) {
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
  private readonly _plan = signal<NumberLinePlanV1 | null>(null);
  private readonly _solutionJson = signal<string | null>(null);
  private readonly _answerPayloadJson = signal<string | null>(null);
  private readonly _feedback = signal<NumberLineFeedback | null>(null);
  private readonly _showSolution = signal(false);
  private readonly _activityMode = signal(false);
  private readonly _disabled = signal(false);

  readonly placement = signal<NumberLinePlacement>({});
  /** Seçili kart id'si (havuzda ya da doğruda). */
  readonly selected = signal<string | null>(null);

  readonly plan = this._plan.asReadonly();
  readonly solutionShown = this._showSolution.asReadonly();
  readonly activity = this._activityMode.asReadonly();
  readonly imageUrl = computed(() => this._region()?.imageUrl || null);
  readonly passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);
  readonly passageTitle = computed(() => this._region()?.passage?.title ?? null);
  readonly readOnly = computed(() => this._showSolution() || this._disabled());
  readonly feedbackState = this._feedback.asReadonly();

  /** Çizilen yerleşim: çözüm açıkken hedefler, değilse öğrencinin. */
  readonly shown = computed<NumberLinePlacement>(() => {
    const plan = this._plan();
    if (!plan) return {};
    return this._showSolution() ? solutionPlacement(parseNumberLineSolution(this._solutionJson(), plan)) : this.placement();
  });
  readonly pool = computed(() => (this._plan()?.cards ?? []).filter((c) => !Object.hasOwn(this.shown(), c.id)));
  readonly stacks = computed<TickStack[]>(() => {
    const plan = this._plan();
    if (!plan) return [];
    const byTick = new Map<number, NumberLineCard[]>();
    for (const card of plan.cards) {
      const at = this.shown()[card.id];
      if (at === undefined) continue;
      byTick.set(at, [...(byTick.get(at) ?? []), card]);
    }
    return [...byTick.entries()].sort((a, b) => a[0] - b[0]).map(([at, cards]) => ({ at, cards }));
  });
  readonly tallest = computed(() => Math.max(1, ...this.stacks().map((s) => s.cards.length)));
  /** Çizilen (ve klavyeyle seçilebilen) çentikler: gizliyse yalnız etiketliler. */
  readonly ticks = computed(() => {
    const plan = this._plan();
    if (!plan) return [];
    const all = Array.from({ length: plan.divisions + 1 }, (_, i) => i);
    return plan.hideTicks ? all.filter((i) => plan.labels.some((l) => l.at === i)) : all;
  });
  readonly rowMarks = computed(() => new Map((this._feedback()?.rows ?? []).map((r) => [r.id, r.correct])));
  readonly correctCount = computed(() => [...this.rowMarks().values()].filter(Boolean).length);
  readonly selectedPlaced = computed(() => {
    const id = this.selected();
    return id !== null && Object.hasOwn(this.placement(), id);
  });
  readonly canReset = computed(() => !this.readOnly() && Object.keys(this.placement()).length > 0);

  /** Çentiğin yatay konumu (yüzde). */
  left(at: number): string {
    const plan = this._plan();
    return plan ? `${(at / plan.divisions) * 100}%` : '0%';
  }

  /** Görünür çentikler sıksa doğru yatay kayar: çentik başına en az 28 px. */
  trackWidth(plan: NumberLinePlanV1): string {
    return plan.hideTicks ? '100%' : `max(100%, ${plan.divisions * 28 + 48}px)`;
  }

  tickLabel(at: number): string | null {
    return this._plan()?.labels.find((l) => l.at === at)?.text ?? null;
  }

  tickAria(at: number): string {
    const plan = this._plan();
    return plan ? tickName(plan, at) : String(at);
  }

  cardState(id: string): 'ok' | 'bad' | null {
    const mark = this.rowMarks().get(id);
    return mark === undefined ? null : mark ? 'ok' : 'bad';
  }

  /** Karta dokunma: seçer; zaten seçili ve doğrudaysa havuza geri koyar, havuzdaysa seçimi kaldırır. */
  toggleCard(id: string): void {
    if (this.readOnly()) return;
    if (this.selected() !== id) {
      this.selected.set(id);
      return;
    }
    if (Object.hasOwn(this.placement(), id)) this.unplace(id);
    this.selected.set(null);
  }

  /** Seçili kartı çentiğe koyar. */
  place(at: number): void {
    const id = this.selected();
    const plan = this._plan();
    if (this.readOnly() || id === null || !plan || at < 0 || at > plan.divisions) return;
    this.selected.set(null);
    if (this.placement()[id] === at) return;
    this.placement.set({ ...this.placement(), [id]: at });
    this.emit();
  }

  /** Doğrunun üstüne dokunma: en yakın çentik. */
  placeAtPointer(event: MouseEvent): void {
    const plan = this._plan();
    const axis = event.currentTarget as HTMLElement | null;
    if (!plan || !axis || this.selected() === null) return;
    const rect = axis.getBoundingClientRect();
    if (rect.width <= 0) return;
    const ratio = Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width));
    this.place(Math.round(ratio * plan.divisions));
  }

  /** Doğrudaki seçili kart: ← / → bir çentik kaydırır, Delete / Backspace havuza geri koyar. */
  onCardKey(event: KeyboardEvent, id: string): void {
    const plan = this._plan();
    const at = this.placement()[id];
    if (this.readOnly() || !plan || at === undefined) return;
    if (event.key === 'Delete' || event.key === 'Backspace') {
      event.preventDefault();
      this.unplace(id);
      this.selected.set(null);
      return;
    }
    const step = event.key === 'ArrowLeft' ? -1 : event.key === 'ArrowRight' ? 1 : 0;
    if (!step) return;
    event.preventDefault();
    const next = Math.min(plan.divisions, Math.max(0, at + step));
    if (next === at) return;
    this.selected.set(id);
    this.placement.set({ ...this.placement(), [id]: next });
    this.emit();
  }

  returnSelected(): void {
    const id = this.selected();
    if (id === null || this.readOnly()) return;
    this.unplace(id);
    this.selected.set(null);
  }

  reset(): void {
    if (!this.canReset()) return;
    this.placement.set({});
    this.selected.set(null);
    this.emit();
  }

  private unplace(id: string): void {
    if (!Object.hasOwn(this.placement(), id)) return;
    const next = { ...this.placement() };
    delete next[id];
    this.placement.set(next);
    this.emit();
  }

  private emit(): void {
    const plan = this._plan();
    if (!plan) return;
    const payload = buildNumberLinePayload(plan, this.placement());
    this.lastEmitted = payload;
    this.answerPayloadChange.emit(payload);
    this.answered.emit();
  }

  private restore(): void {
    const json = this._answerPayloadJson();
    if (this.lastEmitted !== null && json === this.lastEmitted) return;
    const plan = this._plan();
    this.placement.set({ ...parseNumberLineAnswer(json, plan) });
    const selected = this.selected();
    if (selected !== null && !plan?.cards.some((c) => c.id === selected)) this.selected.set(null);
  }
}
