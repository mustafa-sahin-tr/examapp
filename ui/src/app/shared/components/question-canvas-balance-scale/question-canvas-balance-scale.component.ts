import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { QuestionRegion } from '../../../models/draws';
import {
  BalancePan,
  BalancePans,
  BalanceScaleFeedback,
  BalanceScalePlanV1,
  BalanceTilt,
  BalanceWeight,
  buildBalanceScalePayload,
  findBalance,
  panTotals,
  parseBalanceScaleAnswer,
  parseBalanceScalePlan,
  isBalanced,
  parseBalanceScaleSolution,
  tiltOf,
  weightText,
} from '../../../models/balance-scale';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';

/** Terazinin eğim açısı (derece); ağır kefe aşağı iner. */
const TILT_DEG = 8;

/**
 * Issue #632: terazi oynatıcısı (`balanceScale`). Ağırlık seçilir (havuzdan ya da kefeden), sonra kefeye dokunulur (ya da "Sol kefeye" /
 * "Sağ kefeye"); kefedeki seçili ağırlığa ikinci dokunuş ya da "Havuza geri" onu kaldırır. Hazır yükler kefede sabit durur. Terazi
 * kefe toplamlarına göre canlı eğilir (`hideTilt`'te kontrol edilene dek düz durur; kontrol sonrası geri bildirimdeki eğim). Toplamlar
 * yazılmaz (öğrenci hesaplasın). Çözüm açıkken kurala uyan bir yerleşim. Her değişiklikte bir kez emit edilir. OnPush: tüm durum signal.
 */
@Component({
  selector: 'app-question-canvas-balance-scale',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective, StorageImageRetryDirective],
  templateUrl: './question-canvas-balance-scale.component.html',
  styleUrl: './question-canvas-balance-scale.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionCanvasBalanceScaleComponent {
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    const changed = value !== this._region();
    this._region.set(value);
    if (!changed) return;
    this.lastEmitted = null;
    this.selected.set(null);
    this.restore();
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this._plan.set(parseBalanceScalePlan(value));
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

  @Input() set feedback(value: BalanceScaleFeedback | null | undefined) {
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
  private readonly _plan = signal<BalanceScalePlanV1 | null>(null);
  private readonly _solutionJson = signal<string | null>(null);
  private readonly _answerPayloadJson = signal<string | null>(null);
  private readonly _feedback = signal<BalanceScaleFeedback | null>(null);
  private readonly _showSolution = signal(false);
  private readonly _activityMode = signal(false);
  private readonly _disabled = signal(false);

  readonly pans = signal<BalancePans>({ left: [], right: [] });
  /** Seçili ağırlık id'si (havuzda ya da kefede). */
  readonly selected = signal<string | null>(null);

  readonly plan = this._plan.asReadonly();
  readonly solutionShown = this._showSolution.asReadonly();
  readonly activity = this._activityMode.asReadonly();
  readonly imageUrl = computed(() => this._region()?.imageUrl || null);
  readonly passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);
  readonly passageTitle = computed(() => this._region()?.passage?.title ?? null);
  readonly readOnly = computed(() => this._showSolution() || this._disabled());
  readonly feedbackState = this._feedback.asReadonly();

  /** #679: çözüm açık ve öğrenci kefeye ağırlık koymuş — terazide öğrencinin yerleşimi, yanlışsa altında örnek doğru yerleşim. */
  readonly compare = computed(() => this._showSolution() && this.pans().left.length + this.pans().right.length > 0);
  private readonly example = computed<BalancePans>(() => {
    const plan = this._plan();
    const rule = this._showSolution() ? parseBalanceScaleSolution(this._solutionJson()) : null;
    return (plan && rule && findBalance(plan, rule.useAll)) ?? { left: [], right: [] };
  });
  /** Karşılaştırmada öğrencinin yerleşimi kurala uyuyor mu; değilse null. */
  readonly compareOk = computed<boolean | null>(() => {
    const plan = this._plan();
    const rule = parseBalanceScaleSolution(this._solutionJson());
    return plan && rule && this.compare() ? isBalanced(plan, this.pans(), rule.useAll) : null;
  });
  /** Yanlış yerleşimin altında gösterilen örnek: "5 kg + 14 kg | 9 kg + 10 kg" (hazır yükler dahil). */
  readonly exampleText = computed(() => {
    const plan = this._plan();
    if (!plan || this.compareOk() !== false) return null;
    const side = (fixed: BalanceWeight[], ids: string[]) => [...fixed, ...this.cardsFor(ids)].map((w) => this.text(w)).join(' + ') || '—';
    return `${side(plan.left, this.example().left)} | ${side(plan.right, this.example().right)}`;
  });

  /** Çizilen yerleşim: öğrencinin (karşılaştırmada da); çözüm açık ama kefeler boşsa kurala uyan bir yerleşim. */
  readonly shown = computed<BalancePans>(() => (this._showSolution() && !this.compare() ? this.example() : this.pans()));
  readonly pool = computed(() => {
    const used = new Set([...this.shown().left, ...this.shown().right]);
    return (this._plan()?.cards ?? []).filter((c) => !used.has(c.id));
  });
  readonly leftCards = computed(() => this.cardsFor(this.shown().left));
  readonly rightCards = computed(() => this.cardsFor(this.shown().right));
  /** Görünen eğim: gizliyken kontrol sonucundaki (yoksa düz), değilse canlı. */
  readonly tilt = computed<BalanceTilt>(() => {
    const plan = this._plan();
    if (!plan) return 'level';
    if (plan.hideTilt && !this._showSolution()) return this._feedback()?.tilt ?? 'level';
    return tiltOf(panTotals(plan, this.shown()));
  });
  readonly tiltDeg = computed(() => (this.tilt() === 'left' ? -TILT_DEG : this.tilt() === 'right' ? TILT_DEG : 0));
  readonly selectedPlaced = computed(() => {
    const id = this.selected();
    return id !== null && (this.pans().left.includes(id) || this.pans().right.includes(id));
  });
  readonly canReset = computed(() => !this.readOnly() && this.pans().left.length + this.pans().right.length > 0);

  text(weight: BalanceWeight): string {
    return weightText(weight, this._plan()?.unit ?? null);
  }

  /** Karta dokunma: seçer; zaten seçiliyse seçimi kaldırır, kefedeyse havuza geri koyar. */
  toggleCard(id: string): void {
    if (this.readOnly()) return;
    if (this.selected() !== id) {
      this.selected.set(id);
      return;
    }
    this.remove(id);
    this.selected.set(null);
  }

  /** Seçili ağırlığı kefeye koyar (başka kefedeyse taşır). */
  place(pan: BalancePan): void {
    const id = this.selected();
    if (this.readOnly() || id === null || !this._plan()?.cards.some((c) => c.id === id)) return;
    this.selected.set(null);
    if (this.pans()[pan].includes(id)) return;
    const { left, right } = this.pans();
    this.pans.set(
      pan === 'left'
        ? { left: [...left, id], right: right.filter((c) => c !== id) }
        : { left: left.filter((c) => c !== id), right: [...right, id] }
    );
    this.emit();
  }

  returnSelected(): void {
    const id = this.selected();
    if (id === null || this.readOnly()) return;
    this.remove(id);
    this.selected.set(null);
  }

  reset(): void {
    if (!this.canReset()) return;
    this.pans.set({ left: [], right: [] });
    this.selected.set(null);
    this.emit();
  }

  private cardsFor(ids: string[]): BalanceWeight[] {
    const byId = new Map((this._plan()?.cards ?? []).map((c) => [c.id, c]));
    return ids.flatMap((id) => byId.get(id) ?? []);
  }

  private remove(id: string): void {
    const { left, right } = this.pans();
    if (!left.includes(id) && !right.includes(id)) return;
    this.pans.set({ left: left.filter((c) => c !== id), right: right.filter((c) => c !== id) });
    this.emit();
  }

  private emit(): void {
    if (!this._plan()) return;
    const payload = buildBalanceScalePayload(this.pans());
    this.lastEmitted = payload;
    this.answerPayloadChange.emit(payload);
    this.answered.emit();
  }

  private restore(): void {
    const json = this._answerPayloadJson();
    if (this.lastEmitted !== null && json === this.lastEmitted) return;
    const plan = this._plan();
    this.pans.set(parseBalanceScaleAnswer(json, plan));
    const selected = this.selected();
    if (selected !== null && !plan?.cards.some((c) => c.id === selected)) this.selected.set(null);
  }
}
