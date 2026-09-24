import { Component, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { EMPTY, catchError, distinctUntilChanged, finalize, of, switchMap } from 'rxjs';
import { QuestionStudyLinkSuggestion, StudyLinkGroup, StudyLinkSummary } from '../../../models/study-link';
import { StudyLinkService } from '../../../services/study-link.service';

/** Çalışma linki bileşenlerinin Transloco scope'u: `public/i18n/study-links/<lang>.json`. */
const STUDY_LINKS_SCOPE = 'study-links';

/**
 * Issue #61 — öğrenci sonuç ekranında, o an görüntülenen soru YANLIŞ cevaplandıysa sorunun alt konu(lar)ına
 * (ve konu seviyesi yedek gruplara) ait aktif çalışma linklerini gruplu gösterir.
 * Öneriler test oturumu başına bir kez, sonucu bloklamadan asenkron yüklenir; hiç link yoksa blok hiç çizilmez.
 */
@Component({
  selector: 'app-study-link-suggestions',
  standalone: true,
  imports: [MatIconModule, TranslocoDirective],
  providers: [provideTranslocoScope(STUDY_LINKS_SCOPE)],
  templateUrl: './study-link-suggestions.component.html',
  styleUrls: ['./study-link-suggestions.component.scss'],
})
export class StudyLinkSuggestionsComponent {
  private readonly service = inject(StudyLinkService);

  /** Tamamlanmış test oturumu (WorksheetInstance) kimliği. */
  readonly testInstanceId = input<number | null>(null);
  /** O an görüntülenen sorunun `Question.id`'si. */
  readonly questionId = input<number | null>(null);
  /** O an görüntülenen test-instance-question kimliği (biliniyorsa öncelikli eşleşme). */
  readonly testInstanceQuestionId = input<number | null>(null);
  /** Görüntülenen soru yanlış cevaplandı mı — doğru/boş sorularda hiçbir şey gösterilmez. */
  readonly answeredWrong = input(false);

  readonly suggestions = signal<QuestionStudyLinkSuggestion[]>([]);
  readonly loading = signal(false);
  readonly failed = signal(false);

  /** Görüntülenen yanlış soruya ait, en az bir linki olan gruplar. */
  readonly groups = computed<StudyLinkGroup[]>(() => {
    if (!this.answeredWrong()) return [];
    const tiqId = this.testInstanceQuestionId();
    const questionId = this.questionId();
    const match =
      (tiqId != null ? this.suggestions().find((s) => s.testInstanceQuestionId === tiqId) : undefined) ??
      (questionId != null ? this.suggestions().find((s) => s.questionId === questionId) : undefined);
    return (match?.groups ?? []).filter((g) => g.links.length > 0);
  });

  readonly showLoading = computed(() => this.answeredWrong() && this.loading());
  readonly showError = computed(() => this.answeredWrong() && !this.loading() && this.failed());

  constructor() {
    toObservable(this.testInstanceId)
      .pipe(
        distinctUntilChanged(),
        switchMap((id) => {
          this.suggestions.set([]);
          this.failed.set(false);
          if (id == null) return EMPTY;
          this.loading.set(true);
          return this.service.getForResult(id).pipe(
            catchError(() => {
              this.failed.set(true);
              return of<QuestionStudyLinkSuggestion[]>([]);
            }),
            finalize(() => this.loading.set(false))
          );
        }),
        takeUntilDestroyed()
      )
      .subscribe((items) => this.suggestions.set(items ?? []));
  }

  /** `@for` track anahtarı: aynı soruda alt konu ve konu grupları çakışmasın diye tür + kimlik. */
  groupKey(group: StudyLinkGroup): string {
    return `${group.kind}-${group.subTopicId ?? group.topicId ?? group.name}`;
  }

  sourceIcon(link: StudyLinkSummary): string {
    return link.sourceType === 'YouTube' ? 'smart_display' : 'article';
  }
}
