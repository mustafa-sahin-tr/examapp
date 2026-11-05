import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { EMPTY, Observable, Subject, catchError, defer, finalize, switchMap, tap } from 'rxjs';

import { LearningOutcomeMastery, MasteryItem, MasteryLevel } from '../../../models/learning-outcome-mastery.model';
import { LearningOutcomeMasteryService } from '../../../services/learning-outcome-mastery.service';
import { LocaleService } from '../../../services/locale.service';

const SCOPE = 'outcome-mastery';

/** İki müfredat birlikte yaşarken (#692) yeni olan önce açılır. */
export const DEFAULT_CURRICULUM = 'Maarif';

const LEVEL_ICONS: Record<MasteryLevel, string> = {
  notStarted: 'radio_button_unchecked',
  familiar: 'trending_up',
  proficient: 'check_circle',
  mastered: 'workspace_premium',
};

/** Sırada ne çalışılacağı önce görünsün: zayıf satır üstte, başlanmamış altta; aynı seviyede sunucu sırası korunur. */
const LEVEL_ORDER: Record<MasteryLevel, number> = { familiar: 0, proficient: 1, mastered: 2, notStarted: 3 };

interface MasteryRow {
  key: string;
  item: MasteryItem;
  icon: string;
  accuracy: string | null;
}

interface SubjectGroup {
  key: string;
  name: string | null;
  rows: MasteryRow[];
  counts: Record<MasteryLevel, number>;
}

/**
 * Issue #696: kazanım haritası. Girdi yoksa öğrencinin kendisi (zayıf satırda pratik bağlantısıyla), `childId` velinin seçili
 * çocuğu, `teacherStudentId` öğretmenin baktığı öğrenci; ikisi de salt okunur. Seviye ve sıra sunucudan gelir.
 * Velide 404 kartı gizler, öğretmende açıklama gösterir: sayfaya bağlantıyla gelmiştir.
 */
@Component({
  selector: 'app-outcome-mastery-card',
  standalone: true,
  imports: [MatButtonModule, MatButtonToggleModule, MatCardModule, MatExpansionModule, MatIconModule, MatProgressSpinnerModule, RouterLink, TranslocoDirective],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './outcome-mastery-card.component.html',
  styleUrl: './outcome-mastery-card.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class OutcomeMasteryCardComponent {
  readonly childId = input<number | null>(null);
  readonly teacherStudentId = input<number | null>(null);

  private readonly service = inject(LearningOutcomeMasteryService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly report = signal<LearningOutcomeMastery | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal(false);
  protected readonly notFound = signal(false);
  private readonly pickedCurriculum = signal<string | null>(null);

  protected readonly audience = computed(() => (this.teacherStudentId() != null ? 'teacher' : this.childId() != null ? 'child' : 'self'));

  protected readonly curricula = computed(() => {
    const versions = new Set((this.report()?.items ?? []).map((i) => i.curriculumVersion).filter((v): v is string => v != null));
    return [...versions].sort((a, b) => Number(b === DEFAULT_CURRICULUM) - Number(a === DEFAULT_CURRICULUM));
  });

  protected readonly curriculum = computed(() => this.pickedCurriculum() ?? this.curricula()[0] ?? null);

  protected readonly groups = computed<SubjectGroup[]>(() => {
    const percent = new Intl.NumberFormat(this.localeService.localeDefinition().angularLocale, {
      style: 'percent',
      maximumFractionDigits: 0,
    });
    const curriculum = this.curriculum();
    const groups = new Map<string, SubjectGroup>();
    (this.report()?.items ?? []).forEach((item, index) => {
      // Kazanımı olmayan satırın müfredatı yok: her iki seçimde de görünür.
      if (item.curriculumVersion != null && item.curriculumVersion !== curriculum) return;
      const key = String(item.subjectId ?? 'other');
      let group = groups.get(key);
      if (!group) {
        group = { key, name: item.subjectName, rows: [], counts: { notStarted: 0, familiar: 0, proficient: 0, mastered: 0 } };
        groups.set(key, group);
      }
      group.counts[item.level]++;
      group.rows.push({
        key: `${item.kind}-${item.learningOutcomeId ?? item.subTopicId ?? item.topicId ?? index}`,
        item,
        icon: LEVEL_ICONS[item.level],
        accuracy: item.accuracyPct == null ? null : percent.format(item.accuracyPct / 100),
      });
    });
    const list = [...groups.values()];
    for (const group of list) group.rows.sort((a, b) => LEVEL_ORDER[a.item.level] - LEVEL_ORDER[b.item.level]);
    // Çalışılmış ders önce: açık gelen ilk panel boş bir "başlanmadı" listesi olmasın.
    const started = (group: SubjectGroup) => (group.rows.length > group.counts.notStarted ? 0 : 1);
    return list.sort((a, b) => started(a) - started(b));
  });

  private readonly requests = new Subject<void>();

  constructor() {
    this.requests
      .pipe(
        switchMap(() => this.fetch()),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();

    effect(() => {
      this.childId();
      this.teacherStudentId();
      untracked(() => this.requests.next());
    });
  }

  protected reload(): void {
    this.requests.next();
  }

  protected pickCurriculum(version: string): void {
    this.pickedCurriculum.set(version);
  }

  /** Zayıf satırdan pratik: konu biliniyorsa konuya, kazanım konusuz eşlendiyse derse kurulur (#521 bağlantısıyla aynı). */
  protected practiceParams(item: MasteryItem): Record<string, number> {
    return item.topicId == null ? { subjectId: item.subjectId! } : { subjectId: item.subjectId!, topicId: item.topicId };
  }

  private fetch(): Observable<unknown> {
    return defer(() => {
      this.loading.set(true);
      this.error.set(false);
      this.notFound.set(false);
      this.report.set(null);
      this.pickedCurriculum.set(null);
      const teacherStudentId = this.teacherStudentId();
      const childId = this.childId();
      const request =
        teacherStudentId != null
          ? this.service.getTeacherStudent(teacherStudentId)
          : childId != null
            ? this.service.getChild(childId)
            : this.service.getMine();
      return request.pipe(
        tap((report) => this.report.set(report)),
        catchError((err: HttpErrorResponse) => {
          if (err?.status === 404) this.notFound.set(true);
          else this.error.set(true);
          return EMPTY;
        }),
        finalize(() => this.loading.set(false))
      );
    });
  }
}
