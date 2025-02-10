import { Component, EventEmitter, Input, OnInit, Output } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { QuestionService } from '../services/question.service';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { CommonModule } from '@angular/common';
import { Question } from '../models/question';

@Component({
  selector: 'app-question-view',
  standalone: true,
  templateUrl: './question-view.component.html',
  styleUrls: ['./question-view.component.scss'],
  imports: [ MatCardModule, MatButtonModule, CommonModule]
})
export class QuestionViewComponent implements OnInit {
  @Input() isPracticeTest: boolean = false; // 🆕 Practice test mi yoksa gerçek sınav mı olduğunu belirlemek için
  @Input() question: Question | null = null; // Soru
  @Output() answerSelected = new EventEmitter<number>(); // 🆕 Event tanımlandı
  @Output() answerOpened = new EventEmitter<number>(); // 🆕 Event tanımlandı
  @Input() selectedAnswerIndex: number | null = null; // Kullanıcının seçtiği şık
  correctAnswerIndex: number | null = null; // Doğru şık (API'den dönecek)
  showFeedback: boolean = false; // Kullanıcının seçim yaptığı anı kontrol etme
  layouts = ['single-column', 'two-column', 'grid', 'top-image']; // Farklı layout seçenekleri
  selectedLayout = 'single-column'; // Varsayılan layout
  correctAnswerVisible = false;

  
  constructor(private questionService: QuestionService) {}

  // fetchQuestion() {
  //   this.questionService.get(this.questionId).subscribe(response => {
  //     this.question = response;
  //   });
  // }

  ngOnInit() {
    // this.questionId = 4;
    this.selectedLayout = this.layouts[Math.floor(Math.random() * this.layouts.length)]; // Rastgele bir layout seç
    // this.fetchQuestion();
  }

  showCorrectAnswer() {
    // if (this.selectedAnswerIndex !== null) return; // Kullanıcı sadece 1 kez seçim yapabilir
    console.log('selected index: ',this.selectedAnswerIndex);
    this.correctAnswerVisible = true;
    this.answerOpened.emit(1); // 🆕 Seçilen cevap üst componente gönderiliyor
  }


  selectAnswer(index: number) {
    // if (this.selectedAnswerIndex !== null) return; // Kullanıcı sadece 1 kez seçim yapabilir
    this.selectedAnswerIndex = index;

    console.log('selected index: ',this.selectedAnswerIndex);
    this.answerSelected.emit(index); // 🆕 Seçilen cevap üst componente gönderiliyor
    // API'ye kullanıcı cevabının doğru olup olmadığını sorma
    // this.questionService.check(this.questionId,index)
    // .subscribe(response => {
    //   this.correctAnswerIndex = response.correctAnswerIndex;
    //   this.showFeedback = true; // Doğru-yanlış göstermek için flag
    // });
  }

  isCorrect(index: number) {
    return this.showFeedback && index === this.correctAnswerIndex;
  }

  isWrong(index: number) {
    return this.showFeedback && index === this.selectedAnswerIndex && index !== this.correctAnswerIndex;
  }

  getAnswerLabel(i: number) {
    return String.fromCharCode(65 + i);
  }
}
