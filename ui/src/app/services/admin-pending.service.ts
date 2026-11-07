import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { AdminPendingSummary } from '../models/admin-pending.model';

/**
 * Issue #861: admin "Bekleyenler" sayıları — menü rozeti (toplam) ve kuyruk sekmeleri. Tek çağrı; liste uçları ayrı.
 */
@Injectable({ providedIn: 'root' })
export class AdminPendingService {
  private readonly http = inject(HttpClient);

  readonly summary = signal<AdminPendingSummary | null>(null);
  readonly total = computed(() => this.summary()?.total ?? 0);

  refresh(): Observable<AdminPendingSummary> {
    return this.http
      .get<AdminPendingSummary>('/api/exam/admin/pending-summary')
      .pipe(tap((summary) => this.summary.set(summary)));
  }

  reset(): void {
    this.summary.set(null);
  }
}
