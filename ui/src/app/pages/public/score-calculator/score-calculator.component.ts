import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';

import { LANDING_REGISTER_STUDENT_URL } from '../landing/landing-content';
import { PUBLIC_PAGES_SCOPE, usePublicPageMeta } from '../public-page-meta';
import { ScoreCalculatorInfoDialogComponent } from './score-calculator-info.dialog';
import { EXAM_SCORE_EXAMS, ExamScoreExam, ExamScoreTest, TestAnswers, estimateScores, isValidAnswers, netOf } from './exam-score';

type AnswerField = keyof TestAnswers;

/** #694: hesapsız puan tahmini. Girilen sayılar yalnız bu sinyalde durur; istek atılmaz, kayıt tutulmaz. */
@Component({
  selector: 'app-score-calculator',
  standalone: true,
  imports: [MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatInputModule, RouterLink, TranslocoDirective],
  providers: [provideTranslocoScope(PUBLIC_PAGES_SCOPE)],
  templateUrl: './score-calculator.component.html',
  styleUrl: './score-calculator.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ScoreCalculatorComponent {
  private readonly transloco = inject(TranslocoService);
  private readonly dialog = inject(MatDialog);
  readonly exams = EXAM_SCORE_EXAMS;
  readonly registerUrl = LANDING_REGISTER_STUDENT_URL;
  readonly examId = signal(this.exams[0].id);
  private readonly answers = signal<Record<string, Partial<TestAnswers>>>({});

  readonly exam = computed<ExamScoreExam>(() => this.exams.find((exam) => exam.id === this.examId())!);
  readonly sessions = computed(() => {
    const tests = this.exam().tests;
    return [...new Set(tests.map((test) => test.session))].map((id) => ({ id, tests: tests.filter((test) => test.session === id) }));
  });
  readonly hasInput = computed(() => this.exam().tests.some((test) => this.fieldValue(test, 'correct') !== null || this.fieldValue(test, 'wrong') !== null));
  readonly hasInvalid = computed(() => this.exam().tests.some((test) => !this.isValid(test)));
  readonly estimates = computed(() => {
    if (this.hasInvalid()) {
      return [];
    }
    const exam = this.exam();
    const answers = Object.fromEntries(exam.tests.map((test) => [test.id, this.answersOf(test)]));
    return estimateScores(exam, answers);
  });

  constructor() {
    usePublicPageMeta('scoreCalculator', [{ name: 'robots', content: 'index, follow' }]);
  }

  selectExam(id: string): void {
    this.examId.set(id);
  }

  openInfo(): void {
    this.dialog.open(ScoreCalculatorInfoDialogComponent, { data: this.exam(), width: '640px', maxWidth: '95vw' });
  }

  fieldValue(test: ExamScoreTest, field: AnswerField): number | null {
    return this.answers()[test.id]?.[field] ?? null;
  }

  setField(test: ExamScoreTest, field: AnswerField, value: string): void {
    this.answers.update((all) => {
      const next = { ...all[test.id] };
      if (value === '') {
        delete next[field];
      } else {
        next[field] = Number(value);
      }
      return { ...all, [test.id]: next };
    });
  }

  isValid(test: ExamScoreTest): boolean {
    return isValidAnswers(this.answersOf(test), test.questions);
  }

  netDisplay(test: ExamScoreTest): string {
    if (!this.isValid(test)) {
      return '–';
    }
    const net = netOf(this.answersOf(test), this.exam().wrongAnswersPerCorrect);
    return net.toLocaleString(this.transloco.getActiveLang(), { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }

  private answersOf(test: ExamScoreTest): TestAnswers {
    const answer = this.answers()[test.id];
    return { correct: answer?.correct ?? 0, wrong: answer?.wrong ?? 0 };
  }
}
