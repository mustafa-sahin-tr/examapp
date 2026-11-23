import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { finalize } from 'rxjs';
import { SessionReview, SessionReviewQuestion, hasDrawing } from '../../../models/session-review';
import { TestService } from '../../../services/test.service';
import { SessionQuestionViewComponent } from './session-question-view.component';

export interface SessionReviewDialogData {
  worksheetId: number;
  testInstanceId: number;
  studentName: string;
}

/**
 * Issue #1310: öğretmen, ödev tablosundan öğrencinin bitmiş testini soru soru görür — cevabı, doğrusu, süresi ve çizimleri.
 * Salt okunur.
 */
@Component({
  selector: 'app-session-review-dialog',
  standalone: true,
  imports: [
    MatButtonModule,
    MatCheckboxModule,
    MatDialogModule,
    MatIconModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
    SessionQuestionViewComponent,
  ],
  providers: [provideTranslocoScope('session-review')],
  templateUrl: './session-review-dialog.component.html',
  styleUrl: './session-review-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SessionReviewDialogComponent implements OnInit {
  protected readonly data = inject<SessionReviewDialogData>(MAT_DIALOG_DATA);
  private readonly testService = inject(TestService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly loading = signal(true);
  protected readonly notFound = signal(false);
  protected readonly failed = signal(false);
  protected readonly review = signal<SessionReview | null>(null);
  protected readonly onlyDrawings = signal(false);
  protected readonly onlyWrong = signal(false);
  protected readonly currentId = signal<number | null>(null);

  protected readonly hasDrawing = hasDrawing;
  protected readonly drawingCount = computed(() => this.review()?.questions.filter(hasDrawing).length ?? 0);
  protected readonly counts = computed(() => {
    const questions = this.review()?.questions ?? [];
    return {
      correct: questions.filter((q) => q.outcome === 'correct').length,
      wrong: questions.filter((q) => q.outcome === 'wrong').length,
      blank: questions.filter((q) => q.outcome === 'blank').length,
    };
  });
  protected readonly visible = computed(() =>
    (this.review()?.questions ?? []).filter(
      (q) => (!this.onlyDrawings() || hasDrawing(q)) && (!this.onlyWrong() || q.outcome === 'wrong'),
    ),
  );
  protected readonly current = computed<SessionReviewQuestion | null>(() => {
    const visible = this.visible();
    return visible.find((q) => q.instanceQuestionId === this.currentId()) ?? visible[0] ?? null;
  });
  protected readonly currentIndex = computed(() => {
    const current = this.current();
    return current ? this.visible().indexOf(current) : -1;
  });

  ngOnInit(): void {
    this.testService
      .getSessionReview(this.data.worksheetId, this.data.testInstanceId)
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (review) => {
          this.review.set(review);
          // Karalama varsa önce onlar: öğretmenin bakmak istediği bunlar.
          this.onlyDrawings.set(review.questions.some(hasDrawing));
        },
        error: (error: HttpErrorResponse) => (error?.status === 404 ? this.notFound : this.failed).set(true),
      });
  }

  protected select(question: SessionReviewQuestion): void {
    this.currentId.set(question.instanceQuestionId);
  }

  protected step(delta: number): void {
    const next = this.visible()[this.currentIndex() + delta];
    if (next) this.select(next);
  }

  protected minutes(seconds: number): { minutes: number; seconds: number } {
    return { minutes: Math.floor(seconds / 60), seconds: seconds % 60 };
  }
}
