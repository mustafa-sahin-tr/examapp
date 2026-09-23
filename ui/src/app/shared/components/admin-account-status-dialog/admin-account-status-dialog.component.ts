import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Subscription, take } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import { AdminAccountTarget } from '../../../models/admin-account-status.model';
import { adminActionErrorMessage } from '../../utils/admin-action-error.util';

/** Yönetim ekranlarının ortak Transloco scope'u (`public/i18n/admin/<lang>.json`). */
const ADMIN_SCOPE = 'admin';
const TEXT_PREFIX = `${ADMIN_SCOPE}.accountStatus`;

export interface AdminAccountStatusDialogData {
  target: AdminAccountTarget;
  /** Teacher.Id / Student.Id */
  id: number;
  /** Listede gösterilen ad (fallback uygulanmış hâli). */
  displayName: string;
  /** İstenen YENİ durum: `false` → devre dışı bırak, `true` → etkinleştir. */
  enable: boolean;
}

/**
 * Dialog sonucu: başarıda sunucunun döndürdüğü yeni durum; bir hata görüldükten sonra vazgeçildiyse `{ refresh: true }`
 * (ör. 502 sessionRevokeFailed: hesap kapanmış olabilir → satır bayat kalmasın, liste yeniden yüklenir). Hatasız iptal → `undefined`.
 */
export type AdminAccountStatusDialogResult = { enabled: boolean } | { refresh: true };

/**
 * Issue #155 — Admin hesap devre dışı bırakma / etkinleştirme onayı. İstek dialog içinde atılır (yükleniyor, çift tıklama
 * ve hata burada yönetilir); başarıda dialog sunucunun döndürdüğü yeni durumla (`{ enabled }`) kapanır, liste satırı
 * bu sonuçla anında güncellenir. Hata sonrası vazgeçme → `{ refresh: true }`; hatasız iptal → `undefined`.
 */
@Component({
  selector: 'app-admin-account-status-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective],
  providers: [provideTranslocoScope(ADMIN_SCOPE)],
  templateUrl: './admin-account-status-dialog.component.html',
  styleUrls: ['./admin-account-status-dialog.component.scss'],
})
export class AdminAccountStatusDialogComponent {
  readonly data = inject<AdminAccountStatusDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef =
    inject<MatDialogRef<AdminAccountStatusDialogComponent, AdminAccountStatusDialogResult | undefined>>(MatDialogRef);
  private readonly adminService = inject(AdminService);
  private readonly transloco = inject(TranslocoService);

  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);
  /** Şablon metinlerinin alt anahtarı: `disable.*` / `enable.*`. */
  readonly mode = computed(() => (this.data.enable ? 'enable' : 'disable'));

  private request?: Subscription;
  /** Bu dialog'da en az bir istek hata aldı mı. */
  private failed = false;

  constructor() {
    // Hata metinleri şablon dışında senkron `translate()` ile okunur; scope baştan yüklensin.
    this.transloco.load(`${ADMIN_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
  }

  cancel(): void {
    if (this.submitting()) return;
    // Hata görüldüyse sunucu tarafı durum belirsiz olabilir (ör. hesap kapandı, oturumlar kapatılamadı) → liste yenilensin.
    if (this.failed) {
      this.dialogRef.close({ refresh: true });
      return;
    }
    this.dialogRef.close();
  }

  /** Çift tıklamaya karşı istek sürerken yeni istek atılmaz. */
  confirm(): void {
    if (this.submitting()) return;
    this.submitting.set(true);
    this.error.set(null);

    this.request = this.adminService.setAccountStatus(this.data.target, this.data.id, this.data.enable).subscribe({
      next: (res) => {
        this.submitting.set(false);
        // Sunucu yanıtı esas; beklenmeyen gövdede istenen durum kullanılır (uç idempotent, 200 = istenen durum).
        const enabled = typeof res?.enabled === 'boolean' ? res.enabled : this.data.enable;
        this.dialogRef.close({ enabled });
      },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        this.failed = true;
        this.error.set(
          adminActionErrorMessage(
            err,
            `${TEXT_PREFIX}.errors`,
            (key, params) => this.transloco.translate<string>(key, params) ?? '',
          ),
        );
      },
    });
  }
}

/** Dialog'u açar. İstek sürerken backdrop/ESC ile kapanmasın diye `disableClose`; çıkış yalnızca butonlarla. */
export function openAdminAccountStatusDialog(
  dialog: MatDialog,
  data: AdminAccountStatusDialogData,
): MatDialogRef<AdminAccountStatusDialogComponent, AdminAccountStatusDialogResult | undefined> {
  return dialog.open<AdminAccountStatusDialogComponent, AdminAccountStatusDialogData, AdminAccountStatusDialogResult | undefined>(
    AdminAccountStatusDialogComponent,
    { data, disableClose: true, autoFocus: 'dialog', restoreFocus: true },
  );
}
