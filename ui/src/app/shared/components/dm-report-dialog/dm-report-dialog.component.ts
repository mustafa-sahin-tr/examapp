import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioChange, MatRadioModule } from '@angular/material/radio';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Subscription, filter, merge, take } from 'rxjs';
import {
  DIRECT_MESSAGES_SCOPE,
  DIRECT_MESSAGE_REPORT_NOTE_MAX_LENGTH,
  DIRECT_MESSAGE_REPORT_REASONS,
  DirectMessageReportReason,
} from '../../../models/direct-message.model';
import { DirectMessageService } from '../../../services/direct-message.service';
import { directMessageError } from '../../utils/direct-message-error.util';

export interface DmReportDialogData {
  conversationId: number;
  /** Verilirse mesaj şikayeti; yoksa konuşma şikayeti. */
  messageId?: number;
}

export interface DmReportDialogResult {
  alreadyReported: boolean;
}

/**
 * Issue #106 — DM şikayeti: neden (radio, zorunlu) + isteğe bağlı not (≤500, sayaç). İstek dialog içinde atılır;
 * başarıda kapanır, çağıran "Şikayetiniz alındı" geri bildirimini gösterir. Not düz metin gönderilir.
 * Overlay'de açıldığı için `direct-messages` scope'unu kendisi sağlar (desen: `comment-report-dialog`).
 */
@Component({
  selector: 'app-dm-report-dialog',
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
  providers: [provideTranslocoScope(DIRECT_MESSAGES_SCOPE)],
  templateUrl: './dm-report-dialog.component.html',
  styleUrls: ['./dm-report-dialog.component.scss'],
})
export class DmReportDialogComponent {
  protected readonly data = inject<DmReportDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<DmReportDialogComponent, DmReportDialogResult | undefined>>(MatDialogRef);
  private readonly api = inject(DirectMessageService);
  private readonly transloco = inject(TranslocoService);

  protected readonly reasons = DIRECT_MESSAGE_REPORT_REASONS;
  protected readonly maxNote = DIRECT_MESSAGE_REPORT_NOTE_MAX_LENGTH;
  protected readonly isMessageReport = this.data.messageId != null;

  protected readonly reason = signal<DirectMessageReportReason | null>(null);
  protected readonly note = signal('');
  protected readonly noteLength = computed(() => this.note().length);
  protected readonly noteTooLong = computed(() => this.noteLength() > this.maxNote);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly canSubmit = computed(() => this.reason() !== null && !this.noteTooLong() && !this.submitting());

  private request?: Subscription;

  constructor() {
    // Hata metinleri şablon dışında senkron `translate()` ile okunur; scope baştan yüklensin.
    this.transloco.load(`${DIRECT_MESSAGES_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
    // `disableClose` yalnız istek sürerken anlamlı: boşta ESC ve backdrop dialog'u kapatır.
    merge(this.dialogRef.backdropClick(), this.dialogRef.keydownEvents().pipe(filter((event) => event.key === 'Escape')))
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.cancel());
  }

  protected onReason(event: MatRadioChange): void {
    this.reason.set(event.value as DirectMessageReportReason);
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
      .report(this.data.conversationId, { messageId: this.data.messageId, reason, note: this.note() })
      .subscribe({
        next: (result) => {
          this.submitting.set(false);
          this.dialogRef.close({ alreadyReported: result?.alreadyReported === true });
        },
        error: (err: unknown) => {
          this.submitting.set(false);
          this.error.set(directMessageError(err, this.t, 'reportDialog.error').message);
        },
      });
  }

  private readonly t = (key: string, params?: Record<string, unknown>): string =>
    this.transloco.translate<string>(`${DIRECT_MESSAGES_SCOPE}.${key}`, params) ?? key;
}

/** Dialog'u açar. `disableClose`: ESC/backdrop komponentte ele alınır — istek sürerken kapanmaz. */
export function openDmReportDialog(
  dialog: MatDialog,
  data: DmReportDialogData,
): MatDialogRef<DmReportDialogComponent, DmReportDialogResult | undefined> {
  return dialog.open<DmReportDialogComponent, DmReportDialogData, DmReportDialogResult | undefined>(DmReportDialogComponent, {
    data,
    disableClose: true,
    autoFocus: 'first-tabbable',
    restoreFocus: true,
    width: '480px',
    maxWidth: '94vw',
  });
}
