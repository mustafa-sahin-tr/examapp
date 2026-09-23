import { Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTableModule } from '@angular/material/table';
import { TranslocoDirective, TranslocoPipe, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { take } from 'rxjs';
import { AdminService } from '../../../services/admin.service';
import { AdminStudentListItem } from '../../../models/admin-student.model';
import { SchoolFilterComponent } from '../../../shared/components/school-filter/school-filter.component';
import { SchoolFilterValue } from '../../../shared/components/school-filter/school-filter.model';
import { PAGE_SIZE_OPTIONS } from '../../../shared/utils/paging.util';
import { provideTranslatedPaginatorIntl } from '../../../shared/utils/paginator-intl.util';
import { SchoolPagedList, adminListErrorMessage } from '../../../shared/utils/school-paged-list';

/** Yönetim ekranlarının ortak Transloco scope'u: `public/i18n/admin/<lang>.json` (issue #183). */
const ADMIN_SCOPE = 'admin';

type AccountStatus = 'active' | 'inactive' | 'unknown';

/** Şablonun doğrudan bastığı satır modeli; boş/null alanların yorumu burada tek yerde yapılır. */
export interface AdminStudentRow {
  id: number;
  /** null → "—" */
  fullName: string | null;
  /** null → ikincil satır gösterilmez */
  studentNumber: string | null;
  /** null → "—" */
  email: string | null;
  /** null → okula bağlı değilse "Okulsuz", bağlıysa (ad çözülemedi) "—" */
  schoolName: string | null;
  noSchool: boolean;
  /** null → "—" */
  gradeName: string | null;
  accountStatus: AccountStatus;
}

/**
 * Issue #153 — Admin öğrenci listesi: server-side sayfalama + okul filtresi.
 * Öğretmen listesiyle (#152) aynı kalıp: URL senkronu, sayfalama ve yükleme durumu `SchoolPagedList`'tedir;
 * URL tek doğruluk kaynağıdır (`?schoolId=5&page=2`, `?unassigned=true`).
 */
@Component({
  selector: 'app-admin-students',
  standalone: true,
  imports: [
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatTableModule,
    TranslocoDirective,
    TranslocoPipe,
    SchoolFilterComponent,
  ],
  providers: [
    provideTranslocoScope(ADMIN_SCOPE),
    provideTranslatedPaginatorIntl(ADMIN_SCOPE, `${ADMIN_SCOPE}.paginator`),
  ],
  templateUrl: './admin-students.component.html',
  styleUrls: ['./admin-students.component.scss'],
})
export class AdminStudentsComponent {
  private readonly adminService = inject(AdminService);
  private readonly transloco = inject(TranslocoService);

  readonly displayedColumns = ['fullName', 'email', 'school', 'grade', 'accountStatus'];
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
  readonly students = this.list.items.asReadonly();
  readonly totalCount = this.list.totalCount.asReadonly();
  readonly isEmpty = this.list.isEmpty;
  readonly showSpinner = this.list.showSpinner;
  readonly isFiltered = this.list.isFiltered;

  readonly rows = computed<AdminStudentRow[]>(() => this.students().map(toRow));

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

  private createList(): SchoolPagedList<AdminStudentListItem> {
    // Hata metinleri şablon dışında senkron `translate()` ile okunur; scope baştan yüklensin.
    this.transloco.load(`${ADMIN_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
    return new SchoolPagedList<AdminStudentListItem>({
      fetch: (query) => this.adminService.getStudents(query),
      errorMessage: (err) => adminListErrorMessage(err, (key) => this.text(key)),
    });
  }

  /** Scope'a göreli anahtarı senkron çözer. */
  private text(key: string): string {
    return this.transloco.translate<string>(`${ADMIN_SCOPE}.students.${key}`) ?? '';
  }
}

function toRow(item: AdminStudentListItem): AdminStudentRow {
  return {
    id: item.id,
    fullName: item.fullName?.trim() || null,
    studentNumber: item.studentNumber.trim() || null,
    email: item.email?.trim() || null,
    schoolName: item.schoolName?.trim() || null,
    noSchool: item.schoolId == null,
    gradeName: item.gradeName?.trim() || null,
    accountStatus: item.isEnabled === true ? 'active' : item.isEnabled === false ? 'inactive' : 'unknown',
  };
}
