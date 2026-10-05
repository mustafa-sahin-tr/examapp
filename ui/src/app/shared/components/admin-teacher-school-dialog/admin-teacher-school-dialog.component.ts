import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Subscription, take } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import { School } from '../../../models/taxonomy';
import { AdminTeacherSchoolResponse } from '../../../models/admin-teacher-school.model';
import { adminActionErrorMessage, backendMessage } from '../../utils/admin-action-error.util';
import { SchoolSelectComponent } from '../school-select/school-select.component';

/** Yönetim ekranlarının ortak Transloco scope'u (`public/i18n/admin/<lang>.json`). */
const ADMIN_SCOPE = 'admin';
const TEXT_PREFIX = `${ADMIN_SCOPE}.teacherSchool`;

export interface AdminTeacherSchoolDialogData {
  /** Teacher.Id */
  teacherId: number;
  /** Listede gösterilen ad (fallback uygulanmış hâli). */
  displayName: string;
  /** Öğretmenin mevcut okulu; okulsuzsa null → "Okula bağla" kipi. */
  currentSchoolId: number | null;
  currentSchoolName: string | null;
  /**
   * Bağımsız öğretmen (liste yalnız onaylanmamış/reddedilmiş bağımsızı buraya getirir; onaylı bağımsız 409'dur):
   * bağımsız bayraklarının değişmediğine dair not gösterilir.
   */
  independent: boolean;
  /** Hesabı onaylı okul öğretmeninde başvuru durumu "Onaylı"ya çekilir; etkiler metni buna göre seçilir. */
  accountApproved: boolean;
}

/**
 * Dialog sonucu: başarıda sunucu yanıtı + seçilen okulun adı (yanıttaki okul seçilenle aynıysa, değilse null); hata
 * görüldükten sonra vazgeçildiyse (ör. 409, 502) `{ refresh: true }` → liste yeniden yüklenir. Hatasız iptal → `undefined`.
 */
export type AdminTeacherSchoolDialogResult =
  | { response: AdminTeacherSchoolResponse; schoolName: string | null }
  | { refresh: true };

/**
 * Issue #313 — admin öğretmeni okula bağlar / okulunu değiştirir (`PUT /api/exam/admin/teachers/{id}/school`).
 * Öğrenci okul dialog'unun (#277) kalıbı: tekil okul seçimi (`app-school-select`), eski → yeni okul önizlemesi, etkiler,
 * istek dialog içinde. Hata satır içinde kalıcı gösterilir (tekrar dene) ve snackbar ile de bildirilir; 409'da
 * "Listeyi yenile" ile kapanır.
 */
@Component({
  selector: 'app-admin-teacher-school-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective, SchoolSelectComponent],
  providers: [provideTranslocoScope(ADMIN_SCOPE)],
  templateUrl: './admin-teacher-school-dialog.component.html',
  styleUrls: ['./admin-teacher-school-dialog.component.scss'],
})
export class AdminTeacherSchoolDialogComponent {
  readonly data = inject<AdminTeacherSchoolDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef =
    inject<MatDialogRef<AdminTeacherSchoolDialogComponent, AdminTeacherSchoolDialogResult | undefined>>(MatDialogRef);
  private readonly adminService = inject(AdminService);
  private readonly transloco = inject(TranslocoService);
  private readonly snackBar = inject(MatSnackBar);

  /** Okulsuz öğretmen → "Okula bağla"; okullu → "Okulu değiştir". */
  readonly mode: 'assign' | 'change' = this.data.currentSchoolId == null ? 'assign' : 'change';
  /** Bağımsız olmayan + hesabı onaylı öğretmende bekleyen talep kapanır ve başvuru durumu "Onaylı" olur. */
  readonly approvesApplication = !this.data.independent && this.data.accountApproved;

  readonly selectedSchoolId = signal<number | null>(null);
  readonly selectedSchool = signal<School | null>(null);
  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);
  /** 409: öğretmenin okulu bu arada değişti — yeniden denemek yerine listeyi yenilemek gerekir. */
  readonly conflict = signal(false);
  readonly canSubmit = computed(() => this.selectedSchoolId() != null && !this.submitting() && !this.conflict());

  private request?: Subscription;
  /** Bu dialog'da en az bir istek hata aldı mı. */
  private failed = false;

  constructor() {
    // Hata metinleri şablon dışında senkron `translate()` ile okunur; scope baştan yüklensin.
    this.transloco.load(`${ADMIN_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
  }

  onSchoolSelected(school: School | null): void {
    this.selectedSchool.set(school);
    // Yeni seçim yapıldı: önceki (409 dışı) hata metni artık geçerli değil.
    if (!this.conflict()) this.error.set(null);
  }

  cancel(): void {
    if (this.submitting()) return;
    this.dialogRef.close(this.failed ? { refresh: true } : undefined);
  }

  /** 409 sonrası: dialog kapanır, liste güncel durumla yeniden yüklenir. */
  reload(): void {
    this.dialogRef.close({ refresh: true });
  }

  /** Çift tıklamaya karşı istek sürerken yeni istek atılmaz. */
  confirm(): void {
    const schoolId = this.selectedSchoolId();
    if (schoolId == null || this.submitting() || this.conflict()) return;
    this.submitting.set(true);
    this.error.set(null);

    this.request = this.adminService.changeTeacherSchool(this.data.teacherId, schoolId).subscribe({
      next: (response) => {
        this.submitting.set(false);
        const selected = this.selectedSchool();
        const name = selected && selected.id === response.schoolId ? selected.name : null;
        this.dialogRef.close({ response, schoolName: name });
      },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        this.failed = true;
        this.conflict.set(err.status === 409);
        const message = this.errorMessage(err);
        this.error.set(message);
        // Satır içi uyarı (role=alert) zaten okunur; snackbar görsel bildirimdir, ekran okuyucuya ikinci kez okutulmaz.
        this.snackBar.open(message, this.translate(`${TEXT_PREFIX}.close`), { duration: 6000, politeness: 'off' });
      },
    });
  }

  private translate(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(key, params) ?? '';
  }

  private errorMessage(err: HttpErrorResponse): string {
    const translate = (key: string, params?: Record<string, unknown>) => this.translate(key, params);
    if (err.status === 409) return backendMessage(err) ?? translate(`${TEXT_PREFIX}.errors.conflict`);
    if (err.status === 400) return backendMessage(err) ?? translate(`${TEXT_PREFIX}.errors.invalidSchool`);
    return adminActionErrorMessage(err, `${TEXT_PREFIX}.errors`, translate);
  }
}

/** Dialog'u açar. İstek sürerken backdrop/ESC ile kapanmasın diye `disableClose`; çıkış yalnızca butonlarla. */
export function openAdminTeacherSchoolDialog(
  dialog: MatDialog,
  data: AdminTeacherSchoolDialogData,
): MatDialogRef<AdminTeacherSchoolDialogComponent, AdminTeacherSchoolDialogResult | undefined> {
  return dialog.open<AdminTeacherSchoolDialogComponent, AdminTeacherSchoolDialogData, AdminTeacherSchoolDialogResult | undefined>(
    AdminTeacherSchoolDialogComponent,
    { data, disableClose: true, autoFocus: 'dialog', restoreFocus: true, width: '520px', maxWidth: '92vw' },
  );
}
