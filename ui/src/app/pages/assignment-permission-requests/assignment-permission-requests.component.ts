import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, take } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { TranslocoDirective, TranslocoPipe, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { LocaleService } from '../../services/locale.service';
import { WorksheetAccessRequestService } from '../../services/worksheet-access-request.service';
import {
  ResponseBase,
  WorksheetAccessRequest,
  WorksheetAccessRequestStatus,
} from '../../models/worksheet-access-request.model';

type Filter = 'pending' | 'all';

/** Çeviriler kendi Transloco scope'unda: `public/i18n/assignment-requests/<lang>.json` (issue #183). */
const ASSIGNMENT_REQUESTS_SCOPE = 'assignment-requests';

@Component({
  selector: 'app-assignment-permission-requests',
  standalone: true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatChipsModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTableModule,
    TranslocoDirective,
    TranslocoPipe,
  ],
  providers: [provideTranslocoScope(ASSIGNMENT_REQUESTS_SCOPE)],
  templateUrl: './assignment-permission-requests.component.html',
  styleUrl: './assignment-permission-requests.component.scss',
})
export class AssignmentPermissionRequestsComponent implements OnInit {
  private readonly service = inject(WorksheetAccessRequestService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);

  protected readonly displayedColumns = ['requester', 'worksheet', 'date', 'note', 'status', 'actions'];

  protected readonly filter = signal<Filter>('pending');
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly actingId = signal<number | null>(null);

  protected readonly requests = this.service.incomingRequests;
  protected readonly isEmpty = computed(() => !this.loading() && !this.error() && this.requests().length === 0);

  constructor() {
    this.preloadScope();
  }

  ngOnInit(): void {
    this.load();
  }

  protected setFilter(value: Filter): void {
    if (value === this.filter()) {
      return;
    }
    this.filter.set(value);
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service
      .loadIncoming(this.filter() === 'all')
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => this.service.refreshPendingCount().subscribe(),
        error: (err: HttpErrorResponse) => {
          this.error.set((err.error as ResponseBase | null)?.message || this.text('messages.loadFailed'));
        },
      });
  }

  protected approve(row: WorksheetAccessRequest): void {
    this.act(row.id, this.service.approve(row.id), this.text('messages.approved'));
  }

  protected reject(row: WorksheetAccessRequest): void {
    this.act(row.id, this.service.reject(row.id), this.text('messages.rejected'));
  }

  private act(id: number, call: ReturnType<WorksheetAccessRequestService['approve']>, successMsg: string): void {
    if (this.actingId() !== null) {
      return;
    }
    this.actingId.set(id);
    call
      .pipe(
        finalize(() => this.actingId.set(null)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => {
          if (res?.success === false) {
            this.snackBar.open(res.message || this.text('messages.actionFailed'), this.text('messages.ok'), {
              duration: 3000,
            });
            return;
          }
          this.snackBar.open(successMsg, this.text('messages.ok'), { duration: 3000 });
          this.load();
        },
        error: (err: HttpErrorResponse) => {
          const msg = (err.error as ResponseBase | null)?.message || this.text('messages.actionFailed');
          this.snackBar.open(msg, this.text('messages.ok'), { duration: 3000 });
        },
      });
  }

  /** Scope'a göreli anahtarı senkron çözer; sözlük şablon render edilirken yüklenmiş olur. */
  private text(key: string): string {
    return this.transloco.translate<string>(`${ASSIGNMENT_REQUESTS_SCOPE}.${key}`) ?? '';
  }

  /** Durum rozetinin çeviri anahtarı (scope'a göreli; şablonda `t()` ile çözülür). */
  protected statusKey(status: WorksheetAccessRequestStatus): string {
    switch (status) {
      case 'Approved':
        return 'status.approved';
      case 'Rejected':
        return 'status.rejected';
      default:
        return 'status.pending';
    }
  }

  protected statusClass(status: WorksheetAccessRequestStatus): string {
    switch (status) {
      case 'Approved':
        return 'is-approved';
      case 'Rejected':
        return 'is-rejected';
      default:
        return 'is-pending';
    }
  }

  protected formatDate(iso: string): string {
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) {
      return '—';
    }
    return date.toLocaleString(this.localeService.localeDefinition().angularLocale, {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  /**
   * Şablon dışı metinler (snackbar, dialog, hata mesajı) senkron `translate()` ile okunur;
   * sözlük şablon render edilmeden de hazır olsun diye scope burada yüklenir.
   */
  private preloadScope(): void {
    this.transloco
      .load(`${ASSIGNMENT_REQUESTS_SCOPE}/${this.transloco.getActiveLang()}`)
      .pipe(take(1))
      .subscribe();
  }

}
