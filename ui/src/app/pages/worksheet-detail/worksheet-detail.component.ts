import { CommonModule } from '@angular/common';
import { Component, inject, Input, OnInit, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, NavigationExtras, Router } from '@angular/router';
import { Test, TestInstance, TestInstanceQuestion } from '../../models/test-instance';
import { lastValueFrom } from 'rxjs';
import { TestService } from '../../services/test.service';
import { QuestionNavigatorComponent } from '../../shared/components/question-navigator/question-navigator.component';
import { AnswerChoice, QuestionRegion } from '../../models/draws';
import { QuestionCanvasViewComponent } from '../../shared/components/question-canvas-view/question-canvas-view.component';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { IsStudentDirective, IsTeacherDirective } from '../../shared/directives/is-student.directive';

@Component({
  selector: 'app-worksheet-detail',
  imports: [
    CommonModule,
    MatIconModule,
    QuestionNavigatorComponent,
    QuestionCanvasViewComponent,
    MatButtonModule,
    IsStudentDirective,
    IsTeacherDirective,
  ],
  templateUrl: './worksheet-detail.component.html',
  styleUrl: './worksheet-detail.component.scss',
})
export class WorksheetDetailComponent implements OnInit {
  private snackBar = inject(MatSnackBar);
  @Input() exam!: Test; // Test bilgisi ve sorular
  route = inject(ActivatedRoute);
  testService = inject(TestService);
  router = inject(Router);
  results!: TestInstance;
  public regions = signal<QuestionRegion[]>([]); // Soru bölgeleri
  public selectedChoices = signal<Map<number, AnswerChoice>>(new Map()); // 🔄 Her soru için seçilen şıkkı sakla
  public correctChoices = signal<Map<number, AnswerChoice>>(new Map()); // 🔄 Her soru için seçilen şıkkı sakla
  testId!: number;
  questions: { status: 'correct' | 'incorrect' | 'unknown' }[] = [];
  public currentIndex = signal(0);
  StartTest(id: number | null) {
    if (id) {
      this.testService.startTest(id).subscribe((response) => {
        if (response.success) {
          this.router.navigate(['/testsolve', response.instanceId]);
        } else {
          this.snackBar.open(response.message, 'Tamam', { duration: 2000 });
        }
      });
    }
  }

  get instanceStatus(): number | undefined {
    return this.exam.instance?.status;
  }

  get instanceCompleted(): boolean {
    return this.exam.instance?.status === 1;
  }

  get instanceStarted(): boolean {
    return this.exam.instance?.status === 0;
  }

  editWorksheet(id: number | null) {
    if (id) {
      this.router.navigate(['/exam', id]);
    }
  }

  navigateToQuestionCanvas() {
    const navigationExtras: NavigationExtras = {
      state: {
        subjectId: null,
        topicId: null,
        subtopicId: null,
        testId: this.exam.subtitle,
        bookId: this.exam.bookId,
        bookTestId: this.exam.bookTestId,
        testValue: this.exam.id,
      },
    };

    setTimeout(() => {
      this.router.navigate(['/questioncanvas'], navigationExtras);
    }, 1000);
  }

  questionSelected(index: number) {
    const question = this.results.testInstanceQuestions[index];
    console.log('Selected question:', question);
    this.currentIndex.set(index);
  }

  ngOnInit() {
    this.route.paramMap.subscribe(async (params) => {
      this.testId = Number(params.get('testId'));
      if (this.testId) {
        this.exam = await lastValueFrom(this.testService.get(this.testId));
        if (this.exam && this.exam.instance) {
          this.testService.getCanvasTestResults(this.exam.instance.id).subscribe((response: TestInstance) => {
            if (response) {
              this.results = response;
              const data: QuestionRegion[] = this.testService.convertTestInstanceToRegions(this.results);
              this.regions.set(data);

              this.results.testInstanceQuestions.forEach((q: TestInstanceQuestion) => {
                if (q.selectedAnswerId) {
                  const selectedChoice = this.regions()
                    .find((a) => a.id == q.question.id)
                    ?.answers.find((a) => a.id === q.selectedAnswerId);
                  if (selectedChoice) {
                    const updatedChoices = new Map(this.selectedChoices());
                    updatedChoices.set(q.question.id, selectedChoice);
                    this.selectedChoices.set(updatedChoices);
                  }
                }

                const correctChoice = this.regions()
                  .find((a) => a.id == q.question.id)
                  ?.answers.find((a) => a.id === q.question.correctAnswerId);
                const updatedCorrectChoices = new Map(this.correctChoices());
                if (correctChoice) {
                  updatedCorrectChoices.set(q.question.id, correctChoice);
                  this.correctChoices.set(updatedCorrectChoices);
                }
              });

              this.questions = this.results.testInstanceQuestions.map((tiq) => {
                if (tiq.selectedAnswerId === null) {
                  return { status: 'unknown' };
                } else if (tiq.selectedAnswerId === tiq.question.correctAnswerId) {
                  return { status: 'correct' };
                } else {
                  return { status: 'incorrect' };
                }
              });
            }
          });
        }
      }
    });
  }
}
