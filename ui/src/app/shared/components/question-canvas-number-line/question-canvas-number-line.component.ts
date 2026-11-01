import { ChangeDetectionStrategy, Component, ElementRef, EventEmitter, Injector, Input, Output, afterNextRender, computed, inject, signal } from '@angular/core';
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

/** Doğrunun üstündeki bir çentikte duran kartlar; `ghosts` çözüm karşılaştırmasında o çentiğe ait olup başka yere konmuş kartlar. */
interface TickStack {
  at: number;
  cards: NumberLineCard[];
  ghosts: NumberLineCard[];
}

/**
 * Issue #588: sayı doğrusu oynatıcısı (`numberLine`). Kart seçilir (havuzdan ya da doğrudan), sonra çentiğe ya da doğrunun üstüne
 * dokunulur: kart en yakın çentiğe oturur. Doğrudaki seçili kart ok tuşlarıyla kayar (odak kartta kalır); kartın köşesindeki ✕,
 * Delete ya da "Havuza geri" kartı kaldırır, ikinci dokunuş yalnız seçimi bırakır. Bir çentikte birden çok kart durabilir (üst üste). Kontrol sonrası kartlar yeşil / kırmızı; çözüm açıkken hedefli
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
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

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

  /** Çözümün hedef çentikleri (çeldirici null); çözüm kapalıyken null. */
  private readonly targets = computed(() => {
    const plan = this._plan();
    return plan && this._showSolution() ? parseNumberLineSolution(this._solutionJson(), plan) : null;
  });
  /** #679: çözüm açık ve öğrenci kart koymuş — kartlar öğrencinin yerinde yeşil / kırmızı, yanlışların doğru yeri kesikli. */
  readonly compare = computed(() => this.targets() !== null && !!this._solutionJson() && Object.keys(this.placement()).length > 0);

  /** Çizilen yerleşim: öğrencinin (karşılaştırmada da); çözüm açık ama kart yoksa hedefler. */
  readonly shown = computed<NumberLinePlacement>(() => {
    const targets = this.targets();
    return targets && !this.compare() ? solutionPlacement(targets) : this.placement();
  });
  /** Karşılaştırmada kartın durumu: hedefli kart tolerans içinde, çeldirici doğruda değilse doğru. */
  private readonly compareStates = computed(() => {
    const plan = this._plan();
    const targets = this.targets();
    const states = new Map<string, boolean>();
    if (!plan || !targets || !this.compare()) return states;
    for (const card of plan.cards) {
      const target = targets[card.id] ?? null;
      const at = this.placement()[card.id];
      states.set(card.id, target === null ? at === undefined : at !== undefined && Math.abs(at - target) <= plan.tolerance);
    }
    return states;
  });
  readonly pool = computed(() => (this._plan()?.cards ?? []).filter((c) => !Object.hasOwn(this.shown(), c.id)));
  readonly stacks = computed<TickStack[]>(() => {
    const plan = this._plan();
    if (!plan) return [];
    const byTick = new Map<number, TickStack>();
    const at = (tick: number) => byTick.get(tick) ?? byTick.set(tick, { at: tick, cards: [], ghosts: [] }).get(tick)!;
    const targets = this.targets();
    for (const card of plan.cards) {
      const placed = this.shown()[card.id];
      if (placed !== undefined) at(placed).cards.push(card);
      const target = targets?.[card.id] ?? null;
      if (target !== null && this.compareStates().get(card.id) === false) at(target).ghosts.push(card);
    }
    return [...byTick.values()].sort((a, b) => a.at - b.at);
  });
  readonly tallest = computed(() => Math.max(1, ...this.stacks().map((s) => s.cards.length + s.ghosts.length)));
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
    const mark = this.compare() ? this.compareStates().get(id) : this.rowMarks().get(id);
    return mark === undefined ? null : mark ? 'ok' : 'bad';
  }

  /** Karta dokunma: seçer; zaten seçiliyse seçimi bırakır (havuza geri koymak köşedeki ✕ ile). */
  toggleCard(id: string): void {
    if (this.readOnly()) return;
    this.selected.set(this.selected() === id ? null : id);
  }

  /** Doğrudaki kartın köşesindeki ✕: kartı havuza geri koyar. */
  removeCard(id: string): void {
    if (this.readOnly()) return;
    this.unplace(id);
    if (this.selected() === id) this.selected.set(null);
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
    // Kart başka çentiğin yığınına geçince düğmesi yeniden çizilir; odak yeni düğmeye taşınır ki ok tuşları sürsün.
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(`.nl-stack [data-card="${id}"]`)?.focus(), { injector: this.injector });
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
