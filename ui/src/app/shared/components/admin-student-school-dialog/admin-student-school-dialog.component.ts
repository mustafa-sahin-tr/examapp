import { Component, DestroyRef, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Subscription, take } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import { School } from '../../../models/taxonomy';
import { adminActionErrorMessage, backendMessage } from '../../utils/admin-action-error.util';
import { SchoolSelectComponent } from '../school-select/school-select.component';

/** Yönetim ekranlarının ortak Transloco scope'u (`public/i18n/admin/<lang>.json`). */
const ADMIN_SCOPE = 'admin';
const TEXT_PREFIX = `${ADMIN_SCOPE}.studentSchool`;

export interface AdminStudentSchoolDialogData {
  /** Student.Id */
  id: number;
  /** Listede gösterilen ad (fallback uygulanmış hâli). */
  displayName: string;
  /** Öğrencinin mevcut okulu; okulsuzsa null. */
  currentSchoolId: number | null;
  currentSchoolName: string | null;
}

/**
 * Dialog sonucu: başarıda sunucunun döndürdüğü okul + seçilen okulun adı; hata görüldükten sonra vazgeçildiyse
 * (ör. 409 eşzamanlı değişiklik, 502) `{ refresh: true }` → liste yeniden yüklenir. Hatasız iptal → `undefined`.
 */
export type AdminStudentSchoolDialogResult =
  | { schoolId: number | null; schoolName: string | null }
  | { refresh: true };

/**
 * Issue #277 (madde 8) — admin öğrencinin okulunu değiştirir (`PUT /api/exam/admin/students/{id}/school`). Öğrenci okulu
 * kayıttan sonra kilitlidir (#259); bu dialog tek değişiklik yoludur. Onay metni etkileri açıklar (yeni okulun atamaları
 * görünür, eski okulun öğretmenleri erişimi kaybeder). İstek dialog içinde atılır; 409'da "Listeyi yenile" ile kapanır.
 * Issue #541: okullu öğrencide "Okulsuz yap" kipi (`schoolId: null`) — ayrı etki listesi ve onay kutusu işaretlenmeden gönderilmez.
 */
@Component({
  selector: 'app-admin-student-school-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatButtonToggleModule, MatCheckboxModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective, SchoolSelectComponent],
  providers: [provideTranslocoScope(ADMIN_SCOPE)],
  templateUrl: './admin-student-school-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./admin-student-school-dialog.component.scss'],
})
export class AdminStudentSchoolDialogComponent {
  readonly data = inject<AdminStudentSchoolDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef =
    inject<MatDialogRef<AdminStudentSchoolDialogComponent, AdminStudentSchoolDialogResult | undefined>>(MatDialogRef);
  private readonly adminService = inject(AdminService);
  private readonly transloco = inject(TranslocoService);

  readonly selectedSchoolId = signal<number | null>(null);
  readonly selectedSchool = signal<School | null>(null);
  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);
  /** 409: öğrencinin okulu bu arada değişti — yeniden denemek yerine listeyi yenilemek gerekir. */
  readonly conflict = signal(false);
  /** Issue #541: `move` başka okula taşır, `remove` okulsuz yapar (yalnız okullu öğrencide seçilebilir). */
  readonly mode = signal<'move' | 'remove'>('move');
  readonly canRemove = this.data.currentSchoolId != null;
  /** Okulsuz yapma onay kutusu. */
  readonly removeConfirmed = signal(false);
  readonly canSubmit = computed(() =>
    !this.submitting() && !this.conflict()
    && (this.mode() === 'remove' ? this.removeConfirmed() : this.selectedSchoolId() != null));

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

  setMode(mode: 'move' | 'remove'): void {
    if (this.submitting() || this.conflict()) return;
    this.mode.set(mode);
    this.removeConfirmed.set(false);
    this.error.set(null);
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
    if (!this.canSubmit()) return;
    const schoolId = this.mode() === 'remove' ? null : this.selectedSchoolId();
    this.submitting.set(true);
    this.error.set(null);

    this.request = this.adminService.changeStudentSchool(this.data.id, schoolId).subscribe({
      next: (res) => {
        this.submitting.set(false);
        const resultId = schoolId == null ? null : typeof res?.schoolId === 'number' ? res.schoolId : schoolId;
        const name = resultId != null && this.selectedSchool()?.id === resultId ? this.selectedSchool()?.name ?? null : null;
        this.dialogRef.close({ schoolId: resultId, schoolName: name });
      },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        this.failed = true;
        this.conflict.set(err.status === 409);
        this.error.set(this.errorMessage(err));
      },
    });
  }

  private errorMessage(err: HttpErrorResponse): string {
    const translate = (key: string, params?: Record<string, unknown>) => this.transloco.translate<string>(key, params) ?? '';
    if (err.status === 409) return backendMessage(err) ?? translate(`${TEXT_PREFIX}.errors.conflict`);
    if (err.status === 400) return backendMessage(err) ?? translate(`${TEXT_PREFIX}.errors.invalidSchool`);
    return adminActionErrorMessage(err, `${TEXT_PREFIX}.errors`, translate);
  }
}

/** Dialog'u açar. İstek sürerken backdrop/ESC ile kapanmasın diye `disableClose`; çıkış yalnızca butonlarla. */
export function openAdminStudentSchoolDialog(
  dialog: MatDialog,
  data: AdminStudentSchoolDialogData,
): MatDialogRef<AdminStudentSchoolDialogComponent, AdminStudentSchoolDialogResult | undefined> {
  return dialog.open<AdminStudentSchoolDialogComponent, AdminStudentSchoolDialogData, AdminStudentSchoolDialogResult | undefined>(
    AdminStudentSchoolDialogComponent,
    { data, disableClose: true, autoFocus: 'dialog', restoreFocus: true, width: '520px', maxWidth: '92vw' },
  );
}
