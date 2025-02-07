import { Component, OnInit } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';

@Component({
  selector: 'app-exam',
  standalone: true,
  templateUrl: './exam.component.html',
  styleUrls: ['./exam.component.scss'],
})
export class ExamComponent implements OnInit {
  questions: any[] = [];
  currentQuestionIndex = 0;
  startTime: number = 0;
  timer: number = 0;
  interval: any;

  constructor(private http: HttpClient, private router: Router) {}

  ngOnInit() {
    this.fetchQuestions();
  }

  fetchQuestions() {
    this.http.get<any[]>('/api/exam/questions').subscribe(response => {
      this.questions = response;
      this.startQuestionTimer();
    });
  }

  startQuestionTimer() {
    this.startTime = Date.now();
    this.timer = 0;

    if (this.interval) clearInterval(this.interval);
    this.interval = setInterval(() => {
      this.timer++;
    }, 1000);
  }

  nextQuestion() {
    if (this.currentQuestionIndex < this.questions.length - 1) {
      this.currentQuestionIndex++;
      this.startQuestionTimer();
    }
  }

  previousQuestion() {
    if (this.currentQuestionIndex > 0) {
      this.currentQuestionIndex--;
      this.startQuestionTimer();
    }
  }

  onAnswerSelected(selectedIndex: number) {
    const elapsedTime = (Date.now() - this.startTime) / 1000;
    
    this.http.post('/api/exam/submit-answer', {
      questionId: this.questions[this.currentQuestionIndex].id,
      selectedAnswer: selectedIndex,
      timeSpent: elapsedTime
    }).subscribe(() => {
      this.nextQuestion();
    });
  }
}
