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
import { Subscription, firstValueFrom, switchMap, timer } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { TranslocoDirective } from '@jsverse/transloco';

import {
  BookImportCleaning,
  BookImportSetup,
  BookImportSetupTest,
  BookKeySource,
  ConfirmBookImportSetup,
  bookImportTestCode,
  isActivitySection,
} from '../../models/book-import';
import { BookImportService } from '../../services/book-import.service';

const KEY_LETTER = /^[A-Ea-e]$/;
/** Uyuşmazlıkta "-": diğer okumada bu soru yok (BookSetupPlanner). */
const MISSING_READ = '-';
/** Anahtarın en fazla hücresi (sunucu: BookImportJobTest.MaxKeyLength). */
const MAX_KEY_LENGTH = 200;
/** Beklenen soru sayısı bilinmeyen teste elle girilen anahtarın ilk hücre sayısı. */
const DEFAULT_KEY_LENGTH = 10;
/** #1219: "QR'dan al" okuması sürerken kurulumun yeniden okunma aralığı. */
const QR_POLL_MS = 4000;

function emptyCleaning(): BookImportCleaning {
  return { status: 'Running', running: true, watermark: false, colourPicked: false, marks: 0, pages: 0, processed: 0, useOriginal: false };
}

/** #1234: görseldeki noktanın (görüntülenen boyutta) 9×9 çevresindeki en doygun piksel; okunamazsa null. */
function saturatedColourAt(image: HTMLImageElement, x: number, y: number): number[] | null {
  if (!image.naturalWidth || !image.clientWidth) return null;
  const scale = image.naturalWidth / image.clientWidth;
  const cx = Math.round(x * scale);
  const cy = Math.round(y * scale);
  const canvas = document.createElement('canvas');
  canvas.width = 9;
  canvas.height = 9;
  const ctx = canvas.getContext('2d');
  if (!ctx) return null;
  ctx.drawImage(image, cx - 4, cy - 4, 9, 9, 0, 0, 9, 9);
  const data = ctx.getImageData(0, 0, 9, 9).data;
  let best: number[] | null = null;
  let bestSaturation = -1;
  for (let i = 0; i < data.length; i += 4) {
    const rgb = [data[i], data[i + 1], data[i + 2]];
    const saturation = Math.max(...rgb) - Math.min(...rgb);
    if (saturation > bestSaturation) {
      bestSaturation = saturation;
      best = rgb;
    }
  }
  // Gri / beyaz bir yere tıklandı: işaret rengi değil.
  return best && bestSaturation >= 60 ? best : null;
}

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
  /** #1219: anahtarın kaynağı (onayla kaydedilir); anahtar yoksa null. */
  source: BookKeySource | null;
  /** #1219: testin sayfalarındaki QR bağlantısı ve okuyucusu olup olmadığı. */
  qrLink: string | null;
  qrReadable: boolean;
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
    source: key ? (test.keySource ?? 'Book') : null,
    qrLink: test.qrLink ?? null,
    qrReadable: test.qrReadable ?? false,
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
  readonly keylessCount = computed(
    () => this.included().filter((r) => !r.key && !isActivitySection(r) && !this.notFound().has(r.order)).length,
  );
  private bulkStopped = false;
  /** #1219 "QR'dan al" arka plan okuması: ilerleme sunucudan (kurulum yeniden okunur); başlatma hatası ('generic' çevrilir). */
  readonly qrRun = computed(() => this.setup()?.qrRun ?? null);
  readonly qrRunning = computed(() => this.qrRun()?.running === true);
  readonly qrError = signal<string | null>(null);
  /** Anahtarı olmayan ve QR'ı okunabilir test: "Hepsini QR'dan al" bunları okur. */
  readonly qrKeylessCount = computed(() => this.included().filter((r) => !r.key && r.qrReadable && !isActivitySection(r)).length);
  /** Kitapta anahtar sayfası yoksa da QR'dan ya da elle anahtar girilmiş test varsa "cevapsız devam" sorulmaz. */
  readonly anyKey = computed(() => this.rows().some((r) => !!r.key));
  private qrPoll?: Subscription;
  /** #1234 sayfa temizliği: sonuç sunucudan (kurulum yeniden okunur); başlatma hatası ('generic' çevrilir). */
  readonly cleaning = computed(() => this.setup()?.cleaning ?? null);
  readonly cleaningRunning = computed(() => this.cleaning()?.running === true);
  readonly cleaningError = signal<string | null>(null);
  /** Görüntüleyicide temizlenmiş sayfanın orijinali gösteriliyor (önce / sonra). */
  readonly showOriginal = signal(false);
  /** Görüntüleyicide işaret rengi seçiliyor: işaretli bir şık kutusuna tıklanır, o renkle yeniden temizlenir. */
  readonly pickingColour = signal(false);
  /** Seçimde renksiz (gri / beyaz) bir yere tıklandı. */
  readonly pickMissed = signal(false);
  private cleaningPoll?: Subscription;
  private destroyed = false;
  readonly saving = signal(false);
  readonly error = signal<string | null>(null);
  /** Anahtar sayfalarının küçük resimleri (dosya sayfası → object URL); hepsi yan yana gösterilir. */
  readonly keyImages = signal<ReadonlyMap<number, string>>(new Map());
  /** Altta sabit anahtar paneli: gösterilen sayfa (null kapalı) ve yakınlaştırma (görsel genişliği, %). Cevap girerken açık kalır. */
  readonly dockPage = signal<number | null>(null);
  readonly dockZoom = signal(100);
  /** #1192: sayfaları tam ekranda gösterilen test (null: kapalı); altta önceki / sonraki sayfa. */
  readonly viewedTest = signal<number | null>(null);
  /** Cevap anahtarı da aynı görünümde büyür. */
  readonly viewingKey = signal(false);
  /** #1229: "Kitabı gez" — aynı görünümde kitabın bütün sayfaları; başlıkta sayfanın testleri. */
  readonly viewingBook = signal(false);
  readonly viewerOpen = computed(() => this.viewingKey() || this.viewingBook() || this.viewedRow() != null);
  readonly viewedRow = computed(() => this.rows().find((r) => r.order === this.viewedTest()) ?? null);
  readonly viewedPages = computed(() => {
    if (this.viewingBook()) return Array.from({ length: this.setup()?.pageCount ?? 0 }, (_, i) => i + 1);
    if (this.viewingKey()) return this.keyPages();
    const row = this.viewedRow();
    if (!row || row.fileFrom == null || row.fileTo == null || row.fileTo < row.fileFrom) return [];
    const last = Math.min(row.fileTo, this.setup()?.pageCount ?? Number.MAX_SAFE_INTEGER);
    return Array.from({ length: Math.max(last - row.fileFrom + 1, 0) }, (_, i) => row.fileFrom! + i);
  });
  readonly viewedPage = signal<number | null>(null);
  /** Gösterilen sayfayı kapsayan testler (satırlardaki güncel aralıklarla). */
  readonly viewedPageRows = computed(() => {
    const page = this.viewedPage();
    return page == null ? [] : this.rows().filter((r) => r.fileFrom != null && r.fileTo != null && r.fileFrom <= page && page <= r.fileTo);
  });
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
          !this.qrRunning() &&
          !this.cleaningRunning() &&
          (!this.setup()?.noAnswerKey || this.noKeyAcknowledged() || this.anyKey()))),
  );


  constructor() {
    this.destroyRef.onDestroy(() => (this.destroyed = true));
    this.destroyRef.onDestroy(() => {
      this.revokeKeyImages();
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
          this.loadKeyImages();
          if (setup.qrRun?.running) this.pollQrRun();
          if (setup.cleaning?.running) this.pollCleaning();
        },
        error: () => this.loadError.set(true),
      });
  }

  private loadKeyImages(): void {
    for (const filePage of this.keyPages()) {
      if (this.keyImages().has(filePage)) continue;
      this.api
        .pageImage(this.jobId(), filePage)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (blob) => {
            const url = URL.createObjectURL(blob);
            this.keyImages.update((images) => new Map(images).set(filePage, url));
          },
          // Yüklenemeyen sayfa küçük resimsiz kalır; tıklanınca büyük görünüm yeniden dener.
          error: () => undefined,
        });
    }
  }

  /** #1229: kitabın bütün sayfaları, 1. sayfadan. */
  browseBook(page = 1): void {
    this.viewedTest.set(null);
    this.viewingKey.set(false);
    this.viewingBook.set(true);
    this.showTestPage(page);
  }

  /** Kitap görünümünde sayfa numarasıyla atla; aralık dışı / geçersiz değer yok sayılır. */
  jumpToPage(value: string): void {
    const page = Number(value);
    if (Number.isInteger(page) && this.viewedPages().includes(page) && page !== this.viewedPage()) this.showTestPage(page);
  }

  /** #1192: testin sayfalarını tam ekranda aç (anahtardaki şüpheli hücreye karar vermek için). */
  /** #1134: etkinlik bölümü satırı (anahtar beklenmez; kod "1-E01"). */
  isActivity(row: EditableSetupTest): boolean {
    return isActivitySection(row);
  }

  testCode(row: Pick<EditableSetupTest, 'unit' | 'testNo' | 'flags'>): string {
    return bookImportTestCode(row);
  }

  showTestPages(row: EditableSetupTest): void {
    this.viewingBook.set(false);
    this.viewingKey.set(false);
    this.viewedTest.set(row.order);
    const first = this.viewedPages()[0];
    if (first != null) this.showTestPage(first);
  }

  openDock(filePage: number | null = this.keyPages()[0] ?? null): void {
    if (filePage != null) this.dockPage.set(filePage);
  }

  closeDock(): void {
    this.dockPage.set(null);
  }

  zoomDock(step: number): void {
    this.dockZoom.update((z) => Math.min(Math.max(z + step, 50), 300));
  }

  /** Anahtar sayfasının küçük resmine tıklanınca o sayfa büyük açılır (alttan diğer anahtar sayfalarına geçilir). */
  showKeyLarge(filePage: number): void {
    this.viewedTest.set(null);
    this.viewingBook.set(false);
    this.viewingKey.set(true);
    this.showTestPage(filePage);
  }

  showTestPage(filePage: number): void {
    this.viewedPage.set(filePage);
    this.viewedFailed.set(false);
    this.revokeViewed();
    this.api
      .pageImage(this.jobId(), filePage, this.showOriginal() || this.pickingColour())
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
    this.pickingColour.set(false);
    this.pickMissed.set(false);
    this.viewedTest.set(null);
    this.viewingKey.set(false);
    this.viewingBook.set(false);
    this.viewedPage.set(null);
    this.revokeViewed();
  }

  testPagesKey(event: KeyboardEvent): void {
    // Sayfa numarası alanında oklar imleci gezdirir, sayfa değiştirmez.
    if (event.target instanceof HTMLInputElement && event.key !== 'Escape') return;
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
    this.update(row, { key: Array<string>(length).fill(''), conflicts: Array<string>(length).fill('.'), source: 'Manual' });
    // Cevaplar basılı anahtara bakarak yazılır: anahtar sayfası altta açık kalır.
    if (this.dockPage() == null) this.openDock();
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
      this.update(row, { key, conflicts, aiLabel: match.label, source: 'Ai' });
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

  /**
   * #1219 "QR'dan al" (order verilirse yalnız o test): yayıncının QR çözümleri arka planda okunur (ses kayıtları yazıya çevrilir,
   * harf öğretmenin söylediğinden). Okuma sürerken kurulum birkaç saniyede bir yeniden okunur; gelen anahtar yalnız hâlâ anahtarsız
   * satıra QR etiketiyle doldurulur (elle girilenin üstüne yazılmaz). Okunamayan harf "?" kalır, onaydan önce elle girilir.
   */
  startQrKeys(order?: number): void {
    if (this.qrRunning()) return;
    this.qrError.set(null);
    this.api
      .startQrKeys(this.jobId(), order)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (run) => {
          this.setup.update((s) => (s ? { ...s, qrRun: run } : s));
          this.pollQrRun();
        },
        error: (error: HttpErrorResponse) => {
          const message = error.error?.message;
          this.qrError.set(typeof message === 'string' ? message : 'generic');
        },
      });
  }

  /**
   * #1234 "Sayfaları temizle" (markColour verilirse o renkle): filigran ve renkli cevap işaretleri arka planda silinir, orijinaller
   * saklanır. Sürerken kurulum birkaç saniyede bir yeniden okunur; onay bitene kadar kapalıdır.
   */
  startCleaning(markColour?: number[]): void {
    if (this.cleaningRunning()) return;
    this.cleaningError.set(null);
    this.api
      .startCleaning(this.jobId(), markColour)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.setup.update((s) =>
            s
              ? {
                  ...s,
                  cleaning: {
                    ...emptyCleaning(), ...s.cleaning, status: 'Running', running: true, processed: 0,
                    markColour: markColour ?? null, colourPicked: !!markColour,
                  },
                }
              : s,
          );
          this.pollCleaning();
        },
        error: (error: HttpErrorResponse) => {
          const message = error.error?.message;
          this.cleaningError.set(typeof message === 'string' ? message : 'generic');
        },
      });
  }

  /** #1234 "Orijinal sayfaları kullan": soru aktarımı orijinal sayfalardan. */
  setUseOriginal(useOriginal: boolean): void {
    this.cleaningError.set(null);
    this.api
      .setCleaning(this.jobId(), useOriginal)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.setup.update((s) => (s?.cleaning ? { ...s, cleaning: { ...s.cleaning, useOriginal } } : s)),
        error: (error: HttpErrorResponse) => {
          const message = error.error?.message;
          this.cleaningError.set(typeof message === 'string' ? message : 'generic');
        },
      });
  }

  /** Görüntüleyicide orijinal / temiz sayfa. */
  toggleOriginal(): void {
    this.showOriginal.update((v) => !v);
    const page = this.viewedPage();
    if (page != null) this.showTestPage(page);
  }

  /** İşaret rengini seç: kitap orijinal sayfalarla açılır, işaretli bir şık kutusuna tıklanır. */
  pickMarkColour(): void {
    this.browseBook(this.viewedPage() ?? 1);
    this.pickingColour.set(true);
    const page = this.viewedPage();
    if (page != null) this.showTestPage(page);
  }

  /** Tıklanan noktanın çevresindeki en doygun renk (harfin üstüne tıklansa da kutunun rengi) ile yeniden temizler. */
  pickColourAt(event: MouseEvent): void {
    if (!this.pickingColour()) return;
    const image = event.target as HTMLImageElement;
    const colour = saturatedColourAt(image, event.offsetX, event.offsetY);
    this.pickMissed.set(!colour);
    if (!colour) return;
    this.closeTestPages();
    this.startCleaning(colour);
  }

  private pollCleaning(): void {
    this.cleaningPoll?.unsubscribe();
    this.cleaningPoll = timer(QR_POLL_MS)
      .pipe(
        switchMap(() => this.api.setup(this.jobId())),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (setup) => {
          this.setup.update((s) => (s ? { ...s, cleaning: setup.cleaning ?? null } : s));
          if (setup.cleaning?.running) this.pollCleaning();
          else {
            this.applyMarkKeys(setup);
            this.refreshImages();
          }
        },
        error: () => this.pollCleaning(),
      });
  }

  /**
   * #1235: temizlik bitince sunucu basılı işaretlerden anahtar yazmış olabilir: anahtarsız ya da işaretten gelen satıra
   * işaret anahtarı, dokunulmamış kitap anahtarına işaretlerin farklı okuması (diğer okuma). Elle değiştirilen satıra dokunulmaz.
   */
  private applyMarkKeys(setup: BookImportSetup): void {
    for (const test of setup.tests) {
      const row = this.rows().find((r) => r.order === test.order);
      if (!row || !test.answerKey) continue;
      const key = test.answerKey.split('');
      const conflicts = test.keyConflicts && test.keyConflicts.length === key.length
        ? test.keyConflicts.split('')
        : Array<string>(key.length).fill('.');
      if (test.keySource === 'Marks' && (!row.key || row.source === 'Marks')) {
        this.update(row, { key, conflicts, source: 'Marks' });
      } else if (row.key && row.key.join('') === test.answerKey && row.source === (test.keySource ?? 'Book')) {
        this.update(row, { conflicts });
      }
    }
  }

  /** Temizlik bitince anahtar küçük resimleri ve açık sayfa yeniden yüklenir (temiz ya da orijinal görsel). */
  private refreshImages(): void {
    this.revokeKeyImages();
    this.keyImages.set(new Map());
    this.loadKeyImages();
    const page = this.viewedPage();
    if (page != null && this.viewerOpen()) this.showTestPage(page);
  }

  private pollQrRun(): void {
    this.qrPoll?.unsubscribe();
    this.qrPoll = timer(QR_POLL_MS)
      .pipe(
        switchMap(() => this.api.setup(this.jobId())),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (setup) => {
          this.setup.update((s) => (s ? { ...s, qrRun: setup.qrRun ?? null } : s));
          for (const test of setup.tests) {
            const row = this.rows().find((r) => r.order === test.order);
            if (!row || row.key || !test.answerKey || test.keySource !== 'Qr') continue;
            const key = test.answerKey.split('');
            this.update(row, { key, conflicts: Array<string>(key.length).fill('.'), source: 'Qr' });
          }
          if (setup.qrRun?.running) this.pollQrRun();
        },
        // Geçici hata: biraz sonra yeniden dene.
        error: () => this.pollQrRun(),
      });
  }

  /** QR'dan gelen anahtarın soru sayısı sayfada görülenden farklıysa (onaydan önce bakılmalı). */
  qrCountDiffers(row: EditableSetupTest): boolean {
    return (row.source === 'Qr' || row.source === 'Marks') && !!row.key && row.expected != null && row.key.length !== row.expected;
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
        keySource: r.key ? r.source : null,
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
    // Altta anahtar paneli açıkken kutu panelin arkasında kalmasın (scroll-margin-bottom panel yüksekliği kadar).
    target?.scrollIntoView({ block: 'nearest' });
  }

  private revokeViewed(): void {
    if (this.viewedObjectUrl) URL.revokeObjectURL(this.viewedObjectUrl);
    this.viewedObjectUrl = null;
    this.viewedImage.set(null);
  }

  private revokeKeyImages(): void {
    for (const url of this.keyImages().values()) URL.revokeObjectURL(url);
  }
}
