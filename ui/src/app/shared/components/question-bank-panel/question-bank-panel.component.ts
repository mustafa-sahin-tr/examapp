import { Component, DestroyRef, computed, inject, input, linkedSignal, output, signal, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { catchError, debounceTime, of, switchMap, tap } from 'rxjs';
import { ClassificationSource } from '../../../models/draws';
import { QuestionBankFilter, QuestionBankItem, QuestionBankPage } from '../../../models/question-bank';
import { QuestionBankService } from '../../../services/question-bank.service';
import {
  ClassificationSelection,
  ClassificationSelectorComponent,
} from '../classification-selector/classification-selector.component';
import {
  QuestionBankPreviewData,
  QuestionBankPreviewDialogComponent,
  QuestionBankPreviewResult,
} from './question-bank-preview-dialog.component';

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
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatTooltipModule,
    TranslocoDirective,
    ClassificationSelectorComponent,
  ],
  providers: [provideTranslocoScope(QUESTION_BANK_SCOPE)],
  templateUrl: './question-bank-panel.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./question-bank-panel.component.scss'],
})
export class QuestionBankPanelComponent {
  private readonly bank = inject(QuestionBankService);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);

  /** Seçim modu (#472): kartlarda onay kutusu, seçim `selectionChange` ile bildirilir. */
  /** Sınıf/Ders/Konu/Alt Konu filtresinin düzeni: sayfada çip, "Havuzdan soru ekle" diyaloğunda açılır kutu. */
  readonly filterLayout = input<'chips' | 'selects'>('chips');
  readonly selectable = input(false);
  /** Seçilemeyen (ör. zaten testte olan) soru id'leri. */
  readonly disabledIds = input<number[]>([]);
  readonly selectionChange = output<number[]>();
  /**
   * Issue #580: kartlarda "Çözümü onayla" (onay bekleyen etkinlik çözümü). Yalnız Admin + QuestionTrainer için açılır;
   * sunucu da diğer rollere 403 verir.
   */
  readonly canApproveSolutions = input(false);
  /** Admin başkasının sorusunu da onaylar; QuestionTrainer yalnız kendi sorusunu (sunucu kuralı). */
  readonly canApproveOthersSolutions = input(false);

  readonly pageSizeOptions = [12, 24, 48];
  readonly AI = ClassificationSource.AI;
  readonly BOOK = ClassificationSource.Book;

  readonly scope = signal<QuestionBankScope>('mine');
  // Başlangıç değeri input'tan gelir; kullanıcı seçimi (`onClassificationChange`) üzerine yazar.
  readonly classification = linkedSignal<ClassificationSelection | null>(() => this.initialClassification());
  /**
   * Açılışta seçili gelecek sınıf/ders/konu/alt konu (ör. "Havuzdan soru ekle" → testin kendi sınıflandırması).
   * Seçici `initialSelection` uygularken `selectionChange` yaymaz; ilk arama bu değeri `classification` üzerinden alır.
   */
  readonly initialClassification = input<ClassificationSelection | null>(null);

  readonly source = signal<QuestionBankSourceFilter>('any');
  readonly status = signal<QuestionBankStatusFilter>('any');
  readonly answer = signal<QuestionBankAnswerFilter>('any');
  readonly inTest = signal<QuestionBankInTestFilter>('any');
  readonly difficulty = signal<QuestionBankDifficultyFilter>('any');
  /** Issue #580: yalnız onay bekleyen çözümü olan sorular. */
  readonly pendingSolutionsOnly = signal(false);
  /** Çözüm onayı süren soru id'leri. */
  readonly approvingIds = signal<ReadonlySet<number>>(new Set());
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
      solutionStatus: this.pendingSolutionsOnly() ? 'pending' : null,
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

  setPendingSolutionsOnly(value: boolean): void {
    this.pendingSolutionsOnly.set(value);
    this.pageIndex.set(0);
  }

  /** Kart etiketi yalnız anahtardan / AI gelen çözümde (elle yazılan sorular zaten onaylı sayılır, etiket gürültü olurdu). */
  hasGeneratedSolution(item: QuestionBankItem): boolean {
    return item.solutionSource === 'Key' || item.solutionSource === 'AI';
  }

  /** Kartta "Çözümü onayla" görünür mü: yetki + onay bekliyor + (kendi sorusu ya da Admin), seçim modunda değil. */
  canApprove(item: QuestionBankItem): boolean {
    return (
      !this.selectable() &&
      this.canApproveSolutions() &&
      item.solutionStatus === 'pending' &&
      (item.isOwn || this.canApproveOthersSolutions())
    );
  }

  isApproving(item: QuestionBankItem): boolean {
    return this.approvingIds().has(item.id);
  }

  /**
   * Issue #580: "Çözümü onayla" önce önizlemeyi açar (soru, çözüm kaynağı, talimat sürümü); onay diyalogdaki "Onayla" ile.
   */
  reviewSolution(item: QuestionBankItem): void {
    if (!this.canApprove(item) || this.isApproving(item)) return;
    const data: QuestionBankPreviewData = { item, approve: true };
    this.dialog
      .open<QuestionBankPreviewDialogComponent, QuestionBankPreviewData, QuestionBankPreviewResult>(
        QuestionBankPreviewDialogComponent,
        { data, maxWidth: '90vw', autoFocus: false }
      )
      .afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((result) => {
        if (result === 'approve') this.approveSolution(item);
      });
  }

  /**
   * Issue #580: sorunun çözümünü onaylar. Başarılıysa kart onaylı görünür; "Onay bekleyen" filtresi açıksa listeden çıkar.
   * Hata mesajı (yerelleştirilmiş) sunucudan gelir.
   */
  approveSolution(item: QuestionBankItem): void {
    if (!this.canApprove(item) || this.isApproving(item)) return;
    this.approvingIds.update((ids) => new Set([...ids, item.id]));
    this.bank
      .approveSolution(item.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (state) => {
          this.approvingIds.update((ids) => without(ids, item.id));
          this.page.update((page) => {
            if (this.pendingSolutionsOnly()) {
              const items = page.items.filter((i) => i.id !== item.id);
              return { ...page, items, total: Math.max(0, page.total - (page.items.length - items.length)) };
            }
            const approved: Partial<QuestionBankItem> = {
              solutionStatus: state?.solutionStatus ?? 'approved',
              ...(state?.solutionSource ? { solutionSource: state.solutionSource } : {}),
            };
            return { ...page, items: page.items.map((i) => (i.id === item.id ? { ...i, ...approved } : i)) };
          });
          this.snackBar.open(this.tr('card.solutionApproved'), this.tr('card.close'), { duration: 3000 });
        },
        error: (error: unknown) => {
          this.approvingIds.update((ids) => without(ids, item.id));
          const status = (error as { status?: number })?.status;
          const serverMessage = (error as { error?: { message?: string } })?.error?.message;
          const message =
            serverMessage || this.tr(status === 403 ? 'card.solutionForbidden' : 'card.solutionApproveFailed');
          this.snackBar.open(message, this.tr('card.close'), { duration: 5000 });
        },
      });
  }

  private tr(key: string): string {
    return this.transloco.translate<string>(`${QUESTION_BANK_SCOPE}.${key}`) ?? '';
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

function without(ids: ReadonlySet<number>, id: number): ReadonlySet<number> {
  const next = new Set(ids);
  next.delete(id);
  return next;
}
