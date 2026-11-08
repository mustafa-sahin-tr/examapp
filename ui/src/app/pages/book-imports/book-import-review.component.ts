import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

import {
  ApproveAllBookImportResult,
  BookImportJobTest,
  BookImportReviewQuestion,
  bookImportTestName,
  isApprovable,
  labelingFixParams,
  needsAttention,
} from '../../models/book-import';
import { BookImportService } from '../../services/book-import.service';

export const ANSWER_LETTERS = ['A', 'B', 'C', 'D', 'E'];

/**
 * Issue #1023 (epic #1017 maketi, adım 5): aktarılan taslak testlerin incelemesi. "Yalnız bakılması gerekenler" varsayılan açık;
 * satırda işaretler, sayılar ve Önizle / Onayla / Sil; önizlemede soru görselleri ve cevapsız sorular için A–D seçici
 * ("cevaplanan x/y"). Cevapsız sorusu olan test onaylanmaz; "Sorunsuz testleri onayla" onay ister ve atlananları nedeniyle
 * bildirir. Onaylanan test etkinleşir, öğretmen paylaşımı Private kalır. "İşaretlemede düzelt" testin bir sayfasını işaretleme
 * ekranında açar (inceleme işaretinde geçen sayfalar bayraklı); orada kaydedilen sorular o sayfanın taslaklarının yerine geçer.
 */
@Component({
  selector: 'app-book-import-review',
  standalone: true,
  imports: [
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    RouterLink,
    TranslocoDirective,
  ],
  templateUrl: './book-import-review.component.html',
  styleUrls: ['./book-import-review.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BookImportReviewComponent {
  private readonly api = inject(BookImportService);
  private readonly destroyRef = inject(DestroyRef);

  readonly jobId = input.required<string>();
  readonly tests = input.required<BookImportJobTest[]>();
  readonly editable = input(true);
  /** Bir işlem testlerin durumunu değiştirdi: takip ekranı yeniden yüklenir. */
  readonly changed = output<void>();

  readonly letters = ANSWER_LETTERS;
  readonly onlyAttention = signal(true);
  readonly confirmingAll = signal(false);
  readonly busy = signal<number | 'all' | null>(null);
  readonly error = signal<string | null>(null);
  readonly result = signal<ApproveAllBookImportResult | null>(null);
  readonly previewId = signal<number | null>(null);
  readonly previewLoading = signal(false);
  readonly questions = signal<BookImportReviewQuestion[]>([]);
  readonly removingId = signal<number | null>(null);

  /** İncelemedeki testler (onaylanan / silinen hariç); filtre açıkken yalnız bakılması gerekenler. */
  readonly open = computed(() => this.tests().filter((t) => t.status !== 'Approved' && t.status !== 'Removed'));
  readonly visible = computed(() => (this.onlyAttention() ? this.open().filter(needsAttention) : this.open()));
  readonly approvable = computed(() => this.open().filter(isApprovable).length);
  readonly approvedCount = computed(() => this.tests().filter((t) => t.status === 'Approved').length);
  readonly answered = computed(() => this.questions().filter((q) => q.answerIndex != null).length);

  readonly isApprovable = isApprovable;

  readonly fixParams = labelingFixParams;

  /** Testin sayfaları; inceleme işaretinde geçenler `flagged`. */
  fixPages(test: BookImportJobTest): { page: number; flagged: boolean }[] {
    if (test.fileFrom == null || test.fileTo == null) return [];
    const flagged = new Set(test.reviewFlags.filter((f) => f.code !== 'countMismatch').flatMap((f) => f.pages));
    return Array.from({ length: test.fileTo - test.fileFrom + 1 }, (_, i) => test.fileFrom! + i).map((page) => ({
      page,
      flagged: flagged.has(page),
    }));
  }

  testName(test: BookImportJobTest): string {
    return bookImportTestName(test);
  }

  testById(id: number): BookImportJobTest | undefined {
    return this.tests().find((t) => t.id === id);
  }

  togglePreview(test: BookImportJobTest): void {
    if (this.previewId() === test.id) {
      this.previewId.set(null);
      return;
    }
    this.previewId.set(test.id);
    this.questions.set([]);
    this.previewLoading.set(true);
    this.api
      .reviewQuestions(this.jobId(), test.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (questions) => {
          this.questions.set(questions);
          this.previewLoading.set(false);
        },
        error: (e: HttpErrorResponse) => this.fail(e),
      });
  }

  pick(test: BookImportJobTest, question: BookImportReviewQuestion, optionIndex: number): void {
    const previous = question.answerIndex;
    const wasAnswerless = previous == null;
    this.patchQuestion(question.id, optionIndex);
    this.api
      .setAnswer(this.jobId(), test.id, question.id, optionIndex)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          if (wasAnswerless) this.changed.emit();
        },
        error: (e: HttpErrorResponse) => {
          this.patchQuestion(question.id, previous);
          this.fail(e);
        },
      });
  }

  approve(test: BookImportJobTest): void {
    this.run(test.id, this.api.approveTest(this.jobId(), test.id), () => {
      if (this.previewId() === test.id) this.previewId.set(null);
    });
  }

  remove(test: BookImportJobTest): void {
    this.removingId.set(null);
    this.run(test.id, this.api.removeTest(this.jobId(), test.id), () => {
      if (this.previewId() === test.id) this.previewId.set(null);
    });
  }

  approveAll(): void {
    this.confirmingAll.set(false);
    this.busy.set('all');
    this.error.set(null);
    this.api
      .approveAll(this.jobId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.busy.set(null);
          this.result.set(result);
          this.changed.emit();
        },
        error: (e: HttpErrorResponse) => this.fail(e),
      });
  }

  private run(id: number, request: ReturnType<BookImportService['approveTest']>, done: () => void): void {
    this.busy.set(id);
    this.error.set(null);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.busy.set(null);
        done();
        this.changed.emit();
      },
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  private patchQuestion(id: number, answerIndex: number | null): void {
    this.questions.update((list) => list.map((q) => (q.id === id ? { ...q, answerIndex } : q)));
  }

  private fail(e: HttpErrorResponse): void {
    this.busy.set(null);
    this.previewLoading.set(false);
    this.error.set((e.error as { message?: string } | null)?.message ?? 'review.failed');
  }
}
