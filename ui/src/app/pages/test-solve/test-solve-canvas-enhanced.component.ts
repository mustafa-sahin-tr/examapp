import {
  AfterViewInit,
  Component,
  ElementRef,
  inject,
  Input,
  OnDestroy,
  OnInit,
  QueryList,
  signal,
  TemplateRef,
  ViewChild,
  ViewChildren,
  computed,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TestService } from '../../services/test.service';
import {
  TEST_SESSION_ERROR_CODES,
  TestInstance,
  TestInstanceQuestion,
  TestSessionResult,
  TestStatus,
} from '../../models/test-instance';
import { CommonModule } from '@angular/common';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { interval, Subscription } from 'rxjs';
import { QuestionLiteViewComponent } from '../question-lite-view/question-lite-view.component';
import { Passage } from '../../models/question';
import { PassageCardComponent } from '../../shared/components/passage-card/passage-card.component';
import { ConfettiService } from '../../services/confetti.service';
import { SpinWheelComponent } from '../../shared/components/spin-wheel/spin-wheel.component';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { HttpErrorResponse } from '@angular/common/http';
import { AnswerChoice, QuestionRegion } from '../../models/draws';
import { firstValueFrom, lastValueFrom, Observable } from 'rxjs';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { TestFinishRetryDialogComponent, TestFinishRetryDialogData } from './test-finish-retry-dialog.component';
import { SidenavService } from '../../services/sidenav.service';
import { MatIconModule } from '@angular/material/icon';
import { CountdownComponent } from '../../shared/components/countdown/countdown.component';
import { Answer } from '../../models/answer';
import { QuestionCanvasViewComponentv5 } from '../../shared/components/question-canvas-view-v5/question-canvas-view-v5.component';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { questionCanvasImageUrl, storageImageKey } from '../../shared/utils/storage-image-url.util';
import { createStorageImageResolver } from '../../shared/utils/storage-image-refresh.util';

/**
 * Sınav çözme ekranının metinleri kendi Transloco scope'unda: `public/i18n/test-solve/<lang>.json`
 * (issue #183). v3 bu sınıftan türer ama @Component metadata'sı miras alınmadığı için scope
 * provider'ını orada da vermek gerekir.
 */
export const TEST_SOLVE_SCOPE = 'test-solve';

/**
 * Bir cevap kaydının sonucu (issue #383). `completed`: backend testin tamamlandığı için cevabı
 * reddetti (#367) — sonuç sayfasına geçilir.
 */
export type AnswerSaveOutcome = 'saved' | 'failed' | 'completed';

/**
 * Backend'in "test artık devam etmiyor, cevap/bitirme reddedildi" yanıtını tanır (issue #367/#383):
 * yalnız gövdedeki `errorCode: 'TestNotInProgress'` (backend 409 ile birlikte her zaman gönderir).
 * Kodsuz bir 409 bu anlama gelmez; genel hata gibi ele alınır.
 */
export function isTestCompletedRejection(source: unknown): boolean {
  const body = source instanceof HttpErrorResponse ? source.error : source;
  if (!body || typeof body !== 'object') return false;
  return (body as Partial<TestSessionResult>).errorCode === TEST_SESSION_ERROR_CODES.testNotInProgress;
}

@Component({
  selector: 'app-test-solve-v2',
  standalone: true,
  templateUrl: './test-solve-canvas-enhanced.component.html',
  styleUrls: ['./test-solve-canvas.component.scss'],
  imports: [
    QuestionLiteViewComponent,
    CommonModule,
    MatToolbarModule,
    MatButtonModule,
    MatCardModule,
    PassageCardComponent,
    SpinWheelComponent,
    MatIconModule,
    CountdownComponent,
    QuestionCanvasViewComponentv5,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(TEST_SOLVE_SCOPE)],
})
export class TestSolveCanvasComponentv2 implements OnInit, AfterViewInit, OnDestroy {
  protected readonly transloco = inject(TranslocoService);

  @ViewChild(SpinWheelComponent) spinWheelComp!: SpinWheelComponent;
  @ViewChild('spinWheelDialog') spinWheelDialog!: TemplateRef<any>; // 📌 Modal Şablonunu Yakala
  @ViewChild('testContent') testContentRef?: ElementRef<HTMLElement>;
  @ViewChild('toolsPanel') toolsPanelRef?: ElementRef<HTMLElement>;
  @ViewChild('questionPanel') questionPanelRef?: ElementRef<HTMLElement>;
  @ViewChild('canvasContainer') canvasContainerRef?: ElementRef<HTMLElement>;
  @ViewChild('canvasView') canvasViewComponent?: QuestionCanvasViewComponentv5;

  testInstanceId!: number;
  /**
   * issue #365 (S3): soru/şık/paragraf görseli yüklenemezse (imzalı URL'nin süresi doldu) test örneği yeniden çekilir
   * ve aynı görselin taze imzalı URL'si ile bir kez yeniden denenir (salt okuma; cevap durumu değişmez).
   */
  readonly imageRefresh = createStorageImageResolver(
    () => this.testService.getCanvasTestWithAnswers(this.testInstanceId),
    { cacheKey: () => this.testInstanceId }
  );
  @Input() testInstance!: TestInstance; // Test bilgisi ve sorular
  testDuration: number = 0; // Saniye cinsinden süre
  questionDuration: number = 0; // Soruya ayrılan süre
  interval: any;
  showStopButton: boolean = false; // Eğer test durdurulabilirse
  questionTimerSubscription!: Subscription;
  testTimerSubscription!: Subscription;
  passageGroups: { [key: number]: number[] } = {};
  leftColumn: any[] = [];
  rightColumn: any[] = [];
  confettiService = inject(ConfettiService);
  correctAnswerVisible = false;
  autoPlay = false;

  @ViewChildren('questionCard') questionCards!: QueryList<ElementRef>;

  /* Image Selector */
  @ViewChild('canvas', { static: false }) canvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('passagecanvas', { static: false }) passagecanvas!: ElementRef<HTMLCanvasElement>;
  private ctx!: CanvasRenderingContext2D | null;
  private psgctx!: CanvasRenderingContext2D | null;
  private img = new Image();
  public imageSrc = signal<string>('test5.png'); // Saklanan resmin yolu
  public regions = signal<QuestionRegion[]>([]); // Soru bölgeleri
  public currentIndex = signal(0);
  public currentScale = signal(0);
  public isImageLoaded = signal(false); // Resim yüklendi mi?
  public hoveredRegion = signal<QuestionRegion | null>(null); // Mouse'un üzerinde olduğu soru veya şık
  public selectedRegion = signal<QuestionRegion | null>(null); // Kullanıcının seçtiği soru veya şık

  public hoveredChoice = signal<AnswerChoice | null>(null); // 🟦 Hangi şık üzerinde geziliyorsa
  public selectedChoices = signal<Map<number, AnswerChoice>>(new Map()); // 🔄 Her soru için seçilen şıkkı sakla

  public imageCache = new Map<string, HTMLImageElement>(); // 📂 Resimleri önbellekte sakla
  public currentImageId = signal<string | null>(null); // 🔄 Mevcut resmin ID'sini takip et

  sidenavService = inject(SidenavService);
  private previousSidenavCollapsed = this.sidenavService.isSidenavCollapsed();

  // YENİ: Gelişmiş UX özellikleri
  public focusMode = signal(false);
  public bookmarkedQuestions = signal<Set<number>>(new Set());
  public fontSize = signal(16); // px
  public highContrast = signal(false);
  public showHintModal = signal(false);
  public currentHint = signal('');
  public showToast = signal(false);
  public toastMessage = signal('');
  public toastType = signal<'success' | 'warning' | 'error' | 'info'>('info');
  public questionStartTimes = signal<Map<number, number>>(new Map());
  public questionDurations = signal<Map<number, number>>(new Map());

  // Cevap sayısını takip etmek için signal
  public answeredQuestionsCount = signal(0);

  /** Issue #383: "Sınavı Bitir" akışı sürüyor (onay sonrası kayıt + tamamlama); çift tıklamayı engeller. */
  public readonly finishing = signal(false);
  /** Uçuşta olan cevap kayıtları; bitirmeden önce hepsi beklenir. */
  private readonly pendingSaves = new Set<Promise<AnswerSaveOutcome>>();
  /** Kaydı başarısız olan soru indeksleri; bitirmeden önce yeniden denenir. */
  private readonly failedSaveIndices = new Set<number>();
  /** Cevabı değişip henüz başarıyla kaydedilmemiş soru indeksleri; bitirirken hepsi yeniden gönderilir. */
  private readonly dirtyQuestionIndices = new Set<number>();
  /** Sonuç sayfasına yönlendirme başladı mı (tekrarlı yönlendirme/bildirimi engeller). */
  private redirectedToResult = false;
  protected readonly snackBar = inject(MatSnackBar);

  // YENİ: Çoklu soru görüntüleme konfigürasyonu
  public questionsPerView = signal<1 | 2 | 4>(1); // Aynı anda gösterilecek soru sayısı
  public viewStartIndex = signal(0); // Görünümün başladığı soru indeksi

  private readonly imagePreloadCache = new Map<string, Promise<void>>();

  // Computed properties
  public progressPercentage = computed(() => {
    const total = this.testInstance?.testInstanceQuestions?.length || 1;
    const answered = this.answeredCount();
    return Math.round((answered / total) * 100);
  });

  public answeredCount = computed(() => {
    // Signal'dan değeri al, böylece reaktif olur
    this.answeredQuestionsCount();
    // Gerçek sayımı yap
    return this.testInstance?.testInstanceQuestions?.filter((q) => this.isQuestionAnswered(q)).length || 0;
  });

  public remainingCount = computed(() => {
    const total = this.testInstance?.testInstanceQuestions?.length || 0;
    return total - this.answeredCount();
  });

  public bookmarkedCount = computed(() => {
    return this.bookmarkedQuestions().size;
  });

  public averageTimePerQuestion = computed(() => {
    const durations = Array.from(this.questionDurations().values());
    if (durations.length === 0) return 0;
    const total = durations.reduce((sum, duration) => sum + duration, 0);
    return Math.round(total / durations.length);
  });

  public toastIcon = computed(() => {
    const icons = {
      success: 'check_circle',
      warning: 'warning',
      error: 'error',
      info: 'info',
    };
    return icons[this.toastType()];
  });

  // YENİ: Çoklu soru görüntüleme için computed properties
  public currentQuestions = computed(() => {
    const startIndex = this.viewStartIndex();
    const count = this.questionsPerView();
    const questions = this.testInstance?.testInstanceQuestions || [];

    return Array.from({ length: count }, (_, i) => {
      const index = startIndex + i;
      return index < questions.length
        ? {
            question: questions[index],
            index,
            region: this.regions()[index],
          }
        : null;
    }).filter((q) => q !== null);
  });

  public canGoNext = computed(() => {
    const startIndex = this.viewStartIndex();
    const count = this.questionsPerView();
    const totalQuestions = this.testInstance?.testInstanceQuestions?.length || 0;
    return startIndex + count < totalQuestions;
  });

  public canGoPrev = computed(() => {
    return this.viewStartIndex() > 0;
  });

  public totalViews = computed(() => {
    const totalQuestions = this.testInstance?.testInstanceQuestions?.length || 0;
    const questionsPerView = this.questionsPerView();
    return Math.ceil(totalQuestions / questionsPerView);
  });

  public currentViewNumber = computed(() => {
    const startIndex = this.viewStartIndex();
    const questionsPerView = this.questionsPerView();
    return Math.floor(startIndex / questionsPerView) + 1;
  });

  public endQuestionIndex = computed(() => {
    const startIndex = this.viewStartIndex();
    const questionsPerView = this.questionsPerView();
    const totalQuestions = this.testInstance?.testInstanceQuestions?.length || 0;
    return Math.min(startIndex + questionsPerView, totalQuestions);
  });

  // Grid style computed properties
  public gridTemplateColumns = computed(() => {
    const count = this.questionsPerView();
    if (count === 1) return '';
    if (count === 2) return '1fr';
    if (count === 4) return '1fr 1fr';
    return '';
  });

  public gridTemplateRows = computed(() => {
    const count = this.questionsPerView();
    if (count === 1) return '';
    if (count === 2) return '1fr 1fr';
    if (count === 4) return '1fr 1fr';
    return '';
  });

  public gridGap = computed(() => {
    const count = this.questionsPerView();
    if (count === 2) return '16px';
    if (count === 4) return '12px';
    return '';
  });

  constructor(
    private route: ActivatedRoute,
    private testService: TestService,
    private router: Router,
    private dialog: MatDialog
  ) {
    this.sidenavService.setSidenavState(false);
    this.sidenavService.setFullScreen(true);
    this.navigationWorksheetId = this.readNavigationWorksheetId();
  }

  /** Sonuç sayfası `/test/{worksheetId}`; çözüm ekranına gelen navigasyon state'inden (worksheet-detail). */
  private readonly navigationWorksheetId: number | null;

  ngAfterViewInit() {
    //this.loadQuestions();
  }

  // 📌 Dışarıdan spin başlatma
  triggerSpin() {
    this.spinWheelComp.spinWheel();
  }

  ngOnDestroy(): void {
    if (this.testTimerSubscription) this.testTimerSubscription.unsubscribe();
    if (this.questionTimerSubscription) this.questionTimerSubscription.unsubscribe();
    this.sidenavService.toggleFullScreen();
  }

  distributeQuestions() {
    const questions = [...this.testInstance.testInstanceQuestions]; // Soruların kopyasını al
    let leftHeight = 0;
    let rightHeight = 0;

    questions.forEach((question, index) => {
      const height = this.getEstimatedHeight(question); // Sorunun yüksekliğini tahmini olarak al

      if (leftHeight <= rightHeight) {
        this.leftColumn.push(question);
        leftHeight += height;
      } else {
        this.rightColumn.push(question);
        rightHeight += height;
      }
    });

    console.log('Left Column:', this.leftColumn);
    console.log('Right Column:', this.rightColumn);
  }

  // Örnek bir tahmini yükseklik hesaplama metodu
  getEstimatedHeight(question: any): number {
    // İçerik uzunluğuna göre bir yükseklik hesaplıyoruz
    const baseHeight = 100; // Minimum kart yüksekliği
    return baseHeight + question.text.length * 0.5; // İçeriğin uzunluğuna göre dinamik yükseklik tahmini
  }

  ngOnInit() {
    this.route.paramMap.subscribe((params) => {
      this.testInstanceId = Number(params.get('testInstanceId'));
      if (this.testInstanceId) {
        this.loadTest(this.testInstanceId);
      } else if (!this.testInstance) {
        this.router.navigate(['/']); // Geçersiz ID varsa anasayfaya yönlendir
      } else {
        // testInstance Input ile geldi, initial count'u set et
        this.updateAnsweredCount();
      }
    });

    // Keyboard shortcuts kurulumu
    this.setupKeyboardShortcuts();
  }

  getPassage(questionIndex: number): Passage | undefined {
    return this.testInstance.testInstanceQuestions[questionIndex].question.passage;
  }

  getPassageText(questionIndex: number) {
    return this.testInstance.testInstanceQuestions[questionIndex].question.passage?.text || '';
  }

  getPassageTitle(questionIndex: number) {
    return this.testInstance.testInstanceQuestions[questionIndex].question.passage?.title || '';
  }

  getPassageImage(questionIndex: number) {
    return this.testInstance.testInstanceQuestions[questionIndex].question.passage?.imageUrl || '';
  }

  async loadTest(testId: number) {
    try {
      // 📥 Test verisini asenkron olarak al
      this.testInstance = await lastValueFrom(this.testService.getCanvasTestWithAnswers(testId));
      console.log('Test loaded', this.testInstance);

      // 📥 Soruları yükle ve resimleri bekle

      let lastAnsweredQuestionIndex = this.testInstance.testInstanceQuestions.findIndex((q) => q.selectedAnswerId);
      if (lastAnsweredQuestionIndex === -1) {
        this.currentIndex.set(0);
      }
      if (lastAnsweredQuestionIndex < this.testInstance.testInstanceQuestions.length - 1) {
        const newIndex = lastAnsweredQuestionIndex + 1;
        this.currentIndex.set(newIndex);
      }

      // 📜 Passage gruplarını oluştur
      this.testInstance.testInstanceQuestions.forEach((q: TestInstanceQuestion) => {
        if (q.question.passage && q.question.passage.id) {
          if (!this.passageGroups[q.question.passage.id]) {
            this.passageGroups[q.question.passage.id] = [];
          }
          this.passageGroups[q.question.passage.id].push(q.order);
        }
      });

      this.testInstance.testInstanceQuestions.forEach((q: TestInstanceQuestion) => {
        if (q.question.passage && q.question.passage.id) {
          q.question.passage.title = this.formatString(
            q.question.passage.title,
            this.passageGroups[q.question.passage.id]
          );
        }
      });

      console.log('TestInstance', this.testInstance.testInstanceQuestions);

      // Issue #383: devam etmeyen (Completed/Expired/...) teste /testsolve ile girilirse sonuç sayfasına yönlendir.
      if (this.testInstance.status !== TestStatus.Started) {
        this.navigateToResult();
        return;
      }

      await this.loadQuestions();
      await this.ensureQuestionAssetsLoaded(this.currentIndex());

      // ⏳ Sayaçları başlat
      this.startTimer();
      this.startQuestionTimer();

      // 📊 Initial cevaplanan soru sayısını hesapla
      this.updateAnsweredCount();
    } catch (error) {
      console.error('Test yüklenirken hata oluştu:', error);
    }
  }

  formatString(format: string, args: number[]): string {
    return format.replace(/{(\d+)}/g, (match, index) => '' + args[index]);
  }

  // Zamanlayıcı başlat
  startTimer() {
    this.testTimerSubscription = interval(1000).subscribe(() => {
      this.testDuration++;
      if (this.testDuration >= this.testInstance.maxDurationSeconds) {
        // Süre doldu: onay sormadan bitir (issue #383).
        void this.completeTest();
      }
    });
  }

  private startQuestionTimer() {
    if (this.canvasViewComponent) {
      // this.canvasViewComponent.retsetImageScale();
    }
    const startTime = Date.now();
    this.questionStartTimes().set(this.currentIndex(), startTime);

    // setTimeout(() => {
    //   requestAnimationFrame(() => this.logCanvasFitDebugInfo());
    // }, 0);
  }

  private endQuestionTimer() {
    const currentIdx = this.currentIndex();
    const startTime = this.questionStartTimes().get(currentIdx);
    if (startTime) {
      const duration = Math.round((Date.now() - startTime) / 1000);
      this.questionDurations().set(currentIdx, duration);
    }
  }

  // private logCanvasFitDebugInfo(): void {
  //   const containerEl = this.testContentRef?.nativeElement;
  //   if (!containerEl) {
  //     console.log('[CanvasFitDebug] test-content elementi bulunamadı.');
  //     return;
  //   }

  //   const containerHeight = Math.round(containerEl.clientHeight);
  //   const containerWidth = Math.round(containerEl.clientWidth);
  //   const defaultView = containerEl.ownerDocument?.defaultView ?? window;
  //   const parsePx = (value: string | null | undefined): number => {
  //     if (!value) {
  //       return 0;
  //     }
  //     const parsed = Number.parseFloat(value);
  //     return Number.isNaN(parsed) ? 0 : parsed;
  //   };

  //   const containerStyle = defaultView ? defaultView.getComputedStyle(containerEl) : undefined;
  //   let containerPaddingTop = 0;
  //   let containerPaddingRight = 0;
  //   let containerPaddingBottom = 0;
  //   let containerPaddingLeft = 0;
  //   let containerColumnGap = 0;
  //   let containerRowGap = 0;
  //   let containerContentHeight = containerHeight;
  //   let containerContentWidth = containerWidth;

  //   if (containerStyle) {
  //     containerPaddingTop = parsePx(containerStyle.paddingTop);
  //     containerPaddingRight = parsePx(containerStyle.paddingRight);
  //     containerPaddingBottom = parsePx(containerStyle.paddingBottom);
  //     containerPaddingLeft = parsePx(containerStyle.paddingLeft);
  //     containerColumnGap = parsePx(containerStyle.columnGap);
  //     containerRowGap = parsePx(containerStyle.rowGap);
  //     containerContentHeight = Math.max(containerHeight - containerPaddingTop - containerPaddingBottom, 0);
  //     containerContentWidth = Math.max(containerWidth - containerPaddingLeft - containerPaddingRight, 0);
  //     console.log(
  //       `[CanvasFitDebug] test-content -> width=${containerWidth}px, height=${containerHeight}px, padding(T:${containerPaddingTop}px R:${containerPaddingRight}px B:${containerPaddingBottom}px L:${containerPaddingLeft}px), iç alan≈${containerContentWidth}px x ${containerContentHeight}px, gap(kolon)≈${containerColumnGap}px, gap(satır)≈${containerRowGap}px.`
  //     );
  //   } else {
  //     console.log('[CanvasFitDebug] test-content stil bilgisi alınamadı; padding değerleri 0 varsayıldı.');
  //   }

  //   const canvasComponent = this.canvasViewComponent;
  //   if (!canvasComponent) {
  //     console.log(
  //       `[CanvasFitDebug] test-content yüksekliği=X=${containerHeight}px, ilgili canvas bileşeni bulunamadı (muhtemelen metin tabanlı soru).`
  //     );
  //     return;
  //   }

  //   const { questionHeight, passageHeight, hasPassageImage } = canvasComponent.getCanvasHeights();
  //   const { questionWidth, passageWidth, hasPassageImage: hasPassageForWidth } = canvasComponent.getCanvasWidths();
  //   const effectiveHasPassage = hasPassageImage || hasPassageForWidth;
  //   const roundedQuestionHeight = Math.round(questionHeight);
  //   const roundedPassageHeight = Math.round(passageHeight);
  //   const roundedQuestionWidth = Math.round(questionWidth);
  //   const roundedPassageWidth = Math.round(passageWidth);
  //   const effectivePassageHeight = effectiveHasPassage ? roundedPassageHeight : 0;
  //   const effectivePassageWidth = effectiveHasPassage ? roundedPassageWidth : 0;
  //   const combinedHeight = roundedQuestionHeight + effectivePassageHeight;
  //   const widestCanvas = Math.max(roundedQuestionWidth, effectivePassageWidth);

  //   if (combinedHeight <= 0) {
  //     console.log(
  //       `[CanvasFitDebug] test-content yüksekliği=X=${containerHeight}px, soru/passage canvas yükseklikleri hesaplanamadı (combinedHeight=0).`
  //     );
  //     return;
  //   }

  //   if (widestCanvas <= 0) {
  //     console.log(
  //       `[CanvasFitDebug] test-content genişliği=W=${containerWidth}px, canvas genişlikleri hesaplanamadı (WM=0).`
  //     );
  //     return;
  //   }

  //   const focusModeActive = this.focusMode();
  //   const toolsPanelEl = this.toolsPanelRef?.nativeElement;
  //   const questionPanelEl = this.questionPanelRef?.nativeElement;
  //   const canvasContainerEl = this.canvasContainerRef?.nativeElement;
  //   const questionWrapperEl = canvasContainerEl
  //     ? (canvasContainerEl.closest('.question-wrapper') as HTMLElement | null)
  //     : null;

  //   const heightLimitCandidates: number[] = [];
  //   const widthLimitCandidates: number[] = [];
  //   const heightLimitDetails: string[] = [];
  //   const widthLimitDetails: string[] = [];
  //   const widthContextExtras: string[] = [];

  //   if (containerContentHeight > 0) {
  //     heightLimitCandidates.push(containerContentHeight);
  //     heightLimitDetails.push(`test-content iç=${containerContentHeight}px`);
  //   }
  //   if (containerContentWidth > 0) {
  //     widthLimitCandidates.push(containerContentWidth);
  //     widthLimitDetails.push(`test-content iç=${containerContentWidth}px`);
  //   }

  //   let questionPanelContentWidth = 0;
  //   let questionPanelContentHeight = 0;

  //   if (questionPanelEl) {
  //     const qpClientWidth = Math.round(questionPanelEl.clientWidth);
  //     const qpClientHeight = Math.round(questionPanelEl.clientHeight);
  //     let qpPaddingTop = 0;
  //     let qpPaddingRight = 0;
  //     let qpPaddingBottom = 0;
  //     let qpPaddingLeft = 0;

  //     if (defaultView) {
  //       const qpStyle = defaultView.getComputedStyle(questionPanelEl);
  //       qpPaddingTop = parsePx(qpStyle.paddingTop);
  //       qpPaddingRight = parsePx(qpStyle.paddingRight);
  //       qpPaddingBottom = parsePx(qpStyle.paddingBottom);
  //       qpPaddingLeft = parsePx(qpStyle.paddingLeft);
  //     }

  //     questionPanelContentWidth = Math.max(qpClientWidth - qpPaddingLeft - qpPaddingRight, 0);
  //     questionPanelContentHeight = Math.max(qpClientHeight - qpPaddingTop - qpPaddingBottom, 0);

  //     if (questionPanelContentWidth > 0) {
  //       widthLimitCandidates.push(questionPanelContentWidth);
  //       widthLimitDetails.push(`question-panel iç=${questionPanelContentWidth}px`);
  //     }
  //     if (questionPanelContentHeight > 0) {
  //       heightLimitCandidates.push(questionPanelContentHeight);
  //       heightLimitDetails.push(`question-panel iç=${questionPanelContentHeight}px`);
  //     }

  //     console.log(
  //       `[CanvasFitDebug] question-panel -> width=${qpClientWidth}px, height=${qpClientHeight}px, padding(T:${qpPaddingTop}px R:${qpPaddingRight}px B:${qpPaddingBottom}px L:${qpPaddingLeft}px), içerik≈${questionPanelContentWidth}px x ${questionPanelContentHeight}px.`
  //     );
  //   } else {
  //     console.log('[CanvasFitDebug] question-panel referansı bulunamadı.');
  //   }

  //   let toolsPanelOuterWidth = 0;
  //   if (!focusModeActive) {
  //     if (toolsPanelEl) {
  //       const toolsClientWidth = Math.round(toolsPanelEl.clientWidth);
  //       let toolsPaddingLeft = 0;
  //       let toolsPaddingRight = 0;
  //       let toolsMarginLeft = 0;
  //       let toolsMarginRight = 0;

  //       if (defaultView) {
  //         const toolsStyle = defaultView.getComputedStyle(toolsPanelEl);
  //         toolsPaddingLeft = parsePx(toolsStyle.paddingLeft);
  //         toolsPaddingRight = parsePx(toolsStyle.paddingRight);
  //         toolsMarginLeft = parsePx(toolsStyle.marginLeft);
  //         toolsMarginRight = parsePx(toolsStyle.marginRight);
  //       }

  //       toolsPanelOuterWidth = toolsClientWidth + toolsMarginLeft + toolsMarginRight;
  //       widthContextExtras.push(`tools-panel dış=${toolsPanelOuterWidth}px`);

  //       console.log(
  //         `[CanvasFitDebug] tools-panel -> clientWidth=${toolsClientWidth}px, paddingLR=${
  //           toolsPaddingLeft + toolsPaddingRight
  //         }px, marginLR=${toolsMarginLeft + toolsMarginRight}px, dış genişlik≈${toolsPanelOuterWidth}px.`
  //       );
  //     } else {
  //       console.log('[CanvasFitDebug] tools-panel referansı bulunamadı (focus modu dışında).');
  //     }
  //   }

  //   let interPanelGap: number | null = null;
  //   if (!focusModeActive && questionPanelEl && toolsPanelEl) {
  //     const questionRect = questionPanelEl.getBoundingClientRect();
  //     const toolsRect = toolsPanelEl.getBoundingClientRect();
  //     interPanelGap = Math.round(toolsRect.left - questionRect.right);
  //     widthContextExtras.push(`panel-gap≈${interPanelGap}px`);
  //     console.log(`[CanvasFitDebug] question-panel ile tools-panel arası boşluk ≈ ${interPanelGap}px.`);
  //   } else if (!focusModeActive) {
  //     console.log('[CanvasFitDebug] panel arası boşluk ölçülemedi.');
  //   }

  //   let wrapperContentWidth = 0;
  //   let wrapperContentHeight = 0;
  //   let questionWrapperVerticalPadding = 0;

  //   if (questionWrapperEl) {
  //     const wrapperClientWidth = Math.round(questionWrapperEl.clientWidth);
  //     const wrapperClientHeight = Math.round(questionWrapperEl.clientHeight);
  //     let wrapperPaddingTop = 0;
  //     let wrapperPaddingRight = 0;
  //     let wrapperPaddingBottom = 0;
  //     let wrapperPaddingLeft = 0;

  //     if (defaultView) {
  //       const wrapperStyle = defaultView.getComputedStyle(questionWrapperEl);
  //       wrapperPaddingTop = parsePx(wrapperStyle.paddingTop);
  //       wrapperPaddingRight = parsePx(wrapperStyle.paddingRight);
  //       wrapperPaddingBottom = parsePx(wrapperStyle.paddingBottom);
  //       wrapperPaddingLeft = parsePx(wrapperStyle.paddingLeft);
  //     }

  //     questionWrapperVerticalPadding = wrapperPaddingTop + wrapperPaddingBottom + 48; // 20 canvas'ın margin'inden geliyor.
  //     wrapperContentWidth = Math.max(wrapperClientWidth - wrapperPaddingLeft - wrapperPaddingRight, 0);
  //     wrapperContentHeight = Math.max(wrapperClientHeight - wrapperPaddingTop - wrapperPaddingBottom, 0);

  //     if (wrapperContentWidth > 0) {
  //       widthLimitCandidates.push(wrapperContentWidth);
  //       widthLimitDetails.push(`question-wrapper iç=${wrapperContentWidth}px`);
  //     }
  //     if (wrapperContentHeight > 0) {
  //       heightLimitCandidates.push(wrapperContentHeight);
  //       heightLimitDetails.push(`question-wrapper iç=${wrapperContentHeight}px`);
  //     }

  //     console.log(
  //       `[CanvasFitDebug] question-wrapper -> width=${wrapperClientWidth}px, height=${wrapperClientHeight}px, padding(T:${wrapperPaddingTop}px R:${wrapperPaddingRight}px B:${wrapperPaddingBottom}px L:${wrapperPaddingLeft}px), içerik≈${wrapperContentWidth}px x ${wrapperContentHeight}px.`
  //     );
  //   } else {
  //     console.log('[CanvasFitDebug] question-wrapper bulunamadı (aktif canvas soru olmayabilir).');
  //   }

  //   let canvasWidthLimitCandidate = 0;
  //   let canvasHeightLimitCandidate = 0;

  //   if (canvasContainerEl) {
  //     const canvasClientWidth = Math.round(canvasContainerEl.clientWidth);
  //     const canvasClientHeight = Math.round(canvasContainerEl.clientHeight);
  //     let canvasPaddingTop = 0;
  //     let canvasPaddingRight = 0;
  //     let canvasPaddingBottom = 0;
  //     let canvasPaddingLeft = 0;
  //     let canvasMarginTop = 0;
  //     let canvasMarginRight = 0;
  //     let canvasMarginBottom = 0;
  //     let canvasMarginLeft = 0;

  //     if (defaultView) {
  //       const canvasStyle = defaultView.getComputedStyle(canvasContainerEl);
  //       canvasPaddingTop = parsePx(canvasStyle.paddingTop);
  //       canvasPaddingRight = parsePx(canvasStyle.paddingRight);
  //       canvasPaddingBottom = parsePx(canvasStyle.paddingBottom);
  //       canvasPaddingLeft = parsePx(canvasStyle.paddingLeft);
  //       canvasMarginTop = parsePx(canvasStyle.marginTop);
  //       canvasMarginRight = parsePx(canvasStyle.marginRight);
  //       canvasMarginBottom = parsePx(canvasStyle.marginBottom);
  //       canvasMarginLeft = parsePx(canvasStyle.marginLeft);
  //     }

  //     const baseWidthCandidates = [wrapperContentWidth, questionPanelContentWidth, containerContentWidth].filter(
  //       (v) => v > 0
  //     );
  //     const baseHeightCandidates = [wrapperContentHeight, questionPanelContentHeight, containerContentHeight].filter(
  //       (v) => v > 0
  //     );
  //     const baseWidthForCanvas = baseWidthCandidates.length ? Math.min(...baseWidthCandidates) : containerContentWidth;
  //     const baseHeightForCanvas = baseHeightCandidates.length
  //       ? Math.min(...baseHeightCandidates)
  //       : containerContentHeight;

  //     const canvasHorizontalGutters = canvasPaddingLeft + canvasPaddingRight + canvasMarginLeft + canvasMarginRight;
  //     const canvasVerticalGutters = canvasPaddingTop + canvasPaddingBottom + canvasMarginTop + canvasMarginBottom;

  //     canvasWidthLimitCandidate = Math.max(baseWidthForCanvas - canvasHorizontalGutters, 0);
  //     canvasHeightLimitCandidate = Math.max(baseHeightForCanvas - canvasVerticalGutters, 0);

  //     const canvasInnerWidth = Math.max(canvasClientWidth - canvasPaddingLeft - canvasPaddingRight, 0);
  //     const canvasInnerHeight = Math.max(canvasClientHeight - canvasPaddingTop - canvasPaddingBottom, 0);

  //     if (canvasWidthLimitCandidate > 0) {
  //       widthLimitCandidates.push(canvasWidthLimitCandidate);
  //       widthLimitDetails.push(`canvas-container sınır≈${canvasWidthLimitCandidate}px`);
  //     }
  //     if (canvasHeightLimitCandidate > 0) {
  //       heightLimitCandidates.push(canvasHeightLimitCandidate);
  //       heightLimitDetails.push(`canvas-container sınır≈${canvasHeightLimitCandidate}px`);
  //     }

  //     console.log(
  //       `[CanvasFitDebug] canvas-container -> clientWidth=${canvasClientWidth}px (iç≈${canvasInnerWidth}px), clientHeight=${canvasClientHeight}px (iç≈${canvasInnerHeight}px), padding(T:${canvasPaddingTop}px R:${canvasPaddingRight}px B:${canvasPaddingBottom}px L:${canvasPaddingLeft}px), margin(T:${canvasMarginTop}px R:${canvasMarginRight}px B:${canvasMarginBottom}px L:${canvasMarginLeft}px), teorik limit≈${canvasWidthLimitCandidate}px x ${canvasHeightLimitCandidate}px.`
  //     );
  //   } else {
  //     console.log('[CanvasFitDebug] canvas-container referansı bulunamadı.');
  //   }

  //   const positiveHeightLimits = heightLimitCandidates.filter((v) => v > 0);
  //   const positiveWidthLimits = widthLimitCandidates.filter((v) => v > 0);

  //   const questionPanelAdjustedHeight =
  //     questionPanelContentHeight > 0 ? Math.max(questionPanelContentHeight - questionWrapperVerticalPadding, 0) : 0;

  //   const baseHeightTarget = positiveHeightLimits.length
  //     ? Math.min(...positiveHeightLimits)
  //     : containerContentHeight > 0
  //       ? containerContentHeight
  //       : containerHeight;
  //   const heightTarget = questionPanelAdjustedHeight > 0 ? questionPanelAdjustedHeight : baseHeightTarget;

  //   const widthTarget = positiveWidthLimits.length
  //     ? Math.min(...positiveWidthLimits)
  //     : containerContentWidth > 0
  //       ? containerContentWidth
  //       : containerWidth;

  //   const heightTargetRounded = Math.round(heightTarget);
  //   const widthTargetRounded = Math.round(widthTarget);

  //   const heightScale = heightTarget > 0 ? heightTarget / combinedHeight : 1;
  //   const widthScale = widthTarget > 0 ? widthTarget / widestCanvas : 1;
  //   const heightScalePercent = ((heightScale - 1) * 100).toFixed(2);
  //   const widthScalePercent = ((widthScale - 1) * 100).toFixed(2);

  //   const heightSummaryEntries: string[] =
  //     questionPanelContentHeight > 0
  //       ? [
  //           `üst limit=question-panel iç ${questionPanelContentHeight}px`,
  //           questionWrapperVerticalPadding > 0
  //             ? `question-wrapper padding düşüldü -> ${questionPanelAdjustedHeight}px`
  //             : undefined,
  //         ]
  //           .filter((entry): entry is string => Boolean(entry))
  //           .concat(heightLimitDetails)
  //       : heightLimitDetails;
  //   const heightSummary = heightSummaryEntries.length ? ` [${heightSummaryEntries.join(' · ')}]` : '';
  //   console.log(
  //     `[CanvasFitDebug] Y ekseni: X=${containerHeight}px, X1=${roundedQuestionHeight}px, X2=${roundedPassageHeight}px${
  //       effectiveHasPassage ? '' : ' (passage yok)'
  //     }, birleşik=${combinedHeight}px, limit≈${heightTargetRounded}px${heightSummary}.`
  //   );

  //   const widthSummaryDetails = widthLimitDetails.concat(widthContextExtras);
  //   const widthSummary = widthSummaryDetails.length ? ` [${widthSummaryDetails.join(' · ')}]` : '';
  //   console.log(
  //     `[CanvasFitDebug] Genişlik: W=${containerWidth}px, W1=${roundedQuestionWidth}px, W2=${roundedPassageWidth}px${
  //       effectiveHasPassage ? '' : ' (passage yok)'
  //     }, WM=${widestCanvas}px, limit≈${widthTargetRounded}px, focusMode=${focusModeActive}${widthSummary}.`
  //   );

  //   if (heightTarget <= 0) {
  //     console.log('[CanvasFitDebug] Y ekseni için kullanılabilir limit hesaplanamadı.');
  //   } else if (heightScale > 1) {
  //     console.log(
  //       `[CanvasFitDebug] Y ekseni: limit içerikten büyük; yaklaşık ${heightScale.toFixed(
  //         3
  //       )}x (~+${heightScalePercent}%) büyütmek mümkün.`
  //     );
  //   } else if (heightScale < 1) {
  //     console.log(
  //       `[CanvasFitDebug] Y ekseni: limit içerikten küçük; yaklaşık ${heightScale.toFixed(
  //         3
  //       )}x (~${heightScalePercent}%) küçültmek gerekir.`
  //     );
  //     // this.canvasViewComponent?.rescaleQuestion(+heightScalePercent);
  //   } else {
  //     console.log('[CanvasFitDebug] Y ekseni: içerik mevcut sınırlara yakın, ek ölçekleme gerekmiyor.');
  //   }

  //   if (widthTarget <= 0) {
  //     console.log('[CanvasFitDebug] Genişlik için kullanılabilir limit hesaplanamadı.');
  //   } else if (widthScale > 1) {
  //     console.log(
  //       `[CanvasFitDebug] Genişlik: kullanılabilir alan daha geniş; yaklaşık ${widthScale.toFixed(
  //         3
  //       )}x (~+${widthScalePercent}%) büyütme yapabilirsin.`
  //     );
  //     //this.canvasViewComponent?.rescaleQuestion(+widthScale.toFixed(3));
  //   } else if (widthScale < 1) {
  //     console.log(
  //       `[CanvasFitDebug] Genişlik: alan daha dar; yaklaşık ${widthScale.toFixed(
  //         3
  //       )}x (~${widthScalePercent}%) küçültmek gerekir.`
  //     );
  //   } else {
  //     console.log('[CanvasFitDebug] Genişlik: içerik mevcut sınırlara uyuyor, ek ölçekleme gerekmiyor.');
  //   }

  //   const scaleCandidates = [
  //     { dimension: 'height', scale: heightScale, limit: heightTargetRounded },
  //     { dimension: 'width', scale: widthScale, limit: widthTargetRounded },
  //   ].filter((entry) => Number.isFinite(entry.scale) && entry.scale > 0);

  //   if (scaleCandidates.length === 0) {
  //     console.log('[CanvasFitDebug] Nihai ölçek hesaplanamadı (geçerli ölçek adayı yok).');
  //     return;
  //   }

  //   const shrinkCandidates = scaleCandidates.filter((entry) => entry.scale < 1);
  //   const limitingEntry =
  //     shrinkCandidates.length > 0
  //       ? shrinkCandidates.reduce((prev, curr) => (curr.scale < prev.scale ? curr : prev))
  //       : scaleCandidates.reduce((prev, curr) => (curr.scale < prev.scale ? curr : prev));

  //   const finalScale = limitingEntry.scale;
  //   const mixedDemand = shrinkCandidates.length > 0 && shrinkCandidates.length !== scaleCandidates.length;

  //   if (mixedDemand) {
  //     console.log(
  //       `[CanvasFitDebug] Ölçek yönleri karışık; küçültme önceliklendirildi (${shrinkCandidates
  //         .map((entry) => `${entry.dimension}≈${entry.scale.toFixed(3)}`)
  //         .join(', ')}).`
  //     );
  //   }

  //   console.log(
  //     `[CanvasFitDebug] Nihai ölçek ≈ ${finalScale.toFixed(3)} (sınırlayan eksen=${limitingEntry.dimension}, limit≈${
  //       limitingEntry.limit
  //     }px).`
  //   );
  //   this.currentScale.set(finalScale);
  //   // this.canvasViewComponent?.rescaleQuestion(finalScale);
  // }

  // Süreyi formatla (mm:ss)
  get formattedTime(): string {
    const minutes = Math.floor(this.testDuration / 60);
    const seconds = this.testDuration % 60;
    return `${minutes}:${seconds < 10 ? '0' : ''}${seconds}`;
  }

  // Soru süresini formatla (mm:ss)
  get formattedQuestionTime(): string {
    const minutes = Math.floor(this.questionDuration / 60);
    const seconds = this.questionDuration % 60;
    return `${minutes}:${seconds < 10 ? '0' : ''}${seconds}`;
  }

  /** Boş (cevaplanmamış) soru sayısı; örnek sorular sayılmaz. */
  public unansweredCount(): number {
    return (this.testInstance?.testInstanceQuestions ?? []).filter(
      (q) => !q.question?.isExample && !this.isQuestionAnswered(q)
    ).length;
  }

  /**
   * "Sınavı Bitir" butonu (issue #383): önce onay diyaloğu, boş soru sayısıyla. Boş soru olsa da
   * bitirmeye izin verilir (ürün kararı: engelleme yok). Vazgeç seçilirse sınav sürer.
   */
  confirmFinishTest(): void {
    if (this.finishing() || !this.testInstance) return;

    const unanswered = this.unansweredCount();
    const data: ConfirmDialogData = {
      title: this.tr('finish.dialogTitle'),
      message:
        unanswered > 0
          ? this.tr('finish.dialogUnanswered', { count: unanswered })
          : this.tr('finish.dialogAllAnswered'),
      confirmText: this.tr('finish.dialogConfirm'),
      cancelText: this.tr('finish.dialogCancel'),
      icon: 'flag',
      confirmColor: 'primary',
    };

    this.dialog
      .open(ConfirmDialogComponent, { data, restoreFocus: true })
      .afterClosed()
      .subscribe((confirmed) => {
        if (confirmed === true) {
          void this.completeTest();
        }
      });
  }

  /**
   * Testi bitirir (onaysız; süre dolunca doğrudan çağrılır). Sıra (issue #383):
   * 1) ekrandaki cevap(lar)ı ve daha önce başarısız olan kayıtları gönder, uçuştaki tüm kayıtları bekle;
   * 2) herhangi biri başarısızsa tamamlama isteği GÖNDERİLMEZ, hata gösterilir, öğrenci tekrar dener;
   * 3) tamamlama başarılıysa sonuç sayfasına (`/test/{worksheetId}`) gidilir.
   */
  async completeTest(): Promise<void> {
    if (this.finishing() || this.redirectedToResult || !this.testInstance) return;
    this.finishing.set(true);
    this.autoPlay = false;
    this.testTimerSubscription?.unsubscribe();

    const saveOutcomes = await this.flushPendingAnswers();
    if (saveOutcomes.includes('completed')) {
      this.handleTestAlreadyCompleted();
      return;
    }
    if (saveOutcomes.includes('failed')) {
      this.abortFinish('finish.saveFailed');
      return;
    }

    let response: TestSessionResult;
    try {
      response = await firstValueFrom(this.testService.completeTest(this.testInstance.id));
    } catch (error) {
      if (isTestCompletedRejection(error)) {
        this.handleTestAlreadyCompleted();
      } else {
        console.error('Error completing test', error);
        this.abortFinish('finish.completeFailed');
      }
      return;
    }

    if (response?.success === false) {
      if (isTestCompletedRejection(response)) {
        this.handleTestAlreadyCompleted();
      } else {
        this.abortFinish('finish.completeFailed', response.message);
      }
      return;
    }

    this.navigateToResult();
  }

  /** Süre doldu mu (sayaç `maxDurationSeconds`'a ulaştı). */
  protected isTimeUp(): boolean {
    return !!this.testInstance && this.testDuration >= this.testInstance.maxDurationSeconds;
  }

  /**
   * Bitirme başarısız. Süre bitmediyse: akışı aç, hata göster, sayacı sürdür (öğrenci butondan tekrar dener).
   * Süre bittiyse: öğrenci sayaçsız cevaplamaya devam etmesin diye ekran `disableClose` bir diyalogla kilitlenir;
   * tek aksiyon "Tekrar dene" → bitirme yeniden denenir.
   */
  private abortFinish(messageKey: string, serverMessage?: string): void {
    const message = serverMessage || this.tr(messageKey);

    if (this.isTimeUp()) {
      const data: TestFinishRetryDialogData = {
        title: this.tr('finish.timeUpTitle'),
        message,
        retryText: this.tr('finish.retry'),
      };
      this.dialog
        .open(TestFinishRetryDialogComponent, { data, disableClose: true, autoFocus: 'first-tabbable' })
        .afterClosed()
        .subscribe((retry) => {
          this.finishing.set(false);
          // Diyalog navigasyonda da kapanır (sonuç undefined); yalnız açık "Tekrar dene" yeniden dener.
          if (retry === true) {
            void this.completeTest();
          }
        });
      return;
    }

    this.finishing.set(false);
    this.showToastMessage(message, 'error');
    this.startTimer();
  }

  /** Bitirmeden önce kaydedilmesi gerekenleri gönderir ve uçuştaki tüm kayıtların sonucunu bekler. */
  private async flushPendingAnswers(): Promise<AnswerSaveOutcome[]> {
    if (!this.testInstance.isPracticeTest) {
      // Kaydı başarısız olan ve cevabı değişip henüz kaydedilmemiş (dirty) TÜM sorular — görünür olmasalar da
      // (önceki görünüm, görünüm boyutu değişimi) — yeniden gönderilir.
      const indices = new Set<number>([...this.failedSaveIndices, ...this.dirtyQuestionIndices]);
      if (this.questionsPerView() === 1) {
        // Tek soru görünümü: aktif soru (süresiyle) her zaman gönderilir.
        this.endQuestionTimer();
        const idx = this.currentIndex();
        const current = this.testInstance.testInstanceQuestions[idx];
        if (current) {
          current.timeTaken = this.questionDurations().get(idx) ?? current.timeTaken;
        }
        indices.add(idx);
      }
      indices.forEach((index) => this.persistQuestionState(index));
    }

    return Promise.all(Array.from(this.pendingSaves));
  }

  /** Sorunun mevcut cevabını (MCQ veya drag-drop payload) kaydeder; cevap yoksa istek göndermez. */
  private persistQuestionState(index: number): void {
    const question = this.testInstance.testInstanceQuestions[index];
    if (!question || question.question.isExample) return;

    if (question.selectedAnswerId) {
      void this.trackAnswerSave(
        index,
        this.testService.saveAnswer({
          testQuestionId: question.id,
          selectedAnswerId: question.selectedAnswerId,
          testInstanceId: this.testInstance.id,
          timeTaken: question.timeTaken,
        })
      );
    } else if (this.isNonEmptyPayload(question.answerPayload)) {
      this.persistDragDropPayloadForQuestion(question.answerPayload!, index);
    }
  }

  /**
   * Bir kayıt isteğini izler: uçuştayken `pendingSaves`'te tutar, başarısızsa indeksi
   * `failedSaveIndices`'e yazar. Dönen promise reddedilmez; sonucu {@link AnswerSaveOutcome} ile bildirir.
   * Bitirme akışı dışında `completed` gelirse (ör. başka sekmede bitirildi) sonuç sayfasına geçilir.
   */
  private trackAnswerSave(questionIndex: number, request$: Observable<TestSessionResult>): Promise<AnswerSaveOutcome> {
    const sentSignature = this.answerSignature(questionIndex);
    const outcome = firstValueFrom(request$).then(
      (response): AnswerSaveOutcome => {
        if (response?.success === false) {
          return isTestCompletedRejection(response) ? 'completed' : 'failed';
        }
        return 'saved';
      },
      (error: unknown): AnswerSaveOutcome => {
        if (isTestCompletedRejection(error)) return 'completed';
        console.error('Error saving answer for question', questionIndex, error);
        return 'failed';
      }
    );

    const tracked: Promise<AnswerSaveOutcome> = outcome.then((result) => {
      this.pendingSaves.delete(tracked);
      if (result === 'saved') {
        this.failedSaveIndices.delete(questionIndex);
        // İstek uçuştayken cevap yeniden değiştiyse soru dirty kalır.
        if (this.answerSignature(questionIndex) === sentSignature) {
          this.dirtyQuestionIndices.delete(questionIndex);
        }
      } else if (result === 'failed') {
        this.failedSaveIndices.add(questionIndex);
      } else if (!this.finishing()) {
        this.handleTestAlreadyCompleted();
      }
      return result;
    });
    this.pendingSaves.add(tracked);
    return tracked;
  }

  private answerSignature(questionIndex: number): string {
    const q = this.testInstance?.testInstanceQuestions?.[questionIndex];
    return `${q?.selectedAnswerId ?? ''}|${q?.answerPayload ?? ''}`;
  }

  /** Cevabı değişen soruyu dirty işaretler (bitirirken yeniden gönderilir). */
  protected markQuestionDirty(questionIndex: number): void {
    this.dirtyQuestionIndices.add(questionIndex);
  }

  /** Test backend'de zaten tamamlanmış (#367 reddi): bilgi ver ve sonuç sayfasına geç. */
  private handleTestAlreadyCompleted(): void {
    if (this.redirectedToResult) return;
    this.snackBar.open(this.tr('finish.alreadyCompleted'), this.tr('finish.dismiss'), { duration: 4000 });
    this.navigateToResult();
  }

  /**
   * Sonuç sayfası `/test/{worksheetId}`. worksheetId önce yanıttan, yoksa worksheet-detail'in verdiği
   * navigasyon state'inden okunur; ikisi de yoksa sınav listesine düşülür.
   */
  protected navigateToResult(): void {
    this.redirectedToResult = true;
    this.testTimerSubscription?.unsubscribe();
    const worksheetId = this.testInstance?.worksheetId ?? this.navigationWorksheetId;
    if (worksheetId) {
      void this.router.navigate(['/test', worksheetId], { replaceUrl: true });
    } else {
      void this.router.navigate(['/tests'], { replaceUrl: true });
    }
  }

  private readNavigationWorksheetId(): number | null {
    const fromNavigation = this.router.getCurrentNavigation()?.extras.state?.['worksheetId'];
    const historyState = typeof history !== 'undefined' ? (history.state as Record<string, unknown> | null) : null;
    const value = Number(fromNavigation ?? historyState?.['worksheetId']);
    return Number.isFinite(value) && value > 0 ? value : null;
  }

  /** Scope'a göreli anahtarı senkron çevirir. */
  protected tr(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${TEST_SOLVE_SCOPE}.${key}`, params) ?? '';
  }

  // Cevap kaydet
  selectAnswer(selectedIndex: any) {
    this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId = selectedIndex;
    this.markQuestionDirty(this.currentIndex());
    if (selectedIndex) {
      this.testInstance.testInstanceQuestions[this.currentIndex()].answerPayload = undefined;
    }

    // Cevaplanan soru sayısını güncelle
    this.updateAnsweredCount();

    if (this.autoNextQuestion()) {
      setTimeout(() => {
        void this.nextQuestion();
      }, 300); // Kısa bir gecikme ile otomatik geçiş
    }
  }

  closeDialog() {
    this.dialog.closeAll();
  }

  openAnswer(selectedIndex: any) {
    this.testInstance.testInstanceQuestions[this.currentIndex()].timeTaken = this.questionDuration;
    this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId = selectedIndex;
    this.correctAnswerVisible = true;
    if (this.questionTimerSubscription) this.questionTimerSubscription.unsubscribe();
    //  this.confettiService.celebrate(); // Basit konfeti efekti
    this.confettiService.launchConfetti(); // Gelişmiş konfeti efekti
    //  this.confettiService.fireworks();
    // this.confettiService.rainbowConfetti();
    // this.confettiService.centerBurst();
    // this.confettiService.cannonShot();
    // this.triggerSpin();
    setTimeout(() => {
      this.dialog.open(this.spinWheelDialog, {
        width: '400px',
        disableClose: true,
      });
    }, 2000); // 2 saniye sonra modalı aç
  }

  persistPracticetime() {
    this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId = 0;
    // durationn süreyi gördüğü anda durdu.
    this.testService
      .saveAnswer({
        testQuestionId: this.testInstance.testInstanceQuestions[this.currentIndex()].id,
        selectedAnswerId: 0,
        testInstanceId: this.testInstance.id,
        timeTaken: this.testInstance.testInstanceQuestions[this.currentIndex()].timeTaken,
      })
      .subscribe({
        next: () => {
          if (this.autoPlay) {
            void this.nextQuestion();
          }
        },
        error: (error) => {
          console.error('Error saving answer', error);
        },
      });
  }

  persistAnswer(selectedAnswerId: number) {
    if (this.testInstance.testInstanceQuestions[this.currentIndex()].question.isExample) return;
    this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId = selectedAnswerId;

    const index = this.currentIndex();
    void this.trackAnswerSave(
      index,
      this.testService.saveAnswer({
        testQuestionId: this.testInstance.testInstanceQuestions[index].id,
        selectedAnswerId: selectedAnswerId,
        testInstanceId: this.testInstance.id,
        timeTaken: this.testInstance.testInstanceQuestions[index].timeTaken,
      })
    );
  }

  // Önceki soruya git - Çoklu görünüm desteği ile
  async prevQuestion(): Promise<void> {
    this.saveCurrentAnswers(); // Mevcut cevapları kaydet
    this.correctAnswerVisible = false;

    if (this.questionsPerView() === 1) {
      // Tek soru modu - eski davranış
      if (this.currentIndex() > 0) {
        const newIndex = this.currentIndex() - 1;
        await this.ensureQuestionAssetsLoaded(newIndex);
        this.currentIndex.set(newIndex);
        this.viewStartIndex.set(newIndex);
        this.startQuestionTimer();
      }
    } else {
      // Çoklu soru modu - bir grup geriye git
      const questionsPerView = this.questionsPerView();
      const currentStart = this.viewStartIndex();
      if (currentStart > 0) {
        const newStart = Math.max(0, currentStart - questionsPerView);
        await this.preloadQuestionRange(newStart, questionsPerView);
        this.viewStartIndex.set(newStart);
        this.currentIndex.set(newStart); // İlk görünen soruyu aktif yap
        this.startQuestionTimer();
      }
    }
  }

  // Sonraki soruya git - Çoklu görünüm desteği ile
  async nextQuestion(): Promise<void> {
    this.saveCurrentAnswers(); // Mevcut cevapları kaydet
    this.correctAnswerVisible = false;

    if (this.questionsPerView() === 1) {
      // Tek soru modu - eski davranış
      if (this.currentIndex() < this.regions().length - 1) {
        const newIndex = this.currentIndex() + 1;
        await this.ensureQuestionAssetsLoaded(newIndex);
        this.currentIndex.set(newIndex);
        this.viewStartIndex.set(newIndex);
        this.startQuestionTimer();
      }
    } else {
      // Çoklu soru modu - bir grup ileriye git
      const questionsPerView = this.questionsPerView();
      const totalQuestions = this.testInstance.testInstanceQuestions.length;
      const currentStart = this.viewStartIndex();

      if (currentStart + questionsPerView < totalQuestions) {
        const newStart = currentStart + questionsPerView;
        await this.preloadQuestionRange(newStart, questionsPerView);
        this.viewStartIndex.set(newStart);
        this.currentIndex.set(newStart); // İlk görünen soruyu aktif yap
        this.startQuestionTimer();
      }
    }
  }

  // YENİ: Mevcut görünümdeki tüm cevapları kaydet
  private saveCurrentAnswers() {
    if (this.questionsPerView() === 1) {
      // Tek soru için eski davranış
      this.endQuestionTimer();
      const currentQuestion = this.testInstance.testInstanceQuestions[this.currentIndex()];
      currentQuestion.timeTaken = this.questionDurations().get(this.currentIndex()) || 0;

      if (this.testInstance.isPracticeTest) {
        this.persistPracticetime();
      } else {
        if (currentQuestion.selectedAnswerId) {
          this.persistAnswer(currentQuestion.selectedAnswerId);
        } else if (this.isNonEmptyPayload(currentQuestion.answerPayload)) {
          this.persistDragDropPayloadForQuestion(currentQuestion.answerPayload!, this.currentIndex());
        }
      }
    } else {
      // Çoklu soru için tüm görünürdeki soruları kaydet
      const currentQuestions = this.currentQuestions();
      currentQuestions.forEach(({ question, index }) => {
        if (this.testInstance.isPracticeTest) {
          return;
        }

        if (question.selectedAnswerId) {
          this.persistAnswerForQuestion(question.selectedAnswerId, index);
          return;
        }

        if (this.isNonEmptyPayload(question.answerPayload)) {
          this.persistDragDropPayloadForQuestion(question.answerPayload!, index);
        }
      });
    }
  }

  // YENİ: Belirli bir soru için cevap kaydet
  private persistAnswerForQuestion(selectedAnswerId: number, questionIndex: number) {
    if (this.testInstance.testInstanceQuestions[questionIndex].question.isExample) return;

    void this.trackAnswerSave(
      questionIndex,
      this.testService.saveAnswer({
        testQuestionId: this.testInstance.testInstanceQuestions[questionIndex].id,
        selectedAnswerId: selectedAnswerId,
        testInstanceId: this.testInstance.id,
        timeTaken: this.questionDuration, // Bu her soru için ayrı tutulmalı
      })
    );
  }

  public saveDragDropAnswerForQuestion(answerPayloadJson: string, questionIndex: number) {
    const question = this.testInstance.testInstanceQuestions[questionIndex];
    question.answerPayload = answerPayloadJson;
    // Drag-drop sorular MCQ gibi selectedAnswerId kullanmıyor; progress için 0 kalsın.
    question.selectedAnswerId = 0;
    this.markQuestionDirty(questionIndex);

    this.updateAnsweredCount();

    if (this.testInstance.isPracticeTest || question.question.isExample) {
      return;
    }

    this.persistDragDropPayloadForQuestion(answerPayloadJson, questionIndex);
  }

  private persistDragDropPayloadForQuestion(answerPayloadJson: string, questionIndex: number) {
    if (this.testInstance.testInstanceQuestions[questionIndex].question.isExample) return;

    const timeTaken = this.questionDurations().get(questionIndex) ?? this.questionDuration;

    void this.trackAnswerSave(
      questionIndex,
      this.testService.saveAnswer({
        testQuestionId: this.testInstance.testInstanceQuestions[questionIndex].id,
        selectedAnswerId: 0,
        answerPayload: answerPayloadJson,
        testInstanceId: this.testInstance.id,
        timeTaken,
      })
    );
  }

  // Testi durdur (opsiyonel)
  pauseTest() {
    if (this.testTimerSubscription) this.testTimerSubscription.unsubscribe();
    if (this.questionTimerSubscription) this.questionTimerSubscription.unsubscribe();
    this.sidenavService.toggleFullScreen();
  }

  async loadQuestions() {
    try {
      // const response = await fetch('questions.json'); // 📥 JSON'u yükle
      const data: QuestionRegion[] = this.testService.convertTestInstanceToRegions(this.testInstance);
      this.regions.set(data);
      this.testInstance.testInstanceQuestions.forEach((q: TestInstanceQuestion) => {
        if (q.selectedAnswerId) {
          const selectedChoice = this.regions()
            .find((a) => a.id == q.question.id)
            ?.answers.find((a) => a.id === q.selectedAnswerId);
          if (selectedChoice) {
            const updatedChoices = new Map(this.selectedChoices());
            updatedChoices.set(q.question.id, selectedChoice);
            this.selectedChoices.set(updatedChoices);
            console.log('Seçilen şık yüklendi:', selectedChoice);
          }
        }
      });
    } catch (error) {
      console.error('Koordinatlar yüklenirken hata oluştu:', error);
    }
  }

  private async ensureQuestionAssetsLoaded(index: number): Promise<void> {
    const regions = this.regions();
    if (!regions || index < 0 || index >= regions.length) {
      return;
    }

    const region = regions[index];
    const tasks: Promise<void>[] = [];

    // issue #365 (S2): v5 görünümünün göstereceği görsel (sunucunun imzaladığı v2, yoksa asıl görsel) ısıtılır.
    // Ön-yükleme haritasının anahtarı imzasız yoldur: aynı nesne için yeni imzalı URL gelirse BU bileşen onu yeniden
    // ön-yüklemez. Tarayıcının HTTP önbelleği ise tam URL'ye (query dahil) göre çalışır; farklı imzalı URL tarayıcı için
    // ayrı bir kaynaktır ve <img> onu gösterirken yeniden indirilir (sunucu aynı 15 dk diliminde aynı URL'yi verir).
    const questionUrl = questionCanvasImageUrl(region);
    tasks.push(this.preloadImage(storageImageKey(questionUrl) || `question-${region.id}`, questionUrl));

    const passageUrl = region?.passage?.imageUrl ?? null;
    if (passageUrl) {
      const passageKey = storageImageKey(passageUrl) || `${region.id}-passage`;
      tasks.push(this.preloadImage(passageKey, passageUrl));
    }

    await Promise.all(tasks);
  }

  private preloadImage(cacheKey: string, url: string | null | undefined): Promise<void> {
    if (!url || url.trim().length === 0) {
      return Promise.resolve();
    }

    const key = cacheKey || url;
    const existing = this.imagePreloadCache.get(key);
    if (existing) {
      return existing;
    }

    const promise = new Promise<void>((resolve) => {
      const image = new Image();
      const finalize = () => {
        image.onload = null;
        image.onerror = null;
        resolve();
      };

      image.onload = finalize;
      image.onerror = finalize;
      image.src = url;

      if (image.complete && image.naturalWidth > 0) {
        finalize();
      }
    });

    this.imagePreloadCache.set(key, promise);
    return promise;
  }

  private async preloadQuestionRange(startIndex: number, count: number): Promise<void> {
    if (count <= 0) {
      return;
    }

    const regions = this.regions();
    if (!regions || !regions.length) {
      return;
    }

    const tasks: Promise<void>[] = [];
    for (let i = 0; i < count; i++) {
      const targetIndex = startIndex + i;
      if (targetIndex >= regions.length) {
        break;
      }
      tasks.push(this.ensureQuestionAssetsLoaded(targetIndex));
    }

    await Promise.all(tasks);
  }

  selectChoice(answer: AnswerChoice) {
    const region = this.regions()[this.currentIndex()];
    const updatedChoices = new Map(this.selectedChoices());
    updatedChoices.set(region.id, answer);
    this.selectedChoices.set(updatedChoices);
    //this.selectedChoice.set(answer);
    this.selectAnswer(answer.id);
  }

  selectAnswerChoice(answer: Answer) {
    this.selectChoice(this.testService.convertAnswerToAnswerChoice(answer));
  }

  // YENİ: Gelişmiş UX metodları

  private setupKeyboardShortcuts() {
    document.addEventListener('keydown', (event) => {
      if (event.ctrlKey || event.metaKey) {
        switch (event.key) {
          case 'ArrowLeft':
            event.preventDefault();
            void this.prevQuestion();
            break;
          case 'ArrowRight':
            event.preventDefault();
            void this.nextQuestion();
            break;
          case 'b':
            event.preventDefault();
            this.toggleBookmark();
            break;
          case 'f':
            event.preventDefault();
            this.toggleFocusMode();
            break;
          case 'h':
            event.preventDefault();
            if (this.testInstance?.isPracticeTest) {
              this.showHint();
            }
            break;
          case '1':
            event.preventDefault();
            this.setQuestionsPerView(1);
            break;
          case '2':
            event.preventDefault();
            this.setQuestionsPerView(2);
            break;
          case '4':
            event.preventDefault();
            this.setQuestionsPerView(4);
            break;
        }
      }
    });
  }

  toggleFocusMode() {
    const nextState = !this.focusMode();
    if (nextState) {
      this.previousSidenavCollapsed = this.sidenavService.isSidenavCollapsed();
      this.sidenavService.setSidenavCollapsed(true);
    } else {
      this.sidenavService.setSidenavCollapsed(this.previousSidenavCollapsed);
    }

    this.focusMode.set(nextState);
    this.showToastMessage(nextState ? this.tr('toast.focusModeOn') : this.tr('toast.focusModeOff'), 'info');
  }

  toggleBookmark() {
    const currentIdx = this.currentIndex();
    const bookmarks = new Set(this.bookmarkedQuestions());

    if (bookmarks.has(currentIdx)) {
      bookmarks.delete(currentIdx);
      this.showToastMessage(this.tr('toast.bookmarkRemoved'), 'info');
    } else {
      bookmarks.add(currentIdx);
      this.showToastMessage(this.tr('toast.bookmarkAdded'), 'success');
    }

    this.bookmarkedQuestions.set(bookmarks);
  }

  isQuestionBookmarked(index?: number): boolean {
    const idx = index !== undefined ? index : this.currentIndex();
    return this.bookmarkedQuestions().has(idx);
  }

  goToQuestion(index: number) {
    void this.focusOnQuestion(index);
  }

  goToNextBookmarked() {
    const bookmarks = Array.from(this.bookmarkedQuestions()).sort((a, b) => a - b);
    const current = this.currentIndex();
    const next = bookmarks.find((idx) => idx > current) || bookmarks[0];

    if (next !== undefined) {
      this.goToQuestion(next);
    }
  }

  goToNextUnanswered() {
    const current = this.currentIndex();
    const questions = this.testInstance.testInstanceQuestions;

    // Sonraki boş soruyu bul
    for (let i = current + 1; i < questions.length; i++) {
      if (!this.isQuestionAnswered(questions[i])) {
        this.goToQuestion(i);
        return;
      }
    }

    // Baştan boş soru bul
    for (let i = 0; i < current; i++) {
      if (!this.isQuestionAnswered(questions[i])) {
        this.goToQuestion(i);
        return;
      }
    }

    this.showToastMessage(this.tr('toast.allAnswered'), 'info');
  }

  clearAnswer() {
    const currentQuestion = this.testInstance.testInstanceQuestions[this.currentIndex()];
    if (this.isQuestionAnswered(currentQuestion)) {
      currentQuestion.selectedAnswerId = undefined as any;
      currentQuestion.answerPayload = undefined;

      // Cevaplanan soru sayısını güncelle
      this.updateAnsweredCount();

      this.showToastMessage(this.tr('toast.answerCleared'), 'info');
      // Save to backend
      this.saveAnswer(null);
    }
  }

  hasAnswer(): boolean {
    return this.isQuestionAnswered(this.testInstance.testInstanceQuestions[this.currentIndex()]);
  }

  showHint() {
    if (!this.testInstance.isPracticeTest) return;

    // Bu kısım gerçek hint sistemine bağlanmalı
    const hints = [
      this.tr('hints.eliminate'),
      this.tr('hints.reread'),
      this.tr('hints.organize'),
      this.tr('hints.recall'),
    ];

    const randomHint = hints[Math.floor(Math.random() * hints.length)];
    this.currentHint.set(randomHint);
    this.showHintModal.set(true);
  }

  closeHint() {
    this.showHintModal.set(false);
  }

  hasHint(): boolean {
    return this.testInstance?.isPracticeTest || false;
  }

  reportQuestion() {
    // Backend'e soru bildirimi gönder
    this.showToastMessage(this.tr('toast.questionReported'), 'success');
  }

  // Otomatik sonraki soruya geçiş özelliği
  public autoNextQuestion = signal(true);

  toggleAutoNextQuestion() {
    this.autoNextQuestion.set(!this.autoNextQuestion());
    this.showToastMessage(
      this.autoNextQuestion() ? this.tr('toast.autoNextOn') : this.tr('toast.autoNextOff'),
      'info'
    );
  }

  clearCanvasAnswer() {
    if (this.canvasViewComponent) {
      // Canvas component'indeki seçimi temizle
      this.canvasViewComponent.selectedChoice = undefined;
      // Canvas'ı yeniden çiz
      // this.canvasViewComponent.drawImageSection();

      // Test instance'daki seçimi de temizle
      const currentQuestion = this.testInstance.testInstanceQuestions[this.currentIndex()];
      currentQuestion.selectedAnswerId = undefined as any;
      currentQuestion.answerPayload = undefined;

      // Cevaplanan soru sayısını güncelle
      this.updateAnsweredCount();

      this.showToastMessage(this.tr('toast.canvasAnswerCleared'), 'success');

      // Save to backend
      this.saveAnswer(null);
    }
  }

  enlargeImage() {
    if (this.canvasViewComponent) {
      this.canvasViewComponent.enlargeImage();
    }
  }

  shrinkImage() {
    if (this.canvasViewComponent) {
      this.canvasViewComponent.shrinkImage();
    }
  }

  retsetImageScale() {
    if (this.canvasViewComponent) {
      // this.canvasViewComponent.retsetImageScale();
    }
  }

  // Cevaplanan soru sayısını güncelle
  private updateAnsweredCount() {
    const count = this.testInstance?.testInstanceQuestions?.filter((q) => this.isQuestionAnswered(q)).length || 0;
    this.answeredQuestionsCount.set(count);
  }

  private isQuestionAnswered(question: TestInstanceQuestion): boolean {
    return !!question.selectedAnswerId || this.isNonEmptyPayload(question.answerPayload);
  }

  private isNonEmptyPayload(payload: string | undefined): boolean {
    return !!payload && payload.trim().length > 0;
  }

  toggleHighContrast() {
    this.highContrast.set(!this.highContrast());
    document.body.classList.toggle('high-contrast', this.highContrast());
    this.showToastMessage(this.highContrast() ? this.tr('toast.highContrastOn') : this.tr('toast.highContrastOff'), 'info');
  }

  showHelp() {
    // Yardım modalı aç
    this.showToastMessage(this.tr('toast.help'), 'info');
  }

  openSettings() {
    // Ayarlar modalı aç
    this.showToastMessage(this.tr('toast.settingsComingSoon'), 'info');
  }

  // YENİ: Görünüm modunu değiştir
  setQuestionsPerView(count: 1 | 2 | 4) {
    console.log(`Görünüm modu değiştiriliyor: ${count} soru`);
    this.questionsPerView.set(count);

    // Mevcut pozisyonu yeni görünüme göre ayarla
    const currentIdx = this.currentIndex();
    const newStartIndex = Math.floor(currentIdx / count) * count;
    this.viewStartIndex.set(newStartIndex);
    void this.preloadQuestionRange(newStartIndex, count);

    console.log(`Yeni başlangıç indeksi: ${newStartIndex}, Mevcut sorular: ${this.currentQuestions().length}`);

    // CSS'i manuel olarak zorla
    setTimeout(() => {
      const container = document.querySelector('.multi-question-container') as HTMLElement;
      if (container) {
        if (count === 2) {
          container.style.display = 'grid';
          container.style.gridTemplateColumns = '1fr';
          container.style.gridTemplateRows = '1fr 1fr';
          container.style.gap = '16px';
          container.style.gridColumnGap = '0px';
          container.style.gridRowGap = '16px';
          console.log("2'li mod CSS zorlandı:", container.style.cssText);
        } else if (count === 4) {
          container.style.display = 'grid';
          container.style.gridTemplateColumns = '1fr 1fr';
          container.style.gridTemplateRows = '1fr 1fr';
          container.style.gap = '12px';
          console.log("4'lü mod CSS zorlandı:", container.style.cssText);
        }
      }
    }, 0);

    this.showToastMessage(this.tr('toast.viewChanged', { count }), 'info');
  }

  // YENİ: Belirli bir soruya odaklan (çoklu görünümde)
  async focusOnQuestion(questionIndex: number): Promise<void> {
    if (this.questionsPerView() === 1) {
      await this.ensureQuestionAssetsLoaded(questionIndex);
      this.currentIndex.set(questionIndex);
      this.viewStartIndex.set(questionIndex);
    } else {
      const questionsPerView = this.questionsPerView();
      const newStartIndex = Math.floor(questionIndex / questionsPerView) * questionsPerView;
      await this.preloadQuestionRange(newStartIndex, questionsPerView);
      this.viewStartIndex.set(newStartIndex);
      this.currentIndex.set(questionIndex);
    }
    this.startQuestionTimer();
  }

  // YENİ: Çoklu görünümde belirli bir soruya cevap seç
  selectAnswerForQuestion(selectedAnswerId: number, questionIndex: number) {
    const question = this.testInstance.testInstanceQuestions[questionIndex];
    question.selectedAnswerId = selectedAnswerId;
    this.markQuestionDirty(questionIndex);
    // MCQ seçildiyse varsa payload'u temizle.
    if (selectedAnswerId) {
      question.answerPayload = undefined;
    }

    // Cevaplanan soru sayısını güncelle
    this.updateAnsweredCount();

    // Canvas soruları için seçimi güncelle
    if (question.question.isCanvasQuestion) {
      const region = this.regions()[questionIndex];
      const selectedChoice = region?.answers.find((a) => a.id === selectedAnswerId);
      if (selectedChoice) {
        const updatedChoices = new Map(this.selectedChoices());
        updatedChoices.set(region.id, selectedChoice);
        this.selectedChoices.set(updatedChoices);
      }
    }

    // Multi-question görünümünde de auto-advance çalışsın
    if (this.autoNextQuestion()) {
      setTimeout(() => {
        if (this.questionsPerView() === 1) {
          void this.nextQuestion();
        } else {
          // Multi-question görünümünde bir sonraki soruya odaklan
          const nextIndex = questionIndex + 1;
          if (nextIndex < this.testInstance.testInstanceQuestions.length) {
            void this.focusOnQuestion(nextIndex);
          }
        }
      }, 300);
    }
  }

  // YENİ: Çoklu görünümde belirli bir soruya choice seç
  selectChoiceForQuestion(answer: AnswerChoice, questionIndex: number) {
    const region = this.regions()[questionIndex];
    const updatedChoices = new Map(this.selectedChoices());
    updatedChoices.set(region.id, answer);
    this.selectedChoices.set(updatedChoices);

    this.selectAnswerForQuestion(answer.id, questionIndex);
  }

  // Yardımcı metodlar
  getMin(a: number, b: number): number {
    return Math.min(a, b);
  }

  private showToastMessage(message: string, type: 'success' | 'warning' | 'error' | 'info') {
    this.toastMessage.set(message);
    this.toastType.set(type);
    this.showToast.set(true);

    // 3 saniye sonra otomatik kapat
    setTimeout(() => {
      this.hideToast();
    }, 3000);
  }

  hideToast() {
    this.showToast.set(false);
  }

  private saveAnswer(answerId: number | null) {
    // Backend'e cevap kaydet
    if (this.testInstance && this.testInstanceId) {
      const currentQuestion = this.testInstance.testInstanceQuestions[this.currentIndex()];
      // API call implementation
    }
  }
}
