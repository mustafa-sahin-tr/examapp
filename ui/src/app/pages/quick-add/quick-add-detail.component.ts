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

import { BookImportJobDetail, BookImportJobTest, BookImportReviewFlag } from '../../models/book-import';
import { canRetryQuickAdd, isQuickAddPending, quickAddPhase } from '../../models/quick-add';
import { QuickAddService } from '../../services/quick-add.service';
import { QUICK_ADD_SCOPE } from './quick-add.component';

/** Takip ekranı yoklama aralığı: tek fotoğraf ~30 sn sürer, liste ekranından sık. */
export const QUICK_ADD_DETAIL_POLL_MS = 3000;
/** Metni olan işaret kodları (`BookImportReviewFlag`); bilinmeyen kod genel nota düşer. */
const QUICK_ADD_FLAG_CODES = [
  'countMismatch', 'missingNumbers', 'extraQuestions', 'optionCount', 'noAnswer', 'notSaved', 'detectionFailed', 'saveFailed',
];

/**
 * Issue #1053 (epic #1048): bir hızlı eklemenin durum kartı (Sırada / İşleniyor / İncelemeye hazır / Başarısız + Yeniden dene)
 * ve fotoğraf başına sonuç (bulunan / kaydedilen soru, cevap anahtarı uyuşmazlığı). İş bitene kadar yoklanır. İnceleme ve onay #1054.
 */
@Component({
  selector: 'app-quick-add-detail',
  standalone: true,
  imports: [
    DatePipe,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    RouterLink,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(QUICK_ADD_SCOPE)],
  templateUrl: './quick-add-detail.component.html',
  styleUrls: ['./quick-add-detail.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuickAddDetailComponent implements OnInit {
  private readonly api = inject(QuickAddService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  readonly job = signal<BookImportJobDetail | null>(null);
  readonly loading = signal(true);
  readonly loadError = signal<'notFound' | 'failed' | null>(null);
  readonly retrying = signal(false);
  readonly retryError = signal(false);
  /** Fotoğraf sırası → küçük resim (blob URL). */
  readonly thumbs = signal<Record<number, string>>({});

  readonly phase = computed(() => {
    const job = this.job();
    return job ? quickAddPhase(job) : null;
  });
  readonly canRetry = computed(() => {
    const job = this.job();
    return job != null && canRetryQuickAdd(job) && !this.retrying();
  });
  readonly photos = computed(() => [...(this.job()?.tests ?? [])].sort((a, b) => a.order - b.order));
  readonly savedTotal = computed(() => this.photos().reduce((sum, p) => sum + p.questionsSaved, 0));

  private poll?: Subscription;

  ngOnInit(): void {
    this.destroyRef.onDestroy(() => Object.values(this.thumbs()).forEach((url) => URL.revokeObjectURL(url)));
    this.load();
  }

  load(): void {
    this.api
      .get(this.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (job) => {
          this.job.set(job);
          this.loading.set(false);
          this.loadError.set(null);
          this.loadThumbs(job);
          this.schedulePoll();
        },
        error: (error: HttpErrorResponse) => {
          this.loading.set(false);
          // Yoklama sırasında geçici hata: son durum kalır, yoklama sürer.
          if (this.job() && error.status !== 404) {
            this.schedulePoll();
            return;
          }
          this.loadError.set(error.status === 404 ? 'notFound' : 'failed');
        },
      });
  }

  private schedulePoll(): void {
    this.poll?.unsubscribe();
    const job = this.job();
    if (!job || !isQuickAddPending(job) || job.stalled) return;
    this.poll = timer(QUICK_ADD_DETAIL_POLL_MS)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
  }

  /** Küçük resimler bir kez çekilir (yetkili istek; blob). */
  private loadThumbs(job: BookImportJobDetail): void {
    for (const test of job.tests) {
      const photo = test.fileFrom ?? test.order;
      if (this.thumbs()[photo] !== undefined) continue;
      this.thumbs.update((thumbs) => ({ ...thumbs, [photo]: '' }));
      this.api
        .photo(job.id, photo)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (blob) => this.thumbs.update((thumbs) => ({ ...thumbs, [photo]: URL.createObjectURL(blob) })),
          error: () => undefined,
        });
    }
  }

  retry(): void {
    const job = this.job();
    if (!job || this.retrying()) return;
    this.retrying.set(true);
    this.retryError.set(false);
    this.api
      .retry(job.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.retrying.set(false);
          this.job.set({ ...job, status: 'Queued', stalled: false, error: null });
          this.schedulePoll();
        },
        error: () => {
          this.retrying.set(false);
          this.retryError.set(true);
        },
      });
  }

  thumb(test: BookImportJobTest): string {
    return this.thumbs()[test.fileFrom ?? test.order] ?? '';
  }

  /** Kartın açıklama satırı. */
  messageKey(job: BookImportJobDetail): string {
    if (job.stalled) return 'detail.stalled';
    switch (quickAddPhase(job)) {
      case 'queued':
        return job.status === 'AwaitingBudget' ? 'detail.waitingBudget' : 'detail.queued';
      case 'processing':
        return 'detail.processing';
      case 'ready':
        return this.savedTotal() > 0 ? 'detail.ready' : 'detail.readyEmpty';
      case 'cancelled':
        return 'detail.cancelled';
      default:
        return 'detail.failed';
    }
  }

  progress(job: BookImportJobDetail): number {
    return job.stageTotal > 0 ? Math.round((100 * job.stageProcessed) / job.stageTotal) : 0;
  }

  /** İşleme biten (hatalı dahil) fotoğraf sayısı. */
  done(job: BookImportJobDetail): number {
    return job.tests.filter((t) => t.status !== 'Pending' && t.status !== 'Running').length;
  }

  flagKey(flag: BookImportReviewFlag): string {
    return QUICK_ADD_FLAG_CODES.includes(flag.code) ? `flags.${flag.code}` : 'flags.other';
  }

  isPhotoPending(test: BookImportJobTest): boolean {
    return test.status === 'Pending' || test.status === 'Running';
  }
}
