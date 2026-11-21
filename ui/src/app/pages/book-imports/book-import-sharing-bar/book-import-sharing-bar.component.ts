import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, booleanAttribute, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';

import { BookAudienceSchool, BookAudienceTeacher } from '../../../models/book-import';
import { WorksheetTeacherSharing } from '../../../models/test-instance';
import { BookImportService } from '../../../services/book-import.service';
import { BookImportSharingComponent } from '../book-import-sharing/book-import-sharing.component';

const KEYS: Record<WorksheetTeacherSharing, string> = {
  [WorksheetTeacherSharing.Private]: 'private',
  [WorksheetTeacherSharing.PublicView]: 'publicView',
  [WorksheetTeacherSharing.PublicAssignable]: 'publicAssignable',
  [WorksheetTeacherSharing.SchoolOnly]: 'schoolOnly',
  [WorksheetTeacherSharing.BookAudience]: 'bookAudience',
};

/**
 * Issue #1266: kurulum onayında seçilen paylaşım bütün aktarıma aittir; inceleme ve onay panelinde görünür ve iş bitene kadar
 * değiştirilir. Değişiklik onaylanmamış testlere ve sonra alınacaklara uygulanır; onaylanmış testler testin ayarından değişir.
 */
@Component({
  selector: 'app-book-import-sharing-bar',
  standalone: true,
  imports: [BookImportSharingComponent, MatButtonModule, MatIconModule, TranslocoDirective],
  templateUrl: './book-import-sharing-bar.component.html',
  styleUrl: './book-import-sharing-bar.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BookImportSharingBarComponent {
  private readonly api = inject(BookImportService);
  private readonly destroyRef = inject(DestroyRef);

  readonly jobId = input.required<string>();
  readonly sharing = input<WorksheetTeacherSharing>(WorksheetTeacherSharing.Private);
  readonly canChange = input(false, { transform: booleanAttribute });
  readonly canShareWithSchool = input(false, { transform: booleanAttribute });
  readonly canShareWithAudience = input(false, { transform: booleanAttribute });
  /** Kaydedildi; üst bileşen işi yeniden okur. */
  readonly changed = output<void>();

  readonly editing = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly draft = signal<WorksheetTeacherSharing>(WorksheetTeacherSharing.Private);
  readonly schools = signal<BookAudienceSchool[]>([]);
  readonly teachers = signal<BookAudienceTeacher[]>([]);

  readonly labelKey = computed(() => 'sharing.options.' + (KEYS[this.sharing()] ?? 'private') + '.label');
  readonly audienceMissing = computed(
    () =>
      this.draft() === WorksheetTeacherSharing.BookAudience && this.schools().length === 0 && this.teachers().length === 0,
  );

  edit(): void {
    this.draft.set(this.sharing());
    this.schools.set([]);
    this.teachers.set([]);
    this.error.set(null);
    this.editing.set(true);
  }

  save(): void {
    const sharing = this.draft();
    const audience =
      sharing === WorksheetTeacherSharing.BookAudience
        ? { schoolIds: this.schools().map((s) => s.id), teacherIds: this.teachers().map((t) => t.id) }
        : undefined;
    this.saving.set(true);
    this.error.set(null);
    this.api
      .setSharing(this.jobId(), sharing, audience)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.editing.set(false);
          this.changed.emit();
        },
        error: (e: HttpErrorResponse) => {
          this.saving.set(false);
          this.error.set((e.error as { message?: string } | null)?.message ?? 'failed');
        },
      });
  }
}
