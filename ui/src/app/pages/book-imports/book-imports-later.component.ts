import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { BookImportDeferredJob, BookImportDeferredTest, bookImportTestName } from '../../models/book-import';
import { BookImportService } from '../../services/book-import.service';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { BOOK_IMPORTS_SCOPE } from './book-imports.component';

/**
 * Issue #1231: "Sonra bakılacaklar" — kalan testleri sonraya bırakılan kitaplar ve onaylanmamış testleri. Teste tıklayınca
 * aktarımın inceleme ekranı o test açık gelir (`?test=`); son test bitince iş tamamlanır ve buradan çıkar.
 */
@Component({
  selector: 'app-book-imports-later',
  standalone: true,
  imports: [DatePipe, MatButtonModule, MatIconModule, MatProgressSpinnerModule, PageHeaderComponent, RouterLink, TranslocoDirective],
  providers: [provideTranslocoScope(BOOK_IMPORTS_SCOPE)],
  templateUrl: './book-imports-later.component.html',
  styleUrls: ['./book-imports-later.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BookImportsLaterComponent implements OnInit {
  private readonly api = inject(BookImportService);
  private readonly destroyRef = inject(DestroyRef);

  readonly jobs = signal<BookImportDeferredJob[]>([]);
  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly busyId = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loadError.set(false);
    this.api
      .deferred()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (jobs) => {
          this.jobs.set(jobs);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.loadError.set(true);
        },
      });
  }

  testName(test: BookImportDeferredTest): string {
    return bookImportTestName(test);
  }

  /** Aktarım listesine geri al. */
  undefer(job: BookImportDeferredJob): void {
    this.busyId.set(job.id);
    this.api
      .setDeferred(job.id, false)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.busyId.set(null);
          this.jobs.update((list) => list.filter((j) => j.id !== job.id));
        },
        error: () => {
          this.busyId.set(null);
          this.load();
        },
      });
  }
}
