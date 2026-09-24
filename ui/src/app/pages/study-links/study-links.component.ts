import { Component, WritableSignal, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective, TranslocoPipe, provideTranslocoScope } from '@jsverse/transloco';
import { Observable, catchError, combineLatest, finalize, map, of, switchMap } from 'rxjs';
import { SubjectService } from '../../services/subject.service';
import { GradesService } from '../../services/grades.service';
import { Subject } from '../../models/subject';
import { Topic } from '../../models/topic';
import { SubTopic } from '../../models/subtopic';
import { TopicStudyLinkManagerComponent } from '../../shared/components/topic-study-link-manager/topic-study-link-manager.component';

/** Çalışma linki bileşenlerinin Transloco scope'u: `public/i18n/study-links/<lang>.json`. */
const STUDY_LINKS_SCOPE = 'study-links';

/** Tek bir seçim listesinin durumu. `items === null` → henüz istenmedi (üst seviye seçilmedi). */
interface ListState<T> {
  items: T[] | null;
  loading: boolean;
  failed: boolean;
}

const IDLE: ListState<never> = { items: null, loading: false, failed: false };

/**
 * Issue #61 — öğretmenin (Admin/Teacher backend yetkisi) tüm konu/alt konular için çalışma linki yönetimi.
 * Admin taksonomi ekranı yalnız admin'e açık olduğundan öğretmen Ders → Konu → Alt Konu seçimini burada,
 * herkese açık `api/subject` uçlarıyla yapar; alt konu seçilmezse konu seviyesi linkler yönetilir.
 */
@Component({
  selector: 'app-study-links',
  standalone: true,
  imports: [
    MatFormFieldModule,
    MatSelectModule,
    MatIconModule,
    MatButtonModule,
    TranslocoDirective,
    TranslocoPipe,
    TopicStudyLinkManagerComponent,
  ],
  providers: [provideTranslocoScope(STUDY_LINKS_SCOPE)],
  templateUrl: './study-links.component.html',
  styleUrls: ['./study-links.component.scss'],
})
export class StudyLinksComponent {
  private readonly subjectService = inject(SubjectService);
  private readonly gradesService = inject(GradesService);

  readonly selectedSubjectId = signal<number | null>(null);
  readonly selectedTopicId = signal<number | null>(null);
  readonly selectedSubTopicId = signal<number | null>(null);

  private readonly subjectsTick = signal(0);
  private readonly topicsTick = signal(0);
  private readonly subTopicsTick = signal(0);

  readonly subjects = signal<ListState<Subject>>(IDLE);
  readonly topics = signal<ListState<Topic>>(IDLE);
  readonly subTopics = signal<ListState<SubTopic>>(IDLE);

  /** Konu seçeneklerinde sınıf adı (aynı adlı konular farklı sınıflarda olabilir). Hata → yalnız konu adı. */
  private readonly gradeNames = toSignal(
    this.gradesService.getGrades().pipe(
      map((grades) => new Map(grades.map((g) => [g.id, g.name] as const))),
      catchError(() => of(new Map<number, string>()))
    ),
    { initialValue: new Map<number, string>() }
  );

  readonly sortedTopics = computed(() =>
    [...(this.topics().items ?? [])].sort((a, b) => a.gradeId - b.gradeId || a.name.localeCompare(b.name))
  );
  readonly selectedTopic = computed(() => this.sortedTopics().find((t) => t.id === this.selectedTopicId()) ?? null);
  readonly selectedSubTopic = computed(
    () => (this.subTopics().items ?? []).find((st) => st.id === this.selectedSubTopicId()) ?? null
  );
  readonly scopeName = computed(() => this.selectedSubTopic()?.name ?? this.selectedTopic()?.name ?? '');

  constructor() {
    this.bindList(
      toObservable(this.subjectsTick).pipe(map(() => true as const)),
      () => this.subjectService.loadCategories(),
      this.subjects
    );
    this.bindList(
      combineLatest([toObservable(this.selectedSubjectId), toObservable(this.topicsTick)]).pipe(map(([id]) => id)),
      (subjectId) => this.subjectService.getTopicsBySubject(subjectId),
      this.topics
    );
    this.bindList(
      combineLatest([toObservable(this.selectedTopicId), toObservable(this.subTopicsTick)]).pipe(map(([id]) => id)),
      (topicId) => this.subjectService.getSubTopicsByTopic(topicId),
      this.subTopics
    );
  }

  selectSubject(id: number | null): void {
    this.selectedSubjectId.set(id);
    this.selectedTopicId.set(null);
    this.selectedSubTopicId.set(null);
  }

  selectTopic(id: number | null): void {
    this.selectedTopicId.set(id);
    this.selectedSubTopicId.set(null);
  }

  selectSubTopic(id: number | null): void {
    this.selectedSubTopicId.set(id);
  }

  retrySubjects(): void {
    this.subjectsTick.update((n) => n + 1);
  }

  retryTopics(): void {
    this.topicsTick.update((n) => n + 1);
  }

  retrySubTopics(): void {
    this.subTopicsTick.update((n) => n + 1);
  }

  gradeName(gradeId: number): string | null {
    return this.gradeNames().get(gradeId) ?? null;
  }

  /**
   * Üst seçim her değiştiğinde listeyi yeniden ister (önceki istek iptal); üst seçim yoksa liste boşa döner.
   * `true` anahtarı parametresiz (kök) liste içindir.
   */
  private bindList<K, T>(
    key$: Observable<K | null>,
    load: (key: K) => Observable<T[]>,
    state: WritableSignal<ListState<T>>
  ): void {
    key$
      .pipe(
        switchMap((key) => {
          if (key == null) {
            state.set(IDLE);
            return of(null);
          }
          state.set({ items: null, loading: true, failed: false });
          return load(key).pipe(
            map((items): ListState<T> => ({ items: items ?? [], loading: false, failed: false })),
            catchError(() => of<ListState<T>>({ items: [], loading: false, failed: true })),
            finalize(() => state.update((s) => ({ ...s, loading: false })))
          );
        }),
        takeUntilDestroyed()
      )
      .subscribe((next) => {
        if (next) state.set(next);
      });
  }
}
