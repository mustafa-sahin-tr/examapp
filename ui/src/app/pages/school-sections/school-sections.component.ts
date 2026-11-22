import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { Observable, filter, finalize, take } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoDirective, TranslocoPipe, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Grade } from '../../models/student';
import { GradesService } from '../../services/grades.service';
import { LocaleService } from '../../services/locale.service';
import { SchoolSection, SchoolSectionService, SchoolSectionStudent } from '../../services/school-section.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { backendMessage } from '../../shared/utils/admin-action-error.util';
import { AddStudentsDialogComponent } from './add-students-dialog.component';
import { SectionFormDialogComponent, SectionFormDialogData } from './section-form-dialog.component';

export const SCHOOL_SECTIONS_SCOPE = 'school-sections';
/** Route data: true → okul yöneticisi kipi (oluştur / adlandır / sil / öğrenci ekle-taşı-çıkar). */
export const SCHOOL_SECTIONS_MANAGE_DATA_KEY = 'schoolSectionsManage';

interface GradeGroup {
  gradeId: number;
  gradeName: string;
  sections: SchoolSection[];
}

/**
 * Issue #1279 (#1071 S4): okul şubeleri. Okulun öğretmenleri `/school-sections`'da şubeleri ve öğrencilerini görür;
 * okul yöneticisi `/school-admin/sections`'da (Okulum sekmesi) yönetir. Kapsam ve yetki sunucuda.
 */
@Component({
  selector: 'app-school-sections',
  standalone: true,
  imports: [
    PageHeaderComponent,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatMenuModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoDirective,
    TranslocoPipe,
  ],
  providers: [provideTranslocoScope(SCHOOL_SECTIONS_SCOPE)],
  templateUrl: './school-sections.component.html',
  styleUrl: './school-sections.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SchoolSectionsComponent implements OnInit {
  private readonly service = inject(SchoolSectionService);
  private readonly gradesService = inject(GradesService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly manage = inject(ActivatedRoute).snapshot.data[SCHOOL_SECTIONS_MANAGE_DATA_KEY] === true;
  protected readonly pageSize = 50;

  protected readonly sections = signal<SchoolSection[]>([]);
  protected readonly grades = signal<Grade[]>([]);
  protected readonly gradeFilter = signal<number | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly selectedId = signal<number | null>(null);
  protected readonly selected = computed(() => this.sections().find((s) => s.id === this.selectedId()) ?? null);
  protected readonly students = signal<SchoolSectionStudent[]>([]);
  protected readonly studentTotal = signal(0);
  protected readonly studentPage = signal(0);
  protected readonly studentsLoading = signal(false);
  protected readonly studentsError = signal(false);
  protected readonly busy = signal(false);

  protected readonly groups = computed<GradeGroup[]>(() => {
    const map = new Map<number, GradeGroup>();
    for (const s of this.sections()) {
      const group = map.get(s.gradeId) ?? { gradeId: s.gradeId, gradeName: s.gradeName, sections: [] };
      group.sections.push(s);
      map.set(s.gradeId, group);
    }
    return [...map.values()];
  });
  /** Seçili şubenin sınıfındaki diğer şubeler (taşıma hedefleri). */
  protected readonly moveTargets = computed(() => {
    const current = this.selected();
    return current ? this.sections().filter((s) => s.gradeId === current.gradeId && s.id !== current.id) : [];
  });

  constructor() {
    this.transloco.load(`${SCHOOL_SECTIONS_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
  }

  ngOnInit(): void {
    this.loadSections();
    this.gradesService
      .getGrades()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (g) => this.grades.set(g ?? []), error: () => this.grades.set([]) });
  }

  protected loadSections(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service
      .list(this.gradeFilter())
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (list) => {
          this.sections.set(list ?? []);
          const keep = list?.some((s) => s.id === this.selectedId());
          if (!keep) {
            this.select(list?.[0]?.id ?? null);
          }
        },
        error: (err: HttpErrorResponse) => this.error.set(backendMessage(err) ?? this.t('state.error')),
      });
  }

  protected onGradeChange(gradeId: number | null): void {
    this.gradeFilter.set(gradeId);
    this.loadSections();
  }

  protected select(id: number | null): void {
    this.selectedId.set(id);
    this.studentPage.set(0);
    this.students.set([]);
    this.studentTotal.set(0);
    if (id != null) {
      this.loadStudents();
    }
  }

  protected loadStudents(): void {
    const id = this.selectedId();
    if (id == null) {
      return;
    }
    this.studentsLoading.set(true);
    this.studentsError.set(false);
    this.service
      .students(id, this.studentPage() + 1, this.pageSize)
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

  protected onStudentPage(event: PageEvent): void {
    this.studentPage.set(event.pageIndex);
    this.loadStudents();
  }

  protected openCreate(): void {
    const data: SectionFormDialogData = { grades: this.grades(), gradeId: this.gradeFilter() ?? this.selected()?.gradeId };
    this.dialog
      .open(SectionFormDialogComponent, { data, autoFocus: 'dialog' })
      .afterClosed()
      .pipe(filter((s): s is SchoolSection => !!s))
      .subscribe((created) => {
        this.notify(this.t('toast.created', { name: created.name }));
        this.selectedId.set(created.id);
        this.loadSections();
        this.select(created.id);
      });
  }

  protected openRename(section: SchoolSection): void {
    const data: SectionFormDialogData = { grades: this.grades(), section };
    this.dialog
      .open(SectionFormDialogComponent, { data })
      .afterClosed()
      .pipe(filter((s): s is SchoolSection => !!s))
      .subscribe(() => this.loadSections());
  }

  protected confirmDelete(section: SchoolSection): void {
    const data: ConfirmDialogData = {
      title: this.t('delete.title', { name: `${section.gradeName} / ${section.name}` }),
      message: this.t('delete.message', { count: section.studentCount }),
      confirmText: this.t('delete.confirm'),
      cancelText: this.t('form.cancel'),
      confirmColor: 'warn',
      icon: 'delete',
    };
    this.dialog
      .open(ConfirmDialogComponent, { data })
      .afterClosed()
      .pipe(filter((ok) => ok === true))
      .subscribe(() =>
        this.run(this.service.delete(section.id), () => {
          this.notify(this.t('toast.deleted', { name: section.name }));
          this.loadSections();
        }),
      );
  }

  protected openAddStudents(section: SchoolSection): void {
    this.dialog
      .open(AddStudentsDialogComponent, { data: section })
      .afterClosed()
      .pipe(filter((r) => !!r))
      .subscribe((r) => {
        this.notify(this.t('toast.assigned', { count: r!.assigned }));
        this.loadSections();
        this.loadStudents();
      });
  }

  protected move(student: SchoolSectionStudent, target: SchoolSection): void {
    this.run(this.service.assign(target.id, [student.studentId]), () => {
      this.notify(this.t('toast.moved', { name: student.fullName || this.t('unknown'), section: target.name }));
      this.loadSections();
      this.loadStudents();
    });
  }

  protected remove(student: SchoolSectionStudent): void {
    const section = this.selected();
    if (!section) {
      return;
    }
    this.run(this.service.remove(section.id, student.studentId), () => {
      this.notify(this.t('toast.removed', { name: student.fullName || this.t('unknown') }));
      this.loadSections();
      this.loadStudents();
    });
  }

  protected formatDate(iso: string): string {
    const date = new Date(iso);
    return Number.isNaN(date.getTime())
      ? '—'
      : date.toLocaleDateString(this.localeService.localeDefinition().angularLocale, {
          day: 'numeric',
          month: 'short',
          year: 'numeric',
        });
  }

  private run(request: Observable<unknown>, done: () => void): void {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    request.pipe(finalize(() => this.busy.set(false))).subscribe({
      next: () => done(),
      error: (err: HttpErrorResponse) => {
        this.notify(backendMessage(err) ?? this.t('state.error'));
        this.loadSections();
        this.loadStudents();
      },
    });
  }

  private notify(message: string): void {
    this.snackBar.open(message, undefined, { duration: 3500 });
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate(`${SCHOOL_SECTIONS_SCOPE}.${key}`, params);
  }
}
