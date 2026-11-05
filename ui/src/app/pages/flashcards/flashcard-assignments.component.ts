import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective } from '@jsverse/transloco';
import { FlashcardAssignTarget, FlashcardDeckAssignment } from '../../models/flashcard.model';
import { Grade, StudentLookup } from '../../models/student';
import { AuthService } from '../../services/auth.service';
import { FlashcardService, flashcardErrorCode } from '../../services/flashcard.service';
import { GradesService } from '../../services/grades.service';
import { StudentService } from '../../services/student.service';

/** Issue #699: öğretmen destesinin atamaları. Hedef kuralları sunucuda; bağımsız öğretmen sınıfa atayamaz (#222). */
@Component({
  selector: 'app-flashcard-assignments',
  standalone: true,
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatProgressSpinnerModule, MatSelectModule, TranslocoDirective],
  templateUrl: './flashcard-assignments.component.html',
  styleUrl: './flashcard-assignments.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FlashcardAssignmentsComponent implements OnInit {
  private readonly flashcards = inject(FlashcardService);
  private readonly gradesService = inject(GradesService);
  private readonly studentService = inject(StudentService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);

  readonly deckId = input.required<number>();

  readonly canAssignGrade = AuthService.schoolIdOf(this.auth.user()) !== null;
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly saving = signal(false);
  /** `flashcards.errors` altındaki anahtar. */
  readonly errorKey = signal<string | null>(null);
  readonly assignments = signal<FlashcardDeckAssignment[]>([]);
  readonly grades = signal<Grade[]>([]);
  readonly students = signal<StudentLookup[]>([]);
  readonly gradeId = signal<number | null>(null);
  readonly studentId = signal<number | null>(null);

  readonly sortedStudents = computed(() =>
    [...this.students()].sort((a, b) => (a.fullName ?? '').localeCompare(b.fullName ?? '')),
  );

  ngOnInit(): void {
    this.load();
    this.studentService
      .getLookup()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (students) => this.students.set(students ?? []),
        error: () => this.errorKey.set('generic'),
      });
    if (this.canAssignGrade) {
      this.gradesService
        .getGrades()
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (grades) => this.grades.set(grades),
          error: () => this.errorKey.set('generic'),
        });
    }
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.flashcards
      .listAssignments(this.deckId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (assignments) => {
          this.assignments.set(assignments);
          this.loading.set(false);
        },
        error: () => {
          this.loadFailed.set(true);
          this.loading.set(false);
        },
      });
  }

  assignGrade(): void {
    const gradeId = this.gradeId();
    if (gradeId !== null) {
      this.assign({ gradeId }, () => this.gradeId.set(null));
    }
  }

  assignStudent(): void {
    const studentId = this.studentId();
    if (studentId !== null) {
      this.assign({ studentId }, () => this.studentId.set(null));
    }
  }

  remove(assignment: FlashcardDeckAssignment): void {
    this.errorKey.set(null);
    this.flashcards
      .unassign(this.deckId(), assignment.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.assignments.update((rows) => rows.filter((row) => row.id !== assignment.id)),
        error: () => this.errorKey.set('generic'),
      });
  }

  private assign(target: FlashcardAssignTarget, onDone: () => void): void {
    this.saving.set(true);
    this.errorKey.set(null);
    this.flashcards
      .assign(this.deckId(), target)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (assignment) => {
          // Sunucu aynı atamada hata vermez, mevcut satırı döndürür: satır iki kez eklenmez.
          this.assignments.update((rows) => [...rows.filter((row) => row.id !== assignment.id), assignment]);
          this.saving.set(false);
          onDone();
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.errorKey.set(flashcardErrorCode(error) ?? 'generic');
        },
      });
  }
}
