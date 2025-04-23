import { AfterViewInit, Component, ElementRef, inject, Input, OnDestroy, OnInit, QueryList, signal, TemplateRef, ViewChild, ViewChildren } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TestService } from '../../services/test.service';
import { TestInstance, TestInstanceQuestion, TestStatus } from '../../models/test-instance';
import { CommonModule } from '@angular/common';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import {  MatCardModule } from '@angular/material/card';
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
  templateUrl: './test-solve-canvas.component.html',
  styleUrls: ['./test-solve-canvas.component.scss'],
  imports: [  QuestionLiteViewComponent,
      CommonModule, MatToolbarModule, MatButtonModule, MatCardModule, PassageCardComponent,
    SpinWheelComponent, QuestionCanvasViewComponent, MatIconModule, CountdownComponent
    ] 
})
export class TestSolveCanvasComponent implements OnInit, AfterViewInit, OnDestroy {
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
  public isImageLoaded = signal(false);  // Resim yüklendi mi?
  public hoveredRegion = signal<QuestionRegion | null>(null); // Mouse'un üzerinde olduğu soru veya şık
  public selectedRegion = signal<QuestionRegion | null>(null); // Kullanıcının seçtiği soru veya şık

  public hoveredChoice = signal<AnswerChoice | null>(null); // 🟦 Hangi şık üzerinde geziliyorsa
  public selectedChoices = signal<Map<number, AnswerChoice>>(new Map()); // 🔄 Her soru için seçilen şıkkı sakla

  public imageCache = new Map<string, HTMLImageElement>(); // 📂 Resimleri önbellekte sakla
  public currentImageId = signal<string | null>(null); // 🔄 Mevcut resmin ID'sini takip et

  sidenavService = inject(SidenavService);

  constructor(private route: ActivatedRoute,
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
    if(this.testTimerSubscription)
      this.testTimerSubscription.unsubscribe();
    if(this.questionTimerSubscription)
      this.questionTimerSubscription.unsubscribe();
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
    return baseHeight + (question.text.length * 0.5); // İçeriğin uzunluğuna göre dinamik yükseklik tahmini
  }

  ngOnInit() {
    this.route.paramMap.subscribe(params => {
      this.testInstanceId = Number(params.get('testInstanceId'));
      if (this.testInstanceId) {
        this.loadTest(this.testInstanceId);        
      } else if(!this.testInstance) {
        this.router.navigate(['/']); // Geçersiz ID varsa anasayfaya yönlendir
      }
    });

  }

  getPassage(questionIndex: number) : Passage | undefined{
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
    

    let lastAnsweredQuestionIndex = this.testInstance.testInstanceQuestions.findIndex(q => q.selectedAnswerId);
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
        q.question.passage.title = this.formatString(q.question.passage.title, this.passageGroups[q.question.passage.id]);
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
    return format.replace(/{(\d+)}/g, (match, index) => "" + args[index]);
  }

  // Zamanlayıcı başlat
  startTimer() {
    this.testTimerSubscription = interval(1000).subscribe(() => {
      this.testDuration++;
      if(this.testDuration >= this.testInstance.maxDurationSeconds) {
        this.completeTest();
      }
    });
  }

  startQuestionTimer() {
    this.questionDuration = this.testInstance.testInstanceQuestions[this.currentIndex()].timeTaken || 0;
    if (this.questionTimerSubscription) {
      this.questionTimerSubscription.unsubscribe(); // Eski timer'ı sıfırla
    }
    this.questionTimerSubscription = interval(1000).subscribe(() => {
      this.questionDuration++;
    });
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
    if(this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId) {
      this.persistAnswer(this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId);
    }
    this.testService.completeTest(this.testInstance.id).subscribe({
      next: () => {
        this.router.navigate(['/student-profile']);
      },
      error: (error) => {
        console.error('Error completing test', error);
      }
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
      `
    };
  }

  closeDialog() {
    this.dialog.closeAll();
  }
  
  openAnswer(selectedIndex: any) {
    this.testInstance.testInstanceQuestions[this.currentIndex()].timeTaken = this.questionDuration;
    this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId = selectedIndex;
    this.correctAnswerVisible = true;
    if(this.questionTimerSubscription)
      this.questionTimerSubscription.unsubscribe();
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
        disableClose: true
      });
    }, 2000); // 2 saniye sonra modalı aç

  }



  persistPracticetime() {
    this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId = 0;
    // durationn süreyi gördüğü anda durdu.
    this.testService.saveAnswer({
      testQuestionId: this.testInstance.testInstanceQuestions[this.currentIndex()].id,
      selectedAnswerId: 0,
      testInstanceId: this.testInstance.id,
      timeTaken: this.testInstance.testInstanceQuestions[this.currentIndex()].timeTaken
    }).subscribe({
      next: () => {
        if(this.autoPlay) {
          this.nextQuestion();
        }        
      },
      error: (error) => {
        console.error('Error saving answer', error);
      }
    })
  }

  persistAnswer(selectedAnswerId: number) {
    if(this.testInstance.testInstanceQuestions[this.currentIndex()].question.isExample) return;
    this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId = selectedAnswerId;
    
    this.testService.saveAnswer({
      testQuestionId: this.testInstance.testInstanceQuestions[this.currentIndex()].id,
      selectedAnswerId: selectedAnswerId,
      testInstanceId: this.testInstance.id,
      timeTaken: this.questionDuration
    }).subscribe({
      next: () => {
        // Cevap kaydedildi, sonraki soruya geç
        //this.nextQuestion();
      },
      error: (error) => {
        console.error('Error saving answer', error);
      }
    })
  }

  
  // Önceki soruya git
  prevQuestion() {
    this.testInstance.testInstanceQuestions[this.currentIndex()].timeTaken = this.questionDuration;
    this.correctAnswerVisible = false;
    if(this.testInstance.isPracticeTest) {
      this.persistPracticetime();
    }
    else {
      if(this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId) {
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
    if(this.testInstance.isPracticeTest) {

    }
    else {
      if(this.testInstance.testInstanceQuestions[this.currentIndex()].selectedAnswerId) {
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
    if(this.testTimerSubscription)
      this.testTimerSubscription.unsubscribe();
    if(this.questionTimerSubscription)
      this.questionTimerSubscription.unsubscribe();
    this.sidenavService.toggleFullScreen();
  }


 

  async loadQuestions() {
    try {
      // const response = await fetch('questions.json'); // 📥 JSON'u yükle
      const data: QuestionRegion[] = this.testService.convertTestInstanceToRegions(this.testInstance);
      this.regions.set(data);
      this.testInstance.testInstanceQuestions.forEach((q: TestInstanceQuestion) => {
        if (q.selectedAnswerId) {          
          const selectedChoice = this.regions().find(a => a.id == q.question.id )?.answers.find(a => a.id === q.selectedAnswerId);
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

}
