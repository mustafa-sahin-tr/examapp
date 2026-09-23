import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
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

/** Yönetim ekranlarının ortak Transloco scope'u: `public/i18n/admin/<lang>.json` (issue #183). */
const ADMIN_SCOPE = 'admin';

type ApprovalKey = 'pending' | 'approved' | 'rejected';
type AccountStatus = 'active' | 'inactive' | 'unknown';

const APPROVAL_KEYS: Record<AdminTeacherApprovalStatus, ApprovalKey> = {
  Pending: 'pending',
  Approved: 'approved',
  Rejected: 'rejected',
};

/** Şablonun doğrudan bastığı satır modeli; boş/null alanların yorumu burada tek yerde yapılır. */
export interface AdminTeacherRow {
  id: number;
  /** null → "—" */
  fullName: string | null;
  /** null → "—" */
  email: string | null;
  /** null → bağımsızsa "Bağımsız", değilse "—" */
  schoolName: string | null;
  independent: boolean;
  approvalKey: ApprovalKey;
  accountStatus: AccountStatus;
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

function toRow(item: AdminTeacherListItem): AdminTeacherRow {
  return {
    id: item.id,
    fullName: item.fullName?.trim() || null,
    email: item.email?.trim() || null,
    schoolName: item.schoolName?.trim() || null,
    independent: item.isIndependentTutor,
    approvalKey: APPROVAL_KEYS[item.approvalStatus] ?? 'pending',
    accountStatus: item.isEnabled === true ? 'active' : item.isEnabled === false ? 'inactive' : 'unknown',
  };
}
