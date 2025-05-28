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
import { TestInstance, TestInstanceQuestion, TestStatus } from '../../models/test-instance';
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
import { AnswerChoice, QuestionRegion } from '../../models/draws';
import { lastValueFrom } from 'rxjs';
import { QuestionCanvasViewComponent } from '../../shared/components/question-canvas-view/question-canvas-view.component';
import { SidenavService } from '../../services/sidenav.service';
import { MatIconModule } from '@angular/material/icon';
import { CountdownComponent } from '../../shared/components/countdown/countdown.component';
import { Answer } from '../../models/answer';

@Component({
  selector: 'app-test-solve',
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
    QuestionCanvasViewComponent,
    MatIconModule,
    CountdownComponent,
  ],
})
export class TestSolveCanvasComponentv2 implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild(SpinWheelComponent) spinWheelComp!: SpinWheelComponent;
  @ViewChild('spinWheelDialog') spinWheelDialog!: TemplateRef<any>; // 📌 Modal Şablonunu Yakala

  testInstanceId!: number;
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
  public isImageLoaded = signal(false); // Resim yüklendi mi?
  public hoveredRegion = signal<QuestionRegion | null>(null); // Mouse'un üzerinde olduğu soru veya şık
  public selectedRegion = signal<QuestionRegion | null>(null); // Kullanıcının seçtiği soru veya şık

  public hoveredChoice = signal<AnswerChoice | null>(null); // 🟦 Hangi şık üzerinde geziliyorsa
  public selectedChoices = signal<Map<number, AnswerChoice>>(new Map()); // 🔄 Her soru için seçilen şıkkı sakla

  public imageCache = new Map<string, HTMLImageElement>(); // 📂 Resimleri önbellekte sakla
  public currentImageId = signal<string | null>(null); // 🔄 Mevcut resmin ID'sini takip et

  sidenavService = inject(SidenavService);

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

  // Computed properties
  public progressPercentage = computed(() => {
    const total = this.testInstance?.testInstanceQuestions?.length || 1;
    const answered = this.answeredCount();
    return Math.round((answered / total) * 100);
  });

  public answeredCount = computed(() => {
    return this.testInstance?.testInstanceQuestions?.filter((q) => q.selectedAnswerId).length || 0;
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

  constructor(
    private route: ActivatedRoute,
    private testService: TestService,
    private router: Router,
    private dialog: MatDialog
  ) {
    this.sidenavService.setSidenavState(false);
    this.sidenavService.setFullScreen(true);
  }

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

      // 🚀 Eğer test tamamlandıysa, öğrenci profiline yönlendir
      if (this.testInstance.status === TestStatus.Completed) {
        this.router.navigate(['/student-profile']);
      }

      await this.loadQuestions();

      // ⏳ Sayaçları başlat
      this.startTimer();
      this.startQuestionTimer();
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
        this.completeTest();
      }
    });
  }

  private startQuestionTimer() {
    const startTime = Date.now();
    this.questionStartTimes().set(this.currentIndex(), startTime);
  }

  private endQuestionTimer() {
    const currentIdx = this.currentIndex();
    const startTime = this.questionStartTimes().get(currentIdx);
    if (startTime) {
      const duration = Math.round((Date.now() - startTime) / 1000);
      this.questionDurations().set(currentIdx, duration);
    }
  }

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

  completeTest() {
    // son soru kaydedilmemiş olaiblir.
    this.autoPlay = false;
    if (this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId) {
      this.persistAnswer(this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId);
    }
    this.testService.completeTest(this.testInstance.id).subscribe({
      next: () => {
        this.router.navigate(['/student-profile']);
      },
      error: (error) => {
        console.error('Error completing test', error);
      },
    });
  }

  // Cevap kaydet
  selectAnswer(selectedIndex: any) {
    this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId = selectedIndex;
  }

  createDialogTemplate() {
    return {
      template: `
        <h2>🎡 Ödül Çarkı! Çevirmek İçin Butona Bas!</h2>
        <app-spin-wheel></app-spin-wheel>
        <button mat-button (click)="closeDialog()">Kapat</button>
      `,
    };
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
            this.nextQuestion();
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

    this.testService
      .saveAnswer({
        testQuestionId: this.testInstance.testInstanceQuestions[this.currentIndex()].id,
        selectedAnswerId: selectedAnswerId,
        testInstanceId: this.testInstance.id,
        timeTaken: this.questionDuration,
      })
      .subscribe({
        next: () => {
          // Cevap kaydedildi, sonraki soruya geç
          //this.nextQuestion();
        },
        error: (error) => {
          console.error('Error saving answer', error);
        },
      });
  }

  // Önceki soruya git
  prevQuestion() {
    this.testInstance.testInstanceQuestions[this.currentIndex()].timeTaken = this.questionDuration;
    this.correctAnswerVisible = false;
    if (this.testInstance.isPracticeTest) {
      this.persistPracticetime();
    } else {
      if (this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId) {
        this.persistAnswer(this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId);
      }
    }

    if (this.currentIndex() > 0) {
      const newIndex = this.currentIndex() - 1;
      this.currentIndex.set(newIndex);
      this.startQuestionTimer();
      // 🔄 Yeni soru için resim yükle
    }
  }

  // Sonraki soruya git
  nextQuestion() {
    this.testInstance.testInstanceQuestions[this.currentIndex()].timeTaken = this.questionDuration;
    this.correctAnswerVisible = false;
    if (this.testInstance.isPracticeTest) {
    } else {
      if (this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId) {
        this.persistAnswer(this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId);
      }
    }

    if (this.currentIndex() < this.regions().length - 1) {
      const newIndex = this.currentIndex() + 1;
      this.currentIndex.set(newIndex);
      this.startQuestionTimer();
    }
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
          }
        }
      });
    } catch (error) {
      console.error('Koordinatlar yüklenirken hata oluştu:', error);
    }
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
            this.prevQuestion();
            break;
          case 'ArrowRight':
            event.preventDefault();
            this.nextQuestion();
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
        }
      }
    });
  }

  toggleFocusMode() {
    this.focusMode.set(!this.focusMode());
    this.showToastMessage(this.focusMode() ? 'Odaklanma modu açıldı' : 'Normal mod açıldı', 'info');
  }

  toggleBookmark() {
    const currentIdx = this.currentIndex();
    const bookmarks = new Set(this.bookmarkedQuestions());

    if (bookmarks.has(currentIdx)) {
      bookmarks.delete(currentIdx);
      this.showToastMessage('İşaret kaldırıldı', 'info');
    } else {
      bookmarks.add(currentIdx);
      this.showToastMessage('Soru işaretlendi', 'success');
    }

    this.bookmarkedQuestions.set(bookmarks);
  }

  isQuestionBookmarked(index?: number): boolean {
    const idx = index !== undefined ? index : this.currentIndex();
    return this.bookmarkedQuestions().has(idx);
  }

  goToQuestion(index: number) {
    if (index >= 0 && index < this.testInstance.testInstanceQuestions.length) {
      this.endQuestionTimer();
      this.currentIndex.set(index);
      this.startQuestionTimer();
    }
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
      if (!questions[i].selectedAnswerId) {
        this.goToQuestion(i);
        return;
      }
    }

    // Baştan boş soru bul
    for (let i = 0; i < current; i++) {
      if (!questions[i].selectedAnswerId) {
        this.goToQuestion(i);
        return;
      }
    }

    this.showToastMessage('Tüm sorular cevaplanmış', 'info');
  }

  clearAnswer() {
    const currentQuestion = this.testInstance.testInstanceQuestions[this.currentIndex()];
    if (currentQuestion.selectedAnswerId) {
      currentQuestion.selectedAnswerId = undefined as any;
      this.showToastMessage('Cevap temizlendi', 'info');
      // Save to backend
      this.saveAnswer(null);
    }
  }

  hasAnswer(): boolean {
    return !!this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId;
  }

  showHint() {
    if (!this.testInstance.isPracticeTest) return;

    // Bu kısım gerçek hint sistemine bağlanmalı
    const hints = [
      'Bu tür sorularda önce şıkları elemeyi deneyin.',
      'Soruyu tekrar okuyup anahtar kelimeleri bulun.',
      'Verilen bilgileri organize edin.',
      'Benzer problemleri hatırlamaya çalışın.',
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
    this.showToastMessage('Soru bildirildi', 'success');
  }

  increaseFontSize() {
    const newSize = Math.min(this.fontSize() + 2, 24);
    this.fontSize.set(newSize);
    document.documentElement.style.setProperty('--question-font-size', `${newSize}px`);
  }

  decreaseFontSize() {
    const newSize = Math.max(this.fontSize() - 2, 12);
    this.fontSize.set(newSize);
    document.documentElement.style.setProperty('--question-font-size', `${newSize}px`);
  }

  toggleHighContrast() {
    this.highContrast.set(!this.highContrast());
    document.body.classList.toggle('high-contrast', this.highContrast());
    this.showToastMessage(this.highContrast() ? 'Yüksek kontrast açıldı' : 'Normal renk modu', 'info');
  }

  showHelp() {
    // Yardım modalı aç
    this.showToastMessage('Yardım: Ctrl+← ← soru, Ctrl+→ → soru, Ctrl+B işaretle', 'info');
  }

  openSettings() {
    // Ayarlar modalı aç
    this.showToastMessage('Ayarlar geliştiriliyor...', 'info');
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
