import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { RouterLink } from '@angular/router';
import { TranslocoDirective } from '@jsverse/transloco';

import {
  ApproveAllBookImportResult,
  BookImportJobKind,
  BookImportJobTest,
  BookImportReviewApi,
  BookImportReviewFlag,
  BookImportReviewQuestion,
  bookImportTestName,
  isApprovable,
  bookImportTestPages,
  compactNumbers,
  groupReviewFlags,
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
 * #1054: hızlı ekleme de aynı ekranı kullanır: satır = fotoğraf (küçük resimli), uçlar `reviewApi`, metinler `reviewQuickAdd`,
 * "İşaretlemede düzelt" yalnız `canFix` iken (işaretleme ekranı öğretmene kapalı).
 */
@Component({
  selector: 'app-book-import-review',
  standalone: true,
  imports: [
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSlideToggleModule,
    RouterLink,
    TranslocoDirective,
  ],
  templateUrl: './book-import-review.component.html',
  styleUrls: ['./book-import-review.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BookImportReviewComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly defaultApi = inject(BookImportService);

  readonly jobId = input.required<string>();
  readonly tests = input.required<BookImportJobTest[]>();
  readonly editable = input(true);
  /** Aktarım sürerken bölüm görünür, düğmeler iş bitene kadar kapalı (#1129). */
  readonly locked = input(false);
  readonly actions = computed(() => this.editable() || this.locked());
  readonly kind = input<BookImportJobKind>('Book');
  /** Issue #1144: karışık sorular (görsel başına bağımsız test); hepsini onaylarken sorular yeni bir teste toplanabilir. */
  readonly loose = input(false);
  /** Toplanan testin varsayılan adı (aktarımın adı). */
  readonly bookName = input('');
  /** Satır bir fotoğraf / görsel (sayfa aralığı yok, ad satırın başlığı). */
  readonly perImage = computed(() => this.kind() === 'QuickAdd' || this.loose());
  readonly reviewApi = input<BookImportReviewApi | null>(null);
  readonly canFix = input(true);
  /** Fotoğraf sırası → küçük resim (hızlı ekleme). */
  readonly thumbs = input<Record<number, string>>({});
  /** Bir işlem testlerin durumunu değiştirdi: takip ekranı yeniden yüklenir. */
  readonly changed = output<void>();

  readonly letters = ANSWER_LETTERS;
  readonly onlyAttention = signal(true);
  readonly confirmingAll = signal(false);
  readonly collect = signal(false);
  readonly collectName = signal('');
  readonly busy = signal<number | 'all' | null>(null);
  readonly error = signal<string | null>(null);
  readonly result = signal<ApproveAllBookImportResult | null>(null);
  readonly previewId = signal<number | null>(null);
  /** Uyarıları açık test: işaretler satırın altında, yerleriyle. */
  readonly flagsId = signal<number | null>(null);
  readonly previewLoading = signal(false);
  readonly questions = signal<BookImportReviewQuestion[]>([]);
  readonly removingId = signal<number | null>(null);
  readonly reimportingId = signal<number | null>(null);
  /** Büyütülen önizleme sorusu (sıra); ok tuşları / düğmelerle önceki-sonraki. */
  readonly zoomIndex = signal<number | null>(null);
  readonly zoomed = computed(() => {
    const index = this.zoomIndex();
    return index == null ? null : (this.questions()[index] ?? null);
  });
  private readonly zoomClose = viewChild('zoomClose', { read: ElementRef<HTMLElement> });
  private readonly zoomFocus = effect(() => {
    if (this.zoomIndex() != null) this.zoomClose()?.nativeElement.focus();
  });

  /** İncelemedeki testler (onaylanan / silinen hariç); filtre açıkken yalnız bakılması gerekenler. */
  readonly open = computed(() => this.tests().filter((t) => t.status !== 'Approved' && t.status !== 'Removed'));
  readonly visible = computed(() => (this.onlyAttention() ? this.open().filter(needsAttention) : this.open()));
  readonly approvable = computed(() => this.open().filter(isApprovable).length);
  readonly approvedCount = computed(() => this.tests().filter((t) => t.status === 'Approved').length);
  readonly answered = computed(() => this.questions().filter((q) => q.answerIndex != null).length);

  readonly isApprovable = isApprovable;

  ngOnInit(): void {
    // Hızlı eklemede az fotoğraf var ve hepsi onaylanır: bütün satırlar görünür.
    if (this.kind() === 'QuickAdd') this.onlyAttention.set(false);
  }

  private get api(): BookImportReviewApi {
    return this.reviewApi() ?? this.defaultApi;
  }

  /** Türe göre değişen metnin anahtarı. */
  label(key: string): string {
    const scope = this.kind() === 'QuickAdd' ? 'reviewQuickAdd' : this.loose() ? 'reviewLoose' : 'review';
    return `${scope}.${key}`;
  }

  fixParams(testId: number, filePage: number): Record<string, string | number> {
    return labelingFixParams(this.jobId(), testId, filePage, this.kind());
  }

  thumb(test: BookImportJobTest): string {
    return this.thumbs()[test.fileFrom ?? test.order] ?? '';
  }

  /** Tuval testin bütün sayfalarıyla açılır; ilk işaretli sayfadan (yoksa ilk sayfadan) başlar, sayfalar orada gezilir. */
  fixStart(test: BookImportJobTest): number | null {
    const pages = bookImportTestPages(test);
    return (pages.find((p) => p.flagged) ?? pages[0])?.page ?? null;
  }

  testName(test: BookImportJobTest): string {
    if (this.perImage()) return test.title ?? `${test.order}`;
    return bookImportTestName(test);
  }

  testById(id: number): BookImportJobTest | undefined {
    return this.tests().find((t) => t.id === id);
  }

  /** Kurulum ve aktarım işaretleri, atlanan tekrarlar. */
  flagCount(test: BookImportJobTest): number {
    return test.flags.length + test.reviewFlags.length + (test.duplicatesSkipped > 0 ? 1 : 0);
  }

  /** Açık testin uyarıları sayfaya göre (her sayfa ve soru bir kez). */
  readonly openFlags = computed(() => {
    const test = this.tests().find((t) => t.id === this.flagsId());
    return test ? groupReviewFlags(test.reviewFlags) : null;
  });

  toggleFlags(test: BookImportJobTest): void {
    this.flagsId.update((id) => (id === test.id ? null : test.id));
  }

  /** İşaretin yeri: "sayfa 69–85 · soru 114, 116". */
  flagWhere(flag: BookImportReviewFlag, t: (key: string, params?: Record<string, unknown>) => string): string {
    const parts = [];
    if (flag.pages.length > 0) parts.push(t('detail.flagPages', { pages: compactNumbers(flag.pages) }));
    if (flag.numbers?.length) parts.push(t('detail.flagNumbers', { numbers: compactNumbers(flag.numbers) }));
    return parts.join(' · ');
  }

  zoom(index: number): void {
    this.zoomIndex.set(index);
  }

  zoomBy(step: number): void {
    const index = this.zoomIndex();
    if (index == null) return;
    this.zoomIndex.set(Math.min(Math.max(index + step, 0), this.questions().length - 1));
  }

  closeZoom(): void {
    this.zoomIndex.set(null);
  }

  zoomKey(event: KeyboardEvent): void {
    if (event.key === 'Escape') this.closeZoom();
    else if (event.key === 'ArrowLeft') this.zoomBy(-1);
    else if (event.key === 'ArrowRight') this.zoomBy(1);
    else return;
    event.preventDefault();
  }

  togglePreview(test: BookImportJobTest): void {
    this.zoomIndex.set(null);
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

  /** Yalnız kitap aktarımında (hızlı eklemenin API'sinde yok). */
  canReimport(): boolean {
    return !!this.api.reimportTest;
  }

  reimport(test: BookImportJobTest): void {
    this.reimportingId.set(null);
    const request = this.api.reimportTest?.(this.jobId(), test.id);
    if (!request) return;
    this.run(test.id, request, () => {
      if (this.previewId() === test.id) this.previewId.set(null);
    });
  }

  openApproveAll(): void {
    if (this.loose() && !this.collectName()) this.collectName.set(this.bookName());
    this.confirmingAll.set(true);
  }

  approveAll(): void {
    const collectName = this.loose() && this.collect() ? this.collectName().trim() : '';
    this.confirmingAll.set(false);
    this.busy.set('all');
    this.error.set(null);
    (collectName ? this.api.approveAll(this.jobId(), collectName) : this.api.approveAll(this.jobId()))
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

  private run(id: number, request: ReturnType<BookImportReviewApi['approveTest']>, done: () => void): void {
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
