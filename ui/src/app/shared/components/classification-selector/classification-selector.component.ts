import { Component, EventEmitter, inject, Input, Output, signal, effect, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatChipsModule } from '@angular/material/chips';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { StudentService } from '../../../services/student.service';
import { SubjectService } from '../../../services/subject.service';
import { Grade } from '../../../models/student';
import { Subject } from '../../../models/subject';
import { Topic } from '../../../models/topic';
import { SubTopic } from '../../../models/subtopic';
import { firstValueFrom } from 'rxjs';

export interface ClassificationSelection {
  gradeId: number | null;
  subjectId: number | null;
  topicId: number | null;
  subtopicIds: number[];
}

@Component({
  selector: 'app-classification-selector',
  standalone: true,
  imports: [CommonModule, MatChipsModule, MatButtonModule, MatIconModule],
  templateUrl: './classification-selector.component.html',
  styleUrls: ['./classification-selector.component.scss'],
})
export class ClassificationSelectorComponent {
  @Input() showApplyButton: boolean = false;

  private suppressSelectionChange = false;

  @Input() set initialSelection(value: ClassificationSelection | null) {
    this.suppressSelectionChange = true;
    if (!value) {
      this.selectedGrade.set(null);
      this.selectedSubject.set(null);
      this.selectedTopic.set(null);
      this.selectedSubtopics.set([]);
      queueMicrotask(() => {
        this.suppressSelectionChange = false;
      });
      return;
    }

    this.selectedGrade.set(value.gradeId);
    this.selectedSubject.set(value.subjectId);
    this.selectedTopic.set(value.topicId);
    this.selectedSubtopics.set(value.subtopicIds || []);

    queueMicrotask(() => {
      this.suppressSelectionChange = false;
    });
  }

  @Output() selectionChange = new EventEmitter<ClassificationSelection>();
  @Output() apply = new EventEmitter<ClassificationSelection>();

  private studentService = inject(StudentService);
  private subjectService = inject(SubjectService);

  public grades = signal<Grade[]>([]);
  public subjects = signal<Subject[]>([]);
  public topics = signal<Topic[]>([]);
  public subtopics = signal<SubTopic[]>([]);

  public selectedGrade = signal<number | null>(null);
  public selectedSubject = signal<number | null>(null);
  public selectedTopic = signal<number | null>(null);
  public selectedSubtopics = signal<number[]>([]);

  public loading = signal(false);

  private lastGradeResolveKey: string | null = null;
  private gradeResolveToken = 0;

  constructor() {
    this.loadGrades();

    // Watch for grade changes
    effect(() => {
      const gradeId = this.selectedGrade();
      if (gradeId) {
        this.loadSubjects(gradeId);
      } else {
        this.subjects.set([]);
        this.topics.set([]);
        this.subtopics.set([]);
      }
    });

    // Watch for subject changes
    effect(() => {
      const subjectId = this.selectedSubject();
      const gradeId = this.selectedGrade();
      if (subjectId && gradeId) {
        this.loadTopics(subjectId, gradeId);
      } else {
        this.topics.set([]);
        this.subtopics.set([]);
      }
    });

    // Watch for topic changes
    effect(() => {
      const topicId = this.selectedTopic();
      if (topicId) {
        this.loadSubtopics(topicId);
      } else {
        this.subtopics.set([]);
      }
    });

    // Auto-resolve gradeId when we have subject+topic but grade is missing.
    // This is important for preview preselect: we often only know subjectId/topicId.
    effect(() => {
      const gradeId = this.selectedGrade();
      const subjectId = this.selectedSubject();
      const topicId = this.selectedTopic();
      const grades = this.grades();

      if (gradeId !== null) {
        this.lastGradeResolveKey = null;
        return;
      }

      if (!subjectId || !topicId || grades.length === 0) {
        return;
      }

      const key = `${subjectId}:${topicId}`;
      if (this.lastGradeResolveKey === key) {
        return;
      }

      this.lastGradeResolveKey = key;
      void this.resolveGradeIdFromTopic(subjectId, topicId);
    });

    // Emit changes (avoid emitting partial state while applying initialSelection)
    effect(() => {
      this.selectedGrade();
      this.selectedSubject();
      this.selectedTopic();
      this.selectedSubtopics();
      if (this.suppressSelectionChange) {
        return;
      }
      this.emitSelectionChange();
    });
  }

  private emitSelectionChange() {
    const selection: ClassificationSelection = {
      gradeId: this.selectedGrade(),
      subjectId: this.selectedSubject(),
      topicId: this.selectedTopic(),
      subtopicIds: this.selectedSubtopics(),
    };
    this.selectionChange.emit(selection);
  }

  private async resolveGradeIdFromTopic(subjectId: number, topicId: number): Promise<void> {
    const token = ++this.gradeResolveToken;
    const grades = this.grades();

    for (const grade of grades) {
      // If a newer resolve request started, abandon this one.
      if (token !== this.gradeResolveToken) {
        return;
      }

      try {
        const topics = await firstValueFrom(this.subjectService.getTopicsBySubjectAndGrade(subjectId, grade.id));
        const match = topics?.find((t) => t.id === topicId);
        if (match?.gradeId) {
          // Only apply if still current.
          if (token === this.gradeResolveToken) {
            this.selectedGrade.set(match.gradeId);
          }
          return;
        }
      } catch {
        // Ignore and continue trying other grades.
      }
    }
  }

  private loadGrades() {
    this.loading.set(true);
    this.studentService.loadGrades().subscribe({
      next: (grades) => {
        this.grades.set(grades);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Error loading grades:', err);
        this.loading.set(false);
      },
    });
  }

  private loadSubjects(gradeId: number) {
    this.loading.set(true);
    this.subjectService.getSubjectsByGrade(gradeId).subscribe({
      next: (subjects) => {
        this.subjects.set(subjects);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Error loading subjects:', err);
        this.loading.set(false);
      },
    });
  }

  private loadTopics(subjectId: number, gradeId: number) {
    this.loading.set(true);
    this.subjectService.getTopicsBySubjectAndGrade(subjectId, gradeId).subscribe({
      next: (topics) => {
        this.topics.set(topics);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Error loading topics:', err);
        this.loading.set(false);
      },
    });
  }

  private loadSubtopics(topicId: number) {
    this.loading.set(true);
    this.subjectService.getSubTopicsByTopic(topicId).subscribe({
      next: (subtopics) => {
        this.subtopics.set(subtopics);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Error loading subtopics:', err);
        this.loading.set(false);
      },
    });
  }

  selectGrade(gradeId: number) {
    this.selectedGrade.set(gradeId);
    this.selectedSubject.set(null);
    this.selectedTopic.set(null);
    this.selectedSubtopics.set([]);
  }

  selectSubject(subjectId: number) {
    this.selectedSubject.set(subjectId);
    this.selectedTopic.set(null);
    this.selectedSubtopics.set([]);
  }

  selectTopic(topicId: number) {
    this.selectedTopic.set(topicId);
    this.selectedSubtopics.set([]);
  }

  toggleSubtopic(subtopicId: number) {
    const current = this.selectedSubtopics();
    const index = current.indexOf(subtopicId);

    if (index > -1) {
      // Remove
      this.selectedSubtopics.set(current.filter((id) => id !== subtopicId));
    } else {
      // Add
      this.selectedSubtopics.set([...current, subtopicId]);
    }
  }

  isSubtopicSelected(subtopicId: number): boolean {
    return this.selectedSubtopics().includes(subtopicId);
  }

  onApply() {
    const selection: ClassificationSelection = {
      gradeId: this.selectedGrade(),
      subjectId: this.selectedSubject(),
      topicId: this.selectedTopic(),
      subtopicIds: this.selectedSubtopics(),
    };
    this.apply.emit(selection);
  }

  public canApply = computed(() => {
    return this.selectedGrade() !== null && this.selectedSubject() !== null && this.selectedTopic() !== null;
  });
}
