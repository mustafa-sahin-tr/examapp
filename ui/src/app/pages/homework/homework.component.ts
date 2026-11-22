import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter, finalize, take } from 'rxjs';
import { FormsModule, NgForm } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Grade } from '../../models/student';
import { Subject } from '../../models/subject';
import { GradesService } from '../../services/grades.service';
import {
  HOMEWORK_BOOK_MAX,
  HOMEWORK_MAX_DUE_DAYS,
  HOMEWORK_NOTE_MAX,
  HOMEWORK_PAGES_MAX,
  HOMEWORK_TEACHER_NOTE_MAX,
  HomeworkCheckStatus,
  HomeworkCreateRequest,
  HomeworkDetail,
  HomeworkService,
  HomeworkStudentStatus,
  HomeworkTarget,
  TeacherAssignmentHomework,
  TeacherHomework,
} from '../../services/homework.service';
import { RouterLink } from '@angular/router';
import { LocaleService } from '../../services/locale.service';
import { SchoolSection, SchoolSectionService, SchoolSectionStudent } from '../../services/school-section.service';
import { SubjectService } from '../../services/subject.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { backendMessage } from '../../shared/utils/admin-action-error.util';

export const HOMEWORK_SCOPE = 'homework';

/** Seçili öğrenci hedefinde bir şubeden yüklenen en çok öğrenci (backend sınırı 200). */
const SECTION_STUDENT_PAGE = 100;

/** Tarayıcının yerel günü, `yyyy-MM-dd` (teslim günü alanı ve sınırları). */
export function localIsoDate(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/**
 * Issue #1282 (#1071 S7): öğretmenin düz ödevleri. Üstte ödev formu (ders, kitap, sayfa, not, teslim günü, hedef: sınıf / şube /
 * şubeden seçili öğrenciler), altta verilen ödevler durum sayılarıyla; "Kontrol et" öğrenci listesini açar, öğretmen her öğrenci
 * için Yapıldı / Kısmen / Yapılmadı + not girer (öğrencinin "yaptım" işaretini onaylar ya da geçersiz kılar). Puan yok.
 */
@Component({
  selector: 'app-homework',
  standalone: true,
  imports: [
    FormsModule,
    PageHeaderComponent,
    MatButtonModule,
    MatButtonToggleModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    RouterLink,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(HOMEWORK_SCOPE)],
  templateUrl: './homework.component.html',
  styleUrl: './homework.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HomeworkComponent implements OnInit {
  private readonly service = inject(HomeworkService);
  private readonly sectionService = inject(SchoolSectionService);
  private readonly gradesService = inject(GradesService);
  private readonly subjectService = inject(SubjectService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly form = viewChild(NgForm);

  protected readonly bookMax = HOMEWORK_BOOK_MAX;
  protected readonly pagesMax = HOMEWORK_PAGES_MAX;
  protected readonly noteMax = HOMEWORK_NOTE_MAX;
  protected readonly teacherNoteMax = HOMEWORK_TEACHER_NOTE_MAX;
  protected readonly statuses: HomeworkCheckStatus[] = ['Done', 'Partial', 'NotDone'];
  protected readonly targets: HomeworkTarget[] = ['Section', 'Grade', 'Students'];
  protected readonly pageSize = 10;
  protected readonly minDue = localIsoDate(new Date());
  protected readonly maxDue = localIsoDate(new Date(Date.now() + HOMEWORK_MAX_DUE_DAYS * 86_400_000));

  protected readonly grades = signal<Grade[]>([]);
  protected readonly sections = signal<SchoolSection[]>([]);
  protected readonly subjects = signal<Subject[]>([]);

  protected readonly target = signal<HomeworkTarget>('Section');
  protected readonly gradeId = signal<number | null>(null);
  protected readonly sectionId = signal<number | null>(null);
  protected readonly sectionStudents = signal<SchoolSectionStudent[]>([]);
  protected readonly studentsLoading = signal(false);
  protected readonly chosen = signal<ReadonlySet<number>>(new Set());
  protected readonly subjectId = signal<number | null>(null);
  protected readonly bookName = signal('');
  protected readonly pages = signal('');
  protected readonly note = signal('');
  protected readonly dueDate = signal(localIsoDate(new Date(Date.now() + 86_400_000)));
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);

  protected readonly items = signal<TeacherHomework[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(0);
  protected readonly listLoading = signal(false);
  protected readonly listError = signal(false);
  /** Issue #1283: ödev olarak verilen test / etkinlik atamaları (yüklenemezse boş; bölüm ipucunu gösterir). */
  protected readonly assignmentItems = signal<TeacherAssignmentHomework[]>([]);

  protected readonly openId = signal<number | null>(null);
  protected readonly detail = signal<HomeworkDetail | null>(null);
  protected readonly detailLoading = signal(false);
  protected readonly detailError = signal(false);
  /** Kaydedilmemiş öğretmen notları (öğrenci id → metin). */
  protected readonly noteDrafts = signal<ReadonlyMap<number, string>>(new Map());
  protected readonly savingStudent = signal<number | null>(null);

  protected readonly allChosen = computed(
    () => this.sectionStudents().length > 0 && this.sectionStudents().every((s) => this.chosen().has(s.studentId)),
  );
  protected readonly canSave = computed(() => {
    const pages = this.pages().trim();
    const note = this.note().trim();
    const due = this.dueDate();
    if (
      this.saving() ||
      this.subjectId() == null ||
      !due ||
      due < this.minDue ||
      due > this.maxDue ||
      (!pages && !note) ||
      pages.length > this.pagesMax ||
      note.length > this.noteMax ||
      this.bookName().trim().length > this.bookMax
    ) {
      return false;
    }
    switch (this.target()) {
      case 'Grade':
        return this.gradeId() != null;
      case 'Section':
        return this.sectionId() != null;
      case 'Students':
        return this.chosen().size > 0;
    }
  });

  constructor() {
    this.transloco.load(`${HOMEWORK_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();
  }

  ngOnInit(): void {
    this.sectionService
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (s) => this.sections.set(s ?? []), error: () => this.sections.set([]) });
    this.gradesService
      .getGrades()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (g) => this.grades.set(g ?? []), error: () => this.grades.set([]) });
    this.subjectService
      .loadCoreSubjects()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (s) => this.subjects.set(s ?? []), error: () => this.subjects.set([]) });
    this.loadList();
    this.service
      .assignments()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (a) => this.assignmentItems.set(a ?? []), error: () => this.assignmentItems.set([]) });
  }

  protected setTarget(target: HomeworkTarget): void {
    this.target.set(target);
    this.saveError.set(null);
  }

  protected setSection(id: number | null): void {
    this.sectionId.set(id);
    this.chosen.set(new Set());
    this.sectionStudents.set([]);
    if (id == null || this.target() !== 'Students') {
      return;
    }
    this.loadSectionStudents(id);
  }

  private loadSectionStudents(id: number): void {
    this.studentsLoading.set(true);
    this.sectionService
      .students(id, 1, SECTION_STUDENT_PAGE)
      .pipe(
        finalize(() => this.studentsLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({ next: (p) => this.sectionStudents.set(p.items ?? []), error: () => this.sectionStudents.set([]) });
  }

  /** "Seçili öğrenciler"e geçince şube zaten seçiliyse öğrencileri yükle. */
  protected onTargetChange(target: HomeworkTarget): void {
    this.setTarget(target);
    const id = this.sectionId();
    if (target === 'Students' && id != null && this.sectionStudents().length === 0) {
      this.loadSectionStudents(id);
    }
  }

  protected toggleStudent(id: number, on: boolean): void {
    const next = new Set(this.chosen());
    if (on) {
      next.add(id);
    } else {
      next.delete(id);
    }
    this.chosen.set(next);
  }

  protected toggleAll(on: boolean): void {
    this.chosen.set(on ? new Set(this.sectionStudents().map((s) => s.studentId)) : new Set());
  }

  protected save(): void {
    if (!this.canSave()) {
      return;
    }
    const request: HomeworkCreateRequest = {
      target: this.target(),
      subjectId: this.subjectId()!,
      bookName: this.bookName().trim() || null,
      pages: this.pages().trim() || null,
      note: this.note().trim() || null,
      dueDate: this.dueDate(),
    };
    if (request.target === 'Grade') {
      request.gradeId = this.gradeId()!;
    } else if (request.target === 'Section') {
      request.sectionId = this.sectionId()!;
    } else {
      request.studentIds = [...this.chosen()];
    }
    this.saving.set(true);
    this.saveError.set(null);
    this.service
      .create(request)
      .pipe(
        finalize(() => this.saving.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (h) => {
          this.snackBar.open(this.t('toast.created', { count: h.studentCount }), undefined, { duration: 4000 });
          // Hedef, ders ve teslim günü kalır (aynı şubeye ikinci ödev hızlı verilsin); içerik alanları boşalır.
          const keep = { subject: this.subjectId(), due: this.dueDate() };
          this.form()?.resetForm();
          this.bookName.set('');
          this.pages.set('');
          this.note.set('');
          this.subjectId.set(keep.subject);
          this.dueDate.set(keep.due);
          this.chosen.set(new Set());
          this.page.set(0);
          this.loadList();
        },
        error: (err: HttpErrorResponse) => this.saveError.set(backendMessage(err) ?? this.t('state.saveError')),
      });
  }

  protected loadList(): void {
    this.listLoading.set(true);
    this.listError.set(false);
    this.service
      .list(this.page() + 1, this.pageSize)
      .pipe(
        finalize(() => this.listLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (p) => {
          this.items.set(p.items ?? []);
          this.total.set(p.totalCount ?? 0);
        },
        error: () => this.listError.set(true),
      });
  }

  protected onPage(e: PageEvent): void {
    this.page.set(e.pageIndex);
    this.openId.set(null);
    this.loadList();
  }

  protected toggleDetail(id: number): void {
    if (this.openId() === id) {
      this.openId.set(null);
      this.detail.set(null);
      return;
    }
    this.openId.set(id);
    this.loadDetail(id);
  }

  private loadDetail(id: number): void {
    this.detail.set(null);
    this.detailError.set(false);
    this.detailLoading.set(true);
    this.noteDrafts.set(new Map());
    this.service
      .detail(id)
      .pipe(
        finalize(() => this.detailLoading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (d) => {
          this.detail.set(d);
          this.replaceSummary(d.homework);
        },
        error: () => this.detailError.set(true),
      });
  }

  protected noteValue(s: HomeworkStudentStatus): string {
    return this.noteDrafts().get(s.studentId) ?? s.teacherNote ?? '';
  }

  protected setNoteDraft(studentId: number, value: string): void {
    const next = new Map(this.noteDrafts());
    next.set(studentId, value);
    this.noteDrafts.set(next);
  }

  /** Durum değişince hemen kaydeder (not taslağıyla birlikte); boş durum kontrolü ve notu kaldırır. */
  protected setStatus(s: HomeworkStudentStatus, status: HomeworkCheckStatus | null): void {
    const note = status == null ? null : this.noteValue(s).trim() || null;
    this.submitCheck(s, status, note);
  }

  /** Not alanından çıkınca, durum seçiliyse ve not değiştiyse kaydeder. */
  protected commitNote(s: HomeworkStudentStatus): void {
    const draft = this.noteDrafts().get(s.studentId);
    if (draft == null || s.teacherStatus == null || draft.trim() === (s.teacherNote ?? '')) {
      return;
    }
    this.submitCheck(s, s.teacherStatus, draft.trim() || null);
  }

  /** Öğrencinin "yaptım" dediklerini tek tıkla "Yapıldı" olarak onaylar. */
  protected confirmAllMarked(): void {
    const d = this.detail();
    if (!d) {
      return;
    }
    for (const s of d.students.filter((x) => x.studentDoneAt && !x.teacherStatus)) {
      this.submitCheck(s, 'Done', null);
    }
  }

  protected readonly pendingConfirmCount = computed(
    () => this.detail()?.students.filter((s) => s.studentDoneAt && !s.teacherStatus).length ?? 0,
  );

  private submitCheck(s: HomeworkStudentStatus, status: HomeworkCheckStatus | null, note: string | null): void {
    const id = this.openId();
    if (id == null) {
      return;
    }
    this.savingStudent.set(s.studentId);
    this.service
      .check(id, s.studentId, status, note)
      .pipe(
        finalize(() => this.savingStudent.set(null)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (updated) => {
          const d = this.detail();
          if (d && this.openId() === id) {
            const students = d.students.map((x) => (x.studentId === updated.studentId ? updated : x));
            this.detail.set({ ...d, students });
            this.replaceSummary(this.recount(d.homework, students));
          }
          const drafts = new Map(this.noteDrafts());
          drafts.delete(s.studentId);
          this.noteDrafts.set(drafts);
        },
        error: (err: HttpErrorResponse) =>
          this.snackBar.open(backendMessage(err) ?? this.t('state.checkError'), undefined, { duration: 4000 }),
      });
  }

  private recount(h: TeacherHomework, students: HomeworkStudentStatus[]): TeacherHomework {
    const count = (p: (s: HomeworkStudentStatus) => boolean) => students.filter(p).length;
    return {
      ...h,
      studentCount: students.length,
      studentDoneCount: count((s) => !!s.studentDoneAt && !s.teacherStatus),
      doneCount: count((s) => s.teacherStatus === 'Done'),
      partialCount: count((s) => s.teacherStatus === 'Partial'),
      notDoneCount: count((s) => s.teacherStatus === 'NotDone'),
      overdueCount: count((s) => s.isOverdue),
    };
  }

  private replaceSummary(h: TeacherHomework): void {
    this.items.set(this.items().map((x) => (x.id === h.id ? h : x)));
    const d = this.detail();
    if (d && d.homework.id === h.id) {
      this.detail.set({ ...d, homework: h });
    }
  }

  protected confirmRemove(h: TeacherHomework): void {
    const data: ConfirmDialogData = {
      title: this.t('remove.title'),
      message: this.t('remove.message', { subject: h.subjectName }),
      confirmText: this.t('remove.confirm'),
      cancelText: this.t('remove.cancel'),
      icon: 'delete',
    };
    this.dialog
      .open(ConfirmDialogComponent, { data })
      .afterClosed()
      .pipe(filter((ok) => ok === true))
      .subscribe(() =>
        this.service
          .remove(h.id)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: () => {
              if (this.openId() === h.id) {
                this.openId.set(null);
                this.detail.set(null);
              }
              this.loadList();
            },
            error: (err: HttpErrorResponse) =>
              this.snackBar.open(backendMessage(err) ?? this.t('state.removeError'), undefined, { duration: 4000 }),
          }),
      );
  }

  /** "Okuma Kitabı · s. 8–10" (boşlar yazılmaz). */
  protected workText(h: { bookName: string | null; pages: string | null }): string {
    return [h.bookName, h.pages ? this.t('pagesText', { pages: h.pages }) : null].filter(Boolean).join(' · ');
  }

  protected targetText(h: TeacherHomework): string {
    return h.targetLabel ?? this.t('targetText.Students');
  }

  /** "12 öğrenci · 5 yaptım dedi · 3 yapıldı …" (sıfırlar yazılmaz, öğrenci sayısı hep). */
  protected countsText(h: TeacherHomework): string {
    const parts = [this.t('count.students', { count: h.studentCount })];
    const add = (key: string, count: number) => {
      if (count > 0) parts.push(this.t(key, { count }));
    };
    add('count.studentDone', h.studentDoneCount);
    add('count.Done', h.doneCount);
    add('count.Partial', h.partialCount);
    add('count.NotDone', h.notDoneCount);
    add('count.overdue', h.overdueCount);
    return parts.join(' · ');
  }

  /** Issue #1283: "24 öğrenci · 10 tamamladı · 2 devam ediyor · 3 gecikti" (sıfırlar yazılmaz). */
  protected assignmentCountsText(a: TeacherAssignmentHomework): string {
    const parts = [this.t('count.students', { count: a.studentCount })];
    const add = (key: string, count: number) => {
      if (count > 0) parts.push(this.t(key, { count }));
    };
    add('count.completed', a.completedCount);
    add('count.inProgress', a.inProgressCount);
    add('count.overdue', a.overdueCount);
    return parts.join(' · ');
  }

  protected formatDateTime(iso: string): string {
    const date = new Date(iso);
    return Number.isNaN(date.getTime())
      ? '—'
      : date.toLocaleString(this.localeService.localeDefinition().angularLocale, {
          day: 'numeric',
          month: 'short',
          weekday: 'short',
          hour: '2-digit',
          minute: '2-digit',
        });
  }

  protected formatDay(iso: string): string {
    // `yyyy-MM-dd` yerel gün; UTC'ye kaymasın diye öğelerinden kurulur.
    const [y, m, d] = iso.split('-').map(Number);
    const date = new Date(y, (m ?? 1) - 1, d ?? 1);
    return Number.isNaN(date.getTime())
      ? '—'
      : date.toLocaleDateString(this.localeService.localeDefinition().angularLocale, { day: 'numeric', month: 'short', weekday: 'short' });
  }

  protected formatDate(iso: string): string {
    const date = new Date(iso);
    return Number.isNaN(date.getTime())
      ? '—'
      : date.toLocaleDateString(this.localeService.localeDefinition().angularLocale, { day: 'numeric', month: 'short' });
  }

  private t(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate(`${HOMEWORK_SCOPE}.${key}`, params);
  }
}
