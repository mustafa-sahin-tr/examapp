import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { Observable } from 'rxjs';
import {
  QUESTION_REPORT_NOTE_MAX_LENGTH,
  QUESTION_REPORT_REASONS,
  QuestionReportCreated,
  QuestionReportReason,
} from '../../models/question-report';

export interface QuestionReportDialogLabels {
  title: string;
  message: string;
  reasonLabel: string;
  reason: (reason: QuestionReportReason) => string;
  noteLabel: string;
  send: string;
  cancel: string;
  error: string;
}

export interface QuestionReportDialogData {
  labels: QuestionReportDialogLabels;
  submit: (reason: QuestionReportReason, note: string | null) => Observable<QuestionReportCreated>;
}

/**
 * Issue #781: neden ve isteğe bağlı not. Kaydı diyalog yapar: yalnız kayıt başarılıysa sonuçla kapanır,
 * hata olursa açık kalır ve öğrencinin yazdığı not kaybolmaz.
 */
@Component({
  selector: 'app-question-report-dialog',
  standalone: true,
  imports: [MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatRadioModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.labels.title }}</h2>
    <mat-dialog-content>
      <p class="report__message">{{ data.labels.message }}</p>
      <p class="report__label" id="question-report-reason">{{ data.labels.reasonLabel }}</p>
      <mat-radio-group class="report__reasons" aria-labelledby="question-report-reason" [value]="reason()"
        (change)="reason.set($event.value)">
        @for (option of reasons; track option) {
          <mat-radio-button [value]="option">{{ data.labels.reason(option) }}</mat-radio-button>
        }
      </mat-radio-group>
      <mat-form-field class="report__note" appearance="outline" subscriptSizing="dynamic">
        <mat-label>{{ data.labels.noteLabel }}</mat-label>
        <textarea matInput rows="3" [attr.maxlength]="noteMaxLength" [value]="note()"
          (input)="note.set($any($event.target).value)"></textarea>
        <mat-hint align="end">{{ note().length }} / {{ noteMaxLength }}</mat-hint>
      </mat-form-field>
      @if (failed()) {
        <p class="report__error" role="alert">{{ data.labels.error }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>{{ data.labels.cancel }}</button>
      <button mat-flat-button type="button" class="report__send" [disabled]="!reason() || saving()" (click)="send()">
        {{ data.labels.send }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .report__message {
      margin: 0 0 16px;
    }

    .report__label {
      margin: 0 0 4px;
      font-weight: 600;
    }

    .report__reasons {
      display: flex;
      flex-direction: column;
      margin-bottom: 16px;
    }

    .report__note {
      width: 100%;
    }

    .report__error {
      margin: 12px 0 0;
      color: var(--ms-danger-border);
      font-weight: 600;
    }
  `,
})
export class QuestionReportDialogComponent {
  protected readonly data = inject<QuestionReportDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<QuestionReportDialogComponent, QuestionReportCreated>>(MatDialogRef);

  protected readonly reasons = QUESTION_REPORT_REASONS;
  protected readonly noteMaxLength = QUESTION_REPORT_NOTE_MAX_LENGTH;
  protected readonly reason = signal<QuestionReportReason | null>(null);
  protected readonly note = signal('');
  protected readonly saving = signal(false);
  protected readonly failed = signal(false);

  protected send(): void {
    const reason = this.reason();
    if (!reason || this.saving()) return;

    this.saving.set(true);
    this.failed.set(false);
    this.data.submit(reason, this.note().trim() || null).subscribe({
      next: (result) => this.dialogRef.close(result),
      error: () => {
        this.saving.set(false);
        this.failed.set(true);
      },
    });
  }
}
