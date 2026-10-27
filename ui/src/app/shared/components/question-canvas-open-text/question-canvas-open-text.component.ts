import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  EventEmitter,
  Input,
  Output,
  computed,
  forwardRef,
  inject,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslocoDirective, TranslocoPipe } from '@jsverse/transloco';

import { QuestionRegion } from '../../../models/draws';
import {
  OPEN_TEXT_LIMITS,
  OpenTextPlanV1,
  OpenTextStudentReview,
  buildOpenTextPayload,
  countOpenTextWords,
  isBlankOpenText,
  openTextWordState,
  parseOpenTextAnswer,
  parseOpenTextPlan,
  parseOpenTextSolution,
  stripOpenTextControlChars,
} from '../../../models/open-text';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { FLUSHABLE_PLAYER, FlushablePlayer } from '../../utils/flushable-player';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';
import { OpenTextReviewStatusComponent } from '../open-text-review-status/open-text-review-status.component';

/** Yazarken kayıt gecikmesi (ms); alan bırakılınca hemen kaydedilir. */
export const OPEN_TEXT_SAVE_DEBOUNCE_MS = 800;

let nextId = 0;

/**
 * Issue #583: açık uçlu cevap oynatıcısı (bileşen H). Soru görselinin altında metin alanı (Material textarea, planın `lines`
 * satırı), sözcük sayacı (en az / en çok; en çok aşılınca uyarı, 2 katı aşılınca kayıt yapılmaz — sunucu reddederdi) ve mevcut
 * save-answer akışı (yazarken {@link OPEN_TEXT_SAVE_DEBOUNCE_MS} sonra, alan bırakılınca hemen). Otomatik kontrol yok:
 * etkinlikte değerlendirme durumu gösterilir — gönderilmemiş taslakta "Cevapları kontrol et ile gönder" ipucu, sonra
 * "Öğretmen değerlendirmesi bekliyor" ya da Yeterli / Kısmen / Eksik + geri bildirim. Çözüm açıkken (reveal) metin salt okunur,
 * örnek cevap (varsa) gösterilir; rubrik öğrenciye hiç gelmez.
 *
 * Metinler düz metindir: yalnız interpolasyon (`white-space: pre-wrap`), HTML olarak yorumlanmaz. OnPush: tüm durum signal.
 */
@Component({
  selector: 'app-question-canvas-open-text',
  standalone: true,
  imports: [
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    TranslocoDirective,
    TranslocoPipe,
    StorageImageRetryDirective,
    OpenTextReviewStatusComponent,
  ],
  templateUrl: './question-canvas-open-text.component.html',
  styleUrl: './question-canvas-open-text.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Ebeveyn sınıfa değil belirtece göre sorgular (`@defer` parçası ilk pakete girmesin).
  providers: [{ provide: FLUSHABLE_PLAYER, useExisting: forwardRef(() => QuestionCanvasOpenTextComponent) }],
})
export class QuestionCanvasOpenTextComponent implements FlushablePlayer {
  /**
   * Etkinlik oynatıcısı aynı örneği sonraki açık uçlu maddede yeniden kullanır (bekleyen metni madde değişmeden önce
   * `flush()` ile alır). Madde değişince bekleyen kayıt iptal edilir ve önceki maddenin "son yayın" izi silinir: aksi halde
   * A'ya dönüşte A'nın kayıtlı payload'u eski yankı sanılıp ekranda B'nin (boş) metni kalırdı. Girdi sırası fark etmesin diye
   * metin o anki payload'dan yeniden okunur.
   */
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    const changed = value !== this._region();
    this._region.set(value);
    if (!changed) return;
    this.cancelPendingSave();
    this.lastEmitted = null;
    this.restoreFromAnswerPayload();
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this._plan.set(parseOpenTextPlan(value));
  }

  /** Öğrenciye giden çözüm yalnız `{"sampleAnswer"}` (reveal / örnek soru); rubrik gelmez. */
  @Input() set interactionSolutionJson(value: string | null | undefined) {
    this._solutionJson.set(value ?? null);
  }

  @Input() imageRefresh: StorageImageResolver | null = null;

  /** Örnek soru (test çözme): örnek cevap geldiyse öğrenci yazdıktan sonra gösterilir. */
  @Input() set practiceFeedback(value: boolean) {
    this._practiceFeedback.set(!!value);
  }

  @Input() hidePassage = false;

  @Input() set answerPayloadJson(value: string | null | undefined) {
    this._answerPayloadJson.set(value ?? null);
    this.restoreFromAnswerPayload();
  }

  /** Etkinlikte öğrencinin değerlendirme durumu (sunucunun `openTextReview`'ü; yoksa null). */
  @Input() set review(value: OpenTextStudentReview | null | undefined) {
    this._review.set(value ?? null);
  }

  /** true iken metin salt okunur, örnek cevap gösterilir. */
  @Input() set showSolution(value: boolean) {
    this._showSolution.set(!!value);
  }

  /** Etkinlik oynatıcısı: değerlendirme durumu / gönderme ipucu gösterilir. */
  @Input() set activityMode(value: boolean) {
    this._activityMode.set(!!value);
  }

  /** Kontrol / bitirme sürerken yazma kapalı. */
  @Input() set disabled(value: boolean) {
    this._disabled.set(!!value);
  }

  @Output() answerPayloadChange = new EventEmitter<string>();
  @Output() answered = new EventEmitter<void>();

  private saveTimer: ReturnType<typeof setTimeout> | null = null;
  /** Son yayınlanan payload: ebeveynin geri verdiği yankı ekrandaki yeni metnin üstüne yazılmaz. */
  private lastEmitted: string | null = null;

  private readonly _region = signal<QuestionRegion | null>(null);
  private readonly _plan = signal<OpenTextPlanV1 | null>(null);
  private readonly _solutionJson = signal<string | null>(null);
  private readonly _answerPayloadJson = signal<string | null>(null);
  private readonly _review = signal<OpenTextStudentReview | null>(null);
  private readonly _showSolution = signal(false);
  private readonly _practiceFeedback = signal(false);
  private readonly _activityMode = signal(false);
  private readonly _disabled = signal(false);

  /** Ekrandaki metin (yazarken anlık). */
  readonly text = signal('');
  /** Son kaydedilen metin. */
  private readonly committed = signal('');

  readonly limits = OPEN_TEXT_LIMITS;
  /** Sayaç öğesinin id'si (aynı sayfada birden çok oynatıcı olabilir). */
  readonly counterId = `ot-counter-${++nextId}`;
  readonly plan = this._plan.asReadonly();
  readonly activity = this._activityMode.asReadonly();
  readonly solutionShown = this._showSolution.asReadonly();
  readonly isDisabled = this._disabled.asReadonly();
  readonly currentReview = this._review.asReadonly();
  readonly imageUrl = computed(() => this._region()?.imageUrl ?? null);
  readonly passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);
  readonly passageTitle = computed(() => this._region()?.passage?.title ?? null);
  readonly rows = computed(() => this._plan()?.lines ?? OPEN_TEXT_LIMITS.defaultLines);
  readonly minWords = computed(() => this._plan()?.minWords ?? null);
  readonly maxWords = computed(() => this._plan()?.maxWords ?? null);
  readonly readOnly = computed(() => this._showSolution() || this._disabled());

  readonly wordCount = computed(() => countOpenTextWords(this.text()));
  readonly wordState = computed(() => openTextWordState(this.wordCount(), this._plan()));
  /** En çok sözcüğün 2 katı aşıldı: ekrandaki metin kaydedilmez; ebeveyn kontrol / bitirmeyi engeller. */
  readonly isBlocked = computed(() => !this.readOnly() && this.wordState() === 'blocked');
  readonly sampleAnswer = computed(() => parseOpenTextSolution(this._solutionJson()).sampleAnswer);

  /** Örnek cevap kutusu: çözüm açıkken, ya da örnek soruda öğrenci yazıp kaydettikten sonra (örnek cevap geldiyse). */
  readonly showSample = computed(
    () => this._showSolution() || (this._practiceFeedback() && !this._activityMode() && !!this.sampleAnswer() && !isBlankOpenText(this.committed()))
  );

  /** Etkinlikte kaydedilmiş ama henüz gönderilmemiş (kontrol edilmemiş) metin: "Cevapları kontrol et ile gönder" ipucu. */
  readonly showSubmitHint = computed(
    () => this._activityMode() && !this._showSolution() && !this._review() && !isBlankOpenText(this.committed())
  );

  constructor() {
    // Yok edilirken yayın yapılmaz; bekleyen metni ebeveyn `flush()` ile alır, alan bırakılınca `change` kaydeder.
    inject(DestroyRef).onDestroy(() => this.cancelPendingSave());
  }

  /** Yazarken: ekranda hemen, kayıt duraksayınca. Kontrol karakterleri atılır (sunucu reddederdi). */
  onInput(event: Event): void {
    const field = event.target as HTMLTextAreaElement | null;
    const raw = field?.value ?? '';
    const clean = stripOpenTextControlChars(raw);
    if (field && clean !== raw) field.value = clean;
    if (this.readOnly()) return;
    this.text.set(clean);
    this.scheduleSave();
  }

  /** Alan bırakıldı: hemen kaydedilir. */
  onCommit(): void {
    this.commit();
  }

  clear(): void {
    if (this.readOnly()) return;
    this.text.set('');
    this.commit();
  }

  /** Bekleyen (yazılmış ama kaydedilmemiş) metni hemen yayınlar. Kontrol / bitir / teslim öncesi çağrılır. */
  flush(): void {
    this.commit();
  }

  private scheduleSave(): void {
    this.cancelPendingSave();
    this.saveTimer = setTimeout(() => {
      this.saveTimer = null;
      this.commit();
    }, OPEN_TEXT_SAVE_DEBOUNCE_MS);
  }

  private cancelPendingSave(): void {
    if (this.saveTimer !== null) clearTimeout(this.saveTimer);
    this.saveTimer = null;
  }

  private commit(): void {
    this.cancelPendingSave();
    if (this._showSolution()) return;
    // En çok sözcüğün 2 katını aşan metin sunucuda reddedilir: kaydedilmez, sayaç uyarır (son geçerli metin kayıtlı kalır).
    if (this.wordState() === 'blocked') return;
    const text = this.text();
    if (text === this.committed()) return;
    const payload = buildOpenTextPayload(text);
    this.committed.set(text);
    this.lastEmitted = payload;
    this.answerPayloadChange.emit(payload);
    this.answered.emit();
  }

  private restoreFromAnswerPayload(): void {
    const json = this._answerPayloadJson();
    const incoming = parseOpenTextAnswer(json);
    if (incoming === this.text()) {
      this.committed.set(incoming);
      return;
    }
    // Ebeveyn kendi yayınımızı geç geri verdiyse (öğrenci bu arada yazmaya devam etmiş olabilir) ekrandaki metin korunur.
    if (this.lastEmitted !== null && json === this.lastEmitted) return;
    this.cancelPendingSave();
    this.text.set(incoming);
    this.committed.set(incoming);
  }
}
