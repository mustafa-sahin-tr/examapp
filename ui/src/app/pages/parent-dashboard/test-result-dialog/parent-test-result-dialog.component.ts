import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { finalize } from 'rxjs';

import { ParentChildTestResult } from '../../../models/parent-dashboard.model';
import { LocaleService } from '../../../services/locale.service';
import { ParentDashboardService } from '../../../services/parent-dashboard.service';
import { approxHourParts, durationKey } from '../parent-format';

/** Veli paneli Transloco scope'u (`public/i18n/parent-dashboard/<lang>.json`); dialog kök enjektörde açıldığı için ayrıca sağlanır. */
const SCOPE = 'parent-dashboard';

export interface ParentTestResultDialogData {
  studentId: number;
  testInstanceId: number;
  /** Listede görünen başlık (yüklenirken gösterilir). */
  title: string;
}

/**
 * Issue #421 (epic #407 V3): çocuğun bitmiş testinin özeti — puan, doğru/yanlış/boş, süre, başlangıç/bitiş ve konu bazında
 * sayılar. Sunucu soru metni/görseli, cevap anahtarı ya da seçilen şık göndermez; bu dialog da göstermez. Salt okunur.
 */
@Component({
  selector: 'app-parent-test-result-dialog',
  standalone: true,
  imports: [MatButtonModule, MatDialogModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './parent-test-result-dialog.component.html',
  styleUrls: ['./parent-test-result-dialog.component.scss'],
})
export class ParentTestResultDialogComponent implements OnInit {
  protected readonly data = inject<ParentTestResultDialogData>(MAT_DIALOG_DATA);
  private readonly dashboardService = inject(ParentDashboardService);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly result = signal<ParentChildTestResult | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.dashboardService
      .getChildTestResult(this.data.studentId, this.data.testInstanceId)
      .pipe(
        finalize(() => this.loading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (result) => this.result.set(result),
        error: (err: HttpErrorResponse) =>
          this.error.set(this.text(err?.status === 404 ? 'testResult.notFound' : 'testResult.loadError')),
      });
  }

  /** Sunucu başlangıç/bitişi saate keser (#421 review): "7 Eki 2026, yaklaşık 14:00". */
  protected formatDate(iso: string | null): string {
    const parts = approxHourParts(iso, this.localeService.localeDefinition().angularLocale);
    return parts ? this.text('testResult.approxTime', parts) : '';
  }

  protected formatDuration(seconds: number): string {
    const { key, params } = durationKey(seconds);
    return this.text(key, params);
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${SCOPE}.${key}`, params) ?? '';
  }
}
