import { NgStyle, isPlatformBrowser } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  PLATFORM_ID,
  computed,
  forwardRef,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { filter, map } from 'rxjs';

import { QuestionRegion } from '../../../models/draws';
import { InteractionBox } from '../../../models/interaction-ids';
import {
  TEXT_SELECT_LIMITS,
  TextSelectAnswer,
  TextSelectFeedback,
  TextSelectPlanV1,
  TextSelectToken,
  buildTextSelectPayload,
  emptyTextSelectAnswer,
  gapAcceptOf,
  gradeTextSelect,
  parseTextSelectAnswer,
  parseTextSelectPlan,
  parseTextSelectSolution,
  textValueOf,
  tokensByLine,
  withSelection,
  withTextValue,
} from '../../../models/text-select';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { FLUSHABLE_PLAYER, FlushablePlayer } from '../../utils/flushable-player';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';

/** Token işareti: kontrol sonrası doğru / fazla seçim / düzeltmesi yanlış / (noktalamada) yanlış sembol. */
export type TextSelectMark = 'correct' | 'extra' | 'value' | 'wrong' | null;

export type TextSelectView = 'image' | 'text';

/** Görünüm başlangıçta bu genişlikte ve altında "Metin görünümü"dür (telefon). */
export const TEXT_SELECT_NARROW_QUERY = '(max-width: 600px)';

/** Düzeltme yazarken kayıt gecikmesi (ms); seçim / sembol hemen kaydedilir. */
export const TEXT_SELECT_SAVE_DEBOUNCE_MS = 600;

interface TokenView {
  selected: boolean;
  value: string;
  mark: TextSelectMark;
  /** Çözüm açıkken: hedef token'ı / gap'in doğru sembolü / hedefin kabul edilen düzeltmesi (hedefin ilk token'ında). */
  solution: boolean;
  solutionText: string;
}

/**
 * Issue #582: metin üzerinde işaretleme oynatıcısı (bileşen D). İki görünüm: görselin üstünde token dokunma kutuları ve
 * "Metin görünümü" (token'lar satırlarına dizilmiş dokunulabilir kelimeler; dar ekranda varsayılan, kullanıcı değiştirebilir).
 * - `select`: kelimeye dokun = seç / bırak; seçili kelime planın `style`ına göre (alt çizgi / daire / vurgu) işaretlenir.
 * - `correct`: kelimeyi seç, alttaki çubukta düzeltmesini yaz (yazarken {@link TEXT_SELECT_SAVE_DEBOUNCE_MS} sonra, alan
 *   bırakılınca hemen kaydedilir).
 * - `punctuation`: `( )` boşluğuna dokun, palet çubuğundan sembol seç (telefonda ekranın altında sabit, ≥ 44px düğmeler).
 * Kontrol sonrası: doğru yeşil, fazla seçim kırmızı "fazla", düzeltmesi yanlış turuncu; sayaç "Bulunan · Kalan · Fazla"
 * (kontrolden önce hedef sayısı gösterilmez). Çözüm açıkken hedefler / doğru semboller / kabul edilen düzeltme gösterilir.
 *
 * Girdi/çıktılar diğer etkileşim oynatıcılarıyla aynı sözleşmede; çözüm (`interactionSolutionJson`) yalnız verilirse
 * kullanılır (#570). OnPush: tüm durum signal.
 */
@Component({
  selector: 'app-question-canvas-text-select',
  standalone: true,
  imports: [
    NgStyle,
    MatButtonModule,
    MatButtonToggleModule,
    MatIconModule,
    TranslocoDirective,
    TranslocoPipe,
    StorageImageRetryDirective,
  ],
  templateUrl: './question-canvas-text-select.component.html',
  styleUrl: './question-canvas-text-select.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // Ebeveyn sınıfa değil belirtece göre sorgular (`@defer` parçası ilk pakete girmesin).
  providers: [{ provide: FLUSHABLE_PLAYER, useExisting: forwardRef(() => QuestionCanvasTextSelectComponent) }],
})
export class QuestionCanvasTextSelectComponent implements FlushablePlayer {
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    if (value !== this._region()) {
      this.cancelPendingSave();
      this.activeId.set(null);
    }
    this._region.set(value);
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this.cancelPendingSave();
    this.lastEmitted = null;
    this.activeId.set(null);
    this._plan.set(parseTextSelectPlan(value));
    this.restoreFromAnswerPayload();
  }

  /** #570: çözüm — backend yalnız örnek soruda, biten testte, çözüm istendiğinde ya da sahibe doldurur. */
  @Input() set interactionSolutionJson(value: string | null | undefined) {
    this._solutionJson.set(value ?? null);
  }

  @Input() imageRefresh: StorageImageResolver | null = null;

  /** Örnek soru: çözüm geldiyse işaretleme istemcide (sunucu kontrolünün ikizi) yapılır. */
  @Input() set practiceFeedback(value: boolean) {
    this._practiceFeedback.set(!!value);
  }

  @Input() hidePassage = false;

  @Input() set answerPayloadJson(value: string | null | undefined) {
    this._answerPayloadJson.set(value ?? null);
    this.restoreFromAnswerPayload();
  }

  /** Sunucu kontrolünün sonucu (satırlar + sayılar). Verilirse `practiceFeedback`in önüne geçer. */
  @Input() set feedback(value: TextSelectFeedback | null | undefined) {
    this._feedback.set(value ?? null);
  }

  /** true iken öğrencinin seçimi yerine çözüm gösterilir (salt okunur). */
  @Input() set showSolution(value: boolean) {
    this._showSolution.set(!!value);
    if (value) this.activeId.set(null);
  }

  /** Etkinlik oynatıcısı (daha büyük dokunma hedefleri). */
  @Input() set activityMode(value: boolean) {
    this._activityMode.set(!!value);
  }

  /** Kontrol / bitirme sürerken dokunma yok sayılır. */
  @Input() set disabled(value: boolean) {
    this._disabled.set(!!value);
  }

  @Output() answerPayloadChange = new EventEmitter<string>();
  @Output() answered = new EventEmitter<void>();

  private readonly transloco = inject(TranslocoService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly i18nTick = toSignal(
    this.transloco.events$.pipe(
      filter((e) => e.type === 'langChanged' || e.type === 'translationLoadSuccess'),
      map(() => Date.now())
    ),
    { initialValue: 0 }
  );
  private saveTimer: ReturnType<typeof setTimeout> | null = null;
  /** Son yayınlanan payload: ebeveynin geri verdiği yankı ekrandaki yeni değerin üstüne yazılmaz. */
  private lastEmitted: string | null = null;

  private readonly _region = signal<QuestionRegion | null>(null);
  private readonly _plan = signal<TextSelectPlanV1 | null>(null);
  private readonly _solutionJson = signal<string | null>(null);
  private readonly _answerPayloadJson = signal<string | null>(null);
  private readonly _feedback = signal<TextSelectFeedback | null>(null);
  private readonly _showSolution = signal(false);
  private readonly _practiceFeedback = signal(false);
  private readonly _activityMode = signal(false);
  private readonly _disabled = signal(false);

  /** Ekrandaki cevap (düzeltme yazılırken anlık). */
  readonly answer = signal<TextSelectAnswer>(emptyTextSelectAnswer());
  /** Son kaydedilen cevap (örnek sorunun istemci işareti bunu kullanır). */
  private readonly committed = signal<TextSelectAnswer>(emptyTextSelectAnswer());
  /** Düzeltme çubuğunun (correct) / palet çubuğunun (punctuation) bağlı olduğu token. */
  readonly activeId = signal<string | null>(null);
  readonly view = signal<TextSelectView>('image');

  readonly limits = TEXT_SELECT_LIMITS;
  readonly plan = this._plan.asReadonly();
  readonly activity = this._activityMode.asReadonly();
  readonly solutionShown = this._showSolution.asReadonly();
  readonly isDisabled = this._disabled.asReadonly();
  readonly mode = computed(() => this._plan()?.mode ?? 'select');
  readonly styleName = computed(() => this._plan()?.style ?? 'highlight');
  readonly tokens = computed(() => this._plan()?.tokens ?? []);
  readonly lines = computed(() => tokensByLine(this.tokens()));
  readonly gaps = computed(() => this.tokens().filter((t) => t.gap));
  readonly imageUrl = computed(() => this._region()?.imageUrl ?? null);
  readonly passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);
  readonly passageTitle = computed(() => this._region()?.passage?.title ?? null);
  readonly solution = computed(() => parseTextSelectSolution(this._solutionJson(), this._plan()));

  /** Sunucu sonucu ya da (örnek soruda) istemci kontrolü; çözüm açıkken yok. */
  readonly result = computed<TextSelectFeedback | null>(() => {
    if (this._showSolution()) return null;
    const server = this._feedback();
    if (server) return server;
    const plan = this._plan();
    if (!this._practiceFeedback() || !plan) return null;
    const committed = this.committed();
    const empty = committed.selected.length === 0 && Object.keys(committed.values).length === 0;
    return empty ? null : gradeTextSelect(plan, this.solution(), committed);
  });

  /** #679: çözüm açık ve öğrenci işaretlemiş — öğrencinin seçimleri yeşil / kırmızı, kaçırılan hedef kesikli, yanlışta doğrusu. */
  readonly compare = computed(() => {
    const answer = this.answer();
    return this._showSolution() && !!this._solutionJson() && (answer.selected.length > 0 || Object.keys(answer.values).length > 0);
  });
  /** Yalnız doğru hâl çiziliyor (çözüm açık, karşılaştırma yok). */
  readonly solutionOnly = computed(() => this._showSolution() && !this.compare());

  readonly tokenViews = computed(() => {
    const plan = this._plan();
    const views = new Map<string, TokenView>();
    if (!plan) return views;
    const answer = this.answer();
    const selected = new Set(answer.selected);
    const compare = this.compare();
    const result = compare ? gradeTextSelect(plan, this.solution(), answer) : this.result();
    const rows = new Map((result?.rows ?? []).map((row) => [row.id, row]));
    const solution = this.solution();
    const showSolution = this._showSolution() && !compare;
    const targetText = new Map<string, string>();
    const inTarget = new Set<string>();
    solution.targets.forEach((target) => {
      target.tokenIds.forEach((id) => inTarget.add(id));
      if (plan.mode === 'correct' && target.accept.length > 0) targetText.set(target.tokenIds[0], target.accept[0]);
    });
    for (const token of plan.tokens) {
      const row = rows.get(token.id);
      let mark: TextSelectMark = null;
      if (row) mark = row.correct ? 'correct' : row.reason === 'extra' ? 'extra' : row.reason === 'value' ? 'value' : 'wrong';
      if (showSolution) {
        const accept = token.gap ? gapAcceptOf(solution, token.id) : [];
        views.set(token.id, {
          selected: false,
          value: '',
          mark: null,
          solution: token.gap ? accept.length > 0 : inTarget.has(token.id),
          solutionText: token.gap ? accept.join(' / ') : targetText.get(token.id) ?? '',
        });
      } else if (compare) {
        // Kaçırılan hedef kesikli; yanlış gap'te kabul edilen sembol, yanlış düzeltmede kabul edilen düzeltme.
        const wrong = mark !== null && mark !== 'correct';
        const accept = token.gap ? gapAcceptOf(solution, token.id) : [];
        views.set(token.id, {
          selected: selected.has(token.id),
          value: textValueOf(answer, token.id),
          mark: token.gap && !mark ? 'wrong' : mark,
          solution: !token.gap && inTarget.has(token.id) && !selected.has(token.id),
          solutionText: token.gap ? (wrong || !mark ? accept.join(' / ') : '') : mark === 'value' ? (targetText.get(token.id) ?? '') : '',
        });
      } else {
        views.set(token.id, {
          selected: selected.has(token.id),
          value: textValueOf(answer, token.id),
          mark,
          solution: false,
          solutionText: '',
        });
      }
    }
    return views;
  });

  readonly activeToken = computed<TextSelectToken | null>(() => {
    const id = this.activeId();
    if (!id || this._showSolution() || this._disabled()) return null;
    const token = this.tokens().find((t) => t.id === id) ?? null;
    if (!token) return null;
    if (this.mode() === 'punctuation') return token.gap ? token : null;
    return this.mode() === 'correct' && !token.gap && this.answer().selected.includes(token.id) ? token : null;
  });

  /**
   * Kontrol sonrası sayaç; kontrolden önce yok (kitap hedef sayısını söylemez). Örnek sorunun istemci kontrolü her seçimde
   * çalıştığından select / correct'te sayaç gösterilmez (kalan sayısı hedef sayısını ele verirdi); yalnız token işaretleri.
   */
  readonly counter = computed(() => {
    this.i18nTick();
    const result = this.result();
    if (!result) return null;
    if (this.mode() !== 'punctuation' && !this._feedback()) return null;
    if (this.mode() === 'punctuation') {
      const correct = result.rows.filter((r) => r.correct).length;
      return this.t('counterPunctuation', { correct, wrong: result.rows.length - correct });
    }
    return this.t('counter', { found: result.found, missing: result.missing, extra: result.extra });
  });

  /** Fazla seçim eşiği aşıldı: sunucu işaret göndermedi. */
  readonly tooMany = computed(() => !!this.result()?.tooMany);

  readonly answeredCount = computed(() => {
    const answer = this.answer();
    return this.mode() === 'punctuation' ? Object.keys(answer.values).length : answer.selected.length;
  });

  readonly hint = computed(() => {
    this.i18nTick();
    if (this._showSolution()) return this.t(this.compare() ? 'solutionCompareHint' : 'solutionShownHint');
    return this.t(`hint.${this.mode()}`);
  });

  constructor() {
    if (isPlatformBrowser(inject(PLATFORM_ID)) && typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      this.view.set(window.matchMedia(TEXT_SELECT_NARROW_QUERY).matches ? 'text' : 'image');
    }
    // Yok edilirken yayın yapılmaz; bekleyen düzeltmeyi ebeveyn `flush()` ile alır, alan bırakılınca `change` kaydeder.
    inject(DestroyRef).onDestroy(() => this.cancelPendingSave());
  }

  setView(view: TextSelectView): void {
    this.view.set(view === 'text' ? 'text' : 'image');
  }

  viewOf(token: TextSelectToken): TokenView {
    return (
      this.tokenViews().get(token.id) ?? { selected: false, value: '', mark: null, solution: false, solutionText: '' }
    );
  }

  /** Plan koordinatları soru görseli pikselinde; kaplama görselle birlikte ölçeklenir. */
  boxStyle(box: InteractionBox): Record<string, string> {
    const region = this._region();
    if (!region || !region.width || !region.height) return { display: 'none' };
    return {
      left: `${(box.x / region.width) * 100}%`,
      top: `${(box.y / region.height) * 100}%`,
      width: `${(box.width / region.width) * 100}%`,
      height: `${(box.height / region.height) * 100}%`,
    };
  }

  /** Gap'te görünen içerik: çözüm / öğrencinin sembolü ya da boş parantez. */
  gapLabel(token: TextSelectToken): string {
    const view = this.viewOf(token);
    const inner = this.solutionOnly() ? view.solutionText : view.value;
    return inner ? `( ${inner} )` : '(   )';
  }

  gapNo(token: TextSelectToken): number {
    return this.gaps().findIndex((g) => g.id === token.id) + 1;
  }

  tokenAria(token: TextSelectToken): string {
    this.i18nTick();
    const view = this.viewOf(token);
    const parts: string[] = [];
    if (token.gap) {
      parts.push(this.t('gapAria', { n: this.gapNo(token) }));
      const shown = this.solutionOnly() ? view.solutionText : view.value;
      parts.push(shown ? this.t('gapValue', { value: shown }) : this.t('gapEmpty'));
      if (this.compare() && view.solutionText) parts.push(this.t('acceptedCorrection', { value: view.solutionText }));
    } else {
      parts.push(token.text);
      if (this.compare()) {
        if (view.selected) parts.push(this.t('selected'));
        if (view.solution) parts.push(this.t('solutionTarget'));
        if (view.solutionText) parts.push(this.t('acceptedCorrection', { value: view.solutionText }));
      } else if (this._showSolution()) {
        if (view.solution) parts.push(this.t('solutionTarget'));
        if (view.solutionText) parts.push(this.t('acceptedCorrection', { value: view.solutionText }));
      } else {
        if (view.selected) parts.push(this.t('selected'));
        if (view.selected && this.mode() === 'correct') {
          parts.push(view.value ? this.t('correctionValue', { value: view.value }) : this.t('correctionEmpty'));
        }
      }
    }
    if (view.mark) parts.push(this.t(`mark.${view.mark}`));
    return parts.join(', ');
  }

  isPressed(token: TextSelectToken): boolean | null {
    if (token.gap) return null;
    return this.viewOf(token).selected;
  }

  /** Token'a dokunma / Enter. */
  onToken(token: TextSelectToken): void {
    const plan = this._plan();
    if (!plan || this._showSolution() || this._disabled()) return;
    if (plan.mode === 'punctuation') {
      if (!token.gap) return;
      this.activeId.set(this.activeId() === token.id ? null : token.id);
      return;
    }
    if (token.gap) return;
    const selected = this.answer().selected.includes(token.id);
    if (plan.mode === 'select') {
      this.setAnswer(withSelection(this.answer(), plan, token.id, !selected));
      this.commit();
      return;
    }
    // correct: seçili değilse seç ve düzeltme çubuğunu aç; seçili ama çubuk başka kelimedeyse çubuğu buraya al; çubuğu açık
    // seçili kelimeye yeniden dokunma seçimi bırakır (düzeltmesi de silinir).
    if (!selected) {
      this.setAnswer(withSelection(this.answer(), plan, token.id, true));
      this.activeId.set(token.id);
      this.commit();
      this.focusCorrection();
    } else if (this.activeId() !== token.id) {
      this.activeId.set(token.id);
      this.focusCorrection();
    } else {
      this.deselect(token.id);
    }
  }

  /** Düzeltme çubuğundaki "Seçimi kaldır". */
  deselect(tokenId: string): void {
    const plan = this._plan();
    if (!plan || this._showSolution() || this._disabled()) return;
    this.setAnswer(withSelection(this.answer(), plan, tokenId, false));
    if (this.activeId() === tokenId) this.activeId.set(null);
    this.commit();
  }

  /** Düzeltme yazılırken: ekranda hemen, kayıt duraksayınca. */
  onCorrectionInput(tokenId: string, event: Event): void {
    this.setValue(tokenId, (event.target as HTMLInputElement | null)?.value ?? '');
    this.scheduleSave();
  }

  /** Alan bırakıldı / Enter: kaydedilir. */
  onCorrectionCommit(tokenId: string, event?: Event): void {
    if (event) this.setValue(tokenId, (event.target as HTMLInputElement | null)?.value ?? '');
    this.commit();
  }

  /** Palet sembolü: gap'e koyar (aynı sembol yeniden = boşalt), kaydeder ve sıradaki boş gap'e geçer. */
  chooseSymbol(gapId: string, symbol: string): void {
    if (this._showSolution() || this._disabled()) return;
    const current = textValueOf(this.answer(), gapId);
    const next = current === symbol ? '' : symbol;
    this.setValue(gapId, next);
    this.commit();
    if (next) {
      const gaps = this.gaps();
      const index = gaps.findIndex((g) => g.id === gapId);
      const following = [...gaps.slice(index + 1), ...gaps.slice(0, index)].find((g) => !textValueOf(this.answer(), g.id));
      this.activeId.set(following?.id ?? null);
    }
  }

  clearGap(gapId: string): void {
    if (this._showSolution() || this._disabled()) return;
    this.setValue(gapId, '');
    this.commit();
  }

  closeBar(): void {
    this.commit();
    this.activeId.set(null);
  }

  clearAll(): void {
    if (this._showSolution() || this._disabled()) return;
    this.answer.set(emptyTextSelectAnswer());
    this.activeId.set(null);
    this.commit();
  }

  /** Bekleyen (yazılmış ama kaydedilmemiş) düzeltmeyi hemen yayınlar. Kontrol / bitir / teslim öncesi çağrılır. */
  flush(): void {
    this.commit();
  }

  private setAnswer(next: TextSelectAnswer): void {
    this.answer.set(next);
  }

  private setValue(id: string, value: string): void {
    if (this._showSolution() || !this.tokens().some((t) => t.id === id)) return;
    this.answer.set(withTextValue(this.answer(), id, value));
  }

  private focusCorrection(): void {
    queueMicrotask(() => this.host.nativeElement.querySelector<HTMLInputElement>('[data-testid="ts-correction"]')?.focus());
  }

  private scheduleSave(): void {
    this.cancelPendingSave();
    this.saveTimer = setTimeout(() => {
      this.saveTimer = null;
      this.commit();
    }, TEXT_SELECT_SAVE_DEBOUNCE_MS);
  }

  private cancelPendingSave(): void {
    if (this.saveTimer !== null) clearTimeout(this.saveTimer);
    this.saveTimer = null;
  }

  private commit(): void {
    this.cancelPendingSave();
    const payload = buildTextSelectPayload(this.answer());
    if (payload === buildTextSelectPayload(this.committed())) return;
    this.committed.set(this.answer());
    this.lastEmitted = payload;
    this.answerPayloadChange.emit(payload);
    this.answered.emit();
  }

  private restoreFromAnswerPayload(): void {
    const answer = parseTextSelectAnswer(this._answerPayloadJson(), this._plan());
    const incoming = buildTextSelectPayload(answer);
    if (incoming === buildTextSelectPayload(this.answer())) {
      this.committed.set(this.answer());
      return;
    }
    // Ebeveyn kendi yayınımızı geç geri verdiyse (öğrenci bu arada yazmaya devam etmiş olabilir) ekrandaki değer korunur.
    if (this.lastEmitted !== null && incoming === this.lastEmitted) return;
    this.cancelPendingSave();
    this.answer.set(answer);
    this.committed.set(answer);
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`shared.questionCanvas.textSelect.${key}`, params) ?? '';
  }
}
