import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { LearningOutcome } from '../../../models/learning-outcome';
import { LearningOutcomeService } from '../../../services/learning-outcome.service';
import { LearningOutcomePickerComponent } from './learning-outcome-picker.component';

export interface LearningOutcomeDialogData {
  questionId: number;
  gradeId: number | null;
  subjectId: number | null;
}

/** Issue #692: kayıtlı sorunun kazanımlarını düzenler. */
@Component({
  selector: 'app-learning-outcome-dialog',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoDirective, MatButtonModule, MatDialogModule, LearningOutcomePickerComponent],
  providers: [provideTranslocoScope('learning-outcomes')],
  template: `
    <ng-container *transloco="let t; prefix: 'learning-outcomes'">
      <h2 mat-dialog-title>{{ t('dialogTitle', { id: data.questionId }) }}</h2>
      <mat-dialog-content>
        @if (error()) {
          <p role="alert" data-testid="outcome-dialog-error">{{ t(error()!) }}</p>
        }
        @if (!loading()) {
          <app-learning-outcome-picker [gradeId]="data.gradeId" [subjectId]="data.subjectId"
            [selected]="selected()" (selectedChange)="selected.set($event)" />
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ t('cancel') }}</button>
        <button mat-flat-button type="button" data-testid="outcome-dialog-save" [disabled]="loading() || saving()" (click)="save()">
          {{ t('save') }}
        </button>
      </mat-dialog-actions>
    </ng-container>
  `,
})
export class LearningOutcomeDialogComponent implements OnInit {
  protected readonly data = inject<LearningOutcomeDialogData>(MAT_DIALOG_DATA);
  private readonly service = inject(LearningOutcomeService);
  private readonly dialogRef = inject(MatDialogRef<LearningOutcomeDialogComponent>);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly selected = signal<LearningOutcome[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<'loadError' | 'saveError' | null>(null);

  ngOnInit(): void {
    this.service
      .getForQuestion(this.data.questionId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (outcomes) => {
          this.selected.set(outcomes);
          this.loading.set(false);
        },
        // Mevcut bağlar bilinmeden kaydetmek onları silerdi; kaydet kapalı kalır.
        error: () => this.error.set('loadError'),
      });
  }

  protected save(): void {
    this.saving.set(true);
    this.error.set(null);
    this.service
      .setForQuestion(this.data.questionId, this.selected().map((outcome) => outcome.id))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.dialogRef.close(true),
        error: () => {
          this.saving.set(false);
          this.error.set('saveError');
        },
      });
  }
}
