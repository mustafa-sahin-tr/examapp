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
import { firstValueFrom } from 'rxjs';
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
/** Uyuşmazlıkta "-": diğer okumada bu soru yok (BookSetupPlanner). */
const MISSING_READ = '-';
/** Anahtarın en fazla hücresi (sunucu: BookImportJobTest.MaxKeyLength). */
const MAX_KEY_LENGTH = 200;
/** Beklenen soru sayısı bilinmeyen teste elle girilen anahtarın ilk hücre sayısı. */
const DEFAULT_KEY_LENGTH = 10;

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
  /** "AI ile bul" önerisiyle doldurulduysa: anahtardaki etiket ("5-36"), anahtar sayfasından okunduysa null. */
  aiLabel?: string | null;
  /** Sayfalarda görülen soru sayısı; anahtarsız teste elle girilen anahtarın hücre sayısı. */
  expected: number | null;
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
    expected: test.expectedQuestions,
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
  /** "AI ile bul": aranan testler (sıra), AI'nın bulamadıkları, toplu aramanın ilerlemesi; hata mesajı ('generic' çevrilir). */
  readonly finding = signal<ReadonlySet<number>>(new Set());
  readonly notFound = signal<ReadonlySet<number>>(new Set());
  readonly bulk = signal<{ done: number; total: number } | null>(null);
  readonly matchError = signal<string | null>(null);
  /** Onaya giren, anahtarı olmayan ve AI'ya henüz sorulmamış test: "Hepsini AI ile bul" bunları arar. */
  readonly keylessCount = computed(() => this.included().filter((r) => !r.key && !this.notFound().has(r.order)).length);
  private bulkStopped = false;
  private destroyed = false;
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  readonly keyPage = signal<number | null>(null);
  readonly keyImage = signal<string | null>(null);
  readonly keyImageLarge = signal(false);
  /** #1192: sayfaları tam ekranda gösterilen test (null: kapalı); altta önceki / sonraki sayfa. */
  readonly viewedTest = signal<number | null>(null);
  /** Cevap anahtarı da aynı görünümde büyür. */
  readonly viewingKey = signal(false);
  readonly viewerOpen = computed(() => this.viewingKey() || this.viewedRow() != null);
  readonly viewedRow = computed(() => this.rows().find((r) => r.order === this.viewedTest()) ?? null);
  readonly viewedPages = computed(() => {
    if (this.viewingKey()) return this.keyPages();
    const row = this.viewedRow();
    if (!row || row.fileFrom == null || row.fileTo == null || row.fileTo < row.fileFrom) return [];
    const last = Math.min(row.fileTo, this.setup()?.pageCount ?? Number.MAX_SAFE_INTEGER);
    return Array.from({ length: Math.max(last - row.fileFrom + 1, 0) }, (_, i) => row.fileFrom! + i);
  });
  readonly viewedPage = signal<number | null>(null);
  readonly viewedImage = signal<string | null>(null);
  readonly viewedFailed = signal(false);
  private viewedObjectUrl: string | null = null;
  private readonly viewClose = viewChild('viewClose', { read: ElementRef<HTMLElement> });
  private readonly viewFocus = effect(() => {
    if (this.viewerOpen()) this.viewClose()?.nativeElement.focus();
  });

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
      // Issue #1032: demoda kurulum kaydedilmez, kontroller engellemez.
      (this.setup()?.isDemo === true ||
        (this.included().length > 0 &&
          this.openConflicts() === 0 &&
          this.unreadable() === 0 &&
          !this.invalidPages() &&
          this.bookName().trim().length > 0 &&
          (!this.setup()?.noAnswerKey || this.noKeyAcknowledged()))),
  );

  private objectUrl: string | null = null;

  constructor() {
    this.destroyRef.onDestroy(() => (this.destroyed = true));
    this.destroyRef.onDestroy(() => {
      this.revokeImage();
      this.revokeViewed();
    });
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
          if (setup.isDemo) this.noKeyAcknowledged.set(true);
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
    this.keyImage.set(null);
    this.api
      .pageImage(this.jobId(), filePage)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => {
          // Hızlı geçişte geç gelen eski sayfa yenisinin yerine geçmesin.
          if (this.keyPage() !== filePage) return;
          this.revokeImage();
          this.objectUrl = URL.createObjectURL(blob);
          this.keyImage.set(this.objectUrl);
        },
        error: () => this.keyImage.set(null),
      });
  }

  /** #1192: testin sayfalarını tam ekranda aç (anahtardaki şüpheli hücreye karar vermek için). */
  showTestPages(row: EditableSetupTest): void {
    this.viewingKey.set(false);
    this.viewedTest.set(row.order);
    const first = this.viewedPages()[0];
    if (first != null) this.showTestPage(first);
  }

  /** Soldaki anahtar görseline tıklanınca o anahtar sayfası büyük açılır. */
  showKeyLarge(): void {
    this.viewedTest.set(null);
    this.viewingKey.set(true);
    const page = this.keyPage() ?? this.keyPages()[0];
    if (page != null) this.showTestPage(page);
  }

  showTestPage(filePage: number): void {
    this.viewedPage.set(filePage);
    this.viewedFailed.set(false);
    this.revokeViewed();
    this.api
      .pageImage(this.jobId(), filePage)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => {
          if (this.viewedPage() !== filePage || !this.viewerOpen()) return;
          this.viewedObjectUrl = URL.createObjectURL(blob);
          this.viewedImage.set(this.viewedObjectUrl);
        },
        error: () => {
          if (this.viewedPage() === filePage) this.viewedFailed.set(true);
        },
      });
  }

  testPageBy(step: number): void {
    const pages = this.viewedPages();
    const next = pages[pages.indexOf(this.viewedPage() ?? -1) + step];
    if (next != null) this.showTestPage(next);
  }

  closeTestPages(): void {
    this.viewedTest.set(null);
    this.viewingKey.set(false);
    this.viewedPage.set(null);
    this.revokeViewed();
  }

  testPagesKey(event: KeyboardEvent): void {
    if (event.key === 'Escape') this.closeTestPages();
    else if (event.key === 'ArrowLeft') this.testPageBy(-1);
    else if (event.key === 'ArrowRight') this.testPageBy(1);
    else return;
    event.preventDefault();
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
      case 'Enter':
        // #1188: farklı okunan kutuda Enter mevcut harfi onaylar, sıradaki farklı kutuya geçer.
        event.preventDefault();
        if (KEY_LETTER.test(row.key[cell] ?? '')) this.setCell(rowIndex, cell, row.key[cell]);
        this.focusFirstConflict();
        return;
      case 'Tab':
        return;
      default:
        // Hücreye yalnız A–E yazılır.
        if (key.length === 1) event.preventDefault();
    }
  }

  /** Anahtarı okunamayan teste boş anahtar açar (beklenen soru sayısı kadar hücre); cevaplar elle yazılır. */
  enterKey(rowIndex: number): void {
    const row = this.rows()[rowIndex];
    if (!row || row.key) return;
    const length = Math.min(Math.max(row.expected ?? DEFAULT_KEY_LENGTH, 1), MAX_KEY_LENGTH);
    this.update(row, { key: Array<string>(length).fill(''), conflicts: Array<string>(length).fill('.') });
    // Satır yeniden çizildikten sonra ilk hücreye geç.
    setTimeout(() => this.focusCell(rowIndex, 0));
  }

  /**
   * "AI ile bul" (isteğe bağlı; diğer yol "Anahtar gir"): testin anahtarını AI'ya arattırır. Öneri yalnız ekranda doldurulur (AI
   * etiketiyle), öğretmen bakıp onaylar; bulunamazsa satırda söylenir, anahtar elle girilebilir. Bu arada elle girilmiş anahtarın
   * üstüne yazılmaz; başka teste önerilmiş anahtar satırı ikinci kez kullanılmaz.
   */
  async findKey(order: number): Promise<boolean> {
    if (this.finding().has(order) || this.rows().find((r) => r.order === order)?.key) return true;
    this.finding.update((set) => new Set(set).add(order));
    this.matchError.set(null);
    try {
      const result = await firstValueFrom(this.api.findKeyWithAi(this.jobId(), order));
      this.setup.update((s) => (s ? { ...s, aiCostUsd: result.aiCostUsd } : s));
      // Sayfanın maliyet kartı (toplam + aşama tablosu) işi yeniden okur.
      this.changed.emit();
      const match = result.match;
      const row = this.rows().find((r) => r.order === order);
      const taken = match?.label && this.rows().some((r) => r.order !== order && r.aiLabel === match.label);
      if (!row || row.key) return true;
      if (!match || taken) {
        this.notFound.update((set) => new Set(set).add(order));
        return true;
      }
      const key = match.answerKey.split('');
      const conflicts = (match.keyConflicts ?? '').padEnd(key.length, '.').split('').slice(0, key.length);
      this.update(row, { key, conflicts, aiLabel: match.label });
      return true;
    } catch (error) {
      const message = (error as HttpErrorResponse).error?.message;
      this.matchError.set(typeof message === 'string' ? message : 'generic');
      return false;
    } finally {
      this.finding.update((set) => {
        const next = new Set(set);
        next.delete(order);
        return next;
      });
    }
  }

  /** "Hepsini AI ile bul": anahtarsız testleri sırayla arar (tek tek, durdurulabilir); hata olursa durur. */
  async findAllKeys(): Promise<void> {
    if (this.bulk()) return;
    const orders = this.included()
      .filter((r) => !r.key && !this.notFound().has(r.order))
      .map((r) => r.order);
    this.bulkStopped = false;
    this.bulk.set({ done: 0, total: orders.length });
    for (const order of orders) {
      if (this.bulkStopped || this.destroyed) break;
      const ok = await this.findKey(order);
      if (!ok) break;
      this.bulk.update((b) => (b ? { ...b, done: b.done + 1 } : b));
    }
    this.bulk.set(null);
  }

  stopFindAll(): void {
    this.bulkStopped = true;
  }

  /** Anahtarın sonuna bir soru ekler (okumada eksik kalan soru). */
  addKeyCell(rowIndex: number): void {
    const row = this.rows()[rowIndex];
    if (!row?.key || row.key.length >= MAX_KEY_LENGTH) return;
    this.update(row, { key: [...row.key, ''], conflicts: [...row.conflicts, '.'] });
    setTimeout(() => this.focusCell(rowIndex, row.key!.length));
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

  /** #1188: kutunun altındaki diğer okumaya tıklanınca o harf alınır. */
  takeOtherRead(rowIndex: number, cell: number): void {
    const other = this.conflictLabel(this.rows()[rowIndex], cell);
    if (!other) return;
    // "-": diğer okumada bu soru yok; kutu silinir (sondaysa anahtar kısalır).
    this.setCell(rowIndex, cell, other === MISSING_READ ? '' : other, true);
    this.focusFirstConflict();
  }

  /** #1188: aktarılacak testlerde ilk açık uyuşmazlığa odaklan. */
  focusFirstConflict(): void {
    const rows = this.rows();
    for (let r = 0; r < rows.length; r++) {
      if (!rows[r].include) continue;
      const c = rows[r].conflicts.findIndex((x) => x !== '.');
      if (c >= 0) {
        this.focusCell(r, c);
        return;
      }
    }
  }

  conflictLabel(row: EditableSetupTest, cell: number): string | null {
    const other = row.conflicts[cell];
    return other && other !== '.' ? other : null;
  }

  private fail(error: HttpErrorResponse): void {
    this.saving.set(false);
    this.error.set(typeof error.error?.message === 'string' ? error.error.message : 'generic');
  }

  private setCell(rowIndex: number, cell: number, value: string, resolve = false): void {
    this.rows.update((rows) =>
      rows.map((r, i) => {
        if (i !== rowIndex || !r.key) return r;
        const key = [...r.key];
        const conflicts = [...r.conflicts];
        key[cell] = value;
        // Öğretmen hücreyi düzeltti/onayladı: uyuşmazlık kapandı.
        if (value || resolve) conflicts[cell] = '.';
        // #1167: sondaki hücre silinince anahtar kısalır (fazla okunmuş soru); aradaki boş hücre okunamadı olarak kalır.
        // Yalnız son hücre silinince: elle girilen anahtarın henüz boş hücreleri yazarken kaybolmasın.
        if (!value && cell === key.length - 1) while (key.length > 0 && key[key.length - 1] === '') key.pop();
        conflicts.length = key.length;
        // Bütün hücreler silindi: test anahtarsız kalır.
        return { ...r, key: key.length > 0 ? key : null, conflicts, aiLabel: key.length > 0 ? r.aiLabel : undefined };
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

  private revokeViewed(): void {
    if (this.viewedObjectUrl) URL.revokeObjectURL(this.viewedObjectUrl);
    this.viewedObjectUrl = null;
    this.viewedImage.set(null);
  }

  private revokeImage(): void {
    if (this.objectUrl) URL.revokeObjectURL(this.objectUrl);
    this.objectUrl = null;
  }
}
