import { AfterViewInit, Component, ElementRef, OnInit, QueryList, ViewChildren } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TestService } from '../services/test.service';
import { Question } from '../models/question';
import { TestInstance, TestInstanceQuestion, TestStatus } from '../models/test-instance';
import { QuestionViewComponent } from '../question-view/question-view.component';
import { CommonModule } from '@angular/common';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import {  MatCardModule } from '@angular/material/card';
import { interval, Subscription } from 'rxjs';
import { QuestionLiteViewComponent } from '../question-lite-view/question-lite-view.component';

@Component({
  selector: 'app-test-solve',
  standalone: true,
  templateUrl: './test-solve.component.html',
  styleUrls: ['./test-solve.component.scss'],
  imports: [QuestionViewComponent, CommonModule, MatToolbarModule, MatButtonModule, MatCardModule , QuestionLiteViewComponent ]
})
export class TestSolveComponent implements OnInit, AfterViewInit {
  testInstanceId!: number;
  testInstance!: TestInstance; // Test bilgisi ve sorular
  currentQuestionIndex:number = 0; // Şu anki soru index'i
  testDuration: number = 0; // Saniye cinsinden süre
  questionDuration: number = 0; // Soruya ayrılan süre
  interval: any;
  showStopButton: boolean = false; // Eğer test durdurulabilirse
  questionTimerSubscription!: Subscription;
  testTimerSubscription!: Subscription;
  passageGroups: { [key: number]: number[] } = {};
  leftColumn: any[] = [];
  rightColumn: any[] = [];
  @ViewChildren('questionCard') questionCards!: QueryList<ElementRef>;
  constructor(private route: ActivatedRoute,
    private testService: TestService,
    private router: Router) {}

  ngAfterViewInit() {
    
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
      } else {
        this.router.navigate(['/']); // Geçersiz ID varsa anasayfaya yönlendir
      }
    });
  }

  // Test ve daha önceki cevapları yükle
  loadTest(testId: number) {
    this.testService.getTestWithAnswers(testId).subscribe(data => {
      console.log('Test loaded', data);

      this.testInstance = data;
      // check passages 
      this.testInstance.testInstanceQuestions.forEach((q: TestInstanceQuestion) => {
        if (q.question.passage && q.question.passage.id) {
          if(!this.passageGroups[q.question.passage.id])
            this.passageGroups[q.question.passage.id] = [];
          this.passageGroups[q.question.passage.id].push(q.order);
        }
      });

      this.testInstance.testInstanceQuestions.forEach((q: TestInstanceQuestion) => {
        if (q.question.passage && q.question.passage.id) {
          q.question.passage.title = this.formatString(q.question.passage.title, this.passageGroups[q.question.passage.id]);
        }
      });

      console.log('TestInstance', this.testInstance.testInstanceQuestions);

      if(this.testInstance.status === TestStatus.Completed) {
        this.router.navigate(['/student-profile']);
      }
      
      this.startTimer();
      this.startQuestionTimer();
    });
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
    this.questionDuration = this.testInstance.testInstanceQuestions[this.currentQuestionIndex].timeTaken || 0;
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
    this.testService.completeTest(this.testInstance.id).subscribe({
      next: () => {
        this.router.navigate(['/test-summary', this.testInstance.id]);
      },
      error: (error) => {
        console.error('Error completing test', error);
      }
    });
  }

  // Cevap kaydet
  selectAnswer(selectedIndex: number) {
    this.testInstance.testInstanceQuestions[this.currentQuestionIndex].selectedAnswerId = selectedIndex;
  }
  
  openAnswer(selectedIndex: number) {
    this.testInstance.testInstanceQuestions[this.currentQuestionIndex].timeTaken = this.questionDuration;
    this.testInstance.testInstanceQuestions[this.currentQuestionIndex].selectedAnswerId = selectedIndex;
    if(this.questionTimerSubscription)
      this.questionTimerSubscription.unsubscribe();
  }

  persistPracticetime() {
    this.testInstance.testInstanceQuestions[this.currentQuestionIndex].selectedAnswerId = 0;
    // durationn süreyi gördüğü anda durdu.
    this.testService.saveAnswer({
      testQuestionId: this.testInstance.testInstanceQuestions[this.currentQuestionIndex].id,
      selectedAnswerId: 0,
      testInstanceId: this.testInstance.id,
      timeTaken: this.testInstance.testInstanceQuestions[this.currentQuestionIndex].timeTaken
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

  persistAnswer(selectedIndex: number) {
    this.testInstance.testInstanceQuestions[this.currentQuestionIndex].selectedAnswerId = selectedIndex;
    this.testInstance.testInstanceQuestions[this.currentQuestionIndex].timeTaken = this.questionDuration;
    this.testService.saveAnswer({
      testQuestionId: this.testInstance.testInstanceQuestions[this.currentQuestionIndex].id,
      selectedAnswerId: selectedIndex,
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
    if(this.testInstance.isPracticeTest) {
      this.persistPracticetime();
    }
    else {
      this.persistAnswer(this.testInstance.testInstanceQuestions[this.currentQuestionIndex].selectedAnswerId);      
    }
    if (this.currentQuestionIndex > 0) {
      this.currentQuestionIndex--;
      this.startQuestionTimer(); 
    }
  }

  // Sonraki soruya git
  nextQuestion() {
    if(this.testInstance.isPracticeTest) {

    }
    else {
      this.persistAnswer(this.testInstance.testInstanceQuestions[this.currentQuestionIndex].selectedAnswerId);
    }    
    if (this.currentQuestionIndex < this.testInstance.testInstanceQuestions.length - 1) {
      this.currentQuestionIndex++;
      this.startQuestionTimer(); 
    }
  }

  // Testi durdur (opsiyonel)
  pauseTest() {
    if(this.testTimerSubscription)
      this.testTimerSubscription.unsubscribe();
    if(this.questionTimerSubscription)
      this.questionTimerSubscription.unsubscribe();
  }
}
