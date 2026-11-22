import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize, take } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { TranslocoDirective, TranslocoPipe, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Grade } from '../../../models/student';
import { CacheService } from '../../../services/cache.service';
import { LocaleService } from '../../../services/locale.service';
import {
  SchoolAdminService,
  SchoolAdminStudent,
  SchoolAdminSummary,
  SchoolAdminTeacher,
} from '../../../services/school-admin.service';
import { PageHeaderComponent } from '../../../shared/components/page-header/page-header.component';

/** Çeviriler kendi Transloco scope'unda: `public/i18n/school-overview/<lang>.json`. */
export const SCHOOL_OVERVIEW_SCOPE = 'school-overview';

/**
 * Issue #1278 (#1071 S3): okul yöneticisinin okul paneli — sayılar, öğretmenler, öğrenciler (salt okunur). Onay / ret
 * mevcut kuyruklarda: öğretmen başvuruları `/school-admin/teacher-approvals`, öğrenci üyelikleri `/student-school-requests`.
 * Kapsam sunucuda (yönetici değil → 403; guard yalnız menü kolaylığı).
 */
@Component({
  selector: 'app-school-overview',
  standalone: true,
  imports: [
    PageHeaderComponent,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatTableModule,
    TranslocoDirective,
    TranslocoPipe,
  ],
  providers: [provideTranslocoScope(SCHOOL_OVERVIEW_SCOPE)],
  templateUrl: './school-overview.component.html',
  styleUrl: './school-overview.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SchoolOverviewComponent implements OnInit {
  private readonly service = inject(SchoolAdminService);
  private readonly cache = inject(CacheService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);

  protected readonly teacherColumns = ['name', 'subjects', 'status', 'approvedAt'];
  protected readonly studentColumns = ['name', 'number', 'grade', 'verifiedAt'];

  protected readonly summary = signal<SchoolAdminSummary | null>(null);
  protected readonly summaryError = signal(false);

  protected readonly teachers = signal<SchoolAdminTeacher[]>([]);
  protected readonly teacherTotal = signal(0);
  protected readonly teacherPage = signal(0);
  protected readonly teachersLoading = signal(false);
  protected readonly teachersError = signal(false);

  protected readonly students = signal<SchoolAdminStudent[]>([]);
  protected readonly studentTotal = signal(0);
  protected readonly studentPage = signal(0);
  protected readonly studentsLoading = signal(false);
  protected readonly studentsError = signal(false);
  protected readonly gradeFilter = signal<number | null>(null);
  protected readonly grades = signal<Grade[]>([]);

  protected readonly pageSize = 20;
  protected readonly pendingTotal = computed(() => {
    const s = this.summary();
    return s ? s.pendingTeacherApplications + s.pendingStudentRequests : 0;
  });

  constructor() {
    this.transloco.load(`${SCHOOL_OVERVIEW_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
  }

  ngOnInit(): void {
    this.loadSummary();
    this.loadTeachers();
    this.loadStudents();
    this.cache
      .getGrades()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (g) => this.grades.set((g ?? []) as Grade[]), error: () => this.grades.set([]) });
  }

  protected loadSummary(): void {
    this.summaryError.set(false);
    this.service
      .refreshSummary()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (s) => this.summary.set(s), error: () => this.summaryError.set(true) });
  }

  protected loadTeachers(): void {
    this.teachersLoading.set(true);
    this.teachersError.set(false);
    this.service
      .getTeachers(this.teacherPage() + 1, this.pageSize)
      .pipe(
        finalize(() => this.teachersLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (page) => {
          this.teachers.set(page?.items ?? []);
          this.teacherTotal.set(page?.totalCount ?? 0);
        },
        error: () => this.teachersError.set(true),
      });
  }

  protected loadStudents(): void {
    this.studentsLoading.set(true);
    this.studentsError.set(false);
    this.service
      .getStudents(this.studentPage() + 1, this.pageSize, this.gradeFilter())
      .pipe(
        finalize(() => this.studentsLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (page) => {
          this.students.set(page?.items ?? []);
          this.studentTotal.set(page?.totalCount ?? 0);
        },
        error: () => this.studentsError.set(true),
      });
  }

  protected onTeacherPage(event: PageEvent): void {
    this.teacherPage.set(event.pageIndex);
    this.loadTeachers();
  }

  protected onStudentPage(event: PageEvent): void {
    this.studentPage.set(event.pageIndex);
    this.loadStudents();
  }

  protected onGradeChange(gradeId: number | null): void {
    this.gradeFilter.set(gradeId);
    this.studentPage.set(0);
    this.loadStudents();
  }

  protected formatDate(iso: string | null): string {
    if (!iso) {
      return '—';
    }
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) {
      return '—';
    }
    return date.toLocaleDateString(this.localeService.localeDefinition().angularLocale, {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
    });
  }
}
