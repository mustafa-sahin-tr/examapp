import { Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { catchError, debounceTime, of, switchMap, tap } from 'rxjs';
import { ClassificationSource } from '../../../models/draws';
import { QuestionBankFilter, QuestionBankItem, QuestionBankPage } from '../../../models/question-bank';
import { QuestionBankService } from '../../../services/question-bank.service';
import {
  ClassificationSelection,
  ClassificationSelectorComponent,
} from '../classification-selector/classification-selector.component';
import { QuestionBankPreviewDialogComponent } from './question-bank-preview-dialog.component';

/** Çeviriler kendi Transloco scope'unda: `public/i18n/question-bank/<lang>.json`. */
export const QUESTION_BANK_SCOPE = 'question-bank';

export type QuestionBankScope = 'mine' | 'all';
export type QuestionBankSourceFilter = 'any' | 'AI' | 'Human';
export type QuestionBankStatusFilter = 'any' | 'classified' | 'unclassified';
export type QuestionBankAnswerFilter = 'any' | 'yes' | 'no';
export type QuestionBankInTestFilter = 'any' | 'yes' | 'no';
export type QuestionBankDifficultyFilter = 'any' | 'easy' | 'medium' | 'hard';

/** Zorluk grupları (1-10 ölçeği, iki uç dahil). */
export const DIFFICULTY_RANGES: Record<Exclude<QuestionBankDifficultyFilter, 'any'>, [number, number]> = {
  easy: [1, 3],
  medium: [4, 7],
  hard: [8, 10],
};

const EMPTY_PAGE: QuestionBankPage = { items: [], total: 0, skip: 0, take: 0 };

/**
 * issue #471: soru bankası arama paneli — sınıf → ders → konu → alt konu kademeli seçim, durum filtreleri, sayfalı
 * sonuç ızgarası ve önizleme. Soru bankası sayfası kullanır; test ekranındaki "Havuzdan ekle" (#472) aynı bileşeni
 * `selectable` ile seçim modunda kullanır (zaten testte olanlar `disabledIds` ile pasif).
 */
@Component({
  selector: 'app-question-bank-panel',
  standalone: true,
  imports: [
    MatButtonModule,
    MatButtonToggleModule,
    MatCardModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatSelectModule,
    MatTooltipModule,
    TranslocoDirective,
    ClassificationSelectorComponent,
  ],
  providers: [provideTranslocoScope(QUESTION_BANK_SCOPE)],
  templateUrl: './question-bank-panel.component.html',
  styleUrls: ['./question-bank-panel.component.scss'],
})
export class QuestionBankPanelComponent {
  private readonly bank = inject(QuestionBankService);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);

  /** Seçim modu (#472): kartlarda onay kutusu, seçim `selectionChange` ile bildirilir. */
  readonly selectable = input(false);
  /** Seçilemeyen (ör. zaten testte olan) soru id'leri. */
  readonly disabledIds = input<number[]>([]);
  readonly selectionChange = output<number[]>();

  readonly pageSizeOptions = [12, 24, 48];
  readonly AI = ClassificationSource.AI;

  readonly scope = signal<QuestionBankScope>('mine');
  readonly classification = signal<ClassificationSelection | null>(null);
  readonly source = signal<QuestionBankSourceFilter>('any');
  readonly status = signal<QuestionBankStatusFilter>('any');
  readonly answer = signal<QuestionBankAnswerFilter>('any');
  readonly inTest = signal<QuestionBankInTestFilter>('any');
  readonly difficulty = signal<QuestionBankDifficultyFilter>('any');
  readonly pageIndex = signal(0);
  readonly pageSize = signal(24);

  // İlk arama debounce'tan önce de "yükleniyor" sayılır (boş sonuç mesajı bir an görünmesin).
  readonly loading = signal(true);
  readonly error = signal(false);
  readonly page = signal<QuestionBankPage>(EMPTY_PAGE);
  readonly selectedIds = signal<ReadonlySet<number>>(new Set());

  private readonly disabledSet = computed(() => new Set(this.disabledIds()));

  readonly filter = computed<QuestionBankFilter>(() => {
    const c = this.classification();
    const difficulty = this.difficulty();
    const range = difficulty === 'any' ? null : DIFFICULTY_RANGES[difficulty];
    const source = this.source();
    const status = this.status();
    const answer = this.answer();
    const inTest = this.inTest();
    return {
      scope: this.scope(),
      gradeId: c?.gradeId ?? null,
      subjectId: c?.subjectId ?? null,
      topicId: c?.topicId ?? null,
      subTopicIds: c?.subtopicIds ?? [],
      minDifficulty: range?.[0] ?? null,
      maxDifficulty: range?.[1] ?? null,
      source: source === 'any' ? null : source,
      classified: status === 'any' ? null : status === 'classified',
      hasCorrectAnswer: answer === 'any' ? null : answer === 'yes',
      inTest: inTest === 'any' ? null : inTest === 'yes',
      skip: this.pageIndex() * this.pageSize(),
      take: this.pageSize(),
    };
  });

  constructor() {
    toObservable(this.filter)
      .pipe(
        tap(() => this.loading.set(true)),
        debounceTime(250),
        tap(() => this.error.set(false)),
        switchMap((f) =>
          this.bank.search(f).pipe(
            catchError(() => {
              this.error.set(true);
              return of(EMPTY_PAGE);
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((page) => {
        this.page.set(page ?? EMPTY_PAGE);
        this.loading.set(false);
      });
  }

  // Filtre değişince ilk sayfaya dönülür.
  onClassificationChange(selection: ClassificationSelection): void {
    this.classification.set(selection);
    this.pageIndex.set(0);
  }

  setScope(value: QuestionBankScope): void {
    this.scope.set(value);
    this.pageIndex.set(0);
  }

  setSource(value: QuestionBankSourceFilter): void {
    this.source.set(value);
    this.pageIndex.set(0);
  }

  setStatus(value: QuestionBankStatusFilter): void {
    this.status.set(value);
    this.pageIndex.set(0);
  }

  setAnswer(value: QuestionBankAnswerFilter): void {
    this.answer.set(value);
    this.pageIndex.set(0);
  }

  setInTest(value: QuestionBankInTestFilter): void {
    this.inTest.set(value);
    this.pageIndex.set(0);
  }

  setDifficulty(value: QuestionBankDifficultyFilter): void {
    this.difficulty.set(value);
    this.pageIndex.set(0);
  }

  onPage(event: PageEvent): void {
    this.pageSize.set(event.pageSize);
    this.pageIndex.set(event.pageIndex);
  }

  isDisabled(item: QuestionBankItem): boolean {
    return this.disabledSet().has(item.id);
  }

  /** issue #472: seçim modunda teste bağlanamayan soru (başkasının sahipsiz/legacy sorusu; karar sunucuda). */
  isLinkBlocked(item: QuestionBankItem): boolean {
    return this.selectable() && !this.isDisabled(item) && item.canLink === false;
  }

  isSelected(item: QuestionBankItem): boolean {
    return this.selectedIds().has(item.id);
  }

  toggleSelection(item: QuestionBankItem): void {
    if (!this.selectable() || this.isDisabled(item) || this.isLinkBlocked(item)) return;
    const next = new Set(this.selectedIds());
    if (next.has(item.id)) next.delete(item.id);
    else next.add(item.id);
    this.selectedIds.set(next);
    this.selectionChange.emit([...next]);
  }

  clearSelection(): void {
    this.selectedIds.set(new Set());
    this.selectionChange.emit([]);
  }

  preview(item: QuestionBankItem): void {
    this.dialog.open(QuestionBankPreviewDialogComponent, {
      data: item,
      maxWidth: '90vw',
      autoFocus: false,
    });
  }
}
