import {
  Component,
  Input,
  Output,
  EventEmitter,
  signal,
  ElementRef,
  ViewChild,
  AfterViewInit,
  OnDestroy,
  inject,
  computed,
  ChangeDetectionStrategy
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { AnswerChoice, QuestionRegion } from '../../../models/draws';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';
import { questionCanvasImageUrl } from '../../utils/storage-image-url.util';
import { StorageImageResolver } from '../../utils/storage-image-refresh.util';
import { StorageImageRetryDirective } from '../../directives/storage-image-retry.directive';
import {
  PassageLayoutPlan,
  planPassageLayout,
  planQuestionLayout,
  QuestionLayoutPlan,
} from './question-layout-planner';

const EMPTY_REGION: QuestionRegion = {
  id: 0,
  name: '',
  x: 0,
  y: 0,
  width: 0,
  height: 0,
  passageId: '',
  answers: [],
  imageId: '',
  imageUrl: '',
  exampleAnswer: null,
  isExample: false,
};

type LayoutType = 'side-1col' | 'side-2col' | 'top-1row' | 'top-2row' | 'top-4row';

interface LayoutResult {
  layout: LayoutType;
  answerMinWidth: number;
}

/** visualScale güncellemesi için minimum anlamlı fark (~%1). */
const VISUAL_SCALE_EPSILON = 0.01;
/** Ölçek alt/üst sınırı: 0.2'nin altına inilmez, doğal boyutun üstüne çıkılmaz. */
const MIN_VISUAL_SCALE = 0.2;
const MAX_VISUAL_SCALE = 1;
/**
 * Sayfa genişliği kayıtlı olmayan eski sorularda yükseklik yüzünden soru görseli bu genişliğin
 * altına küçültülmez; içerik kaydırılır (issue #778).
 */
const MIN_FIT_QUESTION_WIDTH_PX = 400;
/** Gövde metninin sayfa genişliğine oranı: 45 kitap sorusundaki ölçümün varsayımı. */
const BODY_TEXT_PAGE_RATIO = 0.018;
/** Sayfa genişliği biliniyorsa hedeflenen gövde metni aralığı (px). */
const MIN_BODY_TEXT_PX = 12;
const MAX_BODY_TEXT_PX = 22;
/** Görsel doğal boyutunun bu oranından küçük gösteriliyorsa büyütme düğmesi çıkar. */
const ZOOM_OFFER_SCALE = 0.75;
/** Büyütme penceresi görseli ekrandakinin bu katı genişlikte açar (doğal boyutu aşmadan). */
const ZOOM_FACTOR = 2;
/** Büyütme düğmesi görselin üstünde kendi satırında durur (metni kapatmasın); soru için kullanılamayan yükseklik (px). */
const ZOOM_ROW_HEIGHT_PX = 48;
/** Sekme satırının soru için kullanılamayan yüksekliği (px). */
const PANE_TABS_HEIGHT_PX = 60;

/**
 * Okuma parçasının kaydırma konumu ve görülüp görülmediği, parça bazında. v3 her soru için görüntüleyiciyi
 * yeniden oluşturur; aynı gruptaki sorular arasında geçerken öğrenci kaldığı yerde kalsın diye burada tutulur.
 */
const passageViewState = new Map<number, { scrollTop: number; seen: boolean }>();

function clampVisualScale(value: number): number {
  return Math.max(MIN_VISUAL_SCALE, Math.min(MAX_VISUAL_SCALE, value));
}

@Component({
  selector: 'app-question-canvas-view-v5',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatButtonToggleModule, MatIconModule, TranslocoDirective, StorageImageRetryDirective],
  templateUrl: './question-canvas-view-v5.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./question-canvas-view-v5.component.scss'],
})
export class QuestionCanvasViewComponentv5 {
  @ViewChild('questionImage') private questionImageRef?: ElementRef<HTMLImageElement>;
  @ViewChild('questionImageBox') private questionImageBoxRef?: ElementRef<HTMLDivElement>;
  @ViewChild('zoomDialog') private zoomDialogRef?: ElementRef<HTMLDialogElement>;
  @ViewChild('passageScroller') private passageScrollerRef?: ElementRef<HTMLDivElement>;

  /** Host elementi: tüketici buna yükseklik sınırı verirse içerik dikeyde de sığdırılır. */
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  private resizeObserver?: ResizeObserver;

  public contentScale = 1;
  public visualScale = signal<number>(1);
  /**
   * Soru görseline uygulanan açık genişlik (px). Yalnızca yükseklik sınırı genişlik oranından
   * daha kısıtlayıcı olduğunda dolu; aksi hâlde null → eski CSS davranışı (max-width: 100%).
   */
  public questionImageWidth = signal<number | null>(null);

  public questionImageSource = signal<string | null>(null);
  public passageImageSource = signal<string | null>(null);
  public _questionRegion = signal<QuestionRegion>(EMPTY_REGION);
  readonly currentLayout = signal<LayoutType>('top-4row');
  readonly answerMinWidth = signal<number>(0);
  /** Yükseklik-bazlı sığdırmada ekrandaki alana göre seçilen düzen; diğer tüketicilerde null. */
  readonly plan = signal<QuestionLayoutPlan | null>(null);
  readonly passagePlan = signal<PassageLayoutPlan | null>(null);
  /** Soru üstte, şıklar tek sütunda: şık bloğu ortalanmış soru görseliyle aynı sol kenardan başlar. */
  readonly singleColumnAnswersWidth = computed(() => {
    const plan = this.plan();
    return plan?.arrangement === 'top' && plan.answerColumns === 1 ? this.questionImageWidth() : null;
  });
  readonly activePane = signal<'passage' | 'question'>('question');
  readonly passageArrangement = computed(() => this.passagePlan()?.arrangement ?? null);
  /** Kayıtlı boyuttan görsel genişliği: yüklenmeden yer ayrılır, kaydırma konumu geri yüklenebilir. */
  readonly passageImageWidth = computed(() => {
    const plan = this.passagePlan();
    const size = this._questionRegion().passage;
    return plan && size?.width ? Math.round(Math.min(plan.boxWidth, size.width)) : null;
  });
  readonly passageAspectRatio = computed(() => {
    const size = this._questionRegion().passage;
    return this.passagePlan() && size?.width && size?.height ? `auto ${size.width} / ${size.height}` : null;
  });
  readonly zoomImages = signal<{ src: string; alt: string; width: number | null }[]>([]);

  readonly containerClass = computed(() => {
    const plan = this.plan();
    return plan ? `layout-planned layout-planned-${plan.arrangement}` : `layout-${this.currentLayout()}`;
  });
  readonly questionZoomAvailable = computed(() => {
    const plan = this.plan();
    return !!plan && plan.scale < ZOOM_OFFER_SCALE;
  });
  readonly questionAspectRatio = computed(() => {
    const region = this._questionRegion();
    const effectiveHeight = this.isWholeFigure(region) ? region.height : region.sanitizedHeight || region.height;
    return this.plan() && region.width && effectiveHeight ? `auto ${region.width} / ${effectiveHeight}` : null;
  });
  /** Yedek düzen: soru tek görsel, şıklar görselsiz büyük harf düğmeleri. */
  readonly wholeFigure = computed(() => !!this.plan() && this.isWholeFigure(this._questionRegion()));
  readonly displayedQuestionSource = computed(() =>
    this.wholeFigure() ? this._questionRegion().imageUrl : this.questionImageSource(),
  );

  // Sıralı cevaplar getter'ı: order > tag > id
  public get sortedAnswers(): AnswerChoice[] {
    const answers = this._questionRegion().answers || [];
    return [...answers].sort((a, b) => {
      // Öncelik: order, sonra label, sonra id
      if (a.order != null && b.order != null && a.order !== b.order) return a.order - b.order;
      if (a.label && b.label && a.label !== b.label) return a.label.localeCompare(b.label);
      return (a.id ?? 0) - (b.id ?? 0);
    });
  }
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    const region = value ?? EMPTY_REGION;
    // Veri elimizde olduğu için direkt hesaplıyoruz; saf hesap sonucu component state'ine burada yazılır
    const result = this.calculateBestLayout(region);
    this.currentLayout.set(result.layout);
    this.answerMinWidth.set(result.answerMinWidth);

    // 2. Cevapları ID'ye göre sırala
    if (region.answers) {
      region.answers = [...region.answers].sort((a, b) => a.label.localeCompare(b.label) || a.id - b.id);
    }

    this._questionRegion.set(region);
    // issue #365 (S2): v2 varyantı sunucuda türetilip imzalanır; imzalı URL istemcide yeniden yazılamaz.
    this.questionImageSource.set(questionCanvasImageUrl(region));
    const passageUrl = region?.passage?.imageUrl ?? null;
    this.passageImageSource.set(passageUrl && passageUrl.trim().length > 0 ? passageUrl : null);
    this.enterPassage(region.passage?.id);
    queueMicrotask(() => {
      this.updateVisualScale();
      this.restorePassageScroll();
    });
  }
  /**
   * issue #365 (S3): soru/şık/paragraf görseli yüklenemezse (imza süresi doldu) taze imzalı URL'yi bulan fonksiyon.
   * Sahibi olan sayfa verir (ör. test örneğini yeniden çeker); verilmezse görsel yeniden denenmez.
   */
  @Input() imageRefresh: StorageImageResolver | null = null;
  @Input() correctAnswerVisible: boolean = false;
  @Input() isPreviewMode: boolean = false;
  /**
   * Ekrandaki alana göre düzen seçimi (issue #74, #778) için açık opt-in. Varsayılan false: düzen
   * kaynak boyutlardan, ölçek yalnızca genişlik oranından hesaplanır (practice-solve / image-selector /
   * v2 kod yolu eskisiyle aynı). Tüketici host'a definite yükseklik veriyorsa (test-solve-canvas-v3)
   * true verir.
   */
  @Input() enableHeightFit: boolean = false;
  private _hidePassage = false;
  @Input() set hidePassage(value: boolean) {
    this._hidePassage = value;
    // Önce-paragraf modu açılıp kapanınca soru alanı değişir; host boyutu değişmediği için observer tetiklenmez.
    queueMicrotask(() => this.updateVisualScale());
  }
  get hidePassage(): boolean {
    return this._hidePassage;
  }
  @Input() set selectedChoice(choice: AnswerChoice | undefined) {
    this._selectedChoice.set(choice);
  }
  @Input() set correctChoice(choice: AnswerChoice | undefined) {
    this._correctChoice.set(choice);
  }
  @Input() set mode(m: 'exam' | 'result' | null) {
    this._mode.set(m || 'exam');
  }

  ngAfterViewInit(): void {
    const imageBox = this.questionImageBoxRef?.nativeElement;
    if (!imageBox) {
      return;
    }

    this.resizeObserver = new ResizeObserver(() => this.updateVisualScale());
    this.resizeObserver.observe(imageBox);
    if (this.enableHeightFit) {
      // Host'un kullanılabilir yüksekliği değişince (pencere/dock/panel) yeniden sığdır.
      this.resizeObserver.observe(this.host.nativeElement);
    }
    this.updateVisualScale();
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
  }

  /**
   * Saf hesap fonksiyonu: region'dan layout + genişlik değerini üretir.
   * Component state'ine yan etki yapmaz; sonucu setter kendi signal'larına yazar.
   * Eşikler (1.1, 800, 0.6, %10 tolerans, maxAns) docs/question-layout-algorithm-analysis.md ile doğrulanmıştır.
   */
  private calculateBestLayout(region: QuestionRegion): LayoutResult {
    const { width: qW, sanitizedHeight, height: qH, answers } = region;
    const effectiveHeight = sanitizedHeight || qH;
    const qRatio = qW / effectiveHeight;

    const maxAns = answers?.reduce((max, ans) => (ans.width > (max?.width || 0) ? ans : max), answers?.[0]);
    const aW = 20 + (maxAns?.width || 0); // 2 × (padding 8 + border ~2) — iki taraflı pay + en geniş şık görseli
    const answerMinWidth = aW;
    const gap = 12; // CSS'teki gap değeriyle uyumlu olmalı

    // 1. Durum: Soru Dikey veya Kareyse (Yan yana yerleşim)
    if (qRatio < 1.1) {
      const layout: LayoutType = effectiveHeight > 800 || qRatio < 0.6 ? 'side-1col' : 'side-2col';
      return { layout, answerMinWidth };
    }

    // 2. Durum: Soru Yatay ise (Genişlik bazlı hiyerarşik kontrol)
    // 4 şık yan yana sığar mı? (%10 tolerans)
    const totalWidth4 = aW * 4 + gap * 3;
    if (totalWidth4 < qW * 1.1) {
      return { layout: 'top-1row', answerMinWidth }; // 4 tane yan yana
    }

    // 2 şık yan yana sığar mı?
    const totalWidth2 = aW * 2 + gap;
    if (totalWidth2 < qW * 1.1) {
      return { layout: 'top-2row', answerMinWidth }; // 2 satır 2 sütun (2x2)
    }

    // Sığmıyorsa alt alta
    return { layout: 'top-4row', answerMinWidth };
  }

  @Output() hoverRegion = new EventEmitter<MouseEvent>();
  @Output() selectRegion = new EventEmitter<MouseEvent>();
  @Output() choiceSelected = new EventEmitter<AnswerChoice>();
  @Output() answerOpened = new EventEmitter<number>();
  @Output() questionRemove = new EventEmitter<number>();

  public hoveredChoice = signal<AnswerChoice | null>(null);
  private _selectedChoice = signal<AnswerChoice | undefined>(undefined);
  private _correctChoice = signal<AnswerChoice | undefined>(undefined);
  private _mode = signal<'exam' | 'result'>('exam');

  public getAnswerClasses(answer: AnswerChoice): Record<string, boolean> {
    const mode = this._mode();
    const isSelected = this._selectedChoice() === answer;
    const isCorrect = !!answer.isCorrect;
    const isHovered = this.hoveredChoice() === answer;
    return {
      'is-selected': isSelected,
      // Seçim tek başına doğru cevap gibi görünmemeli (issue #779).
      'is-correct': isCorrect,
      'is-hovered': mode === 'exam' && !isCorrect && !isSelected && isHovered,
      'is-incorrect': mode === 'result' && isSelected && !isCorrect,
    };
  }

  /** Klavye/ekran okuyucu için `aria-pressed` durumu. */
  isSelected(answer: AnswerChoice): boolean {
    return this._selectedChoice() === answer;
  }

  /** #841: etiketsiz şıkta sıradan harf (0 → A). */
  choiceLetter(index: number): string {
    return String.fromCharCode(65 + index);
  }

  /** Çözerken seçili şıkta işaret gösterilir; sonuç ekranında renkler yeterlidir. */
  showSelectionMark(answer: AnswerChoice): boolean {
    return this._mode() === 'exam' && this.isSelected(answer);
  }

  hoverAnswer(answer: AnswerChoice | null) {
    this.hoveredChoice.set(answer);
  }

  selectAnswer(index: number) {
    const answer = this.sortedAnswers?.[index];
    if (!answer) return;
    this._selectedChoice.set(answer);
    this.choiceSelected.emit(answer);
  }

  showCorrectAnswer() {
    this.answerOpened.emit(1);
  }

  removeQuestion() {
    this.questionRemove.emit(this._questionRegion().id);
  }

  /**
   * `enableHeightFit` açıkken düzen ve ölçek görsel yüklenmesini beklemeden, kayıtlı boyutlar ve
   * host'un alanından hesaplanır (issue #778). Host kaydırılmıyorsa (telefon, tam ekran) yükseklik
   * sınırı yoktur. Diğer tüketiciler yalnız genişlik oranını kullanır.
   */
  public updateVisualScale(): void {
    if (!this.enableHeightFit) {
      this.updateWidthOnlyScale();
      return;
    }

    const region = this._questionRegion();
    const hostEl = this.host.nativeElement;
    const whole = this.isWholeFigure(region);
    const questionHeight = whole ? region.height : region.sanitizedHeight || region.height;
    if (!region.width || !questionHeight) {
      this.plan.set(null);
      this.passagePlan.set(null);
      this.visualScale.set(1);
      this.questionImageWidth.set(null);
      return;
    }
    if (!hostEl.clientWidth) {
      return;
    }

    const overflowY = getComputedStyle(hostEl).overflowY;
    const bounded = (overflowY === 'auto' || overflowY === 'scroll') && hostEl.clientHeight > 0;
    const hostWidth = hostEl.clientWidth;
    const hostHeight = bounded ? hostEl.clientHeight : null;
    const passagePlan = this.isPassageVisible() ? this.planPassage(hostWidth, hostHeight) : null;
    let availableHeight = passagePlan ? passagePlan.questionHeight : hostHeight;
    if (availableHeight !== null) {
      availableHeight -=
        ZOOM_ROW_HEIGHT_PX +
        (passagePlan?.arrangement === 'tabs' ? PANE_TABS_HEIGHT_PX : 0);
    }

    const plan = planQuestionLayout({
      question: { width: region.width, height: questionHeight },
      answers: (region.answers ?? []).map((answer) =>
        whole ? { width: 0, height: 0 } : { width: answer.width || 0, height: answer.height || 0 },
      ),
      width: passagePlan ? passagePlan.questionWidth : hostWidth,
      height: availableHeight,
      ...this.scaleLimits(region),
      arrangement: region.layoutChoice === 'whole' ? undefined : (region.layoutChoice ?? undefined),
    });

    this.plan.set(plan);
    // Soru genişliğe uyar; kısa şıklar çok geniş sorunun ölçeğiyle okunmaz hâle gelmesin (kart genişliği yine sınırlar).
    this.visualScale.set(clampVisualScale(plan.scale));
    this.questionImageWidth.set(Math.round(region.width * plan.scale));
    this.passagePlan.set(passagePlan);
  }


  /**
   * Seçilirse ya da bir şıkkın görseli yoksa (bilinmeyen tip) tüm soru tek görsel gösterilir.
   * Tam görsel yoksa düşülecek bir yer de yoktur; mevcut düzen kalır.
   */
  private isWholeFigure(region: QuestionRegion): boolean {
    if (!this.enableHeightFit || !region.imageUrl) {
      return false;
    }
    return region.layoutChoice === 'whole' || (region.answers ?? []).some((answer) => !answer.imageUrl);
  }

  /** Aynı sayfanın soruları aynı üst sınırı paylaşır; yer varsa hepsi aynı metin boyutunda görünür. */
  private scaleLimits(region: QuestionRegion): { minScale: number; maxScale: number } {
    const pageWidth = region.sourcePageWidth;
    if (!pageWidth) {
      return { minScale: Math.min(1, MIN_FIT_QUESTION_WIDTH_PX / region.width), maxScale: 1 };
    }
    const bodyText = pageWidth * BODY_TEXT_PAGE_RATIO;
    return { minScale: Math.min(1, MIN_BODY_TEXT_PX / bodyText), maxScale: Math.min(1, MAX_BODY_TEXT_PX / bodyText) };
  }

  private planPassage(width: number, height: number | null): PassageLayoutPlan {
    const passage = this._questionRegion().passage;
    const plan = planPassageLayout({
      passage: passage?.width && passage?.height ? { width: passage.width, height: passage.height } : null,
      width,
      height,
    });
    // Parçanın büyütme düğmesi de kendi satırında: yanda kayan kutu ve üstteki parçanın altındaki soru o kadar kısalır.
    if (plan.arrangement === 'beside' && plan.boxHeight !== null) {
      return { ...plan, boxHeight: Math.max(0, plan.boxHeight - ZOOM_ROW_HEIGHT_PX) };
    }
    if (plan.arrangement === 'top' && plan.questionHeight !== null) {
      return { ...plan, questionHeight: Math.max(0, plan.questionHeight - ZOOM_ROW_HEIGHT_PX) };
    }
    return plan;
  }

  /** İlk kez görülen parçada dar ekran metinle açılır; aynı grubun sonraki sorularında soruyla. */
  private enterPassage(passageId: number | undefined): void {
    if (!passageId) {
      this.activePane.set('question');
      return;
    }
    const state = passageViewState.get(passageId);
    this.activePane.set(state?.seen ? 'question' : 'passage');
    passageViewState.set(passageId, { scrollTop: state?.scrollTop ?? 0, seen: true });
  }

  selectPane(pane: 'passage' | 'question'): void {
    this.activePane.set(pane);
    // display:none kaydırma konumunu sıfırlar; görünür olduktan sonra geri yüklenir.
    setTimeout(() => this.restorePassageScroll());
  }

  rememberPassageScroll(event: Event): void {
    const passageId = this._questionRegion().passage?.id;
    const state = passageId ? passageViewState.get(passageId) : undefined;
    if (state) {
      state.scrollTop = (event.target as HTMLElement).scrollTop;
    }
  }

  restorePassageScroll(): void {
    const scroller = this.passageScrollerRef?.nativeElement;
    const passageId = this._questionRegion().passage?.id;
    const state = passageId ? passageViewState.get(passageId) : undefined;
    if (scroller && state) {
      scroller.scrollTop = state.scrollTop;
    }
  }

  private isPassageVisible(): boolean {
    return !this.hidePassage && !!this.passageImageSource();
  }

  answerAspectRatio(answer: AnswerChoice): string | null {
    return this.plan() && answer.width && answer.height ? `auto ${answer.width} / ${answer.height}` : null;
  }

  /** Okuma metnini ekrandakinin iki katı genişlikte açar. */
  openPassageZoom(image: HTMLImageElement, alt: string): void {
    const width = this.zoomedWidth(image.getBoundingClientRect().width, image.naturalWidth);
    this.showZoom([{ src: image.currentSrc || image.src, alt, width }]);
  }

  /** Şıklar da küçük kaldığı için soru görseliyle birlikte aynı ölçekte gösterilir. */
  openQuestionZoom(image: HTMLImageElement, questionAlt: string, choiceAlt: (label: string) => string): void {
    const scale = this.visualScale();
    const choices = this.wholeFigure() ? [] : this.sortedAnswers;
    const answers = choices.map((answer) => ({
      src: answer.imageUrl,
      alt: choiceAlt(answer.label),
      width: answer.width ? this.zoomedWidth(answer.width * scale, answer.width) : null,
    }));
    const width = this.zoomedWidth(image.getBoundingClientRect().width, image.naturalWidth);
    this.showZoom([{ src: image.currentSrc || image.src, alt: questionAlt, width }, ...answers]);
  }

  private zoomedWidth(shownWidth: number, naturalWidth: number): number {
    return Math.round(Math.min(naturalWidth || Infinity, shownWidth * ZOOM_FACTOR));
  }

  private showZoom(images: { src: string; alt: string; width: number | null }[]): void {
    this.zoomImages.set(images);
    this.zoomDialogRef?.nativeElement.showModal();
  }

  choiceAlt(t: (key: string, params: Record<string, unknown>) => string): (label: string) => string {
    return (label) => t('choiceAria', { label });
  }

  closeZoom(): void {
    this.zoomDialogRef?.nativeElement.close();
  }

  /** Pencerenin dışına (backdrop) tıklama olayının hedefi dialog'un kendisidir. */
  closeZoomOnBackdrop(event: MouseEvent): void {
    if (event.target === this.zoomDialogRef?.nativeElement) {
      this.closeZoom();
    }
  }

  /**
   * Eski (yalnızca genişlik) hesaplama — `enableHeightFit=false` iken birebir bu yol çalışır.
   * Soru görseline açık genişlik yazılmaz, hiçbir yükseklik ölçümü yapılmaz.
   */
  private updateWidthOnlyScale(): void {
    const questionWidth = this._questionRegion().width || 0;
    if (!questionWidth) {
      this.visualScale.set(1);
      return;
    }

    const renderedWidth = this.questionImageRef?.nativeElement.getBoundingClientRect().width || 0;
    if (!renderedWidth) {
      return;
    }

    const ratio = clampVisualScale(renderedWidth / questionWidth);
    // Eşik altı farklarda signal'ı güncelleme: şık genişliği → sayfa yüksekliği → scrollbar →
    // konteyner genişliği → ResizeObserver zinciriyle oluşabilecek piksellik salınımı keser
    // ve (load) + microtask + ResizeObserver'ın aynı değeri art arda yazmasını engeller.
    if (Math.abs(ratio - this.visualScale()) < VISUAL_SCALE_EPSILON) {
      return;
    }
    this.visualScale.set(ratio);
  }

  public getScaledAnswerWidth(answer: AnswerChoice): number | null {
    const answerWidth = answer.width || 0;
    if (!answerWidth) {
      return null;
    }

    return Math.round(answerWidth * this.visualScale());
  }

  enlargeImage() {
    this.rescaleQuestion(1.05);
  }

  shrinkImage() {
    this.rescaleQuestion(0.95);
  }

  retsetImageScale() {
    this.contentScale = 1;
  }

  public rescaleQuestion(factor: number) {
    const next = this.contentScale * factor;
    this.contentScale = Math.max(0.2, Math.min(3, next));
  }
}
