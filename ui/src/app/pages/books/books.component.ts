import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormArray, FormBuilder, FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Observable, firstValueFrom, map } from 'rxjs';

import {
  LabelingBook,
  LabelingBookPageInfo,
  LabelingBookSaveRequest,
  MAX_BOOK_FILE_PAGE,
} from '../../models/labeling-book';
import { Grade } from '../../models/student';
import { Subject } from '../../models/subject';
import { SubTopic } from '../../models/subtopic';
import { Topic } from '../../models/topic';
import { GradesService } from '../../services/grades.service';
import { LabelingBookService } from '../../services/labeling-book.service';
import { SubjectService } from '../../services/subject.service';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../shared/components/confirm-dialog/confirm-dialog.component';
import {
  BookDraft,
  BookSection,
  BookValidationError,
  MAX_BOOK_NAME,
  OffsetDraft,
  TocDraft,
  formatRanges,
  offsetPreview,
  validateBookDraft,
} from './book-ranges';

/** Sayfa metinleri kendi Transloco scope'unda: `public/i18n/books/<lang>.json`. */
const BOOKS_SCOPE = 'books';
/** `Book.MaxNotesLength` (sunucu). */
const MAX_NOTES = 500;

type RangeGroup = FormGroup<{ fileFrom: FormControl<number | null>; fileTo: FormControl<number | null> }>;
type OffsetGroup = FormGroup<{
  fileFrom: FormControl<number | null>;
  fileTo: FormControl<number | null>;
  offset: FormControl<number | null>;
}>;
type TocGroup = FormGroup<{
  fileFrom: FormControl<number | null>;
  fileTo: FormControl<number | null>;
  gradeId: FormControl<number | null>;
  subjectId: FormControl<number | null>;
  topicId: FormControl<number | null>;
  subTopicId: FormControl<number | null>;
}>;

/**
 * Issue #577: kitap kaydı (Admin + QuestionTrainer). Dosya sayfası → basılı sayfa ofset aralıkları (fasiküllü kitapta
 * birden çok, ofset negatif olabilir), cevap anahtarı aralıkları ve içindekiler (dosya aralığı → sınıf / ders / konu /
 * alt konu). İşaretleme ekranı sayfanın sınıf ve konu varsayılanını buradan alır. Dönüştürücü kayıtlı hali sunucuya
 * sorar (fasikül doğrulaması).
 */
@Component({
  selector: 'app-books',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatTooltipModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(BOOKS_SCOPE)],
  templateUrl: './books.component.html',
  styleUrls: ['./books.component.scss'],
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class BooksComponent {
  private readonly api = inject(LabelingBookService);
  private readonly gradesApi = inject(GradesService);
  private readonly subjectsApi = inject(SubjectService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);

  readonly maxName = MAX_BOOK_NAME;
  readonly maxNotes = MAX_NOTES;
  readonly maxFilePage = MAX_BOOK_FILE_PAGE;

  // --- Liste ---
  readonly books = signal<LabelingBook[]>([]);
  readonly loading = signal(false);
  readonly error = signal(false);
  /** Düzenlenen kitap: id, `'new'` (yeni kayıt) ya da seçim yok. */
  readonly selectedId = signal<number | 'new' | null>(null);
  readonly selectedBook = computed(() => {
    const id = this.selectedId();
    return typeof id === 'number' ? (this.books().find((b) => b.id === id) ?? null) : null;
  });
  /**
   * Asıl baskı seçenekleri: kendisi ve kendisi başka kitaba bağlı olanlar hariç (sunucu kuralı: tek seviye). Kayıtlı
   * bağlantı listede yoksa da gösterilir ki seçim kaybolmasın.
   */
  readonly canonicalOptions = computed(() => {
    const id = this.selectedId();
    const current = this.selectedBook()?.canonicalBookId ?? null;
    return this.books().filter((b) => b.id !== id && (b.canonicalBookId == null || b.id === current));
  });

  // --- Form ---
  readonly form = this.fb.group({
    name: this.fb.control('', { nonNullable: true }),
    canonicalBookId: this.fb.control<number | null>(null),
    notes: this.fb.control('', { nonNullable: true }),
    offsets: this.fb.array<OffsetGroup>([]),
    answerKeyRanges: this.fb.array<RangeGroup>([]),
    toc: this.fb.array<TocGroup>([]),
  });
  private readonly formValue = toSignal(this.form.valueChanges.pipe(map(() => this.form.getRawValue())), {
    initialValue: this.form.getRawValue(),
  });
  readonly errors = computed(() => validateBookDraft(this.formValue() as BookDraft));
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly deleting = signal(false);

  // --- Taksonomi (sınıf → ders → konu → alt konu; istek başına önbellek) ---
  readonly grades = signal<Grade[]>([]);
  readonly gradesError = signal(false);
  readonly subjectsByGrade = signal<Record<number, Subject[]>>({});
  readonly topicsByKey = signal<Record<string, Topic[]>>({});
  readonly subTopicsByTopic = signal<Record<number, SubTopic[]>>({});
  private readonly requested = new Set<string>();

  // --- Dönüştürücü (kayıtlı hali sunucuya sorar) ---
  readonly convFile = signal<number | null>(null);
  readonly convPrinted = signal<number | null>(null);
  readonly convNear = signal<number | null>(null);
  readonly convPage = signal<LabelingBookPageInfo | null>(null);
  readonly convPageLoading = signal(false);
  readonly convPageError = signal(false);
  readonly convFilePages = signal<number[] | null>(null);
  readonly convPrintedLoading = signal(false);
  readonly convPrintedError = signal(false);

  constructor() {
    this.load();
    this.loadGrades();
  }

  get offsets(): FormArray<OffsetGroup> {
    return this.form.controls.offsets;
  }

  get answerKeyRanges(): FormArray<RangeGroup> {
    return this.form.controls.answerKeyRanges;
  }

  get toc(): FormArray<TocGroup> {
    return this.form.controls.toc;
  }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.api
      .list()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (books) => {
          this.books.set(books ?? []);
          this.loading.set(false);
        },
        error: (error) => {
          console.error('labeling books failed', error);
          this.loading.set(false);
          this.error.set(true);
        },
      });
  }

  loadGrades(): void {
    this.gradesError.set(false);
    this.gradesApi
      .getGrades()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (grades) => this.grades.set(grades ?? []),
        error: (error) => {
          console.error('books grades failed', error);
          this.gradesError.set(true);
        },
      });
  }

  // ---------------- Seçim ----------------

  async select(book: LabelingBook): Promise<void> {
    if (this.selectedId() === book.id || !(await this.confirmDiscard())) {
      return;
    }
    this.selectedId.set(book.id);
    this.fillForm(book);
  }

  async createNew(): Promise<void> {
    if (!(await this.confirmDiscard())) {
      return;
    }
    this.selectedId.set('new');
    this.fillForm(null);
  }

  async cancel(): Promise<void> {
    if (!(await this.confirmDiscard())) {
      return;
    }
    this.selectedId.set(null);
    this.fillForm(null);
  }

  private fillForm(book: LabelingBook | null): void {
    this.saveError.set(null);
    this.resetConverter();
    this.offsets.clear({ emitEvent: false });
    this.answerKeyRanges.clear({ emitEvent: false });
    this.toc.clear({ emitEvent: false });
    for (const o of book?.offsets ?? []) {
      this.offsets.push(this.offsetGroup(o), { emitEvent: false });
    }
    for (const r of book?.answerKeyRanges ?? []) {
      this.answerKeyRanges.push(this.rangeGroup(r), { emitEvent: false });
    }
    for (const entry of book?.toc ?? []) {
      this.toc.push(
        this.tocGroup({
          fileFrom: entry.fileFrom,
          fileTo: entry.fileTo,
          gradeId: entry.gradeId,
          subjectId: entry.subjectId,
          topicId: entry.topicId ?? null,
          subTopicId: entry.subTopicId ?? null,
        }),
        { emitEvent: false }
      );
      this.ensureTaxonomy(entry.gradeId, entry.subjectId, entry.topicId ?? null);
    }
    // `form.reset(...)` dizileri de sıfırlardı: tekil alanlar ayrı ayrı yazılır, değişim tek seferde yayılır.
    const { name, canonicalBookId, notes } = this.form.controls;
    name.reset(book?.name ?? '', { emitEvent: false });
    canonicalBookId.reset(book?.canonicalBookId ?? null, { emitEvent: false });
    notes.reset(book?.notes ?? '', { emitEvent: false });
    this.form.updateValueAndValidity();
    this.form.markAsPristine();
    this.form.markAsUntouched();
  }

  private async confirmDiscard(): Promise<boolean> {
    if (this.selectedId() == null || !this.form.dirty) {
      return true;
    }
    const data: ConfirmDialogData = {
      title: this.tr('discard.title'),
      message: this.tr('discard.message'),
      confirmText: this.tr('discard.confirm'),
      cancelText: this.tr('discard.cancel'),
      icon: 'warning',
      confirmColor: 'warn',
    };
    const result = await firstValueFrom(this.dialog.open(ConfirmDialogComponent, { data }).afterClosed());
    return result === true;
  }

  // ---------------- Satırlar ----------------

  addOffset(): void {
    // Fasikül: yeni aralık bir öncekinin bittiği yerden başlasın.
    const last = this.offsets.at(this.offsets.length - 1)?.getRawValue();
    const from = last?.fileTo != null ? last.fileTo + 1 : null;
    this.offsets.push(this.offsetGroup({ fileFrom: from, fileTo: null, offset: last?.offset ?? 0 }));
    this.form.markAsDirty();
  }

  addAnswerKeyRange(): void {
    this.answerKeyRanges.push(this.rangeGroup({ fileFrom: null, fileTo: null }));
    this.form.markAsDirty();
  }

  addTocEntry(): void {
    const last = this.toc.at(this.toc.length - 1)?.getRawValue();
    this.toc.push(
      this.tocGroup({
        fileFrom: last?.fileTo != null ? last.fileTo + 1 : null,
        fileTo: null,
        gradeId: last?.gradeId ?? null,
        subjectId: last?.subjectId ?? null,
        topicId: null,
        subTopicId: null,
      })
    );
    this.form.markAsDirty();
  }

  removeRow(section: BookSection, index: number): void {
    this.form.controls[section].removeAt(index);
    this.form.markAsDirty();
  }

  onTocGradeChange(index: number): void {
    const row = this.toc.at(index);
    row.patchValue({ subjectId: null, topicId: null, subTopicId: null });
    this.ensureTaxonomy(row.controls.gradeId.value, null, null);
  }

  onTocSubjectChange(index: number): void {
    const row = this.toc.at(index);
    row.patchValue({ topicId: null, subTopicId: null });
    this.ensureTaxonomy(row.controls.gradeId.value, row.controls.subjectId.value, null);
  }

  onTocTopicChange(index: number): void {
    const row = this.toc.at(index);
    row.patchValue({ subTopicId: null });
    this.ensureTaxonomy(row.controls.gradeId.value, row.controls.subjectId.value, row.controls.topicId.value);
  }

  subjectsFor(row: TocGroup): Subject[] {
    const gradeId = row.controls.gradeId.value;
    return gradeId != null ? (this.subjectsByGrade()[gradeId] ?? []) : [];
  }

  topicsFor(row: TocGroup): Topic[] {
    const { gradeId, subjectId } = row.getRawValue();
    return gradeId != null && subjectId != null ? (this.topicsByKey()[`${subjectId}:${gradeId}`] ?? []) : [];
  }

  subTopicsFor(row: TocGroup): SubTopic[] {
    const topicId = row.controls.topicId.value;
    return topicId != null ? (this.subTopicsByTopic()[topicId] ?? []) : [];
  }

  /** "dosya 40 → basılı 39"; ofset ya da uçlar eksikse null. */
  preview(index: number): ReturnType<typeof offsetPreview> {
    return offsetPreview((this.formValue().offsets[index] ?? {}) as OffsetDraft);
  }

  rowErrors(section: BookSection | 'name', index: number): BookValidationError[] {
    return this.errors().filter((e) => e.section === section && e.index === index);
  }

  errorText(error: BookValidationError): string {
    return this.tr(`validation.${error.key}`, error.params);
  }

  bookSummary(book: LabelingBook): string {
    const parts: string[] = [];
    if (book.offsets.length > 0) {
      parts.push(this.tr('list.offsets', { count: book.offsets.length }));
    }
    if (book.toc.length > 0) {
      parts.push(this.tr('list.toc', { count: book.toc.length }));
    }
    if (book.answerKeyRanges.length > 0) {
      parts.push(this.tr('list.answerKey', { ranges: formatRanges(book.answerKeyRanges) }));
    }
    return parts.join(' · ');
  }

  canonicalName(book: LabelingBook): string | null {
    if (book.canonicalBookId == null) {
      return null;
    }
    return this.books().find((b) => b.id === book.canonicalBookId)?.name ?? book.canonicalBookName ?? null;
  }

  // ---------------- Kaydet / sil ----------------

  save(): void {
    if (this.saving() || this.errors().length > 0 || this.selectedId() == null) {
      this.form.markAllAsTouched();
      return;
    }
    const request = this.buildRequest();
    const id = this.selectedId();
    const call: Observable<LabelingBook> = typeof id === 'number' ? this.api.update(id, request) : this.api.create(request);
    this.saving.set(true);
    this.saveError.set(null);
    call.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.books.update((list) => {
          const others = list.filter((b) => b.id !== saved.id);
          return [...others, saved].sort((a, b) => a.name.localeCompare(b.name, 'tr'));
        });
        this.selectedId.set(saved.id);
        this.fillForm(saved);
        this.snackBar.open(this.tr('saved'), this.tr('ok'), { duration: 3000 });
      },
      error: (error) => {
        this.saving.set(false);
        console.error('labeling book save failed', error);
        const body = (error as { error?: { message?: string; errorCode?: string } })?.error;
        // 403 LabelingBookRenameForbidden: kullanımdaki ortak kitabın adını yalnız yönetici değiştirir.
        const fallback = body?.errorCode === 'LabelingBookRenameForbidden' ? this.tr('renameForbidden') : this.tr('saveFailed');
        this.saveError.set(body?.message || fallback);
      },
    });
  }

  async remove(): Promise<void> {
    const book = this.selectedBook();
    if (!book || this.deleting()) {
      return;
    }
    const data: ConfirmDialogData = {
      title: this.tr('delete.title'),
      message: this.tr('delete.message', { name: book.name }),
      confirmText: this.tr('delete.confirm'),
      cancelText: this.tr('delete.cancel'),
      icon: 'delete',
      confirmColor: 'warn',
    };
    const confirmed = await firstValueFrom(this.dialog.open(ConfirmDialogComponent, { data }).afterClosed());
    if (confirmed !== true) {
      return;
    }
    this.deleting.set(true);
    this.api
      .delete(book.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.deleting.set(false);
          this.books.update((list) => list.filter((b) => b.id !== book.id));
          this.selectedId.set(null);
          this.fillForm(null);
          this.snackBar.open(this.tr('delete.done'), this.tr('ok'), { duration: 3000 });
        },
        error: (error) => {
          this.deleting.set(false);
          console.error('labeling book delete failed', error);
          const status = (error as { status?: number })?.status;
          const serverMessage = (error as { error?: { message?: string } })?.error?.message;
          const message = status === 409 ? serverMessage || this.tr('delete.inUse') : this.tr('delete.failed');
          this.snackBar.open(message, this.tr('ok'), { duration: 6000 });
        },
      });
  }

  private buildRequest(): LabelingBookSaveRequest {
    const value = this.form.getRawValue();
    const notes = value.notes.trim();
    return {
      name: value.name.trim(),
      canonicalBookId: value.canonicalBookId,
      notes: notes || null,
      offsets: value.offsets.map((o) => ({ fileFrom: o.fileFrom!, fileTo: o.fileTo!, offset: o.offset! })),
      answerKeyRanges: value.answerKeyRanges.map((r) => ({ fileFrom: r.fileFrom!, fileTo: r.fileTo! })),
      toc: value.toc.map((t) => ({
        fileFrom: t.fileFrom!,
        fileTo: t.fileTo!,
        gradeId: t.gradeId!,
        subjectId: t.subjectId!,
        topicId: t.topicId,
        subTopicId: t.subTopicId,
      })),
    };
  }

  // ---------------- Dönüştürücü ----------------

  convertFile(): void {
    const book = this.selectedBook();
    const filePage = this.convFile();
    if (!book || filePage == null || filePage < 1) {
      return;
    }
    this.convPageLoading.set(true);
    this.convPageError.set(false);
    this.convPage.set(null);
    this.api
      .getPage(book.id, filePage)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (info) => {
          this.convPage.set(info);
          this.convPageLoading.set(false);
        },
        error: (error) => {
          console.error('labeling book page lookup failed', error);
          this.convPageLoading.set(false);
          this.convPageError.set(true);
        },
      });
  }

  convertPrinted(): void {
    const book = this.selectedBook();
    const printed = this.convPrinted();
    if (!book || printed == null) {
      return;
    }
    this.convPrintedLoading.set(true);
    this.convPrintedError.set(false);
    this.convFilePages.set(null);
    this.api
      .getFilePages(book.id, printed, this.convNear())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.convFilePages.set(result?.filePages ?? []);
          this.convPrintedLoading.set(false);
        },
        error: (error) => {
          console.error('labeling book printed lookup failed', error);
          this.convPrintedLoading.set(false);
          this.convPrintedError.set(true);
        },
      });
  }

  /** `<input type="number">` değerini signal'a yazar (boş → null). */
  setNumber(target: EventTarget | null, into: 'convFile' | 'convPrinted' | 'convNear'): void {
    const raw = (target as HTMLInputElement | null)?.value ?? '';
    const value = raw.trim() === '' ? null : Number(raw);
    this[into].set(value != null && Number.isFinite(value) ? Math.trunc(value) : null);
  }

  private resetConverter(): void {
    this.convPage.set(null);
    this.convPageError.set(false);
    this.convFilePages.set(null);
    this.convPrintedError.set(false);
  }

  // ---------------- Yardımcılar ----------------

  private ensureTaxonomy(gradeId: number | null, subjectId: number | null, topicId: number | null): void {
    if (gradeId != null) {
      this.fetchOnce(`g:${gradeId}`, this.subjectsApi.getSubjectsByGrade(gradeId), (list) =>
        this.subjectsByGrade.update((m) => ({ ...m, [gradeId]: list }))
      );
    }
    if (gradeId != null && subjectId != null) {
      const key = `${subjectId}:${gradeId}`;
      this.fetchOnce(`t:${key}`, this.subjectsApi.getTopicsBySubjectAndGrade(subjectId, gradeId), (list) =>
        this.topicsByKey.update((m) => ({ ...m, [key]: list }))
      );
    }
    if (topicId != null) {
      this.fetchOnce(`s:${topicId}`, this.subjectsApi.getSubTopicsByTopic(topicId), (list) =>
        this.subTopicsByTopic.update((m) => ({ ...m, [topicId]: list }))
      );
    }
  }

  /** Aynı liste bir kez istenir; hata olursa sonraki seçimde yeniden denenir. */
  private fetchOnce<T>(key: string, source: Observable<T[]>, apply: (list: T[]) => void): void {
    if (this.requested.has(key)) {
      return;
    }
    this.requested.add(key);
    source.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (list) => apply(list ?? []),
      error: (error) => {
        console.error('books taxonomy failed', key, error);
        this.requested.delete(key);
      },
    });
  }

  private offsetGroup(value: OffsetDraft): OffsetGroup {
    return this.fb.group({
      fileFrom: this.fb.control<number | null>(value.fileFrom),
      fileTo: this.fb.control<number | null>(value.fileTo),
      offset: this.fb.control<number | null>(value.offset),
    });
  }

  private rangeGroup(value: { fileFrom: number | null; fileTo: number | null }): RangeGroup {
    return this.fb.group({
      fileFrom: this.fb.control<number | null>(value.fileFrom),
      fileTo: this.fb.control<number | null>(value.fileTo),
    });
  }

  private tocGroup(value: TocDraft): TocGroup {
    return this.fb.group({
      fileFrom: this.fb.control<number | null>(value.fileFrom),
      fileTo: this.fb.control<number | null>(value.fileTo),
      gradeId: this.fb.control<number | null>(value.gradeId),
      subjectId: this.fb.control<number | null>(value.subjectId),
      topicId: this.fb.control<number | null>(value.topicId),
      subTopicId: this.fb.control<number | null>(value.subTopicId),
    });
  }

  private tr(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${BOOKS_SCOPE}.${key}`, params) ?? '';
  }
}
