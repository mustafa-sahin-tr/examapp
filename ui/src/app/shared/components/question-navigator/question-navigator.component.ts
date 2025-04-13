import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output, signal, WritableSignal } from '@angular/core';

@Component({
  selector: 'app-question-navigator',
  imports: [CommonModule],
  standalone: true,
  templateUrl: './question-navigator.component.html',
  styleUrl: './question-navigator.component.scss'
})
export class QuestionNavigatorComponent {
  @Output() questionSelected = new EventEmitter<number>();
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
  selectedQuestion: WritableSignal<number | null> = signal(0);

  selectQuestion(index: number) {
    this.selectedQuestion.set(index);
    this.questionSelected.emit(index);
  }
}
