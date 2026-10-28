import { NgStyle, NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  computed,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { filter, map } from 'rxjs';

import { ActivityFeedbackRow } from '../../../models/activity';
import { QuestionRegion } from '../../../models/draws';
import { LayoutSegment, splitPlaceholders } from '../../../models/interaction-layout';
import {
  SLOT_FILL_LIMITS,
  SlotBox,
  SlotFillPlanV1,
  SlotFillSlot,
  SlotValues,
  buildSlotFillPayload,
  emptySlotValues,
  parseSlotFillPlan,
  parseSlotFillSolution,
  parseSlotFillValues,
  solutionDisplay,
  solutionOf,
  valueOf,
  withValue,
} from '../../../models/slot-fill';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { isSlotAnswerCorrect } from '../../utils/slot-normalizer';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';
import { FractionInputComponent } from '../fraction-input/fraction-input.component';

export type SlotMark = 'correct' | 'wrong' | null;

/** Yazarken kayıt gecikmesi (ms): her tuşta sunucu kaydı olmasın, duraksayınca kaydedilsin. */
export const SLOT_FILL_SAVE_DEBOUNCE_MS = 600;

/**
 * Issue #575: cevap kutusu oynatıcısı. Görselin üstünde, plandaki kutu konumlarında giriş alanları (görselle birlikte
 * ölçeklenir): sayı (`inputmode=decimal`; kesirde pay/payda, tam sayılı kesirde tam + pay/payda), metin, açılır seçim ve
 * sembol paletli alan. Her değişiklik bir sunucu kaydıdır: yazarken {@link SLOT_FILL_SAVE_DEBOUNCE_MS} bekleyip, alan
 * bırakılınca (`change`) hemen kaydedilir. Kontrol / bitir / otomatik teslim öncesi ebeveyn {@link flush}'ı çağırır.
 *
 * Issue #590 (dilim b): planda `layout` varsa (görselsiz) kutular görsel üstünde değil düzende çizilir — `list` (soru +
 * kısa cevap satırları), `text` (satır içi boşluklu metin), `table` (boş hücre = cevap kutusu). İsteğe bağlı görsel
 * etkileşimsiz olarak üstte kalır. Giriş alanları ve kontrol aynıdır.
 *
 * Girdi/çıktılar matris / sürükle-bırak oynatıcısıyla aynı: test ekranı ve etkinlik oynatıcısı aynı sözleşmeyle besler.
 * Çözüm (`interactionSolutionJson`) yalnız verilirse kullanılır (#570); puan yok, kontrol kutu başına doğru/yanlış.
 */
@Component({
  selector: 'app-question-canvas-slot-fill',
  standalone: true,
  imports: [
    NgStyle,
    NgTemplateOutlet,
    MatButtonModule,
    MatIconModule,
    TranslocoDirective,
    TranslocoPipe,
    StorageImageRetryDirective,
    FractionInputComponent,
  ],
  templateUrl: './question-canvas-slot-fill.component.html',
  styleUrl: './question-canvas-slot-fill.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionCanvasSlotFillComponent {
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    if (value !== this._region()) {
      // Oynatıcı bileşeni sıradaki maddeye yeniden kullanır: önceki maddenin bekleyen kaydı yeni maddeye yazılmasın
      // (alan bırakılınca `change` zaten kaydetti).
      this.cancelPendingSave();
      this.resetTransientState();
    }
    this._region.set(value);
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this.cancelPendingSave();
    this.lastEmitted = null;
    this.resetTransientState();
    this._plan.set(parseSlotFillPlan(value));
    this.restoreFromAnswerPayload();
  }

  /** #570: çözüm (`{"s1":{...}}`) — backend yalnız örnek soruda, biten testte, çözüm istendiğinde ya da sahibe doldurur. */
  @Input() set interactionSolutionJson(value: string | null | undefined) {
    this._solutionJson.set(value ?? null);
  }

  @Input() imageRefresh: StorageImageResolver | null = null;

  /** Örnek soru: çözüm geldiyse doldurulan kutular istemcide (TS normalleştirici) işaretlenir. */
  @Input() set practiceFeedback(value: boolean) {
    this._practiceFeedback.set(!!value);
  }

  @Input() hidePassage = false;

  @Input() set answerPayloadJson(value: string | null | undefined) {
    this._answerPayloadJson.set(value ?? null);
    this.restoreFromAnswerPayload();
  }

  /** Sunucu kontrolünün kutu sonucu (satır id = kutu id'si). Verilirse `practiceFeedback`in önüne geçer. */
  @Input() set feedbackRows(value: ActivityFeedbackRow[] | null | undefined) {
    this._feedback.set(value ? new Map(value.map((row) => [row.id, row.correct])) : null);
  }

  /** true iken öğrencinin değerleri yerine kabul edilen değerler gösterilir (salt okunur). */
  @Input() set showSolution(value: boolean) {
    this._showSolution.set(!!value);
  }

  /** Etkinlik oynatıcısı (daha büyük dokunma hedefleri). */
  @Input() set activityMode(value: boolean) {
    this._activityMode.set(!!value);
  }

  @Output() answerPayloadChange = new EventEmitter<string>();
  @Output() answered = new EventEmitter<void>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private saveTimer: ReturnType<typeof setTimeout> | null = null;
  /** Son yayınlanan payload: ebeveynin geri verdiği yankı (geç gelse de) ekrandaki yeni değerin üstüne yazılmaz. */
  private lastEmitted: string | null = null;
  private readonly transloco = inject(TranslocoService);
  private readonly i18nTick = toSignal(
    this.transloco.events$.pipe(
      filter((e) => e.type === 'langChanged' || e.type === 'translationLoadSuccess'),
      map(() => Date.now())
    ),
    { initialValue: 0 }
  );

  private readonly _region = signal<QuestionRegion | null>(null);
  private readonly _plan = signal<SlotFillPlanV1 | null>(null);
  private readonly _solutionJson = signal<string | null>(null);
  private readonly _answerPayloadJson = signal<string | null>(null);
  private readonly _feedback = signal<Map<string, boolean> | null>(null);
  private readonly _showSolution = signal(false);
  private readonly _practiceFeedback = signal(false);
  private readonly _activityMode = signal(false);

  /** Ekrandaki (yazılmakta olan) değerler. */
  readonly values = signal<SlotValues>(emptySlotValues());
  /** Son kaydedilen (commit) değerler: istemci işareti yazarken değil, alan bırakılınca güncellenir. */
  private readonly committed = signal<SlotValues>(emptySlotValues());
  /** Sembol paletinin bağlı olduğu kutu (son odaklanan palet kutusu). */
  readonly focusedPaletteId = signal<string | null>(null);

  readonly limits = SLOT_FILL_LIMITS;
  readonly plan = this._plan.asReadonly();
  readonly activity = this._activityMode.asReadonly();
  readonly solutionShown = this._showSolution.asReadonly();
  readonly imageUrl = computed(() => this._region()?.imageUrl ?? null);
  readonly passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);
  readonly passageTitle = computed(() => this._region()?.passage?.title ?? null);
  readonly slots = computed(() => this._plan()?.slots ?? []);
  readonly layout = computed(() => this._plan()?.layout ?? null);
  /** Görsel üstündeki kaplama yalnız kutulu (görselli) planda. */
  readonly boxedSlots = computed(() => (this.layout() ? [] : this.slots()));
  /** Kutu id → (kutu, plan sırası) — düzendeki yer tutucular bununla çözülür; bilinmeyen id düz metin kalmaz, atlanır. */
  readonly slotRef = computed(() => new Map(this.slots().map((slot, index) => [slot.id, { slot, index }])));
  readonly textSegments = computed<LayoutSegment[]>(() => {
    const layout = this.layout();
    return layout?.kind === 'text' ? splitPlaceholders(layout.text) : [];
  });
  readonly tableCells = computed<{ header: string[] | null; rows: LayoutSegment[][][] } | null>(() => {
    const layout = this.layout();
    if (layout?.kind !== 'table') return null;
    return { header: layout.header ?? null, rows: layout.rows.map((row) => row.map(splitPlaceholders)) };
  });
  readonly solution = computed(() => parseSlotFillSolution(this._solutionJson(), this._plan()));

  /** Palet çubuğunun kutusu: odaklanan palet kutusu, yoksa ilk palet kutusu. */
  readonly paletteSlot = computed<{ slot: SlotFillSlot; index: number } | null>(() => {
    const slots = this.slots();
    const focused = this.focusedPaletteId();
    let index = focused ? slots.findIndex((s) => s.id === focused && s.input.kind === 'palette') : -1;
    if (index < 0) index = slots.findIndex((s) => s.input.kind === 'palette');
    return index < 0 ? null : { slot: slots[index], index };
  });

  readonly slotMark = computed(() => {
    const feedback = this._feedback();
    const showSolution = this._showSolution();
    const practice = this._practiceFeedback();
    const solution = this.solution();
    const committed = this.committed();
    return (slot: SlotFillSlot): SlotMark => {
      if (showSolution) return null;
      if (feedback) {
        if (!feedback.has(slot.id)) return null;
        return feedback.get(slot.id) ? 'correct' : 'wrong';
      }
      // İstemci işareti yalnız örnek soruda ve çözüm geldiyse; boş kutu işaretlenmez.
      if (!practice) return null;
      const expected = solutionOf(solution, slot.id);
      const actual = valueOf(committed, slot.id);
      if (!expected || !actual.trim()) return null;
      return isSlotAnswerCorrect(slot.input.kind, expected, actual) ? 'correct' : 'wrong';
    };
  });

  readonly filledCount = computed(() => {
    const values = this.values();
    return this.slots().filter((slot) => !!valueOf(values, slot.id).trim()).length;
  });

  readonly hint = computed(() => {
    this.i18nTick();
    if (this._showSolution()) return this.t('solutionShownHint');
    return this.t('hint');
  });

  /** Plan koordinatları soru görseli pikselinde (region.width/height); kaplama görselle birlikte ölçeklenir. */
  boxStyle(box: SlotBox | null | undefined): Record<string, string> {
    const region = this._region();
    if (!region || !box || !region.width || !region.height) return { display: 'none' };
    return {
      left: `${(box.x / region.width) * 100}%`,
      top: `${(box.y / region.height) * 100}%`,
      width: `${(box.width / region.width) * 100}%`,
      height: `${(box.height / region.height) * 100}%`,
    };
  }

  valueFor(slotId: string): string {
    return valueOf(this.values(), slotId);
  }

  /** Çözüm açıkken kutuda gösterilen kabul edilen değer (çözüm yoksa boş). */
  solutionText(slot: SlotFillSlot): string {
    return solutionDisplay(slot.input, solutionOf(this.solution(), slot.id));
  }

  slotAria(slot: SlotFillSlot, index: number): string {
    this.i18nTick();
    const label = slot.label?.trim();
    const base = label ? this.t('slotAriaLabeled', { n: index + 1, label }) : this.t('slotAria', { n: index + 1 });
    const mark = this.slotMark()(slot);
    return mark ? `${base}, ${this.t(mark === 'correct' ? 'markCorrect' : 'markWrong')}` : base;
  }

  constructor() {
    // Yok edilirken yayın yapılmaz (ebeveyn o an başka maddede olabilir); bekleyen değeri ebeveyn `flush()` ile alır,
    // alan bırakılınca da `change` kaydeder.
    inject(DestroyRef).onDestroy(() => this.cancelPendingSave());
  }

  /** Yazarken: ekrandaki değer hemen, kayıt duraksayınca. */
  onInput(slotId: string, event: Event): void {
    const raw = (event.target as HTMLInputElement | null)?.value ?? '';
    this.setValue(slotId, raw);
    this.scheduleSave();
  }

  /** Bekleyen (yazılmış ama kaydedilmemiş) değeri hemen yayınlar. Kontrol / bitir / teslim öncesi çağrılır. */
  flush(): void {
    this.commit();
  }

  /** Alan bırakıldı / Enter / seçim: değer kaydedilir. */
  onCommit(slotId: string, event?: Event): void {
    if (event) this.setValue(slotId, (event.target as HTMLInputElement | HTMLSelectElement | null)?.value ?? '');
    this.commit();
  }

  onFractionInput(slotId: string, value: string): void {
    this.setValue(slotId, value);
    this.scheduleSave();
  }

  onFractionCommit(slotId: string, value: string): void {
    this.setValue(slotId, value);
    this.commit();
  }

  onPaletteFocus(slotId: string): void {
    this.focusedPaletteId.set(slotId);
  }

  /** Sembol düğmesi: palet kutusunda imlecin yerine sembolü koyar ve kaydeder (odak alana döner). */
  insertSymbol(slotId: string, symbol: string): void {
    if (this._showSolution()) return;
    const el = this.host.nativeElement.querySelector<HTMLInputElement>(`input[data-slot-input="${cssEscape(slotId)}"]`);
    const current = this.valueFor(slotId);
    const start = el?.selectionStart ?? current.length;
    const end = el?.selectionEnd ?? current.length;
    const next = (current.slice(0, start) + symbol + current.slice(end)).slice(0, SLOT_FILL_LIMITS.maxValueLength);
    this.setValue(slotId, next);
    if (el) {
      el.value = next;
      el.focus();
      const caret = Math.min(start + symbol.length, next.length);
      el.setSelectionRange(caret, caret);
    }
    this.commit();
  }

  clearAll(): void {
    if (this._showSolution()) return;
    this.values.set(emptySlotValues());
    this.commit();
  }

  private setValue(slotId: string, value: string): void {
    if (this._showSolution() || !this.slots().some((s) => s.id === slotId)) return;
    this.values.set(withValue(this.values(), slotId, value.slice(0, SLOT_FILL_LIMITS.maxValueLength)));
  }

  private scheduleSave(): void {
    this.cancelPendingSave();
    this.saveTimer = setTimeout(() => {
      this.saveTimer = null;
      this.commit();
    }, SLOT_FILL_SAVE_DEBOUNCE_MS);
  }

  private cancelPendingSave(): void {
    if (this.saveTimer !== null) clearTimeout(this.saveTimer);
    this.saveTimer = null;
  }

  private commit(): void {
    this.cancelPendingSave();
    const payload = buildSlotFillPayload(this.values());
    if (payload === buildSlotFillPayload(this.committed())) return;
    this.committed.set(this.values());
    this.lastEmitted = payload;
    this.answerPayloadChange.emit(payload);
    this.answered.emit();
  }

  /** Madde değişince palet odağı sıfırlanır. */
  private resetTransientState(): void {
    this.focusedPaletteId.set(null);
  }

  private restoreFromAnswerPayload(): void {
    const values = parseSlotFillValues(this._answerPayloadJson(), this._plan());
    const incoming = buildSlotFillPayload(values);
    // Ebeveyn kendi yayınımızı geri verdiyse ekrandaki değer korunur:
    // - ekrandakiyle aynıysa ham metin (ör. sondaki boşluk "İç |") kırpılmasın ("İçAnadolu" olurdu);
    // - son yayınımızla aynıysa (yankı, öğrenci bu arada aynı kutunun başka alanına yazmış olabilir: "5" yayınlandı, pay
    //   "8" yazıldı) eski değer ekrana geri yazılmasın — yoksa kesir bileşeni payı kaybederdi (#575 E2E).
    if (incoming === buildSlotFillPayload(this.values())) {
      this.committed.set(this.values());
      return;
    }
    if (this.lastEmitted !== null && incoming === this.lastEmitted) return;
    this.cancelPendingSave();
    this.values.set(values);
    this.committed.set(values);
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`shared.questionCanvas.slotFill.${key}`, params) ?? '';
  }
}

/** Seçici içinde kutu id'si (id deseni zaten güvenli; yine de kaçışlanır). */
function cssEscape(value: string): string {
  return typeof CSS !== 'undefined' && CSS.escape ? CSS.escape(value) : value.replace(/["\\]/g, '\\$&');
}
