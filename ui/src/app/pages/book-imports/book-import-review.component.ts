import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
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
  isBulkApprovable,
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
 * "İşaretlemede düzelt" yalnız `canFix` iken (öğretmen işaretleme ekranına yalnız hızlı ekleme düzeltmesi için girer, #1060).
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
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
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
  /** #1229: kitabın sayfa sayısı; "Kitabı gez" bütün sayfaları gezer (yoksa düğme görünmez). */
  readonly pageCount = input<number | null>(null);
  /** Satır bir fotoğraf / görsel (sayfa aralığı yok, ad satırın başlığı). */
  readonly perImage = computed(() => this.kind() === 'QuickAdd' || this.loose());
  readonly reviewApi = input<BookImportReviewApi | null>(null);
  readonly canFix = input(true);
  /** Fotoğraf sırası → küçük resim (hızlı ekleme). */
  readonly thumbs = input<Record<number, string>>({});
  /** Bir işlem testlerin durumunu değiştirdi: takip ekranı yeniden yüklenir. */
  readonly changed = output<void>();
  /** #1231: kitap aktarımında kalan testler sonraya bırakılabilir; `deferred` bırakıldığını söyler. */
  readonly deferrable = input(false);
  readonly deferred = input(false);
  readonly deferChange = output<boolean>();
  /** #1231: "Sonra bakılacaklar"dan açılan test: görünür yapılır, uyarıları açılır ve satırına kaydırılır. */
  readonly focusTest = input<number | null>(null);

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
  /** #1169: testten çıkarılması onay bekleyen soru. */
  readonly removingQuestionId = signal<number | null>(null);
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

  /**
   * #1190: uyarılı sayfa işaretlemeye gitmeden büyük görselde; uyarılı sayfalar arasında gezilir. #1229: "Kitabı gez"
   * (`testId` null) aynı görünümde kitabın bütün sayfalarını gezer; sayfanın testleri ve bütün testlerin uyarıları gösterilir.
   */
  readonly pageView = signal<{ testId: number | null; page: number } | null>(null);
  readonly pageViewUrl = signal<string | null>(null);
  readonly pageViewLoading = signal(false);
  readonly bookView = computed(() => this.pageView()?.testId === null);
  readonly pageViewPages = computed(() =>
    this.bookView()
      ? Array.from({ length: this.pageCount() ?? 0 }, (_, i) => i + 1)
      : (this.openFlags()?.pages.map((p) => p.page) ?? [])
  );
  /** Kitap görünümünde bütün testlerin uyarıları sayfaya göre. */
  private readonly bookFlags = computed(() => groupReviewFlags(this.tests().flatMap((t) => t.reviewFlags)).pages);
  readonly pageViewFlags = computed(() => {
    const view = this.pageView();
    if (!view) return null;
    const pages = this.bookView() ? this.bookFlags() : (this.openFlags()?.pages ?? []);
    return pages.find((p) => p.page === view.page) ?? null;
  });
  /** Gösterilen sayfanın testleri (kitap görünümü başlığı; düzeltme ilk teste açılır). */
  readonly pageViewTests = computed(() => {
    const page = this.pageView()?.page;
    return page == null ? [] : this.tests().filter((t) => t.fileFrom != null && t.fileTo != null && t.fileFrom <= page && page <= t.fileTo);
  });
  readonly canViewPage = computed(() => !!this.api.pageImage);
  readonly canBrowseBook = computed(() => this.canViewPage() && !this.perImage() && (this.pageCount() ?? 0) > 0);
  private pageViewObjectUrl: string | null = null;
  private readonly pageClose = viewChild('pageClose', { read: ElementRef<HTMLElement> });
  private readonly pageFocus = effect(() => {
    if (this.pageView() != null) this.pageClose()?.nativeElement.focus();
  });

  /** İncelemedeki testler (onaylanan / silinen hariç); filtre açıkken yalnız bakılması gerekenler. */
  readonly open = computed(() => this.tests().filter((t) => t.status !== 'Approved' && t.status !== 'Removed'));
  readonly visible = computed(() => (this.onlyAttention() ? this.open().filter(needsAttention) : this.open()));
  readonly approvable = computed(() => this.open().filter(isBulkApprovable).length);
  readonly canDefer = computed(() => this.deferrable() && this.editable() && !this.deferred() && this.open().length > 0);
  private focused = false;
  private readonly focusRow = effect(() => {
    const id = this.focusTest();
    const test = this.open().find((t) => t.id === id);
    if (this.focused || !test) return;
    this.focused = true;
    if (!needsAttention(test)) this.onlyAttention.set(false);
    if (this.flagCount(test) > 0) this.flagsId.set(test.id);
    afterNextRender(() => this.host.nativeElement.querySelector(`[data-test-row="${test.id}"]`)?.scrollIntoView({ block: 'center' }), {
      injector: this.injector,
    });
  });
  readonly approvedCount = computed(() => this.tests().filter((t) => t.status === 'Approved').length);
  /** #1134: etkinlik taslaklarının cevabı yoktur; sayaç yalnız çoktan seçmeli soruları sayar. */
  readonly choiceQuestions = computed(() => this.questions().filter((q) => !q.interactionType));
  readonly answered = computed(() => this.choiceQuestions().filter((q) => q.answerIndex != null).length);

  readonly isApprovable = isApprovable;

  constructor() {
    this.destroyRef.onDestroy(() => this.revokePageView());
  }

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
    // #1134: "etkinlik bölümü" bir uyarı değil, satırın türü.
    return test.flags.filter((f) => f !== 'activity').length + test.reviewFlags.length + (test.duplicatesSkipped > 0 ? 1 : 0);
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

  /** #1229: kitabın bütün sayfaları (başlangıç 1. sayfa). */
  browseBook(page = 1): void {
    this.openPage(null, page);
  }

  openPage(test: BookImportJobTest | null, page: number): void {
    const load = this.api.pageImage;
    if (!load) return;
    this.pageView.set({ testId: test?.id ?? null, page });
    this.revokePageView();
    this.pageViewLoading.set(true);
    load
      .call(this.api, this.jobId(), page)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => {
          // Hızlı geçişte geç gelen eski sayfa yeni sayfanın yerine geçmesin.
          if (this.pageView()?.page !== page) return;
          this.pageViewObjectUrl = URL.createObjectURL(blob);
          this.pageViewUrl.set(this.pageViewObjectUrl);
          this.pageViewLoading.set(false);
        },
        error: () => {
          if (this.pageView()?.page === page) this.pageViewLoading.set(false);
        },
      });
  }

  pageBy(step: number): void {
    const view = this.pageView();
    if (!view) return;
    const pages = this.pageViewPages();
    const next = pages[pages.indexOf(view.page) + step];
    if (next != null) this.openPage(view.testId == null ? null : (this.testById(view.testId) ?? null), next);
  }

  /** Kitap görünümünde sayfa numarasıyla atla; aralık dışı / geçersiz değer yok sayılır. */
  jumpToPage(value: string): void {
    const page = Number(value);
    const view = this.pageView();
    if (!view || !Number.isInteger(page) || !this.pageViewPages().includes(page) || page === view.page) return;
    this.openPage(view.testId == null ? null : (this.testById(view.testId) ?? null), page);
  }

  closePage(): void {
    this.pageView.set(null);
    this.revokePageView();
  }

  pageKey(event: KeyboardEvent): void {
    // Sayfa numarası alanında oklar imleci gezdirir, sayfa değiştirmez.
    if (event.target instanceof HTMLInputElement && event.key !== 'Escape') return;
    if (event.key === 'Escape') this.closePage();
    else if (event.key === 'ArrowLeft') this.pageBy(-1);
    else if (event.key === 'ArrowRight') this.pageBy(1);
    else return;
    event.preventDefault();
  }

  private revokePageView(): void {
    if (this.pageViewObjectUrl) URL.revokeObjectURL(this.pageViewObjectUrl);
    this.pageViewObjectUrl = null;
    this.pageViewUrl.set(null);
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

  /** #1169: yalnız kitap aktarımında; testin son sorusu çıkarılmaz (testi silmek gerekir). */
  canRemoveQuestion(): boolean {
    return !!this.api.removeQuestion && this.questions().length > 1;
  }

  removeQuestion(test: BookImportJobTest, question: BookImportReviewQuestion): void {
    this.removingQuestionId.set(null);
    const request = this.api.removeQuestion?.(this.jobId(), test.id, question.id);
    if (!request) return;
    this.busy.set(test.id);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.busy.set(null);
        this.questions.update((questions) => questions.filter((q) => q.id !== question.id));
        this.changed.emit();
      },
      error: (e: HttpErrorResponse) => this.fail(e),
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
