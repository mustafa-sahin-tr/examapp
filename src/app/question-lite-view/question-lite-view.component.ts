import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';

@Component({
  selector: 'app-question-lite-view',
  standalone: true,
  imports: [CommonModule,MatCardModule, MatButtonModule],
  templateUrl: './question-lite-view.component.html',
  styleUrl: './question-lite-view.component.scss'
})
export class QuestionLiteViewComponent {
  @Input() question: any;
  @Input() isPracticeTest: boolean = false;
  selectedAnswerIndex: number | null = null;
  correctAnswerVisible: boolean = false;

  selectAnswer(index: number) {
    this.selectedAnswerIndex = index;
    // Backend'e seçilen şıkı gönder (Örn: API servisi ile)
    console.log(`Seçilen Şık: ${index}`);
  }

  showCorrectAnswer() {
    this.correctAnswerVisible = true;
  }

  getAnswerLabel(i: number) {
    return String.fromCharCode(65 + i);
  }
}
