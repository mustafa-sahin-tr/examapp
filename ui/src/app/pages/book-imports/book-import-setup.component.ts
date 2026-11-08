import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { TranslocoDirective } from '@jsverse/transloco';

import { BookImportSetup, BookImportSetupTest, ConfirmBookImportSetup } from '../../models/book-import';
import { BookImportService } from '../../services/book-import.service';

const KEY_LETTER = /^[A-Ea-e]$/;

/** Onay tablosunun düzenlenebilir satırı. Anahtar hücreleri: "A"–"E", okunamayan "?", boş "". */
export interface EditableSetupTest {
  order: number;
  unit: number;
  testNo: number;
  include: boolean;
  title: string;
  minutes: number | null;
  fileFrom: number | null;
  fileTo: number | null;
  key: string[] | null;
  /** Hücre başına ikinci okumanın değeri; "." uyumlu ya da öğretmen düzeltti. */
  conflicts: string[];
  flags: string[];
}

export function toEditable(test: BookImportSetupTest): EditableSetupTest {
  const key = test.answerKey ? test.answerKey.split('') : null;
  return {
    order: test.order,
    unit: test.unit,
    testNo: test.testNo,
    include: true,
    title: test.title ?? '',
    minutes: test.minutes,
    fileFrom: test.fileFrom,
    fileTo: test.fileTo,
    key,
    conflicts: key ? (test.keyConflicts ?? '').padEnd(key.length, '.').split('').slice(0, key.length) : [],
    flags: test.flags,
  };
}

/**
 * Issue #1021: kurulum onay ekranı. Claude'un çıkardığı test listesini ve cevap anahtarını gösterir; iki okumanın uyuşmadığı
 * hücreler sarı, okunamayanlar kırmızı işaretlidir ve onaydan önce düzeltilir. Anahtar ızgarası klavyeyle gezilir: harf (A–E)
 * yazınca sağa geçer, oklar hücreler arasında gezer. Anahtar bulunamadıysa öğretmen cevapsız aktarıma devam eder ya da iptal eder.
 */
@Component({
  selector: 'app-book-import-setup',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatRadioModule,
    TranslocoDirective,
  ],
  templateUrl: './book-import-setup.component.html',
  styleUrls: ['./book-import-setup.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BookImportSetupComponent implements OnInit {
  private readonly api = inject(BookImportService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);

  readonly jobId = input.required<string>();
  /** Onaylandı ya da iptal edildi: takip ekranı yenilenir. */
  readonly changed = output<void>();

  readonly setup = signal<BookImportSetup | null>(null);
  readonly rows = signal<EditableSetupTest[]>([]);
  readonly loadError = signal(false);
  readonly bookName = signal('');
  readonly useExisting = signal<'existing' | 'new'>('existing');
  readonly noKeyAcknowledged = signal(false);
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly keyPage = signal<number | null>(null);
  readonly keyImage = signal<string | null>(null);
  readonly keyImageLarge = signal(false);

  readonly keyPages = computed(() =>
    (this.setup()?.answerKeyRanges ?? []).flatMap((r) =>
      Array.from({ length: r.fileTo - r.fileFrom + 1 }, (_, i) => r.fileFrom + i),
    ),
  );
  readonly included = computed(() => this.rows().filter((r) => r.include));
  readonly openConflicts = computed(() =>
    this.included().reduce((sum, r) => sum + r.conflicts.filter((c) => c !== '.').length, 0),
  );
  readonly unreadable = computed(() =>
    this.included().reduce((sum, r) => sum + (r.key ?? []).filter((c) => !KEY_LETTER.test(c)).length, 0),
  );
  readonly invalidPages = computed(() =>
    this.included().some(
      (r) =>
        r.fileFrom == null ||
        r.fileTo == null ||
        r.fileFrom < 1 ||
        r.fileTo < r.fileFrom ||
        r.fileTo > (this.setup()?.pageCount ?? Number.MAX_SAFE_INTEGER),
    ),
  );
  readonly canConfirm = computed(
    () =>
      !this.saving() &&
      this.included().length > 0 &&
      this.openConflicts() === 0 &&
      this.unreadable() === 0 &&
      !this.invalidPages() &&
      this.bookName().trim().length > 0 &&
      (!this.setup()?.noAnswerKey || this.noKeyAcknowledged()),
  );

  private objectUrl: string | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => this.revokeImage());
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loadError.set(false);
    this.api
      .setup(this.jobId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (setup) => {
          this.setup.set(setup);
          this.rows.set(setup.tests.map(toEditable));
          this.bookName.set(setup.bookName);
          const first = this.keyPages()[0];
          if (first != null) this.showKeyPage(first);
        },
        error: () => this.loadError.set(true),
      });
  }

  showKeyPage(filePage: number): void {
    this.keyPage.set(filePage);
    this.api
      .pageImage(this.jobId(), filePage)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => {
          this.revokeImage();
          this.objectUrl = URL.createObjectURL(blob);
          this.keyImage.set(this.objectUrl);
        },
        error: () => this.keyImage.set(null),
      });
  }

  update(row: EditableSetupTest, patch: Partial<EditableSetupTest>): void {
    this.rows.update((rows) => rows.map((r) => (r === row ? { ...r, ...patch } : r)));
  }

  /** Anahtar hücresinde klavye: harf yaz → sağa, oklar gezer, Backspace/Delete siler. */
  onKeyCell(event: KeyboardEvent, rowIndex: number, cell: number): void {
    const row = this.rows()[rowIndex];
    if (!row?.key) return;
    const key = event.key;
    if (KEY_LETTER.test(key)) {
      event.preventDefault();
      this.setCell(rowIndex, cell, key.toUpperCase());
      this.focusCell(rowIndex, cell + 1);
      return;
    }
    switch (key) {
      case 'ArrowRight':
        event.preventDefault();
        this.focusCell(rowIndex, cell + 1);
        return;
      case 'ArrowLeft':
        event.preventDefault();
        this.focusCell(rowIndex, cell - 1);
        return;
      case 'ArrowDown':
        event.preventDefault();
        this.focusCell(this.nextKeyedRow(rowIndex, 1), cell);
        return;
      case 'ArrowUp':
        event.preventDefault();
        this.focusCell(this.nextKeyedRow(rowIndex, -1), cell);
        return;
      case 'Backspace':
      case 'Delete':
        event.preventDefault();
        this.setCell(rowIndex, cell, '');
        if (key === 'Backspace') this.focusCell(rowIndex, cell - 1);
        return;
      case 'Tab':
        return;
      default:
        // Hücreye yalnız A–E yazılır.
        if (key.length === 1) event.preventDefault();
    }
  }

  /** Anahtarı bulunmayan kitap: cevapsız aktarıma devam. */
  acknowledgeNoKey(): void {
    this.noKeyAcknowledged.set(true);
  }

  cancel(): void {
    this.saving.set(true);
    this.api
      .cancel(this.jobId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.changed.emit(),
        error: (error: HttpErrorResponse) => this.fail(error),
      });
  }

  confirm(): void {
    if (!this.canConfirm()) return;
    const setup = this.setup()!;
    const existing = setup.existingBook && this.useExisting() === 'existing' ? setup.existingBook : null;
    const request: ConfirmBookImportSetup = {
      bookName: this.bookName().trim(),
      existingBookId: existing?.id ?? null,
      tests: this.included().map((r) => ({
        order: r.order,
        title: r.title.trim() || null,
        minutes: r.minutes,
        fileFrom: r.fileFrom!,
        fileTo: r.fileTo!,
        answerKey: r.key ? r.key.join('') : null,
      })),
    };

    this.saving.set(true);
    this.error.set(null);
    this.api
      .confirmSetup(this.jobId(), request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.changed.emit(),
        error: (error: HttpErrorResponse) => {
          // Aynı adlı kitap: seçim göster (mevcut kitabı kullan / yeni ad).
          if (error.status === 409 && error.error?.existingBook) {
            this.setup.update((s) => (s ? { ...s, existingBook: error.error.existingBook } : s));
            this.useExisting.set('existing');
          }
          this.fail(error);
        },
      });
  }

  conflictLabel(row: EditableSetupTest, cell: number): string | null {
    const other = row.conflicts[cell];
    return other && other !== '.' ? other : null;
  }

  private fail(error: HttpErrorResponse): void {
    this.saving.set(false);
    this.error.set(typeof error.error?.message === 'string' ? error.error.message : 'generic');
  }

  private setCell(rowIndex: number, cell: number, value: string): void {
    this.rows.update((rows) =>
      rows.map((r, i) => {
        if (i !== rowIndex || !r.key) return r;
        const key = [...r.key];
        const conflicts = [...r.conflicts];
        key[cell] = value;
        // Öğretmen hücreyi düzeltti/onayladı: uyuşmazlık kapandı.
        if (value) conflicts[cell] = '.';
        return { ...r, key, conflicts };
      }),
    );
  }

  private nextKeyedRow(from: number, step: 1 | -1): number {
    const rows = this.rows();
    for (let i = from + step; i >= 0 && i < rows.length; i += step) if (rows[i].key) return i;
    return from;
  }

  private focusCell(rowIndex: number, cell: number): void {
    const target = this.host.nativeElement.querySelector<HTMLInputElement>(`[data-cell="${rowIndex}-${cell}"]`);
    target?.focus();
    target?.select();
  }

  private revokeImage(): void {
    if (this.objectUrl) URL.revokeObjectURL(this.objectUrl);
    this.objectUrl = null;
  }
}
