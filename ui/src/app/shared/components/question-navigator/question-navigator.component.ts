import { CommonModule } from '@angular/common';
import { Component, Input, signal, WritableSignal } from '@angular/core';

@Component({
  selector: 'app-question-navigator',
  imports: [CommonModule],
  standalone: true,
  templateUrl: './question-navigator.component.html',
  styleUrl: './question-navigator.component.scss'
})
export class QuestionNavigatorComponent {
  @Input() questions: { status: 'correct' | 'incorrect' | 'unknown' }[] = 
   [
  { status: 'correct' },
  { status: 'incorrect' },
  { status: 'unknown' },
  { status: 'correct' },
  { status: 'incorrect' },
  { status: 'unknown' },
  { status: 'correct' },
  { status: 'incorrect' },
  { status: 'unknown' },
  { status: 'correct' },
  { status: 'incorrect' },
  { status: 'unknown' },
];
  selectedQuestion: WritableSignal<number | null> = signal(null);

  selectQuestion(index: number) {
    this.selectedQuestion.set(index);
  }
}
