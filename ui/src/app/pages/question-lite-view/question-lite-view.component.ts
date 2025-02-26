import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnInit, Output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { Question } from '../../models/question';
import { SafeHtmlPipe } from '../../services/safehtml';

@Component({
  selector: 'app-question-lite-view',
  standalone: true,
  imports: [CommonModule,MatCardModule, MatButtonModule, SafeHtmlPipe],
  templateUrl: './question-lite-view.component.html',
  styleUrl: './question-lite-view.component.scss'
})
export class QuestionLiteViewComponent implements OnInit {
    correctAnswerVisible: boolean = false;
    @Output() answerOpened = new EventEmitter<number>(); // 🆕 Event tanımlandı
    @Output() answerSelected = new EventEmitter<number>(); // 🆕 Event tanımlandı
    @Input() isPracticeTest: boolean = false; // 🆕 Practice test mi yoksa gerçek sınav mı olduğunu belirlemek için
    @Input() question: Question | null = null; // Soru
    @Input() selectedAnswerId: number | null = null; // Kullanıcının seçtiği şık
    showFeedback: boolean = false; // Kullanıcının seçim yaptığı anı kontrol etme
    correctAnswerIndex: number | null = null; // Doğru şık (API'den dönecek)

  ngOnInit() {
    console.log('Lite : ', this.question);
  }

  

  getAnswerLabel(i: number) {
    return String.fromCharCode(65 + i);
  }

  showCorrectAnswer() {
    // if (this.selectedAnswerIndex !== null) return; // Kullanıcı sadece 1 kez seçim yapabilir
    console.log('selected index: ',this.selectedAnswerId);
    this.correctAnswerVisible = true;
    this.answerOpened.emit(1); // 🆕 Seçilen cevap üst componente gönderiliyor
  }


  selectAnswer(id: number) {
    // if (this.selectedAnswerIndex !== null) return; // Kullanıcı sadece 1 kez seçim yapabilir
    this.selectedAnswerId = id;

    console.log('selected index: ',this.selectedAnswerId);
    this.answerSelected.emit(id); // 🆕 Seçilen cevap üst componente gönderiliyor
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
    return this.showFeedback && index === this.selectedAnswerId && index !== this.correctAnswerIndex;
  }

  
}
