import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective } from '@jsverse/transloco';
import { FlashcardDeckSummary, FlashcardMode, SaveFlashcardDeck } from '../../models/flashcard.model';
import { Grade } from '../../models/student';
import { Subject } from '../../models/subject';
import { Topic } from '../../models/topic';
import { GradesService } from '../../services/grades.service';
import { SubjectService } from '../../services/subject.service';

/** Issue #699: deste adı; öğretmende sınıf + ders zorunlu, konu isteğe bağlı. Öğrenci destesinde yalnız ad sorulur. */
@Component({
  selector: 'app-flashcard-deck-form',
  standalone: true,
  imports: [MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslocoDirective],
  templateUrl: './flashcard-deck-form.component.html',
  styleUrl: './flashcard-deck-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FlashcardDeckFormComponent implements OnInit {
  private readonly gradesService = inject(GradesService);
  private readonly subjectService = inject(SubjectService);
  private readonly destroyRef = inject(DestroyRef);

  readonly mode = input.required<FlashcardMode>();
  readonly deck = input<FlashcardDeckSummary | null>(null);
  readonly saving = input(false);
  readonly saved = output<SaveFlashcardDeck>();
  readonly cancelled = output<void>();

  readonly title = signal('');
  readonly gradeId = signal<number | null>(null);
  readonly subjectId = signal<number | null>(null);
  readonly topicId = signal<number | null>(null);
  readonly grades = signal<Grade[]>([]);
  readonly subjects = signal<Subject[]>([]);
  readonly topics = signal<Topic[]>([]);
  readonly lookupFailed = signal(false);

  readonly isTeacher = computed(() => this.mode() === 'teacher');
  readonly canSave = computed(
    () =>
      this.title().trim().length > 0 &&
      !this.saving() &&
      (!this.isTeacher() || (this.gradeId() !== null && this.subjectId() !== null)),
  );

  ngOnInit(): void {
    const deck = this.deck();
    if (deck) {
      this.title.set(deck.title);
      this.gradeId.set(deck.gradeId);
      this.subjectId.set(deck.subjectId);
      this.topicId.set(deck.topicId);
    }
    if (!this.isTeacher()) {
      return;
    }
    this.gradesService
      .getGrades()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (grades) => this.grades.set(grades),
        error: () => this.lookupFailed.set(true),
      });
    this.loadSubjects();
    this.loadTopics();
  }

  onGradeChange(gradeId: number | null): void {
    this.gradeId.set(gradeId);
    this.subjectId.set(null);
    this.topicId.set(null);
    this.topics.set([]);
    this.loadSubjects();
  }

  onSubjectChange(subjectId: number | null): void {
    this.subjectId.set(subjectId);
    this.topicId.set(null);
    this.loadTopics();
  }

  submit(): void {
    if (!this.canSave()) {
      return;
    }
    this.saved.emit({
      title: this.title().trim(),
      gradeId: this.gradeId(),
      subjectId: this.subjectId(),
      topicId: this.topicId(),
    });
  }

  private loadSubjects(): void {
    const gradeId = this.gradeId();
    if (gradeId === null) {
      this.subjects.set([]);
      return;
    }
    this.subjectService
      .getSubjectsByGrade(gradeId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (subjects) => this.subjects.set(subjects),
        error: () => this.lookupFailed.set(true),
      });
  }

  private loadTopics(): void {
    const gradeId = this.gradeId();
    const subjectId = this.subjectId();
    if (gradeId === null || subjectId === null) {
      this.topics.set([]);
      return;
    }
    this.subjectService
      .getTopicsBySubjectAndGrade(subjectId, gradeId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (topics) => this.topics.set(topics),
        error: () => this.lookupFailed.set(true),
      });
  }
}
