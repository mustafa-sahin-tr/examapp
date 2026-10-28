import { NgStyle, NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  EventEmitter,
  Injector,
  Input,
  Output,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { CdkDrag, CdkDragDrop, CdkDropList, DragDropModule } from '@angular/cdk/drag-drop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { filter, map } from 'rxjs';

import { ActivityFeedbackRow } from '../../../models/activity';
import { QuestionRegion } from '../../../models/draws';
import { LayoutSegment, splitPlaceholders } from '../../../models/interaction-layout';
import {
  DragPlaceCard,
  DragPlaceMap,
  DragPlacePlanV1,
  DragPlaceTarget,
  buildDragPlacePayload,
  cardsIn,
  dragPlaceRowId,
  emptyDragPlaceMap,
  parseDragPlacePlacements,
  parseDragPlacePlan,
  parseDragPlaceSolution,
  placementCount,
  withCards,
} from '../../../models/drag-place';
import { InteractionBox } from '../../../models/interaction-ids';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';
import { QuestionImageCropComponent } from '../question-image-crop/question-image-crop.component';
import { seededShuffle } from '../../utils/seeded-shuffle';

/** Dokunmatik / dar ekranda sürükleme kapalı, yalnız dokun-dokun (#573 ile aynı kural). */
const TAP_ONLY_QUERY = '(pointer: coarse), (max-width: 640px)';
/** Havuz / sıra listesinin cdk id'leri; hedefler `dp-target-<id>`. */
const POOL = 'pool';
const SEQUENCE = 'sequence';
/** Sayfada birden çok oynatıcı olabilir (test ekranı): cdk liste id'leri bileşen başına ayrı. */
let nextInstance = 0;

export type PlacementMark = 'correct' | 'wrong' | null;

/** Dokun-dokun / klavye seçimi: seçilen kart (havuzdan). */
interface CardSelection {
  cardId: string;
}

/** Sürüklenen kartın verisi: kart ve nereden alındığı (`pool`, `sequence` ya da hedef id'si). */
export interface DragPlaceDragData {
  cardId: string;
  from: string;
  /** Sıra modunda listedeki konum. */
  index?: number;
}

/**
 * Issue #576: sürükle-yerleştir oynatıcısı. `place` modunda kartlar havuzdan görsel üstündeki bırakma alanlarına
 * (kutulu hedef) ya da görselin altındaki kovalara (kutusuz hedef) taşınır; hedef kapasitesi kadar kart alır, tekrar
 * kullanımda kart havuzda kalır. `order` modunda kartlar bir sıra listesine eklenir, sürükleyerek ya da yukarı/aşağı ile
 * sıralanır. Her yerde dokun-dokun (kartı seç, hedefe dokun; yerleşmiş karta dokununca havuza döner) ve klavye
 * (Enter/Boşluk seç, Tab/oklarla hedefe git, Enter bırak, Esc vazgeç) çalışır; dokunmatik / dar ekranda sürükleme kapalı.
 *
 * Kontrolden sonra (`feedbackRows`, satır id = `hedef:kart`) yerleşen her kart ✓/✗ alır; yanlış kartın "Havuza geri al"
 * düğmesi vardır. Girdi/çıktılar diğer etkileşim oynatıcılarıyla aynı sözleşmede (#570: çözüm yalnız verilirse).
 */
@Component({
  selector: 'app-question-canvas-drag-place',
  standalone: true,
  imports: [
    NgStyle,
    NgTemplateOutlet,
    DragDropModule,
    MatButtonModule,
    MatIconModule,
    TranslocoDirective,
    TranslocoPipe,
    StorageImageRetryDirective,
    QuestionImageCropComponent,
  ],
  templateUrl: './question-canvas-drag-place.component.html',
  styleUrl: './question-canvas-drag-place.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionCanvasDragPlaceComponent {
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    if (value !== this._region()) {
      this.selected.set(null);
      this.announcement.set(null);
    }
    this._region.set(value);
  }

  @Input({ required: true }) set interactionPlanJson(value: string | null | undefined) {
    this.lastEmitted = null;
    this.selected.set(null);
    this.announcement.set(null);
    this.pendingTarget.set(null);
    this._plan.set(parseDragPlacePlan(value));
    this.placed.set(emptyDragPlaceMap());
    this.restoreFromAnswerPayload();
  }

  /** #570: çözüm (`{"t1":["c1"]}`) — backend yalnız örnek soruda, biten testte, çözüm istendiğinde ya da sahibe doldurur. */
  @Input() set interactionSolutionJson(value: string | null | undefined) {
    this._solutionJson.set(value ?? null);
  }

  @Input() imageRefresh: StorageImageResolver | null = null;

  /** Örnek soru: çözüm geldiyse yerleşen kartlar istemcide işaretlenir. */
  @Input() set practiceFeedback(value: boolean) {
    this._practiceFeedback.set(!!value);
  }

  @Input() hidePassage = false;

  @Input() set answerPayloadJson(value: string | null | undefined) {
    this._answerPayloadJson.set(value ?? null);
    this.restoreFromAnswerPayload();
  }

  /** Sunucu kontrolünün yerleşim sonucu (satır id = `hedef:kart`). Verilirse `practiceFeedback`in önüne geçer. */
  @Input() set feedbackRows(value: ActivityFeedbackRow[] | null | undefined) {
    this._feedback.set(value ? new Map(value.map((row) => [row.id, row.correct])) : null);
  }

  /** true iken öğrencinin yerleşimi yerine doğru yerleşim gösterilir (salt okunur). */
  @Input() set showSolution(value: boolean) {
    this._showSolution.set(!!value);
    if (value) {
      this.selected.set(null);
      this.pendingTarget.set(null);
    }
  }

  /** Etkinlik oynatıcısı (daha büyük dokunma hedefleri). */
  @Input() set activityMode(value: boolean) {
    this._activityMode.set(!!value);
  }

  /**
   * Ebeveyn kilidi (etkinlikte kontrol / bitirme sürerken): etkileşim kapalı, seçim düşer — gönderilen cevap ile ekran
   * ayrışmasın.
   */
  @Input() set disabled(value: boolean) {
    this._disabled.set(!!value);
    if (value) {
      this.selected.set(null);
      this.pendingTarget.set(null);
    }
  }

  /**
   * Havuz sırasını karıştırma tohumu (ör. oturum maddesi id'si): aynı maddede sıra sabit kalır, plan sırası cevabı
   * ele vermez. Verilmezse plandan türetilir.
   */
  @Input() set shuffleSeed(value: string | number | null | undefined) {
    this._shuffleSeed.set(value ?? null);
  }

  @Output() answerPayloadChange = new EventEmitter<string>();
  @Output() answered = new EventEmitter<void>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly transloco = inject(TranslocoService);
  private readonly i18nTick = toSignal(
    this.transloco.events$.pipe(
      filter((e) => e.type === 'langChanged' || e.type === 'translationLoadSuccess'),
      map(() => Date.now())
    ),
    { initialValue: 0 }
  );

  private readonly _region = signal<QuestionRegion | null>(null);
  private readonly _plan = signal<DragPlacePlanV1 | null>(null);
  private readonly _solutionJson = signal<string | null>(null);
  private readonly _answerPayloadJson = signal<string | null>(null);
  private readonly _feedback = signal<Map<string, boolean> | null>(null);
  private readonly _showSolution = signal(false);
  private readonly _practiceFeedback = signal(false);
  private readonly _activityMode = signal(false);
  private readonly coarsePointer = signal(false);
  private readonly _disabled = signal(false);
  private readonly _shuffleSeed = signal<string | number | null>(null);
  private readonly injector = inject(Injector);
  /** Son yayınlanan payload: ebeveynin geri verdiği yankı ekrandaki yeni yerleşimin üstüne yazılmaz. */
  private lastEmitted: string | null = null;
  /** Sürükleme bırakıldıktan hemen sonra gelen `click` seçimi / havuza almayı tetiklemesin. */
  private suppressNextClick = false;

  /** Öğrencinin yerleşimi. */
  readonly placed = signal<DragPlaceMap>(emptyDragPlaceMap());
  readonly selected = signal<CardSelection | null>(null);
  /** Önce hedef seçildiyse (kart seçilmeden): sonra dokunulan havuz kartı buraya konur. */
  readonly pendingTarget = signal<string | null>(null);
  private readonly announcement = signal<string | null>(null);

  readonly plan = this._plan.asReadonly();
  readonly activity = this._activityMode.asReadonly();
  readonly solutionShown = this._showSolution.asReadonly();
  readonly tapOnly = this.coarsePointer.asReadonly();
  /** Etkileşim kapalı: çözüm gösteriliyor ya da ebeveyn kilitledi. */
  readonly locked = computed(() => this._showSolution() || this._disabled());
  readonly dragDisabled = computed(() => this.coarsePointer() || this.locked());
  readonly region = this._region.asReadonly();
  readonly imageUrl = computed(() => this._region()?.imageUrl ?? null);
  readonly passageImageUrl = computed(() => this._region()?.passage?.imageUrl ?? null);
  readonly passageTitle = computed(() => this._region()?.passage?.title ?? null);
  readonly orderMode = computed(() => this._plan()?.mode === 'order');
  readonly reuse = computed(() => this._plan()?.reuse === true);
  readonly targets = computed(() => this._plan()?.targets ?? []);
  readonly cards = computed(() => this._plan()?.cards ?? []);
  /** Görsel üstündeki bırakma alanları (yerleştirme modunda kutulu hedefler). */
  readonly zoneTargets = computed(() => (this.orderMode() ? [] : this.targets().filter((t) => !!t.box)));
  /**
   * Görselin altındaki gruplar: yerleştirme modunda tüm hedefler (kutusuz hedef = kova; kutulu hedefin kartları da burada,
   * "Yerleşimler" tepsisi — görsel üstündeki küçük alan yalnız özet gösterir).
   */
  readonly groupTargets = computed(() => (this.orderMode() || this.clozeMode() ? [] : this.targets()));
  /** Issue #590 (dilim b): boşluklu metin — hedefler metnin içindeki boşluklar, kartlar kelime havuzu. */
  readonly clozeSegments = computed<LayoutSegment[]>(() => {
    const layout = this._plan()?.layout;
    return layout?.kind === 'text' && !this.orderMode() ? splitPlaceholders(layout.text) : [];
  });
  readonly clozeMode = computed(() => this.clozeSegments().length > 0);
  readonly solution = computed(() => parseDragPlaceSolution(this._solutionJson(), this._plan()));

  /** Kart numarası ("Kart n") ekrandaki karışık sıraya göre. */
  private readonly cardIndex = computed(() => new Map(this.shuffledCards().map((c, i) => [c.id, { card: c, index: i }])));
  private readonly targetIndex = computed(() => new Map(this.targets().map((t, i) => [t.id, { target: t, index: i }])));

  /** Ekrandaki yerleşim: çözüm açıkken doğru yerleşim. */
  readonly displayMap = computed<DragPlaceMap>(() => (this._showSolution() ? this.solution() : this.placed()));

  /** Kartların ekran sırası: tohuma göre karıştırılmış, aynı maddede sabit. */
  private readonly shuffledCards = computed<DragPlaceCard[]>(() => {
    const cards = this.cards();
    const seed = this._shuffleSeed() ?? cards.map((c) => c.id).join('|');
    return seededShuffle(cards, `${seed}`);
  });

  /** Havuz: tekrar kullanımda tüm kartlar, değilse yerleşmemiş kartlar (karışık sırada). */
  readonly poolCards = computed<DragPlaceCard[]>(() => {
    const cards = this.shuffledCards();
    if (this.reuse()) return cards;
    const shown = this.displayMap();
    const used = new Set(Object.keys(shown).flatMap((key) => cardsIn(shown, key)));
    return cards.filter((c) => !used.has(c.id));
  });

  /** Sıra modunda listedeki kartlar (hedef sırasıyla, boşluksuz). */
  readonly sequence = computed<string[]>(() => {
    const shown = this.displayMap();
    return this.targets()
      .map((t) => cardsIn(shown, t.id)[0])
      .filter((id): id is string => !!id);
  });

  private readonly uid = `dp${++nextInstance}`;
  readonly poolListId = `${this.uid}-pool`;
  readonly sequenceListId = `${this.uid}-sequence`;
  /**
   * Sıra listesi öğeleri ve kalıcı anahtar (kart id'si + kaçıncı kez geçtiği): taşımada `<li>` yeniden kurulmaz, odak kalır.
   */
  readonly sequenceItems = computed(() => {
    const seen = new Map<string, number>();
    return this.sequence().map((cardId) => {
      const n = (seen.get(cardId) ?? 0) + 1;
      seen.set(cardId, n);
      return { cardId, key: `${cardId}#${n}` };
    });
  });

  readonly targetListIds = computed(() => [
    ...this.zoneTargets().map((t) => this.listId(t.id)),
    ...this.groupTargets().map((t) => this.groupListId(t)),
    ...(this.clozeMode() ? this.targets().map((t) => this.listId(t.id)) : []),
  ]);
  readonly poolConnectedTo = computed(() => (this.orderMode() ? [this.sequenceListId] : this.targetListIds()));
  readonly targetConnectedTo = computed(() => [this.poolListId, ...this.targetListIds()]);
  readonly sequenceConnectedTo = computed(() => [this.poolListId]);
  readonly placedCount = computed(() => placementCount(this.placed()));

  /** Yerleşen kartın işareti: sunucu kontrolü, yoksa örnek soruda çözümle karşılaştırma. */
  readonly pairMark = computed(() => {
    const feedback = this._feedback();
    const showSolution = this._showSolution();
    const practice = this._practiceFeedback();
    const solution = this.solution();
    const hasSolution = Object.keys(solution).length > 0;
    return (targetId: string, cardId: string): PlacementMark => {
      if (showSolution) return null;
      if (feedback) {
        const key = dragPlaceRowId(targetId, cardId);
        if (!feedback.has(key)) return null;
        return feedback.get(key) ? 'correct' : 'wrong';
      }
      if (!practice || !hasSolution) return null;
      return cardsIn(solution, targetId).includes(cardId) ? 'correct' : 'wrong';
    };
  });

  /**
   * Hedefte beklenen kartlardan en az biri eksik mi (sunucu satırı `"<hedef>:"`, kart kısmı boş). Hangi kartın eksik olduğu
   * hiçbir yerde hesaplanmaz / gösterilmez.
   */
  readonly targetIncomplete = computed(() => {
    const feedback = this._feedback();
    const showSolution = this._showSolution();
    return (targetId: string): boolean =>
      !showSolution && !!feedback && feedback.get(dragPlaceRowId(targetId, '')) === false;
  });

  /**
   * Hedefin özet durumu (görsel üstü alan + tepsi): yanlış yerleşim varsa `wrong`, yoksa eksik kart (`"t:"`) `incomplete`,
   * yerleşenlerin hepsi doğruysa `correct`; işaret yoksa null.
   */
  readonly targetState = computed(() => {
    const mark = this.pairMark();
    const incomplete = this.targetIncomplete();
    const shown = this.displayMap();
    return (targetId: string): 'correct' | 'wrong' | 'incomplete' | null => {
      const marks = cardsIn(shown, targetId).map((cardId) => mark(targetId, cardId));
      if (marks.includes('wrong')) return 'wrong';
      if (incomplete(targetId)) return 'incomplete';
      return marks.length > 0 && marks.every((m) => m === 'correct') ? 'correct' : null;
    };
  });

  readonly hint = computed(() => {
    this.i18nTick();
    const announcement = this.announcement();
    if (announcement) return announcement;
    if (this._showSolution()) return this.t('solutionShownHint');
    if (this.orderMode()) return this.t('hintOrder');
    const selection = this.selected();
    if (selection) return this.t('hintSelected', { card: this.cardText(selection.cardId) });
    const pending = this.pendingTarget();
    const pendingTarget = pending ? this.targetIndex().get(pending)?.target : null;
    if (pendingTarget) return this.t('hintTargetSelected', { target: this.targetText(pendingTarget) });
    return this.t(this.coarsePointer() ? 'hintTap' : 'hintIdle');
  });

  constructor() {
    if (typeof window !== 'undefined' && typeof window.matchMedia === 'function') {
      const query = window.matchMedia(TAP_ONLY_QUERY);
      this.coarsePointer.set(query.matches);
      const listener = (event: MediaQueryListEvent) => this.coarsePointer.set(event.matches);
      query.addEventListener('change', listener);
      inject(DestroyRef).onDestroy(() => query.removeEventListener('change', listener));
    }
  }

  // ---------------------------------------------------------------------------
  // Görünüm yardımcıları
  // ---------------------------------------------------------------------------

  listId(targetId: string): string {
    return `${this.uid}-target-${targetId}`;
  }

  /** Grup (tepsi / kova) listesinin cdk id'si; kutulu hedefin görsel üstü alanıyla çakışmasın. */
  groupListId(target: DragPlaceTarget): string {
    return target.box ? `${this.listId(target.id)}-tray` : this.listId(target.id);
  }

  /** Görsel üstü alanın ipucu metni (fare üstünde): hedef adı + kartlar. */
  zoneTitle(target: DragPlaceTarget): string {
    const cards = this.cardsOf(target.id).map((id) => this.cardText(id));
    return cards.length ? `${this.targetText(target)}: ${cards.join(', ')}` : this.targetText(target);
  }

  /** Sıra modunda i. konumun adı (hedef etiketi ya da "n. sıra"). */
  positionText(index: number): string {
    const target = this.targets()[index];
    return target ? this.targetText(target) : `${index + 1}`;
  }

  /** Sıra modunda i. konumdaki kartın işareti. */
  sequenceMark(index: number, cardId: string): PlacementMark {
    const target = this.targets()[index];
    return target ? this.pairMark()(target.id, cardId) : null;
  }

  /** Sıra modunda boş kalan konumlar (yer tutucu). */
  readonly emptyPositions = computed(() => {
    const filled = this.sequence().length;
    return this.targets()
      .map((_, i) => i)
      .filter((i) => i >= filled);
  });

  cardsOf(targetId: string): string[] {
    return cardsIn(this.displayMap(), targetId);
  }

  card(cardId: string): DragPlaceCard | null {
    return this.cardIndex().get(cardId)?.card ?? null;
  }

  /** Kartın metni: etiket, yoksa "Kart n". */
  cardText(cardId: string): string {
    this.i18nTick();
    const entry = this.cardIndex().get(cardId);
    if (!entry) return cardId;
    return entry.card.label?.trim() || this.t('cardFallback', { n: entry.index + 1 });
  }

  /** Hedefin metni: etiket, yoksa "Hedef n" / "n. sıra" / "n. boşluk". */
  targetText(target: DragPlaceTarget): string {
    this.i18nTick();
    const index = this.targetIndex().get(target.id)?.index ?? 0;
    if (target.label?.trim()) return target.label.trim();
    const fallback = this.orderMode() ? 'positionFallback' : this.clozeMode() ? 'blankFallback' : 'targetFallback';
    return this.t(fallback, { n: index + 1 });
  }

  /** Boşluklu metinde yer tutucunun hedefi (plan dışı id = null, çizilmez). */
  clozeTarget(id: string): DragPlaceTarget | null {
    return this.targetIndex().get(id)?.target ?? null;
  }

  /** Boşluğun numarası (plan sırası, 1'den). */
  blankNumber(target: DragPlaceTarget): number {
    return (this.targetIndex().get(target.id)?.index ?? 0) + 1;
  }

  targetAria(target: DragPlaceTarget): string {
    const count = this.cardsOf(target.id).length;
    let base = this.t('targetAria', { name: this.targetText(target), count, capacity: target.capacity });
    if (this.targetIncomplete()(target.id)) base = `${base}, ${this.t('targetIncomplete')}`;
    return this.selected() ? `${base}. ${this.t('targetDropAria')}` : base;
  }

  placedCardAria(targetId: string, cardId: string): string {
    const target = this.targetIndex().get(targetId)?.target;
    const base = this.t('placedCardAria', { card: this.cardText(cardId), target: target ? this.targetText(target) : targetId });
    const mark = this.pairMark()(targetId, cardId);
    return mark ? `${base}, ${this.t(mark === 'correct' ? 'markCorrect' : 'markWrong')}` : base;
  }

  isFull(target: DragPlaceTarget): boolean {
    return this.cardsOf(target.id).length >= target.capacity;
  }

  isSelected(cardId: string): boolean {
    return this.selected()?.cardId === cardId;
  }

  /** Plan koordinatları soru görseli pikselinde (region.width/height); kaplama görselle birlikte ölçeklenir. */
  boxStyle(box: InteractionBox | null | undefined): Record<string, string> {
    const region = this._region();
    if (!region || !box || !region.width || !region.height) return { display: 'none' };
    return {
      left: `${(box.x / region.width) * 100}%`,
      top: `${(box.y / region.height) * 100}%`,
      width: `${(box.width / region.width) * 100}%`,
      height: `${(box.height / region.height) * 100}%`,
    };
  }

  dragData(cardId: string, from: string, index?: number): DragPlaceDragData {
    return { cardId, from, index };
  }

  /** Kapasitesi dolu (ve tek kartlık olmayan) hedefe sürükleme girmez. */
  readonly canEnter = (drag: CdkDrag<DragPlaceDragData>, drop: CdkDropList<string>): boolean => {
    const targetId = drop.data;
    const target = this.targetIndex().get(targetId)?.target;
    if (!target) return true;
    const data = drag.data;
    if (data?.from === targetId) return true;
    return target.capacity === 1 || this.cardsIn(targetId).length < target.capacity;
  };

  // ---------------------------------------------------------------------------
  // Etkileşim
  // ---------------------------------------------------------------------------

  onDragStarted(): void {
    this.suppressNextClick = true;
    this.selected.set(null);
  }

  /** Bırakmadan sonra tarayıcı `click` üretmezse bayrak sonraki gerçek dokunuşu yutmasın. */
  onDragEnded(): void {
    setTimeout(() => (this.suppressNextClick = false));
  }

  /** Havuzdaki karta dokunma / Enter: seç (aynı karta tekrar = vazgeç); sıra modunda sıranın sonuna ekle. */
  onPoolCard(cardId: string, event: Event): void {
    event.stopPropagation();
    if (event instanceof KeyboardEvent) event.preventDefault();
    if (this.consumeSuppressedClick() || this.locked()) return;
    if (this.orderMode()) {
      this.insertInSequence(cardId, this.sequence().length);
      return;
    }
    const pending = this.pendingTarget();
    if (pending) {
      this.pendingTarget.set(null);
      this.place(cardId, POOL, pending);
      return;
    }
    if (this.selected()?.cardId === cardId) {
      this.cancelSelection();
      return;
    }
    this.selected.set({ cardId });
    this.announcement.set(null);
    // Klavyeyle seçildiyse odak ilk hedefe geçer (Tab / oklarla diğerlerine).
    if (event instanceof KeyboardEvent) this.focusTarget(0);
  }

  /** Hedefe dokunma / Enter: seçili kartı buraya bırakır. */
  onTarget(target: DragPlaceTarget, event?: Event): void {
    event?.preventDefault();
    event?.stopPropagation();
    if (this.consumeSuppressedClick() || this.locked()) return;
    const selection = this.selected();
    if (!selection) {
      // Önce hedef: seçilir (aynı hedefe tekrar = vazgeç), sonra dokunulan kart buraya konur.
      const same = this.pendingTarget() === target.id;
      this.pendingTarget.set(same ? null : target.id);
      this.announcement.set(same ? this.t('announceCancelled') : null);
      return;
    }
    this.place(selection.cardId, POOL, target.id);
  }

  /** Yerleşmiş karta dokunma: seçim varsa o hedefe bırakır, yoksa kartı havuza geri alır. */
  onPlacedCard(targetId: string, cardId: string, event: Event): void {
    event.stopPropagation();
    if (event instanceof KeyboardEvent) event.preventDefault();
    if (this.consumeSuppressedClick() || this.locked()) return;
    const selection = this.selected();
    const target = this.targetIndex().get(targetId)?.target;
    if (selection && target && selection.cardId !== cardId) {
      this.place(selection.cardId, POOL, targetId);
      return;
    }
    this.returnToPool(targetId, cardId);
  }

  /** Kontrolden sonra yanlış kartın "Havuza geri al" düğmesi. */
  onReturn(targetId: string, cardId: string, event: Event): void {
    event.stopPropagation();
    if (this.locked()) return;
    this.returnToPool(targetId, cardId);
  }

  /** Hedef düğmelerinde oklar: önceki / sonraki hedefe odak (ekran sırasıyla). */
  onTargetKeydown(event: KeyboardEvent): void {
    const key = event.key;
    const step = key === 'ArrowRight' || key === 'ArrowDown' ? 1 : key === 'ArrowLeft' || key === 'ArrowUp' ? -1 : 0;
    if (!step) return;
    event.preventDefault();
    const buttons = this.targetButtons();
    const index = buttons.indexOf(event.currentTarget as HTMLElement);
    this.focusTarget((index < 0 ? 0 : index) + step);
  }

  cancelSelection(): void {
    if (!this.selected() && !this.pendingTarget()) return;
    this.selected.set(null);
    this.pendingTarget.set(null);
    this.announcement.set(this.t('announceCancelled'));
  }

  /** cdk bırakma: hedefe / havuza / sıraya. */
  onDrop(event: CdkDragDrop<string, string, DragPlaceDragData>): void {
    this.selected.set(null);
    if (this.locked()) return;
    const data = event.item.data;
    if (!data) return;
    const to = event.container.data;
    if (to === SEQUENCE) {
      if (data.from === SEQUENCE) this.moveInSequence(data.index ?? event.previousIndex, event.currentIndex);
      else this.insertInSequence(data.cardId, event.currentIndex);
      return;
    }
    if (to === POOL) {
      if (data.from === SEQUENCE) this.removeFromSequence(data.index ?? event.previousIndex);
      else if (data.from !== POOL) this.returnToPool(data.from, data.cardId);
      return;
    }
    if (data.from === to) return;
    this.place(data.cardId, data.from, to);
  }

  /** Sıra modunda yukarı / aşağı. */
  moveBy(index: number, delta: number): void {
    if (this.locked()) return;
    this.moveInSequence(index, index + delta);
    const to = Math.max(0, Math.min(index + delta, this.sequence().length - 1));
    afterNextRender(() => this.focusSequenceControl(to, delta < 0 ? 'up' : 'down'), { injector: this.injector });
  }

  removeAt(index: number): void {
    if (this.locked()) return;
    this.removeFromSequence(index);
  }

  clearAll(): void {
    if (this.locked()) return;
    this.selected.set(null);
    this.commit(emptyDragPlaceMap());
    this.announcement.set(this.t('announceCleared'));
  }

  // ---------------------------------------------------------------------------
  // Durum
  // ---------------------------------------------------------------------------

  private cardsIn(targetId: string): string[] {
    return cardsIn(this.placed(), targetId);
  }

  /**
   * Kartı hedefe koyar. `from` hedefse taşır. Tek kartlık dolu hedefte eski kart kaynağa (hedeften taşınıyorsa) ya da havuza
   * döner; çok kartlık dolu hedefe konmaz. Tekrar kullanım kapalıyken kart başka yerdeyse oradan alınır.
   */
  private place(cardId: string, from: string, targetId: string): void {
    const target = this.targetIndex().get(targetId)?.target;
    if (!target || !this.cardIndex().has(cardId)) return;
    let next = this.placed();
    const fromTarget = from !== POOL && from !== SEQUENCE && this.targetIndex().has(from) ? from : null;
    if (fromTarget) next = withCards(next, fromTarget, cardsIn(next, fromTarget).filter((id) => id !== cardId));
    if (!this.reuse()) {
      Object.keys(next).forEach((key) => {
        if (key !== targetId) next = withCards(next, key, cardsIn(next, key).filter((id) => id !== cardId));
      });
    }
    let current = cardsIn(next, targetId);
    if (current.includes(cardId)) {
      this.selected.set(null);
      this.commit(next);
      return;
    }
    if (current.length >= target.capacity) {
      if (target.capacity !== 1) {
        this.announcement.set(this.t('targetFull', { name: this.targetText(target) }));
        return;
      }
      const occupant = current[0];
      current = [];
      // Hedefler arası taşımada yer değiştirir; havuzdan gelince eski kart havuza döner.
      // Kaynak hedefte (tekrar kullanımda) aynı kart zaten varsa çift olmasın: eski kart havuza döner.
      if (fromTarget && occupant && !cardsIn(next, fromTarget).includes(occupant)) {
        next = withCards(next, fromTarget, [...cardsIn(next, fromTarget), occupant]);
      }
    }
    next = withCards(next, targetId, [...current, cardId]);
    this.selected.set(null);
    this.pendingTarget.set(null);
    this.announcement.set(this.t('announcePlaced', { card: this.cardText(cardId), target: this.targetText(target) }));
    this.commit(next);
  }

  private returnToPool(targetId: string, cardId: string): void {
    const current = this.cardsIn(targetId);
    if (!current.includes(cardId)) return;
    this.selected.set(null);
    this.announcement.set(this.t('announceReturned', { card: this.cardText(cardId) }));
    this.commit(withCards(this.placed(), targetId, current.filter((id) => id !== cardId)));
  }

  private setSequence(sequence: string[]): void {
    let next = emptyDragPlaceMap();
    this.targets().forEach((target, i) => {
      if (sequence[i]) next = withCards(next, target.id, [sequence[i]]);
    });
    this.commit(next);
  }

  private insertInSequence(cardId: string, index: number): void {
    const sequence = [...this.sequence()];
    if (!this.cardIndex().has(cardId)) return;
    if (sequence.length >= this.targets().length) {
      this.announcement.set(this.t('sequenceFull'));
      return;
    }
    if (!this.reuse() && sequence.includes(cardId)) return;
    const at = Math.max(0, Math.min(index, sequence.length));
    sequence.splice(at, 0, cardId);
    this.announcement.set(this.t('announceMoved', { card: this.cardText(cardId), n: at + 1 }));
    this.setSequence(sequence);
  }

  private moveInSequence(from: number, to: number): void {
    const sequence = [...this.sequence()];
    if (from < 0 || from >= sequence.length) return;
    const at = Math.max(0, Math.min(to, sequence.length - 1));
    if (at === from) return;
    const [cardId] = sequence.splice(from, 1);
    sequence.splice(at, 0, cardId);
    this.announcement.set(this.t('announceMoved', { card: this.cardText(cardId), n: at + 1 }));
    this.setSequence(sequence);
  }

  private removeFromSequence(index: number): void {
    const sequence = [...this.sequence()];
    if (index < 0 || index >= sequence.length) return;
    const [cardId] = sequence.splice(index, 1);
    this.announcement.set(this.t('announceReturned', { card: this.cardText(cardId) }));
    this.setSequence(sequence);
  }

  private commit(next: DragPlaceMap): void {
    const plan = this._plan();
    const payload = buildDragPlacePayload(next, plan);
    const changed = payload !== buildDragPlacePayload(this.placed(), plan);
    this.placed.set(next);
    if (!changed) return;
    this.lastEmitted = payload;
    this.answerPayloadChange.emit(payload);
    this.answered.emit();
  }

  private restoreFromAnswerPayload(): void {
    const plan = this._plan();
    const incoming = parseDragPlacePlacements(this._answerPayloadJson(), plan);
    const payload = buildDragPlacePayload(incoming, plan);
    // Ebeveyn kendi yayınımızı geri verdiyse (yankı) ekrandaki yerleşim korunur.
    if (payload === buildDragPlacePayload(this.placed(), plan)) return;
    if (this.lastEmitted !== null && payload === this.lastEmitted) return;
    this.placed.set(incoming);
  }

  private consumeSuppressedClick(): boolean {
    if (!this.suppressNextClick) return false;
    this.suppressNextClick = false;
    return true;
  }

  private targetButtons(): HTMLElement[] {
    return Array.from(this.host.nativeElement.querySelectorAll<HTMLElement>('[data-target-button]'));
  }

  private focusTarget(index: number): void {
    queueMicrotask(() => {
      const buttons = this.targetButtons();
      if (buttons.length === 0) return;
      const at = (index + buttons.length) % buttons.length;
      buttons[at]?.focus();
    });
  }

  private focusSequenceControl(index: number, direction: 'up' | 'down'): void {
    const el = this.host.nativeElement.querySelector<HTMLButtonElement>(
      `[data-sequence-index="${index}"] [data-move="${direction}"]`
    );
    if (el && !el.disabled) el.focus();
    else this.host.nativeElement.querySelector<HTMLElement>(`[data-sequence-index="${index}"] [data-move]:not([disabled])`)?.focus();
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`shared.questionCanvas.dragPlace.${key}`, params) ?? '';
  }
}
