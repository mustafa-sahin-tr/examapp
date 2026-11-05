import { ChangeDetectionStrategy, Component, computed, inject, OnDestroy, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { Subscription, timer } from 'rxjs';
import { LearningOutcome } from '../../models/learning-outcome';
import { Grade } from '../../models/student';
import { Subject } from '../../models/subject';
import { GradesService } from '../../services/grades.service';
import { LearningOutcomeService } from '../../services/learning-outcome.service';
import { QuestionDraft, QuestionGenerationService } from '../../services/question-generation.service';
import { SubjectService } from '../../services/subject.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';

const SCOPE = 'question-generation';
const SEARCH_DEBOUNCE_MS = 300;
const MIN_OPTIONS = 2;

export const DIFFICULTIES = [
  { level: 3, key: 'easy' },
  { level: 5, key: 'medium' },
  { level: 8, key: 'hard' },
] as const;

/** Düzenlenen taslak: kaydedilecekler işaretlenir. */
export interface EditableDraft extends QuestionDraft {
  /** Düzenlemede nesne değişir; kaydedilenler bu anahtarla tanınır. */
  key: number;
  selected: boolean;
}

@Component({
  selector: 'app-question-generation',
  standalone: true,
  imports: [
    PageHeaderComponent,
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatRadioModule,
    MatSelectModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './question-generation.component.html',
  styleUrls: ['./question-generation.component.scss'],
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class QuestionGenerationComponent implements OnDestroy {
  private readonly generation = inject(QuestionGenerationService);
  private readonly gradesService = inject(GradesService);
  private readonly subjectService = inject(SubjectService);
  private readonly outcomeService = inject(LearningOutcomeService);

  readonly difficulties = DIFFICULTIES;
  readonly counts = [1, 2, 3, 4, 5];

  readonly grades = signal<Grade[]>([]);
  readonly subjects = signal<Subject[]>([]);
  readonly outcomes = signal<LearningOutcome[]>([]);
  readonly gradeId = signal<number | null>(null);
  readonly subjectId = signal<number | null>(null);
  readonly outcomeId = signal<number | null>(null);
  readonly search = signal('');
  readonly count = signal(3);
  readonly difficulty = signal(5);
  readonly outcomesLoading = signal(false);
  readonly outcomesFailed = signal(false);

  readonly generating = signal(false);
  /** 'unavailable' | 'limit' | 'budget' | 'failed' */
  readonly generateError = signal<string | null>(null);
  readonly rejected = signal(0);

  /** Taslaklar üretildikleri kazanıma aittir: seçim sonradan değişse de kayıt bu kazanıma yapılır. */
  readonly draftOutcome = signal<{ id: number; code: string; description: string; difficulty: number } | null>(null);
  readonly drafts = signal<EditableDraft[]>([]);
  /** Taslak başka kazanım ya da zorluk için: yeni üretim önce bunların kaydedilmesini ya da silinmesini ister, düzenleme kaybolmasın. */
  readonly selectionDiffersFromDrafts = computed(() => {
    const outcome = this.draftOutcome();
    return this.drafts().length > 0 && !!outcome && (outcome.id !== this.outcomeId() || outcome.difficulty !== this.difficulty());
  });
  readonly selectedCount = computed(() => this.drafts().filter((d) => d.selected).length);
  readonly canSave = computed(() => {
    const selected = this.drafts().filter((d) => d.selected);
    return selected.length > 0 && selected.every((d) => this.isDraftComplete(d));
  });

  readonly saving = signal(false);
  readonly saveFailed = signal<string | null>(null);
  readonly savedCount = signal(0);

  private nextDraftKey = 1;
  private subjectsSub?: Subscription;
  private outcomesSub?: Subscription;
  private searchSub?: Subscription;
  private generateSub?: Subscription;

  constructor() {
    this.gradesService.getGrades().subscribe({ next: (g) => this.grades.set(g ?? []), error: () => this.grades.set([]) });
  }

  ngOnDestroy(): void {
    this.subjectsSub?.unsubscribe();
    this.outcomesSub?.unsubscribe();
    this.searchSub?.unsubscribe();
    this.generateSub?.unsubscribe();
  }

  onGradeChange(id: number | null): void {
    this.gradeId.set(id);
    this.clearSubject();
    this.subjectsSub?.unsubscribe();
    if (id === null) {
      this.subjects.set([]);
      return;
    }
    // Önceki sınıfın geç gelen yanıtı listeyi ezmesin.
    this.subjectsSub = this.subjectService.getSubjectsByGrade(id).subscribe({
      next: (s) => this.subjects.set(s ?? []),
      error: () => this.subjects.set([]),
    });
  }

  onSubjectChange(id: number | null): void {
    this.subjectId.set(id);
    this.reloadOutcomes();
  }

  onSearchChange(value: string): void {
    this.search.set(value);
    this.searchSub?.unsubscribe();
    this.searchSub = timer(SEARCH_DEBOUNCE_MS).subscribe(() => this.reloadOutcomes());
  }

  private clearSubject(): void {
    this.subjectId.set(null);
    this.outcomeId.set(null);
    this.outcomes.set([]);
    this.outcomesSub?.unsubscribe();
    this.outcomesLoading.set(false);
    this.outcomesFailed.set(false);
  }

  reloadOutcomes(): void {
    this.searchSub?.unsubscribe();
    this.outcomesSub?.unsubscribe();
    this.outcomeId.set(null);
    const gradeId = this.gradeId();
    const subjectId = this.subjectId();
    if (gradeId === null || subjectId === null) {
      this.outcomes.set([]);
      this.outcomesLoading.set(false);
      return;
    }
    this.outcomesLoading.set(true);
    this.outcomesFailed.set(false);
    this.outcomesSub = this.outcomeService.search(gradeId, subjectId, this.search()).subscribe({
      next: (outcomes) => {
        this.outcomesLoading.set(false);
        this.outcomes.set(outcomes ?? []);
      },
      error: () => {
        this.outcomesLoading.set(false);
        this.outcomes.set([]);
        this.outcomesFailed.set(true);
      },
    });
  }

  generate(): void {
    const outcomeId = this.outcomeId();
    if (outcomeId === null || this.generating() || this.saving() || this.selectionDiffersFromDrafts()) return;

    const difficulty = this.difficulty();
    this.generating.set(true);
    this.generateError.set(null);
    this.savedCount.set(0);
    this.generateSub = this.generation.generate(outcomeId, this.count(), difficulty).subscribe({
      next: (response) => {
        this.generating.set(false);
        this.rejected.set(response.rejected);
        const fresh = response.drafts.map((d) => ({ ...d, key: this.nextDraftKey++, selected: true }));
        this.drafts.update((drafts) => [...drafts, ...fresh]);
        this.draftOutcome.set({ id: response.learningOutcomeId, code: response.outcomeCode, description: response.outcomeDescription, difficulty });
      },
      error: (err: HttpErrorResponse) => {
        this.generating.set(false);
        this.generateError.set(this.errorKey(err));
      },
    });
  }

  private errorKey(err: HttpErrorResponse): string {
    if (err.status === 503) return 'unavailable';
    if (err.status === 429) return (err.error as { errorCode?: string } | null)?.errorCode === 'BudgetExceeded' ? 'budget' : 'limit';
    return 'failed';
  }

  // ---- düzenleme

  private updateDraft(index: number, change: (draft: EditableDraft) => EditableDraft): void {
    this.drafts.update((drafts) => drafts.map((d, i) => (i === index ? change(d) : d)));
  }

  setText(index: number, text: string): void {
    this.updateDraft(index, (d) => ({ ...d, text }));
  }

  setOption(index: number, optionIndex: number, value: string): void {
    this.updateDraft(index, (d) => ({ ...d, options: d.options.map((o, i) => (i === optionIndex ? value : o)) }));
  }

  setCorrect(index: number, correctIndex: number): void {
    this.updateDraft(index, (d) => ({ ...d, correctIndex }));
  }

  setSelected(index: number, selected: boolean): void {
    this.updateDraft(index, (d) => ({ ...d, selected }));
  }

  removeDraft(index: number): void {
    this.drafts.update((drafts) => drafts.filter((_, i) => i !== index));
  }

  clearDrafts(): void {
    this.drafts.set([]);
    this.rejected.set(0);
  }

  optionLabel(index: number): string {
    return String.fromCharCode(65 + index);
  }

  /** Sunucunun kuralları: boş ya da aynı şık, geçersiz doğru cevap. Görsel atfı sunucuda denetlenir. */
  isDraftComplete(draft: QuestionDraft): boolean {
    const options = draft.options.map((o) => o.trim().toLocaleLowerCase('tr'));
    return draft.text.trim().length > 0
      && options.length >= MIN_OPTIONS
      && options.every((o) => o.length > 0)
      && new Set(options).size === options.length
      && draft.correctIndex >= 0
      && draft.correctIndex < options.length;
  }

  save(): void {
    const outcome = this.draftOutcome();
    if (!outcome || !this.canSave() || this.saving() || this.generating()) return;

    const chosen = this.drafts().filter((d) => d.selected);
    this.saving.set(true);
    this.saveFailed.set(null);
    this.savedCount.set(0);
    this.generation
      .save(outcome.id, outcome.difficulty, chosen.map(({ text, options, correctIndex }) => ({ text, options, correctIndex })))
      .subscribe({
        next: (saved) => {
          this.saving.set(false);
          this.savedCount.set(saved.questionIds.length);
          // Kaydedilen taslak ekranda kalırsa ikinci kez kaydedilebilir. Seçim istek sürerken değişmiş olabilir: gönderilenler silinir.
          const sent = new Set(chosen.map((d) => d.key));
          this.drafts.update((drafts) => drafts.filter((d) => !sent.has(d.key)));
          this.rejected.set(0);
        },
        error: (err: HttpErrorResponse) => {
          this.saving.set(false);
          const message = (err.error as { message?: unknown } | null)?.message;
          this.saveFailed.set(typeof message === 'string' ? message : '');
        },
      });
  }
}
