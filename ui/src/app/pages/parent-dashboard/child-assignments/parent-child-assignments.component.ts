import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatChipListboxChange, MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { EMPTY, Observable, Subject, catchError, defer, finalize, switchMap, tap } from 'rxjs';

import {
  PARENT_ASSIGNMENT_STATUSES,
  ParentAssignmentStatus,
  ParentChildAssignmentItem,
  ParentChildAssignmentList,
} from '../../../models/parent-dashboard.model';
import { LocaleService } from '../../../services/locale.service';
import { ParentDashboardService } from '../../../services/parent-dashboard.service';
import { provideTranslatedPaginatorIntl } from '../../../shared/utils/paginator-intl.util';
import { durationKey, formatDateTime } from '../parent-format';
import {
  ParentTestResultDialogComponent,
  ParentTestResultDialogData,
} from '../test-result-dialog/parent-test-result-dialog.component';

/** Veli paneli Transloco scope'u (`public/i18n/parent-dashboard/<lang>.json`). */
const SCOPE = 'parent-dashboard';

interface ListRequest {
  studentId: number;
  status: ParentAssignmentStatus | null;
  page: number;
}

/**
 * Issue #421 (epic #407 V3): veli panelinde seçili çocuğun "Ödevler ve testler" bölümü. Durum çipleri (Tümü / Bekliyor /
 * Gecikti / Tamamlandı — sayılar V2 özetiyle aynı kovalardan), sayfalı liste (en yeni teslim tarihi önce) ve sonucu olan
 * testler için özet dialog'u. Salt okunur; soru içeriği/cevap gösterilmez. Çocuk değişince filtre ve sayfa sıfırlanır.
 * 404 (bağlantı kaldırılmış olabilir) üst bileşene `notFound` ile bildirilir.
 */
@Component({
  selector: 'app-parent-child-assignments',
  standalone: true,
  imports: [
    MatButtonModule,
    MatChipsModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(SCOPE), provideTranslatedPaginatorIntl(SCOPE, `${SCOPE}.assignments.paginator`)],
  templateUrl: './parent-child-assignments.component.html',
  styleUrls: ['./parent-child-assignments.component.scss'],
})
export class ParentChildAssignmentsComponent {
  readonly studentId = input.required<number>();
  /** Liste 404 döndü (bağlantı kaldırılmış olabilir). */
  readonly notFound = output<void>();

  private readonly dashboardService = inject(ParentDashboardService);
  private readonly dialog = inject(MatDialog);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly statuses = PARENT_ASSIGNMENT_STATUSES;
  protected readonly status = signal<ParentAssignmentStatus | null>(null);
  protected readonly page = signal(1);
  protected readonly list = signal<ParentChildAssignmentList | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  private readonly requests = new Subject<ListRequest>();

  constructor() {
    this.requests
      .pipe(
        switchMap((request) => this.fetch(request)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();

    // Çocuk değişince (ilk değer dahil) filtre ve sayfa sıfırlanır, liste yeniden yüklenir. Yalnızca studentId izlenir;
    // gövde untracked — status/page/list sinyallerine yazmak effect'i yeniden tetiklemez.
    effect(() => {
      const studentId = this.studentId();
      untracked(() => {
        this.status.set(null);
        this.page.set(1);
        this.list.set(null);
        this.requests.next({ studentId, status: null, page: 1 });
      });
    });
  }

  protected selectStatus(event: MatChipListboxChange): void {
    const value = event.value as ParentAssignmentStatus | 'all' | null | undefined;
    if (value === null || value === undefined) {
      // Seçili çipe tekrar tıklamak listbox'ta seçimi KALDIRIR (m6): filtre her zaman tek seçili kalmalı — eski seçimi geri koy.
      event.source.value = this.status() ?? 'all';
      return;
    }
    const next = value === 'all' ? null : value;
    if (next === this.status()) return;
    this.status.set(next);
    this.page.set(1);
    this.reload();
  }

  protected changePage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.reload();
  }

  protected reload(): void {
    this.requests.next({ studentId: this.studentId(), status: this.status(), page: this.page() });
  }

  protected openResult(item: ParentChildAssignmentItem): void {
    if (item.testInstanceId === null) return;
    const data: ParentTestResultDialogData = {
      studentId: this.studentId(),
      testInstanceId: item.testInstanceId,
      title: item.title,
    };
    this.dialog.open(ParentTestResultDialogComponent, { data, width: '560px', maxWidth: '95vw', autoFocus: 'dialog' });
  }

  protected count(status: ParentAssignmentStatus): number {
    return this.list()?.counts[status] ?? 0;
  }

  protected totalAll(): number {
    const counts = this.list()?.counts;
    return counts ? counts.completed + counts.overdue + counts.pending : 0;
  }

  protected formatDate(iso: string | null): string {
    return formatDateTime(iso, this.localeService.localeDefinition().angularLocale);
  }

  protected formatDuration(seconds: number): string {
    const { key, params } = durationKey(seconds);
    return this.text(key, params);
  }

  private fetch(request: ListRequest): Observable<unknown> {
    return defer(() => {
      this.loading.set(true);
      this.error.set(null);
      return this.dashboardService.getChildAssignments(request.studentId, request.status, request.page).pipe(
        tap((list) => this.list.set(list)),
        catchError((err: HttpErrorResponse) => {
          this.list.set(null);
          if (err?.status === 404) {
            this.error.set(this.text('summaryNotFound'));
            this.notFound.emit();
          } else {
            this.error.set(this.text('assignments.loadError'));
          }
          return EMPTY;
        }),
        finalize(() => this.loading.set(false))
      );
    });
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${SCOPE}.${key}`, params) ?? '';
  }
}
