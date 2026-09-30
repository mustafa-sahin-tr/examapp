import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioChange, MatRadioModule } from '@angular/material/radio';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Subscription, filter, merge, take } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  WORKSHEET_COMMENT_REPORT_NOTE_MAX_LENGTH,
  WORKSHEET_COMMENT_REPORT_REASONS,
  WorksheetCommentReportReason,
  WorksheetCommentReportResult,
} from '../../../models/worksheet-comment.model';
import { WorksheetCommentService } from '../../../services/worksheet-comment.service';
import { commentErrorMessage } from '../comment-thread/comment-error';

const COMMENTS_SCOPE = 'comments';

export interface CommentReportDialogData {
  worksheetId: number;
  commentId: number;
}

/**
 * Issue #305 — yorum şikayeti: neden (radio, zorunlu) + isteğe bağlı not (≤500, sayaç). İstek dialog içinde atılır;
 * başarıda sunucu yanıtıyla kapanır (`alreadyReported` idempotent tekrar). Hata dialog'da gösterilir.
 * Dialog overlay'de açıldığı için `comments` scope'unu kendisi sağlar.
 */
@Component({
  selector: 'app-comment-report-dialog',
  standalone: true,
  imports: [
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatRadioModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(COMMENTS_SCOPE)],
  templateUrl: './comment-report-dialog.component.html',
  styleUrls: ['./comment-report-dialog.component.scss'],
})
export class CommentReportDialogComponent {
  private readonly data = inject<CommentReportDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef =
    inject<MatDialogRef<CommentReportDialogComponent, WorksheetCommentReportResult | undefined>>(MatDialogRef);
  private readonly api = inject(WorksheetCommentService);
  private readonly transloco = inject(TranslocoService);

  protected readonly reasons = WORKSHEET_COMMENT_REPORT_REASONS;
  protected readonly maxNote = WORKSHEET_COMMENT_REPORT_NOTE_MAX_LENGTH;

  protected readonly reason = signal<WorksheetCommentReportReason | null>(null);
  protected readonly note = signal('');
  protected readonly noteLength = computed(() => this.note().length);
  protected readonly noteTooLong = computed(() => this.noteLength() > this.maxNote);
  protected readonly canSubmit = computed(() => this.reason() !== null && !this.noteTooLong() && !this.submitting());

  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  private request?: Subscription;

  constructor() {
    // Hata metinleri şablon dışında senkron `translate()` ile okunur; scope baştan yüklensin.
    this.transloco.load(`${COMMENTS_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
    // `disableClose` yalnız istek sürerken anlamlı: boşta ESC ve backdrop tıklaması dialog'u kapatır.
    merge(this.dialogRef.backdropClick(), this.dialogRef.keydownEvents().pipe(filter((event) => event.key === 'Escape')))
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.cancel());
  }

  protected onReason(event: MatRadioChange): void {
    this.reason.set(event.value as WorksheetCommentReportReason);
  }

  protected onNote(event: Event): void {
    this.note.set((event.target as HTMLTextAreaElement).value);
  }

  protected cancel(): void {
    if (!this.submitting()) {
      this.dialogRef.close();
    }
  }

  protected submit(): void {
    const reason = this.reason();
    if (!reason || !this.canSubmit()) {
      return;
    }
    this.submitting.set(true);
    this.error.set(null);
    this.request = this.api
      .report(this.data.worksheetId, this.data.commentId, { reason, note: this.note() })
      .subscribe({
        next: (result) => {
          this.submitting.set(false);
          this.dialogRef.close({ alreadyReported: result?.alreadyReported === true, reportedByMe: true });
        },
        error: (err: unknown) => {
          this.submitting.set(false);
          this.error.set(commentErrorMessage(err, this.t, 'reportDialog.error'));
        },
      });
  }

  private readonly t = (key: string, params?: Record<string, unknown>): string =>
    this.transloco.translate<string>(`${COMMENTS_SCOPE}.${key}`, params) ?? key;
}

/** Dialog'u açar. `disableClose`: ESC/backdrop komponentte ele alınır — istek sürerken kapanmaz. */
export function openCommentReportDialog(
  dialog: MatDialog,
  data: CommentReportDialogData
): MatDialogRef<CommentReportDialogComponent, WorksheetCommentReportResult | undefined> {
  return dialog.open<CommentReportDialogComponent, CommentReportDialogData, WorksheetCommentReportResult | undefined>(
    CommentReportDialogComponent,
    { data, disableClose: true, autoFocus: 'first-tabbable', restoreFocus: true, width: '480px', maxWidth: '94vw' }
  );
}
