import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectChange, MatSelectModule } from '@angular/material/select';
import { TranslocoDirective } from '@jsverse/transloco';
import { of } from 'rxjs';
import { catchError, switchMap } from 'rxjs/operators';
import { DailyStartScope } from '../../../models/practice';
import { Subject } from '../../../models/subject';
import { SubTopic } from '../../../models/subtopic';
import { Topic } from '../../../models/topic';
import { StudentService } from '../../../services/student.service';
import { SubjectService } from '../../../services/subject.service';

/**
 * "Günün soruları" kapsam seçici (issue #66): ders (zorunlu değil) → isteğe bağlı konu → isteğe bağlı alt konu.
 * Ders seçilmezse kapsam null'dır (sınıftan rastgele, #99). Seçenekler öğrencinin sınıfına göre yüklenir; kart
 * yalnız açıldığında render ettiği için istekler de o zaman gider.
 */
@Component({
  selector: 'app-daily-scope-picker',
  standalone: true,
  imports: [MatFormFieldModule, MatIconModule, MatSelectModule, TranslocoDirective],
  templateUrl: './daily-scope-picker.component.html',
  styleUrls: ['./daily-scope-picker.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DailyScopePickerComponent implements OnInit {
  private readonly studentService = inject(StudentService);
  private readonly subjectService = inject(SubjectService);
  private readonly destroyRef = inject(DestroyRef);

  /** Ders seçilmemişse null; her seçim değişikliğinde yayılır. */
  readonly scopeChange = output<DailyStartScope | null>();

  readonly loading = signal(true);
  readonly error = signal(false);
  readonly subjects = signal<Subject[]>([]);
  readonly topics = signal<Topic[]>([]);
  readonly subTopics = signal<SubTopic[]>([]);

  readonly subjectId = signal<number | null>(null);
  readonly topicId = signal<number | null>(null);
  readonly subTopicId = signal<number | null>(null);

  private gradeId: number | null = null;

  ngOnInit(): void {
    this.studentService
      .getProfile()
      .pipe(
        switchMap((profile) => {
          this.gradeId = profile.gradeId > 0 ? profile.gradeId : null;
          return this.gradeId ? this.subjectService.getSubjectsByGrade(this.gradeId) : of([] as Subject[]);
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (subjects) => {
          this.subjects.set(subjects);
          this.loading.set(false);
        },
        error: () => {
          this.error.set(true);
          this.loading.set(false);
        },
      });
  }

  onSubjectChange(event: MatSelectChange): void {
    const subjectId = (event.value as number | null) ?? null;
    this.subjectId.set(subjectId);
    this.topicId.set(null);
    this.subTopicId.set(null);
    this.topics.set([]);
    this.subTopics.set([]);
    this.emit();

    if (subjectId == null || this.gradeId == null) return;
    this.subjectService
      .getTopicsBySubjectAndGrade(subjectId, this.gradeId)
      .pipe(
        catchError(() => of([] as Topic[])),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((topics) => {
        // Yanıt gelene kadar ders değiştiyse eski cevabı uygulama.
        if (this.subjectId() === subjectId) this.topics.set(topics);
      });
  }

  onTopicChange(event: MatSelectChange): void {
    const topicId = (event.value as number | null) ?? null;
    this.topicId.set(topicId);
    this.subTopicId.set(null);
    this.subTopics.set([]);
    this.emit();

    if (topicId == null) return;
    this.subjectService
      .getSubTopicsByTopic(topicId)
      .pipe(
        catchError(() => of([] as SubTopic[])),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((subTopics) => {
        if (this.topicId() === topicId) this.subTopics.set(subTopics);
      });
  }

  onSubTopicChange(event: MatSelectChange): void {
    this.subTopicId.set((event.value as number | null) ?? null);
    this.emit();
  }

  private emit(): void {
    const subjectId = this.subjectId();
    if (subjectId == null) {
      this.scopeChange.emit(null);
      return;
    }
    const topicId = this.topicId();
    this.scopeChange.emit({
      subjectId,
      topicId,
      subTopicId: topicId == null ? null : this.subTopicId(),
    });
  }
}
