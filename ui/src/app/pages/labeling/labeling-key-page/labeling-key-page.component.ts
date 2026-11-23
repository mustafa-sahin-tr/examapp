import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  linkedSignal,
  signal,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective } from '@jsverse/transloco';
import { BookImportService } from '../../../services/book-import.service';

/** #1272: anahtar paneli açık bırakıldıysa sonraki sayfa / testte de açılır. */
export const KEY_DOCKED_STORAGE_KEY = 'labeling.fix.keyDocked';
const ZOOM_MIN = 50;
const ZOOM_MAX = 300;

function readDocked(): boolean {
  try {
    return localStorage.getItem(KEY_DOCKED_STORAGE_KEY) === '1';
  } catch {
    return false;
  }
}

function writeDocked(open: boolean): void {
  try {
    localStorage.setItem(KEY_DOCKED_STORAGE_KEY, open ? '1' : '0');
  } catch {
    // depolama kapalı: yalnız bu sayfada geçerli
  }
}

/**
 * Issue #1176: düzeltilen testin cevap anahtarı sayfası. Şeritte küçük görsel; tıklayınca büyür, birden çok sayfa varsa
 * (eski kurulum: kitabın bütün anahtar sayfaları) önceki / sonraki ile gezilir. Görsel aktarımın sayfa ucundan gelir.
 * Issue #1260 (<c>original</c>): düzeltilen sayfanın temizlenmemiş orijinali; işaretli şıklar görünsün diye tuvalin yanında
 * modal olmayan panelde açılır (tuval kullanılabilir kalır).
 * Issue #1272: anahtar ekranın altına sabitlenir (modal değil; tuval üstte kullanılır), yakınlaştırılır; açık kalması
 * tarayıcıda hatırlanır, sonraki sayfa / testte kendiliğinden açılır.
 */
@Component({
  selector: 'app-labeling-key-page',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective],
  templateUrl: './labeling-key-page.component.html',
  styleUrl: './labeling-key-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LabelingKeyPageComponent {
  readonly jobId = input.required<string>();
  readonly pages = input.required<number[]>();
  /** #1260: sayfanın temizlenmemiş orijinali (`?original=true`), modal olmayan panelde. */
  readonly original = input(false, { transform: booleanAttribute });

  private readonly bookImports = inject(BookImportService);
  private readonly dialog = viewChild<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly index = linkedSignal({ source: this.pages, computation: () => 0 });
  protected readonly page = computed(() => this.pages()[this.index()] ?? null);
  protected readonly imageUrl = signal<string | null>(null);
  protected readonly failed = signal(false);
  /** Çeviri anahtarlarının alt dalı: `labeling.fix.key` ya da `labeling.fix.original`. */
  protected readonly ns = computed(() => (this.original() ? 'original' : 'key'));
  /** #1272: alttaki anahtar panelinde görselin genişliği (%). */
  protected readonly zoom = signal(100);

  constructor() {
    effect((onCleanup) => {
      const page = this.page();
      const jobId = this.jobId();
      const original = this.original();
      this.imageUrl.set(null);
      this.failed.set(false);
      if (page === null) return;
      let url: string | null = null;
      const sub = this.bookImports.pageImage(jobId, page, original).subscribe({
        next: (blob) => {
          url = URL.createObjectURL(blob);
          this.imageUrl.set(url);
          // #1272: anahtar paneli açık bırakılmıştı: yeni sayfada / testte de açık.
          if (!this.original() && readDocked()) queueMicrotask(() => this.show());
        },
        error: (error) => {
          console.error('labeling key page failed', error);
          this.failed.set(true);
        },
      });
      onCleanup(() => {
        sub.unsubscribe();
        if (url) URL.revokeObjectURL(url);
      });
    });
    // Alttaki anahtar paneli işaretleme sayfasının genişliğinde durur (sol menünün altında kalmaz).
    const host = inject(ElementRef<HTMLElement>).nativeElement as HTMLElement;
    let observer: ResizeObserver | null = null;
    queueMicrotask(() => {
      const page = host.closest<HTMLElement>('.lb');
      if (!page || typeof ResizeObserver === 'undefined') return;
      const fit = () => {
        const rect = page.getBoundingClientRect();
        host.style.setProperty('--lkp-left', `${Math.max(0, rect.left) + 12}px`);
        host.style.setProperty('--lkp-right', `${Math.max(0, window.innerWidth - rect.right) + 12}px`);
      };
      fit();
      observer = new ResizeObserver(fit);
      observer.observe(page);
    });
    inject(DestroyRef).onDestroy(() => {
      observer?.disconnect();
      this.dialog()?.nativeElement.close();
    });
  }

  protected open(): void {
    if (!this.original()) writeDocked(true);
    this.show();
  }

  protected close(): void {
    if (!this.original()) writeDocked(false);
    this.dialog()?.nativeElement.close();
  }

  /** Orijinal sağda, anahtar altta yüzer; ikisi de modal değil (tuval kullanılabilir kalır). */
  private show(): void {
    const dialog = this.dialog()?.nativeElement;
    if (dialog && !dialog.open) dialog.show();
  }

  protected zoomBy(step: number): void {
    this.zoom.update((z) => Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, z + step)));
  }

  protected move(step: number): void {
    this.index.update((i) => Math.min(this.pages().length - 1, Math.max(0, i + step)));
  }

  protected closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.dialog()?.nativeElement) this.close();
  }
}
