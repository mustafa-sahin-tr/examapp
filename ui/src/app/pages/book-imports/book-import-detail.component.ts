import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { Subscription, timer } from 'rxjs';

import {
  BOOK_IMPORT_STAGES,
  BookImportJobDetail,
  BookImportJobTest,
  BookImportReviewFlag,
  BookImportStage,
  bookImportStatusTone,
  isBookImportActive,
  stageState,
} from '../../models/book-import';
import { BookImportService } from '../../services/book-import.service';
import { BookImportReviewComponent } from './book-import-review.component';
import { BookImportSetupComponent } from './book-import-setup.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { BOOK_IMPORTS_POLL_MS, BOOK_IMPORTS_SCOPE } from './book-imports.component';

type Translate = (key: string, params?: Record<string, unknown>) => string;

/**
 * Issue #1018 (epic #1017): bir kitap aktarımının takibi — aşama adımları, geçerli aşamanın ilerlemesi, test ızgarası ve dikkat
 * gerektiren testler (#1022: bulunan / anahtardaki / kaydedilen soru, sayfa referanslı işaretler). İş sürerken 5 sn'de bir
 * yenilenir. Kurulum onayında onay ekranı (#1021), soru aktarımı bitince inceleme ve onay (#1023) gösterilir. Yeniden dene /
 * iptal (#1025) sonraki dilimde.
 */
@Component({
  selector: 'app-book-import-detail',
  standalone: true,
  imports: [
    BookImportReviewComponent,
    BookImportSetupComponent,
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    PageHeaderComponent,
    RouterLink,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(BOOK_IMPORTS_SCOPE)],
  templateUrl: './book-import-detail.component.html',
  styleUrls: ['./book-import-detail.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BookImportDetailComponent implements OnInit {
  private readonly api = inject(BookImportService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  readonly job = signal<BookImportJobDetail | null>(null);
  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly loadError = signal(false);

  readonly stages = BOOK_IMPORT_STAGES;
  readonly tone = bookImportStatusTone;

  readonly attention = computed(() =>
    (this.job()?.tests ?? []).filter((t) => t.status === 'NeedsReview' || t.status === 'Failed'),
  );
  /** #1023: inceleme ekranı aktarım bitince (incelemede düzenlenebilir, tamamlanınca salt okunur). */
  readonly reviewing = computed(() => {
    const job = this.job();
    return !!job && job.tests.some((t) => t.worksheetId != null) && (job.status === 'ReadyForReview' || job.status === 'Completed');
  });
  readonly testCounts = computed(() => {
    const tests = this.job()?.tests ?? [];
    return {
      done: tests.filter((t) => t.status === 'Done').length,
      review: tests.filter((t) => t.status === 'NeedsReview').length,
      failed: tests.filter((t) => t.status === 'Failed').length,
      running: tests.filter((t) => t.status === 'Running').length,
      approved: tests.filter((t) => t.status === 'Approved').length,
      removed: tests.filter((t) => t.status === 'Removed').length,
      total: tests.length,
    };
  });
  readonly progress = computed(() => {
    const job = this.job();
    return job && job.stageTotal > 0 ? Math.round((100 * job.stageProcessed) / job.stageTotal) : 0;
  });

  private poll?: Subscription;
  private id = '';

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id') ?? '';
    this.load();
  }

  load(): void {
    this.loadError.set(false);
    this.api
      .get(this.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (job) => {
          this.job.set(job);
          this.loading.set(false);
          this.schedulePoll();
        },
        error: (error: HttpErrorResponse) => {
          this.loading.set(false);
          if (error.status === 404) this.notFound.set(true);
          else this.loadError.set(true);
        },
      });
  }

  private schedulePoll(): void {
    this.poll?.unsubscribe();
    const job = this.job();
    if (!job || !isBookImportActive(job.status)) return;
    this.poll = timer(BOOK_IMPORTS_POLL_MS)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
  }

  stageState(stage: BookImportStage): 'done' | 'current' | 'waiting' | 'failed' | 'todo' {
    const job = this.job();
    return job ? stageState(job, stage) : 'todo';
  }

  /** Izgara hücresinin başlığı; aktarılmış testte bulunan / anahtardaki soru sayısıyla (#1022). */
  testCellLabel(test: BookImportJobTest, t: Translate): string {
    const params = { unit: test.unit, no: test.testNo, status: t('testStatus.' + test.status) };
    return test.status === 'Pending' || test.status === 'Running'
      ? t('detail.testCell', params)
      : t('detail.testCellCounts', { ...params, found: test.questionsFound, key: test.keyAnswers });
  }

  /** İşaretin yeri: "sayfa 12, 13 · soru 4, 7" (#1022). */
  flagWhere(flag: BookImportReviewFlag, t: Translate): string {
    const parts = [];
    if (flag.pages.length > 0) parts.push(t('detail.flagPages', { pages: flag.pages.join(', ') }));
    if (flag.numbers?.length) parts.push(t('detail.flagNumbers', { numbers: flag.numbers.join(', ') }));
    return parts.join(' · ');
  }

  testTone(test: BookImportJobTest): 'done' | 'review' | 'failed' | 'running' | 'pending' | 'approved' | 'removed' {
    switch (test.status) {
      case 'Approved':
        return 'approved';
      case 'Removed':
        return 'removed';
      case 'Done':
        return 'done';
      case 'NeedsReview':
        return 'review';
      case 'Failed':
        return 'failed';
      case 'Running':
        return 'running';
      default:
        return 'pending';
    }
  }
}
