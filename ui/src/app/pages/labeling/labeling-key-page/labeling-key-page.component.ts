import {
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

/**
 * Issue #1176: düzeltilen testin cevap anahtarı sayfası. Şeritte küçük görsel; tıklayınca büyür, birden çok sayfa varsa
 * (eski kurulum: kitabın bütün anahtar sayfaları) önceki / sonraki ile gezilir. Görsel aktarımın sayfa ucundan gelir.
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

  private readonly bookImports = inject(BookImportService);
  private readonly dialog = viewChild<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly index = linkedSignal({ source: this.pages, computation: () => 0 });
  protected readonly page = computed(() => this.pages()[this.index()] ?? null);
  protected readonly imageUrl = signal<string | null>(null);
  protected readonly failed = signal(false);

  constructor() {
    effect((onCleanup) => {
      const page = this.page();
      const jobId = this.jobId();
      this.imageUrl.set(null);
      this.failed.set(false);
      if (page === null) return;
      let url: string | null = null;
      const sub = this.bookImports.pageImage(jobId, page).subscribe({
        next: (blob) => {
          url = URL.createObjectURL(blob);
          this.imageUrl.set(url);
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
    inject(DestroyRef).onDestroy(() => this.dialog()?.nativeElement.close());
  }

  protected open(): void {
    this.dialog()?.nativeElement.showModal();
  }

  protected close(): void {
    this.dialog()?.nativeElement.close();
  }

  protected move(step: number): void {
    this.index.update((i) => Math.min(this.pages().length - 1, Math.max(0, i + step)));
  }

  protected closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.dialog()?.nativeElement) this.close();
  }
}
