import { ChangeDetectionStrategy, Component, DestroyRef, booleanAttribute, computed, inject, input, model, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { TranslocoDirective } from '@jsverse/transloco';
import { AdminTeacherListItem } from '../../../models/admin-teacher.model';
import { BookAudienceSchool, BookAudienceTeacher } from '../../../models/book-import';
import { WorksheetTeacherSharing } from '../../../models/test-instance';
import { AdminService } from '../../../services/admin.service';
import { SchoolSelectComponent } from '../../../shared/components/school-select/school-select.component';
import { SchoolOption } from '../../../models/taxonomy';

/** Bir okulun öğretmen listesinde istenen en fazla satır (admin listesi sayfalı). */
export const BOOK_SHARING_TEACHER_PAGE = 200;

/**
 * Issue #1259: kitap aktarımı onayında aktarılan testlerin öğretmen paylaşımı. Admin ayrıca "Seçili okul ve öğretmenler"
 * seçebilir: okullar okul aramasından eklenir; öğretmenler önce okulu seçilip o okulun listesinden işaretlenir.
 */
@Component({
  selector: 'app-book-import-sharing',
  standalone: true,
  imports: [
    MatButtonModule,
    MatCheckboxModule,
    MatChipsModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatRadioModule,
    SchoolSelectComponent,
    TranslocoDirective,
  ],
  templateUrl: './book-import-sharing.component.html',
  styleUrl: './book-import-sharing.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BookImportSharingComponent {
  private readonly admin = inject(AdminService);
  private readonly destroyRef = inject(DestroyRef);

  readonly canShareWithSchool = input(false, { transform: booleanAttribute });
  readonly canShareWithAudience = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });

  readonly sharing = model<WorksheetTeacherSharing>(WorksheetTeacherSharing.Private);
  readonly schools = model<BookAudienceSchool[]>([]);
  readonly teachers = model<BookAudienceTeacher[]>([]);

  protected readonly Sharing = WorksheetTeacherSharing;
  protected readonly options = computed(() => [
    { value: WorksheetTeacherSharing.Private, key: 'private' },
    { value: WorksheetTeacherSharing.PublicView, key: 'publicView' },
    { value: WorksheetTeacherSharing.PublicAssignable, key: 'publicAssignable' },
    ...(this.canShareWithSchool() ? [{ value: WorksheetTeacherSharing.SchoolOnly, key: 'schoolOnly' }] : []),
    ...(this.canShareWithAudience() ? [{ value: WorksheetTeacherSharing.BookAudience, key: 'bookAudience' }] : []),
  ]);

  /** Okul arama kutusu her eklemeden sonra boşalır. */
  protected readonly schoolPick = signal<number | null>(null);
  /** Öğretmen seçmek için açılan okul ve listesi. */
  protected readonly teacherSchool = signal<SchoolOption | null>(null);
  protected readonly teacherSchoolId = signal<number | null>(null);
  protected readonly schoolTeachers = signal<AdminTeacherListItem[]>([]);
  protected readonly teachersLoading = signal(false);
  protected readonly teachersFailed = signal(false);
  protected readonly empty = computed(() => this.schools().length === 0 && this.teachers().length === 0);

  addSchool(school: SchoolOption | null): void {
    if (!school) return;
    if (!this.schools().some((s) => s.id === school.id)) this.schools.update((list) => [...list, { id: school.id, name: school.name }]);
    // Seçim kutusu boşalsın: bir sonraki okul aranabilsin.
    this.schoolPick.set(null);
  }

  removeSchool(id: number): void {
    this.schools.update((list) => list.filter((s) => s.id !== id));
  }

  pickTeacherSchool(school: SchoolOption | null): void {
    this.teacherSchool.set(school);
    this.schoolTeachers.set([]);
    this.teachersFailed.set(false);
    if (!school) return;
    this.teachersLoading.set(true);
    this.admin
      .getTeachers({ page: 1, pageSize: BOOK_SHARING_TEACHER_PAGE, schoolId: school.id })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          // Seçim bu arada değiştiyse eski yanıtı yok say.
          if (this.teacherSchool()?.id !== school.id) return;
          this.schoolTeachers.set(page.items);
          this.teachersLoading.set(false);
        },
        error: () => {
          if (this.teacherSchool()?.id !== school.id) return;
          this.teachersFailed.set(true);
          this.teachersLoading.set(false);
        },
      });
  }

  isTeacherSelected(id: number): boolean {
    return this.teachers().some((t) => t.id === id);
  }

  toggleTeacher(teacher: AdminTeacherListItem, checked: boolean): void {
    if (checked) {
      if (!this.isTeacherSelected(teacher.id))
        this.teachers.update((list) => [...list, { id: teacher.id, name: teacher.fullName || null, schoolName: teacher.schoolName }]);
    } else {
      this.removeTeacher(teacher.id);
    }
  }

  removeTeacher(id: number): void {
    this.teachers.update((list) => list.filter((t) => t.id !== id));
  }
}
