import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Subscription, filter, merge, take } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { WORKSHEET_COMMENT_HIDE_REASON_MAX_LENGTH, WorksheetComment } from '../../../models/worksheet-comment.model';
import { WorksheetCommentService } from '../../../services/worksheet-comment.service';
import { commentErrorMessage } from '../comment-thread/comment-error';

const COMMENTS_SCOPE = 'comments';

export interface CommentHideDialogData {
  worksheetId: number;
  commentId: number;
}

/**
 * Issue #305 — yorumu gizleme onayı: neden zorunlu (1..500, boşluktan ibaret olamaz; sayaç). İstek dialog içinde;
 * başarıda sunucunun döndürdüğü (moderatör görünümündeki) yorumla kapanır. Hata dialog'da gösterilir.
 */
@Component({
  selector: 'app-comment-hide-dialog',
  standalone: true,
  imports: [
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(COMMENTS_SCOPE)],
  templateUrl: './comment-hide-dialog.component.html',
  styleUrls: ['./comment-hide-dialog.component.scss'],
})
export class CommentHideDialogComponent {
  private readonly data = inject<CommentHideDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<CommentHideDialogComponent, WorksheetComment | undefined>>(MatDialogRef);
  private readonly api = inject(WorksheetCommentService);
  private readonly transloco = inject(TranslocoService);

  protected readonly max = WORKSHEET_COMMENT_HIDE_REASON_MAX_LENGTH;
  protected readonly reason = signal('');
  protected readonly touched = signal(false);
  protected readonly length = computed(() => this.reason().length);
  protected readonly tooLong = computed(() => this.length() > this.max);
  protected readonly blank = computed(() => this.reason().trim().length === 0);
  protected readonly showRequired = computed(() => this.touched() && this.blank());
  protected readonly canSubmit = computed(() => !this.blank() && !this.tooLong() && !this.submitting());

  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  private request?: Subscription;

  constructor() {
    this.transloco.load(`${COMMENTS_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
    // `disableClose` yalnız istek sürerken anlamlı: boşta ESC ve backdrop tıklaması dialog'u kapatır.
    merge(this.dialogRef.backdropClick(), this.dialogRef.keydownEvents().pipe(filter((event) => event.key === 'Escape')))
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.cancel());
  }

  protected onInput(event: Event): void {
    this.reason.set((event.target as HTMLTextAreaElement).value);
  }

  protected cancel(): void {
    if (!this.submitting()) {
      this.dialogRef.close();
    }
  }

  protected submit(): void {
    this.touched.set(true);
    if (!this.canSubmit()) {
      return;
    }
    this.submitting.set(true);
    this.error.set(null);
    this.request = this.api.hide(this.data.worksheetId, this.data.commentId, this.reason()).subscribe({
      next: (comment) => {
        this.submitting.set(false);
        this.dialogRef.close(comment);
      },
      error: (err: unknown) => {
        this.submitting.set(false);
        this.error.set(commentErrorMessage(err, this.t, 'hideDialog.error'));
      },
    });
  }

  private readonly t = (key: string, params?: Record<string, unknown>): string =>
    this.transloco.translate<string>(`${COMMENTS_SCOPE}.${key}`, params) ?? key;
}

export function openCommentHideDialog(
  dialog: MatDialog,
  data: CommentHideDialogData
): MatDialogRef<CommentHideDialogComponent, WorksheetComment | undefined> {
  return dialog.open<CommentHideDialogComponent, CommentHideDialogData, WorksheetComment | undefined>(
    CommentHideDialogComponent,
    { data, disableClose: true, autoFocus: 'first-tabbable', restoreFocus: true, width: '480px', maxWidth: '94vw' }
  );
}
