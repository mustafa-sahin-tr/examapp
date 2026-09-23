import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Clipboard } from '@angular/cdk/clipboard';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Subscription, take } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import { AdminPasswordResetTarget } from '../../../models/admin-password-reset.model';

/** Yönetim ekranlarının ortak Transloco scope'u (`public/i18n/admin/<lang>.json`). */
const ADMIN_SCOPE = 'admin';
const TEXT_PREFIX = `${ADMIN_SCOPE}.passwordReset`;

export interface AdminResetPasswordDialogData {
  target: AdminPasswordResetTarget;
  /** Teacher.Id / Student.Id */
  id: number;
  /** Listede gösterilen ad (fallback uygulanmış hâli). */
  displayName: string;
}

type Phase = 'confirm' | 'submitting' | 'done';

export type PasswordResetErrorKey =
  | 'forbidden'
  | 'notFound'
  | 'upstream'
  | 'rateLimited'
  | 'rateLimitedSeconds'
  | 'generic';

/**
 * Issue #156 — Admin şifre sıfırlama: onay → istek → geçici şifrenin tek seferlik gösterimi.
 *
 * Güvenlik: geçici şifre YALNIZCA bu komponentin `password` sinyalinde yaşar. Servis/store/localStorage/
 * router state/console'a yazılmaz, dialog sonucu olarak da dönmez (`close()` argümansız). Dialog kapanırken
 * ve komponent yok edilirken sinyal `null`'a çekilir; yarım kalan istek abonelikten düşülür.
 * İstek dialog içinde atıldığı için yükleniyor / çift tıklama / hata durumu da burada yönetilir.
 */
@Component({
  selector: 'app-admin-reset-password-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective],
  providers: [provideTranslocoScope(ADMIN_SCOPE)],
  templateUrl: './admin-reset-password-dialog.component.html',
  styleUrls: ['./admin-reset-password-dialog.component.scss'],
})
export class AdminResetPasswordDialogComponent {
  readonly data = inject<AdminResetPasswordDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<AdminResetPasswordDialogComponent, void>>(MatDialogRef);
  private readonly adminService = inject(AdminService);
  private readonly transloco = inject(TranslocoService);
  private readonly clipboard = inject(Clipboard);

  readonly phase = signal<Phase>('confirm');
  readonly error = signal<string | null>(null);
  /** Geçici şifre — tek sahibi bu sinyal; kapanışta temizlenir. */
  readonly password = signal<string | null>(null);
  readonly copyState = signal<'idle' | 'copied' | 'failed'>('idle');

  readonly submitting = computed(() => this.phase() === 'submitting');

  private request?: Subscription;

  constructor() {
    // Hata metinleri şablon dışında senkron `translate()` ile okunur; scope baştan yüklensin.
    this.transloco.load(`${ADMIN_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
    inject(DestroyRef).onDestroy(() => this.clearSecret());
  }

  cancel(): void {
    if (this.phase() !== 'confirm') return;
    this.dialogRef.close();
  }

  /** Çift tıklamaya karşı yalnızca `confirm` fazında istek atar. */
  confirm(): void {
    if (this.phase() !== 'confirm') return;
    this.phase.set('submitting');
    this.error.set(null);

    this.request = this.adminService.resetPassword(this.data.target, this.data.id).subscribe({
      next: (res) => {
        const temporaryPassword = typeof res?.temporaryPassword === 'string' ? res.temporaryPassword : '';
        if (!temporaryPassword) {
          this.fail(this.text('errors.generic'));
          return;
        }
        this.password.set(temporaryPassword);
        this.phase.set('done');
      },
      error: (err: HttpErrorResponse) =>
        this.fail(passwordResetErrorMessage(err, (key, params) => this.text(`errors.${key}`, params))),
    });
  }

  copy(): void {
    const value = this.password();
    if (!value) return;
    this.copyState.set(this.clipboard.copy(value) ? 'copied' : 'failed');
  }

  /** Sonuç ekranından çıkış: şifre referansı bırakılmadan kapanır. */
  close(): void {
    this.clearSecret();
    this.dialogRef.close();
  }

  private fail(message: string): void {
    this.error.set(message);
    this.phase.set('confirm');
  }

  private clearSecret(): void {
    this.request?.unsubscribe();
    this.request = undefined;
    this.password.set(null);
    this.copyState.set('idle');
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${TEXT_PREFIX}.${key}`, params) ?? '';
  }
}

/**
 * Şifre sıfırlama hatalarının yorumu. Backend 403/404/502'de yerelleştirilmiş `{ message }` döner — varsa o
 * gösterilir; gövdesiz 403 (admin değil) → yetki metni. 429'un gövdesi düz metindir; kullanıcı dostu metin
 * gösterilir (`Retry-After` saniye ise süre eklenir). 500 ve ağ hataları → genel metin.
 */
export function passwordResetErrorMessage(
  err: HttpErrorResponse,
  text: (key: PasswordResetErrorKey, params?: Record<string, unknown>) => string,
): string {
  switch (err.status) {
    case 403:
      return bodyMessage(err) ?? text('forbidden');
    case 404:
      return bodyMessage(err) ?? text('notFound');
    case 502:
      return bodyMessage(err) ?? text('upstream');
    case 429: {
      const seconds = Number.parseInt(err.headers?.get('Retry-After') ?? '', 10);
      return Number.isFinite(seconds) && seconds > 0
        ? text('rateLimitedSeconds', { seconds })
        : text('rateLimited');
    }
    default:
      return text('generic');
  }
}

function bodyMessage(err: HttpErrorResponse): string | null {
  const body: unknown = err.error;
  if (body && typeof body === 'object' && 'message' in body) {
    const message = (body as { message: unknown }).message;
    if (typeof message === 'string' && message.trim()) return message.trim();
  }
  return null;
}

/**
 * Dialog'u açar. Yanlışlıkla (backdrop/ESC) kapanıp şifrenin okunmadan kaybolmaması ve istek sürerken
 * kapanmaması için `disableClose`; çıkış yalnızca dialog butonlarıyla.
 */
export function openAdminResetPasswordDialog(
  dialog: MatDialog,
  data: AdminResetPasswordDialogData,
): MatDialogRef<AdminResetPasswordDialogComponent, void> {
  return dialog.open<AdminResetPasswordDialogComponent, AdminResetPasswordDialogData, void>(
    AdminResetPasswordDialogComponent,
    { data, disableClose: true, autoFocus: 'dialog', restoreFocus: true },
  );
}
