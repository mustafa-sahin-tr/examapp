import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective } from '@jsverse/transloco';

import { BookImportTrainingSummary, compactNumbers } from '../../models/book-import';
import { BookImportService } from '../../services/book-import.service';

/**
 * Issue #1133 (Admin): onaylı kitap sayfalarından question-detector eğitim verisi. Önce özet (ne alınacak, ne neden atlandı),
 * sonra ZIP; ZIP Mac'te `training/import_book_export.py` ile veri setine yazılır.
 */
@Component({
  selector: 'app-book-import-training',
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective],
  templateUrl: './book-import-training.component.html',
  styleUrls: ['./book-import-training.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BookImportTrainingComponent {
  private readonly api = inject(BookImportService);
  private readonly destroyRef = inject(DestroyRef);

  readonly summary = signal<BookImportTrainingSummary | null>(null);
  readonly loading = signal(false);
  readonly downloading = signal(false);
  readonly error = signal(false);

  readonly choices = computed(() =>
    Object.entries(this.summary()?.choiceCounts ?? {}).map(([choices, count]) => ({ choices: Number(choices), count })),
  );

  /** Atlananlar kitap + nedene göre, sayfalar kısaltılmış ("4–6, 9"). */
  readonly skipped = computed(() => {
    const groups = new Map<string, { bookName: string; reason: string; pages: number[] }>();
    for (const s of this.summary()?.skipped ?? []) {
      const key = `${s.jobId}|${s.reason}`;
      const group = groups.get(key) ?? { bookName: s.bookName, reason: s.reason, pages: [] };
      group.pages.push(s.filePage);
      groups.set(key, group);
    }
    return [...groups.values()].map((g) => ({ ...g, pages: compactNumbers(g.pages), count: g.pages.length }));
  });

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.api
      .trainingSummary()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (summary) => {
          this.loading.set(false);
          this.summary.set(summary);
        },
        error: () => {
          this.loading.set(false);
          this.error.set(true);
        },
      });
  }

  download(): void {
    if (this.downloading()) return;
    this.downloading.set(true);
    this.error.set(false);
    this.api
      .trainingZip()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (blob) => {
          this.downloading.set(false);
          const url = URL.createObjectURL(blob);
          const link = document.createElement('a');
          link.href = url;
          link.download = `book-import-training-${new Date().toISOString().slice(0, 10)}.zip`;
          link.click();
          URL.revokeObjectURL(url);
        },
        error: () => {
          this.downloading.set(false);
          this.error.set(true);
        },
      });
  }
}
