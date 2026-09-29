import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { AbstractControl, FormControl, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Observable, Subscription, take } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import {
  AdminTeacherSuspensionResponse,
  TEACHER_SUSPENSION_REASON_MAX_LENGTH,
} from '../../../models/admin-teacher-suspension.model';
import { adminActionErrorMessage, backendMessage } from '../../utils/admin-action-error.util';

/** Yönetim ekranlarının ortak Transloco scope'u (`public/i18n/admin/<lang>.json`). */
const ADMIN_SCOPE = 'admin';
const TEXT_PREFIX = `${ADMIN_SCOPE}.teacherSuspension`;

export type AdminTeacherSuspensionMode = 'suspend' | 'unsuspend';

export interface AdminTeacherSuspensionDialogData {
  /** Teacher.Id (admin öğretmen listesindeki `id`). */
  teacherId: number;
  /** Listede gösterilen ad (fallback uygulanmış hâli). */
  displayName: string;
  /** `suspend` → zorunlu nedenle askıya al; `unsuspend` → basit onayla askıyı kaldır. */
  mode: AdminTeacherSuspensionMode;
}

/**
 * Dialog sonucu: başarıda sunucu yanıtı + gönderilen (trim'lenmiş) neden (`unsuspend`'de null; yanıt nedeni dönmez);
 * bir hata görüldükten sonra vazgeçildiyse `{ refresh: true }` (ör. 409 zaten askıda / eşzamanlı değişiklik → satır
 * bayat, liste yeniden yüklenir). Hatasız iptal → `undefined`.
 */
export type AdminTeacherSuspensionDialogResult =
  | { response: AdminTeacherSuspensionResponse; reason: string | null }
  | { refresh: true };

/**
 * Issue #289 — öğretmen hesap onayını askıya alma / askıyı kaldırma onayı. #155 hesap durumu dialog'uyla aynı desen:
 * istek dialog içinde atılır (yükleniyor, çift tıklama, hata burada), başarıda sunucu yanıtıyla kapanır.
 * Askıya almada neden zorunludur (1-500, boşluktan ibaret olamaz; sayaç gösterilir).
 */
@Component({
  selector: 'app-admin-teacher-suspension-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(ADMIN_SCOPE)],
  templateUrl: './admin-teacher-suspension-dialog.component.html',
  styleUrls: ['./admin-teacher-suspension-dialog.component.scss'],
})
export class AdminTeacherSuspensionDialogComponent {
  readonly data = inject<AdminTeacherSuspensionDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef =
    inject<MatDialogRef<AdminTeacherSuspensionDialogComponent, AdminTeacherSuspensionDialogResult | undefined>>(MatDialogRef);
  private readonly adminService = inject(AdminService);
  private readonly transloco = inject(TranslocoService);

  readonly maxLength = TEACHER_SUSPENSION_REASON_MAX_LENGTH;
  readonly isSuspend = this.data.mode === 'suspend';

  readonly reasonControl = new FormControl('', {
    nonNullable: true,
    validators: this.isSuspend
      ? [Validators.required, noWhitespaceValidator, Validators.maxLength(TEACHER_SUSPENSION_REASON_MAX_LENGTH)]
      : [],
  });

  private readonly reason = toSignal(this.reasonControl.valueChanges, { initialValue: this.reasonControl.value });
  readonly reasonLength = computed(() => this.reason().length);
  /** Onay butonu: askıya almada geçerli neden şart. */
  readonly canConfirm = computed(() => !this.isSuspend || isValidReason(this.reason()));

  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);

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
    // Hata görüldüyse (ör. 409 zaten askıda / eşzamanlı değişiklik) satır bayat olabilir → liste yenilensin.
    if (this.failed) {
      this.dialogRef.close({ refresh: true });
      return;
    }
    this.dialogRef.close();
  }

  /** Çift tıklamaya karşı istek sürerken yeni istek atılmaz. */
  confirm(): void {
    if (this.submitting()) return;
    if (this.isSuspend) {
      this.reasonControl.markAsTouched();
      if (this.reasonControl.invalid) return;
    }
    const reason = this.isSuspend ? this.reasonControl.value.trim() : null;
    const call: Observable<AdminTeacherSuspensionResponse> =
      reason !== null
        ? this.adminService.suspendTeacher(this.data.teacherId, reason)
        : this.adminService.unsuspendTeacher(this.data.teacherId);

    this.submitting.set(true);
    this.error.set(null);
    this.request = call.subscribe({
      next: (response) => {
        this.submitting.set(false);
        this.dialogRef.close({ response, reason });
      },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        this.failed = true;
        this.error.set(this.errorMessage(err));
      },
    });
  }

  /** 400/409'da backend'in yerelleştirilmiş `message`'ı; diğerleri admin aksiyonlarının ortak yorumu. */
  private errorMessage(err: HttpErrorResponse): string {
    const translate = (key: string, params?: Record<string, unknown>) => this.transloco.translate<string>(key, params) ?? '';
    if (err.status === 400 || err.status === 409) {
      return backendMessage(err) ?? translate(`${TEXT_PREFIX}.errors.generic`);
    }
    return adminActionErrorMessage(err, `${TEXT_PREFIX}.errors`, translate);
  }
}

function isValidReason(value: string): boolean {
  const trimmed = value.trim();
  return trimmed.length > 0 && value.length <= TEACHER_SUSPENSION_REASON_MAX_LENGTH;
}

/** Yalnızca boşluktan oluşan neden `required`'ı geçer; backend trim'leyip reddeder. */
function noWhitespaceValidator(control: AbstractControl<string | null>): ValidationErrors | null {
  const value = control.value ?? '';
  return value.length > 0 && value.trim().length === 0 ? { whitespace: true } : null;
}

/** Dialog'u açar. İstek sürerken backdrop/ESC ile kapanmasın diye `disableClose`; çıkış yalnızca butonlarla. */
export function openAdminTeacherSuspensionDialog(
  dialog: MatDialog,
  data: AdminTeacherSuspensionDialogData,
): MatDialogRef<AdminTeacherSuspensionDialogComponent, AdminTeacherSuspensionDialogResult | undefined> {
  return dialog.open<
    AdminTeacherSuspensionDialogComponent,
    AdminTeacherSuspensionDialogData,
    AdminTeacherSuspensionDialogResult | undefined
  >(AdminTeacherSuspensionDialogComponent, { data, disableClose: true, autoFocus: 'dialog', restoreFocus: true });
}
