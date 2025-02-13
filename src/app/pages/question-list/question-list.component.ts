import { CommonModule } from '@angular/common';
import { Component, effect, inject, input, Input, OnInit, signal } from '@angular/core';
import { TestInstanceQuestion } from '../../models/test-instance';
import { QuestionLiteViewComponent } from '../question-lite-view/question-lite-view.component';
import { QuestionService } from '../../services/question.service';
import { TestService } from '../../services/test.service';

@Component({
  selector: 'app-question-list',
  imports: [CommonModule, QuestionLiteViewComponent],
  standalone: true,
  templateUrl: './question-list.component.html',
  styleUrl: './question-list.component.scss'
})
export class QuestionListComponent {
  @Input() isPracticeTest: boolean = false;
  @Input() questionListTpe: string = 'lite';  
  @Input() questions: TestInstanceQuestion[] = [];
  
  testId = input<number>();
  questionService = inject(QuestionService);

  constructor() {
    effect(() => {
      if (this.testId()) {  // `userId` değişirse çalışır
        console.log('Test ID Değişti : ', this.testId()); 
        this.questionService.getAll(this.testId()).subscribe(questions => {
          this.questions = [];
          let questionNumner = 1;
          
              questions.map(q => this.questions.push({
                  id: this.testId() || 0,
                    question: q,
                    order: questionNumner++,
                    selectedAnswerId: 0,
                    timeTaken: 0
                  }));
              
        });  
      }
    });
  }
}

