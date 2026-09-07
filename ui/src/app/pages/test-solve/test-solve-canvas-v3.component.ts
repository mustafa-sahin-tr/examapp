import {
  Component,
  HostListener,
  TemplateRef,
  ViewChild,
  ViewContainerRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { Overlay, OverlayRef } from '@angular/cdk/overlay';
import { TemplatePortal } from '@angular/cdk/portal';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatDialog } from '@angular/material/dialog';
import { MatBottomSheet, MatBottomSheetModule, MatBottomSheetRef } from '@angular/material/bottom-sheet';
import { TestSolveCanvasComponentv2 } from './test-solve-canvas-enhanced.component';
import { TestService } from '../../services/test.service';
import { QuestionLiteViewComponent } from '../question-lite-view/question-lite-view.component';
import { CountdownComponent } from '../../shared/components/countdown/countdown.component';
import { QuestionCanvasViewComponentv5 } from '../../shared/components/question-canvas-view-v5/question-canvas-view-v5.component';
import { QuestionCanvasDragDropLabelingComponent } from '../../shared/components/question-canvas-dragdrop-labeling/question-canvas-dragdrop-labeling.component';

/**
 * Sınav çözme ekranı (issue #72 yeniden tasarımı).
 *
 * - Masaüstü: soru sahnesi + her zaman görünür insight paneli (soru haritası, özet, hızlı araçlar).
 * - Mobil: insight paneli sayfa akışında DEĞİL; alt dock'taki "Harita" butonu aynı içeriği
 *   `MatBottomSheet` ile açar. Böylece soru geçişlerinde sayfa alttaki haritaya kaymaz.
 * - Tek birleşik dock (`solve-dock`): masaüstünde sticky alt bar, mobilde fixed alt tab bar.
 * - Tam ekran: aktif soru kartı `question-card--fullscreen` sınıfıyla viewport'u kaplar;
 *   içerik sığmazsa küçültülmez, kart içinde scroll olur. Kart içinden önceki/sonraki soruya geçilebilir.
 *   Kart, CDK Overlay ile `cdk-overlay-container`'a (body) portal edilir: uygulama kabuğundaki
 *   `mat-sidenav-content` kendi stacking context'ini (z-index: 1) oluşturduğundan, içerideki
 *   `position: fixed; z-index: 40` kart sidenav'ın (z-index: 4) altında kalıyordu.
 */
@Component({
  selector: 'app-test-solve-v3',
  standalone: true,
  templateUrl: 'test-solve-canvas-v3.component.html',
  styleUrls: ['test-solve-canvas-v3.component.scss'],
  imports: [
    CommonModule,
    MatButtonModule,
    MatIconModule,
    MatTooltipModule,
    MatBottomSheetModule,
    QuestionLiteViewComponent,
    CountdownComponent,
    QuestionCanvasViewComponentv5,
    QuestionCanvasDragDropLabelingComponent,
  ],
})
export class TestSolveCanvasComponentv3 extends TestSolveCanvasComponentv2 {
  private static readonly MOBILE_VIEWPORT_QUERY =
    '(max-width: 640px), ((max-height: 500px) and (orientation: landscape))';

  private readonly bottomSheet = inject(MatBottomSheet);
  private readonly overlay = inject(Overlay);
  private readonly viewContainerRef = inject(ViewContainerRef);
  @ViewChild('mapSheetContent') private mapSheetContent?: TemplateRef<unknown>;
  @ViewChild('fullscreenCardTpl') private fullscreenCardTpl?: TemplateRef<unknown>;
  private mapSheetRef: MatBottomSheetRef<unknown> | null = null;
  private fullscreenOverlayRef: OverlayRef | null = null;

  private passageOnlyByQuestionIndex = new Map<number, boolean>();
  private mobileViewportQuery: MediaQueryList | null = null;
  private readonly mobileViewportQueryListener = (event: MediaQueryListEvent) => {
    this.isMobileViewport.set(event.matches);
    if (!event.matches) {
      this.closeMobileMapSheet();
    }
  };

  public isMobileViewport = signal(false);
  /** Mobil soru haritası bottom-sheet'i açık mı (dock butonunun aktif görünümü için). */
  public mobileMapSheetOpen = signal(false);
  /** Aktif soru kartı tam ekran overlay modunda mı. */
  public fullscreenOpen = signal(false);

  /** Overlay'de gösterilecek aktif soru; tam ekran kapalıyken null. */
  public readonly fullscreenQuestion = computed(() => {
    if (!this.fullscreenOpen()) return null;
    const index = this.currentIndex();
    return this.currentQuestions().find((q) => q.index === index) ?? null;
  });

  public readonly canFullscreenPrev = computed(() => this.currentIndex() > 0);
  public readonly canFullscreenNext = computed(
    () => this.currentIndex() < (this.testInstance?.testInstanceQuestions?.length ?? 0) - 1
  );

  constructor(route: ActivatedRoute, testService: TestService, router: Router, dialog: MatDialog) {
    super(route, testService, router, dialog);

    if (typeof window !== 'undefined') {
      this.mobileViewportQuery = window.matchMedia(TestSolveCanvasComponentv3.MOBILE_VIEWPORT_QUERY);
      this.isMobileViewport.set(this.mobileViewportQuery.matches);
      this.mobileViewportQuery.addEventListener('change', this.mobileViewportQueryListener);
    }
  }

  public override async loadTest(testId: number) {
    await super.loadTest(testId);
    this.closeMobileMapSheet();
    this.closeFullscreen();
    this.initializePassageFirstState();
  }

  private initializePassageFirstState() {
    this.passageOnlyByQuestionIndex.clear();

    const questions = this.testInstance?.testInstanceQuestions ?? [];
    questions.forEach((q, index) => {
      const hasPassage = !!q.question?.passage;
      const showPassageFirst = !!q.question?.showPassageFirst;
      this.passageOnlyByQuestionIndex.set(index, hasPassage && showPassageFirst);
    });
  }

  public isPassageFirstActive(questionIndex: number): boolean {
    const q = this.testInstance?.testInstanceQuestions?.[questionIndex]?.question;
    return !!q?.showPassageFirst && !!q?.passage;
  }

  public isShowingPassageOnly(questionIndex: number): boolean {
    if (!this.isPassageFirstActive(questionIndex)) return false;
    return this.passageOnlyByQuestionIndex.get(questionIndex) ?? false;
  }

  public showQuestion(questionIndex: number) {
    this.passageOnlyByQuestionIndex.set(questionIndex, false);
  }

  public showPassageOnly(questionIndex: number) {
    if (!this.isPassageFirstActive(questionIndex)) return;
    this.passageOnlyByQuestionIndex.set(questionIndex, true);
  }

  // Explicitly re-expose on v3 for Angular template type-checking.
  public override saveDragDropAnswerForQuestion(answerPayloadJson: string, questionIndex: number) {
    super.saveDragDropAnswerForQuestion(answerPayloadJson, questionIndex);
  }

  // ---------------------------------------------------------------------------
  // Mobil soru haritası (bottom-sheet)
  // ---------------------------------------------------------------------------

  public openMobileMapSheet() {
    if (!this.mapSheetContent || this.mapSheetRef) return;

    this.mapSheetRef = this.bottomSheet.open(this.mapSheetContent, {
      panelClass: 'solve-map-sheet',
      restoreFocus: true,
      ariaLabel: 'Soru haritası',
    });
    this.mobileMapSheetOpen.set(true);
    this.mapSheetRef.afterDismissed().subscribe(() => {
      this.mapSheetRef = null;
      this.mobileMapSheetOpen.set(false);
    });
  }

  public closeMobileMapSheet() {
    this.mapSheetRef?.dismiss();
  }

  public toggleMobileMapSheet() {
    if (this.mapSheetRef) {
      this.closeMobileMapSheet();
    } else {
      this.openMobileMapSheet();
    }
  }

  /** Haritadan soru seçimi — hem masaüstü panelde hem mobil sheet'te aynı şablon kullanılır. */
  public selectFromMap(questionIndex: number) {
    this.closeMobileMapSheet();
    void this.focusOnQuestion(questionIndex);
  }

  /** Ctrl+F (base class kısayolu): artık odak modu yerine mevcut soruyu tam ekran aç/kapat. */
  public override toggleFocusMode() {
    if (this.fullscreenOpen()) {
      this.closeFullscreen();
    } else {
      void this.openFullscreen(this.currentIndex());
    }
  }

  // ---------------------------------------------------------------------------
  // Tam ekran soru görünümü
  // ---------------------------------------------------------------------------

  public isFullscreenCard(questionIndex: number): boolean {
    return this.fullscreenOpen() && questionIndex === this.currentIndex();
  }

  public async openFullscreen(questionIndex: number) {
    if (questionIndex !== this.currentIndex()) {
      await this.focusOnQuestion(questionIndex);
    }
    this.fullscreenOpen.set(true);
    this.attachFullscreenOverlay();
    if (!this.fullscreenOverlayRef) {
      // Overlay açılamadıysa (ör. fullscreenCardTpl henüz çözülmemiş) state'i geri al;
      // aksi halde grid'deki kart gizlenir ve soru ekrandan tamamen kaybolur.
      this.fullscreenOpen.set(false);
    }
  }

  public closeFullscreen() {
    this.fullscreenOpen.set(false);
    this.detachFullscreenOverlay();
  }

  /**
   * Tam ekran kartı `cdk-overlay-container`'a portal eder. Embedded view bu komponentin
   * ViewContainerRef'i ile oluşturulduğundan change detection ve ViewChild sorguları
   * (canvasView vb.) kart grid'deymiş gibi çalışmaya devam eder.
   */
  private attachFullscreenOverlay() {
    if (this.fullscreenOverlayRef || !this.fullscreenCardTpl) return;

    this.fullscreenOverlayRef = this.overlay.create({
      positionStrategy: this.overlay.position().global().top('0').left('0'),
      width: '100%',
      height: '100%',
      hasBackdrop: false,
      // Kart kendi içinde scroll eder (overscroll-behavior: contain); sayfa scroll'una dokunma.
      scrollStrategy: this.overlay.scrollStrategies.noop(),
      panelClass: 'solve-fullscreen-pane',
    });
    this.fullscreenOverlayRef.attach(new TemplatePortal(this.fullscreenCardTpl, this.viewContainerRef));
  }

  private detachFullscreenOverlay() {
    this.fullscreenOverlayRef?.dispose();
    this.fullscreenOverlayRef = null;
  }

  public async fullscreenPrev() {
    if (!this.canFullscreenPrev()) return;
    if (this.questionsPerView() === 1) {
      await this.prevQuestion();
    } else {
      await this.focusOnQuestion(this.currentIndex() - 1);
    }
  }

  public async fullscreenNext() {
    if (!this.canFullscreenNext()) return;
    if (this.questionsPerView() === 1) {
      await this.nextQuestion();
    } else {
      await this.focusOnQuestion(this.currentIndex() + 1);
    }
  }

  @HostListener('document:keydown.escape')
  public onEscape() {
    if (this.fullscreenOpen()) {
      this.closeFullscreen();
    }
  }

  public override ngOnDestroy(): void {
    if (this.mobileViewportQuery) {
      this.mobileViewportQuery.removeEventListener('change', this.mobileViewportQueryListener);
    }
    this.closeMobileMapSheet();
    this.detachFullscreenOverlay();

    super.ngOnDestroy();
  }
}
