import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';

export interface ReviewDialogLabels {
  title: string;
  summary: string;
  emptyHeading: string;
  bookmarkedHeading: string;
  allAnswered: string;
  warning: string;
  questionAria: (number: number) => string;
  finish: string;
  keepSolving: string;
}

export interface ReviewDialogData {
  /** Sıfır tabanlı soru indeksleri; ekranda +1 ile gösterilir. */
  emptyIndexes: number[];
  bookmarkedIndexes: number[];
  labels: ReviewDialogLabels;
}

/** `true`: testi bitir. `{ goTo }`: o soruya git. Kapatılırsa sonuç yoktur, test sürer. */
export type ReviewDialogResult = true | { goTo: number } | undefined;

/** Issue #779: bitirmeden önce boş ve işaretli soruları listeler, soruya tek tıkla götürür. */
@Component({
  selector: 'app-test-solve-review-dialog',
  standalone: true,
  imports: [MatButtonModule, MatDialogModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.labels.title }}</h2>
    <mat-dialog-content>
      <p class="review__summary">{{ data.labels.summary }}</p>
      @if (data.emptyIndexes.length) {
        <h3 class="review__heading">{{ data.labels.emptyHeading }}</h3>
        <div class="review__numbers">
          @for (index of data.emptyIndexes; track index) {
            <button mat-stroked-button type="button" class="review__number" [attr.aria-label]="data.labels.questionAria(index + 1)"
              [mat-dialog-close]="{ goTo: index }">{{ index + 1 }}</button>
          }
        </div>
      } @else {
        <p class="review__summary">{{ data.labels.allAnswered }}</p>
      }
      @if (data.bookmarkedIndexes.length) {
        <h3 class="review__heading">{{ data.labels.bookmarkedHeading }}</h3>
        <div class="review__numbers">
          @for (index of data.bookmarkedIndexes; track index) {
            <button mat-stroked-button type="button" class="review__number" [attr.aria-label]="data.labels.questionAria(index + 1)"
              [mat-dialog-close]="{ goTo: index }">{{ index + 1 }}</button>
          }
        </div>
      }
      <p class="review__warning">{{ data.labels.warning }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close cdkFocusInitial>{{ data.labels.keepSolving }}</button>
      <button mat-flat-button type="button" class="review__finish" [mat-dialog-close]="true">{{ data.labels.finish }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    .review__summary {
      margin: 0 0 12px;
    }

    .review__heading {
      margin: 16px 0 8px;
      font-size: 0.95rem;
      font-weight: 700;
    }

    .review__numbers {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }

    .review__number {
      min-width: 44px;
      width: 44px;
      height: 44px;
      padding: 0;
    }

    .review__warning {
      margin: 16px 0 0;
      font-size: 0.875rem;
    }
  `,
})
export class TestSolveReviewDialogComponent {
  readonly data = inject<ReviewDialogData>(MAT_DIALOG_DATA);
}
