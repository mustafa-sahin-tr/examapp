import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { TestService } from '../services/test.service';
import { Question } from '../models/question';
import { TestInstance } from '../models/test-instance';
import { QuestionViewComponent } from '../question-view/question-view.component';
import { CommonModule } from '@angular/common';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import {  MatCardModule } from '@angular/material/card';
import { interval, Subscription } from 'rxjs';

@Component({
  selector: 'app-test-solve',
  standalone: true,
  templateUrl: './test-solve.component.html',
  styleUrls: ['./test-solve.component.scss'],
  imports: [QuestionViewComponent, CommonModule, MatToolbarModule, MatButtonModule, MatCardModule]
})
export class TestSolveComponent implements OnInit {

  testInstanceId!: number;

  testInstance!: TestInstance; // Test bilgisi ve sorular
  currentQuestionIndex:number = 0; // Şu anki soru index'i
  testDuration: number = 0; // Saniye cinsinden süre
  questionDuration: number = 0; // Soruya ayrılan süre
  interval: any;
  showStopButton: boolean = false; // Eğer test durdurulabilirse
  questionTimerSubscription!: Subscription;
  testTimerSubscription!: Subscription;
  constructor(private route: ActivatedRoute,
    private testService: TestService,
    private router: Router) {}

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

      console.log('TestInstance', this.testInstance.testInstanceQuestions);
      this.startTimer();
      this.startQuestionTimer();
    });
  }

  // Zamanlayıcı başlat
  startTimer() {
    this.testTimerSubscription = interval(1000).subscribe(() => {
      this.testDuration++;
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

  

  // Cevap kaydet
  selectAnswer(selectedIndex: number) {
    this.testInstance.testInstanceQuestions[this.currentQuestionIndex].selectedAnswerId = selectedIndex;
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
    this.persistAnswer(this.testInstance.testInstanceQuestions[this.currentQuestionIndex].selectedAnswerId);
    if (this.currentQuestionIndex > 0) {
      this.currentQuestionIndex--;
      this.startQuestionTimer(); 
    }
  }

  // Sonraki soruya git
  nextQuestion() {
    this.persistAnswer(this.testInstance.testInstanceQuestions[this.currentQuestionIndex].selectedAnswerId);
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
