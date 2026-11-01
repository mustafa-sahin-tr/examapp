import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, computed, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { QuestionRegion } from '../../../models/draws';
import {
  ClockAnswer,
  ClockHands,
  ClockSetPlanV1,
  buildClockSetPayload,
  clockMatches,
  formatClockTime,
  handsFor,
  hourAngle,
  minuteAngle,
  parseClockSetAnswer,
  parseClockSetPlan,
  parseClockSetSolution,
  snapHour,
  snapMinute,
  turnHour,
  turnMinute,
} from '../../../models/clock-set';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';

export type ClockHandKind = 'hour' | 'minute';

/** Kadran çizimi (viewBox 0..200, merkez 100). */
const CENTER = 100;
const NUMBER_RADIUS = 72;
const TICK_OUTER = 92;

/** Kurulmamış saatin başlangıç konumu: 12.00. */
const START: ClockHands = { h: 0, m: 0 };

/**
 * Issue #634: saat kurma oynatıcısı (`clockSet`). Her saat SVG kadran; öğrenci akrebi ve yelkovanı **ayrı** çevirir (karar #634: akrep
 * yelkovana bağlı değil, yeri öğretilen kazanım). Kadrana dokunmak / sürüklemek seçili eli o yöne çevirir; ele dokunmak o eli seçer.
 * Her el klavyede kaydırıcıdır (← / → bir adım, PageUp / PageDown yelkovanda 15 dk, akrepte bir saat). Yelkovan planın adımına,
 * akrep 2,5°'ye (saat aralığının 1/12'si) oturur. Sabit saatler oynatılamaz. Kontrol sonrası saat başına doğru / yanlış çerçeve;
 * çözüm açıkken doğru saatler. Her değişiklikte bir kez emit edilir. OnPush: tüm durum signal.
 */
@Component({
  selector: 'app-question-canvas-clock-set',
  standalone: true,
  imports: [MatButtonModule, MatButtonToggleModule, MatIconModule, TranslocoDirective, StorageImageRetryDirective],
  templateUrl: './question-canvas-clock-set.component.html',
  styleUrl: './question-canvas-clock-set.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionCanvasClockSetComponent {
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    const changed = value !== this._region();
    this._region.set(value);
    if (!changed) return;
    this.lastEmitted = null;
    this.restore();
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this._plan.set(parseClockSetPlan(value));
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

  /** Saat id → doğru mu (`parseClockSetFeedback`). */
  @Input() set feedback(value: Record<string, boolean> | null | undefined) {
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
  /** Sürüklenen saat (pointer capture süresince). */
  private dragging: string | null = null;

  private readonly _region = signal<QuestionRegion | null>(null);
  private readonly _plan = signal<ClockSetPlanV1 | null>(null);
  private readonly _solutionJson = signal<string | null>(null);
  private readonly _answerPayloadJson = signal<string | null>(null);
  private readonly _feedback = signal<Record<string, boolean> | null>(null);
  private readonly _showSolution = signal(false);
  private readonly _activityMode = signal(false);
  private readonly _disabled = signal(false);

  readonly answer = signal<ClockAnswer>({});
  /** Kadrana dokunuşun çevirdiği el. */
  readonly activeHand = signal<ClockHandKind>('minute');

  readonly plan = this._plan.asReadonly();
  readonly solutionShown = this._showSolution.asReadonly();
  readonly activity = this._activityMode.asReadonly();
  readonly feedbackState = this._feedback.asReadonly();
  readonly imageUrl = computed(() => this._region()?.imageUrl || null);
  readonly passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);
  readonly passageTitle = computed(() => this._region()?.passage?.title ?? null);
  readonly readOnly = computed(() => this._showSolution() || this._disabled());
  readonly canReset = computed(() => !this.readOnly() && Object.keys(this.answer()).length > 0);

  readonly numbers = Array.from({ length: 12 }, (_, i) => {
    const n = i + 1;
    const a = (n / 12) * 2 * Math.PI;
    return { n, x: CENTER + NUMBER_RADIUS * Math.sin(a), y: CENTER - NUMBER_RADIUS * Math.cos(a) };
  });
  readonly ticks = Array.from({ length: 60 }, (_, i) => {
    const a = (i / 60) * 2 * Math.PI;
    const inner = i % 5 === 0 ? 82 : 87;
    return {
      major: i % 5 === 0,
      x1: CENTER + inner * Math.sin(a),
      y1: CENTER - inner * Math.cos(a),
      x2: CENTER + TICK_OUTER * Math.sin(a),
      y2: CENTER - TICK_OUTER * Math.cos(a),
    };
  });

  /** #679: çözüm açık ve öğrenci bir saat kurmuş — öğrencinin elleri yeşil / kırmızı çerçeveyle, yanlışta doğru saat kesikli eller. */
  readonly compare = computed(() => this._showSolution() && Object.keys(this.answer()).length > 0);

  /** Çizilen eller: sabit saatte sabit, çözüm açıkken (cevap yoksa) hedef, değilse öğrencinin (kurulmamış = 12.00). */
  readonly faces = computed(() => {
    const plan = this._plan();
    if (!plan) return [];
    const solution = this._showSolution() ? parseClockSetSolution(this._solutionJson()) : null;
    const compare = this.compare();
    const answer = this.answer();
    const feedback = this._feedback();
    return plan.clocks.map((clock, index) => {
      const fixed = clock.fixed != null;
      const target = solution?.[clock.id];
      const own = answer[clock.id];
      if (compare && !fixed && target != null) {
        const ok = own != null && clockMatches(own, target);
        return { clock, number: index + 1, fixed, hands: own ?? START, set: own != null, result: ok, ghost: ok ? null : handsFor(target) };
      }
      const hands = fixed ? handsFor(clock.fixed!) : target != null ? handsFor(target) : (own ?? START);
      return {
        clock,
        number: index + 1,
        fixed,
        hands,
        set: fixed || target != null || own != null,
        result: fixed || !feedback || this._showSolution() ? null : (feedback[clock.id] ?? false),
        ghost: null as ClockHands | null,
      };
    });
  });

  hourDeg(hands: ClockHands): number {
    return hourAngle(hands.h);
  }

  minuteDeg(hands: ClockHands): number {
    return minuteAngle(hands.m);
  }

  /** Akrebin ekran okuyucu değeri: tam saatte "3", arada "3–4". */
  /** Doğru saat yazısı ("3:45"; 0 → 12). */
  targetText(hands: ClockHands): string {
    return formatClockTime(hands.h).replace(/^0/, '');
  }

  hourValueText(hands: ClockHands): string {
    const at = hands.h / 60;
    const low = Math.floor(at) % 12 || 12;
    const high = (Math.floor(at) + 1) % 12 || 12;
    return at % 1 === 0 ? String(low) : `${low}–${high}`;
  }

  setActiveHand(hand: ClockHandKind): void {
    this.activeHand.set(hand);
  }

  onPointerDown(event: PointerEvent, clockId: string): void {
    if (this.readOnly() || !this.isFree(clockId)) return;
    const hand = (event.target as Element | null)?.closest?.('[data-hand]')?.getAttribute('data-hand');
    if (hand === 'hour' || hand === 'minute') this.activeHand.set(hand);
    this.dragging = clockId;
    (event.currentTarget as Element | null)?.setPointerCapture?.(event.pointerId);
    event.preventDefault();
    this.turnTo(event, clockId);
  }

  onPointerMove(event: PointerEvent, clockId: string): void {
    if (this.dragging !== clockId) return;
    this.turnTo(event, clockId);
  }

  onPointerUp(event: PointerEvent): void {
    this.dragging = null;
    (event.currentTarget as Element | null)?.releasePointerCapture?.(event.pointerId);
  }

  onHandKeydown(event: KeyboardEvent, clockId: string, hand: ClockHandKind): void {
    if (this.readOnly() || !this.isFree(clockId)) return;
    const step = this._plan()?.step ?? 5;
    const delta: Record<string, number> = {
      ArrowRight: 1,
      ArrowUp: 1,
      ArrowLeft: -1,
      ArrowDown: -1,
      PageUp: hand === 'minute' ? Math.max(1, 15 / step) : 12,
      PageDown: hand === 'minute' ? -Math.max(1, 15 / step) : -12,
    };
    const d = delta[event.key];
    if (d === undefined) return;
    event.preventDefault();
    this.activeHand.set(hand);
    const current = this.answer()[clockId] ?? START;
    this.update(
      clockId,
      hand === 'hour' ? { h: turnHour(current.h, d), m: current.m } : { h: current.h, m: turnMinute(current.m, d, step) }
    );
  }

  reset(): void {
    if (!this.canReset()) return;
    this.answer.set({});
    this.emit();
  }

  private isFree(clockId: string): boolean {
    return this._plan()?.clocks.some((c) => c.id === clockId && c.fixed == null) ?? false;
  }

  /** İşaretçinin kadran merkezine göre açısı (12'den saat yönünde) → seçili el. Merkeze çok yakın dokunuş yok sayılır. */
  private turnTo(event: PointerEvent, clockId: string): void {
    const svg = (event.currentTarget as Element | null)?.closest?.('svg') ?? (event.currentTarget as Element | null);
    const rect = svg?.getBoundingClientRect();
    if (!rect || !rect.width) return;
    const dx = event.clientX - (rect.left + rect.width / 2);
    const dy = event.clientY - (rect.top + rect.height / 2);
    if (Math.hypot(dx, dy) < rect.width * 0.06) return;
    const angle = (Math.atan2(dx, -dy) * 180) / Math.PI;
    const current = this.answer()[clockId] ?? START;
    const next =
      this.activeHand() === 'hour'
        ? { h: snapHour(angle), m: current.m }
        : { h: current.h, m: snapMinute(angle, this._plan()?.step ?? 5) };
    this.update(clockId, next);
  }

  private update(clockId: string, hands: ClockHands): void {
    const current = this.answer()[clockId];
    if (current && current.h === hands.h && current.m === hands.m) return;
    this.answer.set({ ...this.answer(), [clockId]: hands });
    this.emit();
  }

  private emit(): void {
    if (!this._plan()) return;
    const payload = buildClockSetPayload(this.answer());
    this.lastEmitted = payload;
    this.answerPayloadChange.emit(payload);
    this.answered.emit();
  }

  private restore(): void {
    const json = this._answerPayloadJson();
    if (this.lastEmitted !== null && json === this.lastEmitted) return;
    this.answer.set(parseClockSetAnswer(json, this._plan()));
  }
}
