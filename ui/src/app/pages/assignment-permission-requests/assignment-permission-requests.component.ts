import { CommonModule } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { WorksheetAccessRequestService } from '../../services/worksheet-access-request.service';
import {
  ResponseBase,
  WorksheetAccessRequest,
  WorksheetAccessRequestStatus,
} from '../../models/worksheet-access-request.model';

type Filter = 'pending' | 'all';

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
  ],
  templateUrl: './assignment-permission-requests.component.html',
  styleUrl: './assignment-permission-requests.component.scss',
})
export class AssignmentPermissionRequestsComponent implements OnInit {
  private readonly service = inject(WorksheetAccessRequestService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly displayedColumns = ['requester', 'worksheet', 'date', 'note', 'status', 'actions'];

  protected readonly filter = signal<Filter>('pending');
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly actingId = signal<number | null>(null);

  protected readonly requests = this.service.incomingRequests;
  protected readonly isEmpty = computed(() => !this.loading() && !this.error() && this.requests().length === 0);

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
          this.error.set((err.error as ResponseBase | null)?.message || 'Talepler yüklenemedi.');
        },
      });
  }

  protected approve(row: WorksheetAccessRequest): void {
    this.act(row.id, this.service.approve(row.id), 'Talep onaylandı.');
  }

  protected reject(row: WorksheetAccessRequest): void {
    this.act(row.id, this.service.reject(row.id), 'Talep reddedildi.');
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
            this.snackBar.open(res.message || 'İşlem tamamlanamadı.', 'Tamam', { duration: 3000 });
            return;
          }
          this.snackBar.open(successMsg, 'Tamam', { duration: 3000 });
          this.load();
        },
        error: (err: HttpErrorResponse) => {
          const msg = (err.error as ResponseBase | null)?.message || 'İşlem tamamlanamadı.';
          this.snackBar.open(msg, 'Tamam', { duration: 3000 });
        },
      });
  }

  protected statusLabel(status: WorksheetAccessRequestStatus): string {
    switch (status) {
      case 'Approved':
        return 'Onaylandı';
      case 'Rejected':
        return 'Reddedildi';
      default:
        return 'Bekliyor';
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
    return date.toLocaleString('tr-TR', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }
}
