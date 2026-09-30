import { Component, DestroyRef, computed, inject, input, linkedSignal, output, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { EMPTY, Observable, catchError, finalize, switchMap, tap } from 'rxjs';
import {
  AppRouteLink,
  WORKSHEET_COMMENT_REPORT_REASONS,
  WorksheetComment,
  WorksheetCommentReportItem,
  WorksheetCommentReportReason,
  WorksheetCommentReportsPage,
  toQuestionOrder,
  worksheetCommentLink,
} from '../../../models/worksheet-comment.model';
import { parseUtcDate } from '../../../pages/notifications/notification-format';
import { LocaleService } from '../../../services/locale.service';
import { WorksheetCommentService } from '../../../services/worksheet-comment.service';
import { provideTranslatedPaginatorIntl } from '../../utils/paginator-intl.util';
import { openCommentHideDialog } from '../comment-hide-dialog/comment-hide-dialog.component';
import { commentErrorMessage } from '../comment-thread/comment-error';

const COMMENTS_SCOPE = 'comments';

/** Sunucu pageSize'ı 1..50'ye sıkıştırır. */
export const COMMENT_REPORTS_PAGE_SIZE_OPTIONS: readonly number[] = [10, 20, 50];
export const COMMENT_REPORTS_DEFAULT_PAGE_SIZE = 20;

/** Şablonun bastığı satır. */
export interface CommentReportRow {
  key: string;
  comment: WorksheetComment;
  worksheetTitle: string;
  questionOrder: number | null;
  reportCount: number;
  /** Yalnız sıfır olmayan nedenler, sabit sırayla. */
  reasons: { reason: WorksheetCommentReportReason; count: number }[];
  lastReportedAt: string;
  notes: string[];
  link: AppRouteLink;
}

function toRow(item: WorksheetCommentReportItem): CommentReportRow {
  const comment = item.comment;
  return {
    key: `r${comment.id}`,
    comment,
    worksheetTitle: item.worksheetTitle?.trim() ?? '',
    questionOrder: toQuestionOrder(comment.questionOrder),
    reportCount: item.reportCount ?? 0,
    reasons: WORKSHEET_COMMENT_REPORT_REASONS.map((reason) => ({ reason, count: item.reasons?.[reason] ?? 0 })).filter(
      (entry) => entry.count > 0
    ),
    lastReportedAt: item.lastReportedAt,
    notes: (item.notes ?? []).filter((note) => typeof note === 'string' && note.trim().length > 0),
    link: worksheetCommentLink({
      worksheetId: comment.worksheetId,
      questionId: comment.questionId,
      commentId: comment.id,
      rootCommentId: comment.parentCommentId,
    }),
  };
}

/**
 * Issue #305 — şikayet edilmiş yorumlar (moderatör listesi), sayfalı. `worksheetId` verilirse o worksheet'in
 * (`GET .../comments/reports`, öğretmen yalnız moderatörü olduklarını görür), verilmezse admin'in tüm listesi
 * (`GET api/admin/comments/reports`). Satırda yoruma git (derin link), gizle (dialog) / gizlemeyi kaldır.
 * Şikayet edenlerin kimliği sunucudan gelmez.
 */
@Component({
  selector: 'app-comment-report-list',
  standalone: true,
  imports: [
    MatButtonModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    RouterLink,
    TranslocoDirective,
  ],
  providers: [
    provideTranslocoScope(COMMENTS_SCOPE),
    provideTranslatedPaginatorIntl(COMMENTS_SCOPE, `${COMMENTS_SCOPE}.reports.paginator`),
  ],
  templateUrl: './comment-report-list.component.html',
  styleUrls: ['./comment-report-list.component.scss'],
})
export class CommentReportListComponent {
  private readonly api = inject(WorksheetCommentService);
  private readonly dialog = inject(MatDialog);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  /** Boşsa admin'in tüm worksheet'lerdeki listesi. */
  readonly worksheetId = input<number | null>(null);
  /** "Şikayetler (N)" başlığı çizilsin mi (admin sayfası kendi başlığını çizer). */
  readonly showHeading = input(true);
  /** Toplam şikayetli yorum sayısı (her başarılı yüklemede). */
  readonly totalCountChange = output<number>();

  protected readonly pageSizeOptions = COMMENT_REPORTS_PAGE_SIZE_OPTIONS;
  /** 0 tabanlı; worksheet değişince başa döner. */
  protected readonly pageIndex = linkedSignal({ source: this.worksheetId, computation: () => 0 });
  protected readonly pageSize = signal(COMMENT_REPORTS_DEFAULT_PAGE_SIZE);
  protected readonly loading = signal(false);
  protected readonly loaded = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly items = signal<WorksheetCommentReportItem[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly actionError = signal<string | null>(null);
  /** İşlemdeki satırın yorum kimliği (çift tıklama / eşzamanlı aksiyon). */
  protected readonly busyId = signal<number | null>(null);

  protected readonly rows = computed(() => this.items().map(toRow));
  protected readonly isAdminList = computed(() => this.worksheetId() === null);
  protected readonly isEmpty = computed(() => this.loaded() && !this.error() && this.items().length === 0);
  protected readonly locale = computed(() => this.localeService.localeDefinition().angularLocale);

  private readonly reloadTick = signal(0);
  private readonly request = computed(() => ({
    worksheetId: this.worksheetId(),
    page: this.pageIndex() + 1,
    pageSize: this.pageSize(),
    tick: this.reloadTick(),
  }));

  constructor() {
    this.transloco.load(`${COMMENTS_SCOPE}/${this.transloco.getActiveLang()}`).pipe(takeUntilDestroyed()).subscribe();
    toObservable(this.request)
      .pipe(
        switchMap((query) => this.fetch(query.worksheetId, query.page, query.pageSize)),
        takeUntilDestroyed()
      )
      .subscribe();
  }

  /** Tekrar dene / dış yenileme. */
  reload(): void {
    this.reloadTick.update((tick) => tick + 1);
  }

  protected onPage(event: PageEvent): void {
    this.pageSize.set(event.pageSize);
    this.pageIndex.set(event.pageIndex);
  }

  protected hide(row: CommentReportRow): void {
    if (this.busyId() !== null) {
      return;
    }
    this.busyId.set(row.comment.id);
    this.actionError.set(null);
    openCommentHideDialog(this.dialog, { worksheetId: row.comment.worksheetId, commentId: row.comment.id })
      .afterClosed()
      .subscribe((updated) => {
        this.busyId.set(null);
        if (updated) {
          this.replaceComment(row.comment.id, updated, true);
        }
      });
  }

  protected unhide(row: CommentReportRow): void {
    if (this.busyId() !== null) {
      return;
    }
    this.busyId.set(row.comment.id);
    this.actionError.set(null);
    this.api
      .unhide(row.comment.worksheetId, row.comment.id)
      .pipe(
        finalize(() => this.busyId.set(null)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (updated) => this.replaceComment(row.comment.id, updated, false),
        error: (err: unknown) => this.actionError.set(commentErrorMessage(err, this.t, 'reports.unhideError')),
      });
  }

  protected formatDate(iso: string | null | undefined): string {
    if (!iso) {
      return '';
    }
    const date = parseUtcDate(iso);
    return Number.isNaN(date.getTime())
      ? ''
      : date.toLocaleString(this.locale(), { dateStyle: 'medium', timeStyle: 'short' });
  }

  private fetch(worksheetId: number | null, page: number, pageSize: number): Observable<WorksheetCommentReportsPage> {
    this.loading.set(true);
    this.error.set(null);
    const call =
      worksheetId === null
        ? this.api.getAdminReports({ page, pageSize })
        : this.api.getReports(worksheetId, { page, pageSize });
    return call.pipe(
      tap((result) => {
        this.items.set(result?.items ?? []);
        const total = typeof result?.totalCount === 'number' && result.totalCount >= 0 ? result.totalCount : 0;
        this.totalCount.set(total);
        this.loaded.set(true);
        this.totalCountChange.emit(total);
      }),
      catchError((err: unknown) => {
        this.error.set(commentErrorMessage(err, this.t, 'reports.loadError'));
        return EMPTY;
      }),
      finalize(() => this.loading.set(false))
    );
  }

  private replaceComment(commentId: number, updated: WorksheetComment | null, hidden: boolean): void {
    this.items.update((list) =>
      list.map((item) =>
        item.comment.id === commentId
          ? {
              ...item,
              comment: {
                ...item.comment,
                ...(updated && typeof updated.id === 'number' ? updated : {}),
                isHidden: hidden,
              },
            }
          : item
      )
    );
  }

  private readonly t = (key: string, params?: Record<string, unknown>): string =>
    this.transloco.translate<string>(`${COMMENTS_SCOPE}.${key}`, params) ?? key;
}
