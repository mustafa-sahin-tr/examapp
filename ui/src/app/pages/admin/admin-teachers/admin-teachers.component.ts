import { Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective, TranslocoPipe, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { take } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import { AdminTeacherApprovalStatus, AdminTeacherListItem } from '../../../models/admin-teacher.model';
import { SchoolFilterComponent } from '../../../shared/components/school-filter/school-filter.component';
import { SchoolFilterValue } from '../../../shared/components/school-filter/school-filter.model';
import { PAGE_SIZE_OPTIONS } from '../../../shared/utils/paging.util';
import { provideTranslatedPaginatorIntl } from '../../../shared/utils/paginator-intl.util';
import { SchoolPagedList, adminListErrorMessage } from '../../../shared/utils/school-paged-list';
import { openAdminResetPasswordDialog } from '../../../shared/components/admin-reset-password-dialog/admin-reset-password-dialog.component';
import { openAdminAccountStatusDialog } from '../../../shared/components/admin-account-status-dialog/admin-account-status-dialog.component';
import { openAdminTeacherSuspensionDialog } from '../../../shared/components/admin-teacher-suspension-dialog/admin-teacher-suspension-dialog.component';
import { AdminTeacherSuspensionResponse } from '../../../models/admin-teacher-suspension.model';
import { openAdminTeacherSchoolDialog } from '../../../shared/components/admin-teacher-school-dialog/admin-teacher-school-dialog.component';
import { AdminTeacherSchoolResponse } from '../../../models/admin-teacher-school.model';

/** Yönetim ekranlarının ortak Transloco scope'u: `public/i18n/admin/<lang>.json` (issue #183). */
const ADMIN_SCOPE = 'admin';

type ApprovalKey = 'pending' | 'approved' | 'rejected';
type AccountStatus = 'active' | 'inactive' | 'unknown';

const APPROVAL_KEYS: Record<AdminTeacherApprovalStatus, ApprovalKey> = {
  Pending: 'pending',
  Approved: 'approved',
  Rejected: 'rejected',
};

/** Issue #313: okul bağlama/değiştirmenin backend'de 409 ile reddedildiği durumlar (`admin.teacherSchool.blocked.*`). */
export type TeacherSchoolBlockedReason = 'suspended' | 'accountNotApproved' | 'approvedIndependent';

/** Şablonun doğrudan bastığı satır modeli; boş/null alanların yorumu burada tek yerde yapılır. */
export interface AdminTeacherRow {
  id: number;
  /** null → "—" */
  fullName: string | null;
  /** null → "—" */
  email: string | null;
  /** Issue #313: null → "Okula bağla", dolu → "Okulu değiştir" aksiyonu. */
  schoolId: number | null;
  /** Issue #313: backend'in 409 ile reddettiği durum → okul aksiyonu devre dışı + tooltip'te neden; null → serbest. */
  schoolBlockedReason: TeacherSchoolBlockedReason | null;
  /** null → bağımsızsa "Bağımsız", değilse "—" */
  schoolName: string | null;
  independent: boolean;
  approvalKey: ApprovalKey;
  accountStatus: AccountStatus;
  /** Issue #289: hesap onaylı ve askıda değil → "Askıya al" aksiyonu. */
  accountApproved: boolean;
  /** Issue #289: hesap onayı askıda → "Askı" chip'i + "Askıyı kaldır" aksiyonu. */
  accountSuspended: boolean;
  /** ISO-8601; askıda değilse null. */
  suspendedAt: string | null;
  /** Admin'in girdiği neden (kırpılmış); yoksa null. */
  suspensionReason: string | null;
}

/**
 * Issue #152 — Admin öğretmen listesi: server-side sayfalama + okul filtresi.
 * URL senkronu, sayfalama ve yükleme durumu `SchoolPagedList`'tedir (öğrenci listesi #153 ile ortak);
 * URL tek doğruluk kaynağıdır (`?schoolId=5&page=2`, `?unassigned=true`).
 */
@Component({
  selector: 'app-admin-teachers',
  standalone: true,
  imports: [
    DatePipe,
    MatButtonModule,
    MatDialogModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatTableModule,
    MatTooltipModule,
    TranslocoDirective,
    TranslocoPipe,
    SchoolFilterComponent,
  ],
  providers: [
    provideTranslocoScope(ADMIN_SCOPE),
    provideTranslatedPaginatorIntl(ADMIN_SCOPE, `${ADMIN_SCOPE}.paginator`),
  ],
  templateUrl: './admin-teachers.component.html',
  styleUrls: ['./admin-teachers.component.scss'],
})
export class AdminTeachersComponent {
  private readonly adminService = inject(AdminService);
  private readonly transloco = inject(TranslocoService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  readonly displayedColumns = ['fullName', 'email', 'school', 'approvalStatus', 'accountStatus', 'actions'];
  readonly pageSizeOptions = PAGE_SIZE_OPTIONS;

  // SIRA BAĞIMLI: `createList()` alan başlatıcısı `adminService` ve `transloco` alanlarından SONRA,
  // ona dayanan alias alanlarından (filter, pageIndex, ...) ÖNCE gelmelidir; alan başlatıcıları bildirim
  // sırasıyla çalışır. `SchoolPagedList` constructor'ı ilk isteği senkron başlatabilir.
  private readonly list = this.createList();

  readonly filter = this.list.filter.asReadonly();
  /** 0-tabanlı (MatPaginator); backend'e `pageIndex + 1` gider. */
  readonly pageIndex = this.list.pageIndex.asReadonly();
  readonly pageSize = this.list.pageSize.asReadonly();
  readonly loading = this.list.loading.asReadonly();
  readonly error = this.list.error.asReadonly();
  readonly teachers = this.list.items.asReadonly();
  readonly totalCount = this.list.totalCount.asReadonly();
  readonly isEmpty = this.list.isEmpty;
  readonly showSpinner = this.list.showSpinner;
  readonly isFiltered = this.list.isFiltered;

  readonly rows = computed<AdminTeacherRow[]>(() => this.teachers().map(toRow));

  /** Mevcut URL state'iyle yeniden yükler (Yenile / Tekrar dene). */
  load(): void {
    this.list.load();
  }

  /** Filtre değişince 1. sayfaya dönülür; yükleme URL aboneliğinden gelir. */
  onFilterChange(value: SchoolFilterValue): void {
    this.list.onFilterChange(value);
  }

  onPage(event: PageEvent): void {
    this.list.onPage(event);
  }

  /** Şifre sıfırlama dialog'u açıkken ikinci bir dialog açılmaz (çift tıklama). */
  readonly resetDialogOpen = signal(false);

  /**
   * Issue #156 — onay + istek + geçici şifre gösterimi dialog'un içindedir; şifre bu komponente hiç gelmez.
   */
  resetPassword(row: AdminTeacherRow): void {
    if (this.resetDialogOpen()) return;
    this.resetDialogOpen.set(true);
    openAdminResetPasswordDialog(this.dialog, {
      target: 'teacher',
      id: row.id,
      displayName: this.rowDisplayName(row),
    })
      .afterClosed()
      .subscribe(() => this.resetDialogOpen.set(false));
  }

  /** Hesap durumu dialog'u açıkken ikinci bir dialog açılmaz (çift tıklama). */
  readonly statusDialogOpen = signal(false);

  /**
   * Issue #155 — hesabı devre dışı bırak / etkinleştir. Onay + istek dialog'dadır; başarıda satır sunucunun döndürdüğü
   * yeni durumla listede anında güncellenir (yeniden yükleme yok); hata görülüp vazgeçilirse liste yeniden yüklenir.
   * Durumu bilinmeyen satırda aksiyon gösterilmez.
   */
  toggleAccountStatus(row: AdminTeacherRow): void {
    if (this.statusDialogOpen() || row.accountStatus === 'unknown') return;
    this.statusDialogOpen.set(true);
    openAdminAccountStatusDialog(this.dialog, {
      target: 'teacher',
      id: row.id,
      displayName: this.rowDisplayName(row),
      enable: row.accountStatus === 'inactive',
    })
      .afterClosed()
      .subscribe((result) => {
        this.statusDialogOpen.set(false);
        if (!result) return;
        // Hata sonrası vazgeçildi (ör. 502 sessionRevokeFailed: hesap kapanmış olabilir) → satır bayat kalmasın.
        if ('refresh' in result) this.list.load();
        else this.applyAccountStatus(row.id, result.enabled);
      });
  }

  private applyAccountStatus(id: number, enabled: boolean): void {
    this.list.items.update((items) => items.map((item) => (item.id === id ? { ...item, isEnabled: enabled } : item)));
  }

  /** Askı dialog'u açıkken ikinci bir dialog açılmaz (çift tıklama). */
  readonly suspensionDialogOpen = signal(false);

  /**
   * Issue #289 — öğretmen hesap onayını askıya al (zorunlu neden) / askıyı kaldır (basit onay). İstek dialog'dadır;
   * başarıda satır sunucu yanıtıyla anında güncellenir, hata görülüp vazgeçilirse liste yeniden yüklenir.
   * Ne onaylı ne askıda olan (başvurusu bekleyen/reddedilen) satırda aksiyon yoktur.
   */
  toggleSuspension(row: AdminTeacherRow): void {
    if (this.suspensionDialogOpen() || (!row.accountApproved && !row.accountSuspended)) return;
    this.suspensionDialogOpen.set(true);
    openAdminTeacherSuspensionDialog(this.dialog, {
      teacherId: row.id,
      displayName: this.rowDisplayName(row),
      mode: row.accountSuspended ? 'unsuspend' : 'suspend',
    })
      .afterClosed()
      .subscribe((result) => {
        this.suspensionDialogOpen.set(false);
        if (!result) return;
        if ('refresh' in result) this.list.load();
        else this.applySuspension(row.id, result.response, result.reason);
      });
  }

  private applySuspension(id: number, response: AdminTeacherSuspensionResponse, reason: string | null): void {
    this.list.items.update((items) =>
      items.map((item) =>
        item.id === id
          ? {
              ...item,
              accountApproved: response.accountApproved,
              accountSuspended: response.accountSuspended,
              accountSuspendedAt: response.accountSuspendedAt,
              accountSuspensionReason: response.accountSuspended ? reason : null,
            }
          : item,
      ),
    );
  }

  /** Okul dialog'u açıkken ikinci bir dialog açılmaz (çift tıklama). */
  readonly schoolDialogOpen = signal(false);

  /**
   * Issue #313 — öğretmeni okula bağla (okulsuzsa) / okulunu değiştir. Seçim + onay + istek dialog'dadır. Başarıda
   * snackbar gösterilir; `changed=true` ise liste yeniden yüklenir (onay durumu sütunu da değişebilir), `changed=false`
   * ("zaten bu okulda") yükleme yapmaz. Hata görülüp vazgeçilirse (ör. 409, 502) liste yeniden yüklenir.
   * Backend'in 409 ile reddettiği satırlarda (`schoolBlockedReason`) aksiyon devre dışıdır, dialog açılmaz.
   */
  changeSchool(row: AdminTeacherRow): void {
    if (this.schoolDialogOpen() || row.schoolBlockedReason) return;
    this.schoolDialogOpen.set(true);
    const displayName = this.rowDisplayName(row);
    openAdminTeacherSchoolDialog(this.dialog, {
      teacherId: row.id,
      displayName,
      currentSchoolId: row.schoolId,
      currentSchoolName: row.schoolName,
      independent: row.independent,
      accountApproved: row.accountApproved,
    })
      .afterClosed()
      .subscribe((result) => {
        this.schoolDialogOpen.set(false);
        if (!result) return;
        if ('refresh' in result) {
          this.list.load();
          return;
        }
        this.onSchoolSaved(row, displayName, result.response, result.schoolName);
      });
  }

  private onSchoolSaved(
    row: AdminTeacherRow,
    displayName: string,
    response: AdminTeacherSchoolResponse,
    schoolName: string | null,
  ): void {
    // Okul yazıldı ama oturum/profil önbelleği temizlenemedi → başarı yerine uyarı (değişiklik gecikebilir).
    const cacheStale = response.profileCacheStale === true;
    const key = cacheStale
      ? 'cacheStale'
      : !response.changed
        ? 'unchanged'
        : schoolName == null
          ? 'updated'
          : row.schoolId == null
            ? 'assigned'
            : 'changed';
    this.snackBar.open(
      this.transloco.translate<string>(`${ADMIN_SCOPE}.teacherSchool.success.${key}`, { name: displayName, school: schoolName }),
      this.transloco.translate<string>(`${ADMIN_SCOPE}.teacherSchool.close`),
      { duration: cacheStale ? 8000 : 4000 },
    );
    // changed=true: okul + başvuru durumu sunucuda değişmiş olabilir → satır tahminle değil sunucudan yenilenir.
    // changed=false yan etkisizdir; satır zaten bayatsa (farklı okul) yine yenilenir.
    if (response.changed || response.schoolId !== row.schoolId) this.list.load();
  }

  rowDisplayName(row: AdminTeacherRow): string {
    return (
      row.fullName ??
      row.email ??
      this.transloco.translate<string>(`${ADMIN_SCOPE}.passwordReset.unnamed`, { id: row.id })
    );
  }

  private createList(): SchoolPagedList<AdminTeacherListItem> {
    // Hata metinleri şablon dışında senkron `translate()` ile okunur; scope baştan yüklensin.
    this.transloco.load(`${ADMIN_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
    return new SchoolPagedList<AdminTeacherListItem>({
      fetch: (query) => this.adminService.getTeachers(query),
      errorMessage: (err) => adminListErrorMessage(err, (key) => this.text(key)),
    });
  }

  /** Scope'a göreli anahtarı senkron çözer. */
  private text(key: string): string {
    return this.transloco.translate<string>(`${ADMIN_SCOPE}.teachers.${key}`) ?? '';
  }
}

/**
 * Issue #313 — backend'in okul bağlamayı 409 ile reddettiği durumlar (öncelik sırasıyla): askıdaki hesap, hesabı
 * onaylanmamış öğretmen, onaylı bağımsız öğretmen. UI bunları önceden devre dışı bırakır; 409 yine de gelirse backend
 * mesajı dialog'da gösterilir.
 */
function teacherSchoolBlockedReason(item: AdminTeacherListItem): TeacherSchoolBlockedReason | null {
  if (item.accountSuspended === true) return 'suspended';
  if (item.accountApproved !== true) return 'accountNotApproved';
  if (item.isIndependentTutor && item.approvalStatus === 'Approved') return 'approvedIndependent';
  return null;
}

function toRow(item: AdminTeacherListItem): AdminTeacherRow {
  return {
    id: item.id,
    fullName: item.fullName?.trim() || null,
    email: item.email?.trim() || null,
    schoolId: item.schoolId ?? null,
    schoolBlockedReason: teacherSchoolBlockedReason(item),
    schoolName: item.schoolName?.trim() || null,
    independent: item.isIndependentTutor,
    approvalKey: APPROVAL_KEYS[item.approvalStatus] ?? 'pending',
    accountStatus: item.isEnabled === true ? 'active' : item.isEnabled === false ? 'inactive' : 'unknown',
    accountApproved: item.accountApproved === true,
    accountSuspended: item.accountSuspended === true,
    suspendedAt: item.accountSuspendedAt ?? null,
    suspensionReason: item.accountSuspensionReason?.trim() || null,
  };
}
