import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { EMPTY, Observable, Subject, catchError, defer, finalize, switchMap, tap } from 'rxjs';

import { ParentChildProgress, ParentRankScope } from '../../../models/parent-dashboard.model';
import { LocaleService } from '../../../services/locale.service';
import { ParentDashboardService } from '../../../services/parent-dashboard.service';
import { formatDay, formatNumber } from '../parent-format';

/** Veli paneli Transloco scope'u (`public/i18n/parent-dashboard/<lang>.json`). */
const SCOPE = 'parent-dashboard';

/** Rozetin ikonu yoksa gösterilen Material Symbols adı. */
export const DEFAULT_BADGE_ICON = 'military_tech';

/** Şablonun gösterdiği, önceden biçimlendirilmiş görünüm. */
interface ProgressView {
  level: number;
  totalXp: string;
  weeklyXp: string;
  ranks: { scope: ParentRankScope; rank: string; total: string }[];
  badges: { name: string; icon: string; earnedOn: string }[];
}

/**
 * Issue #422 (epic #407 V4): veli panelinde seçili çocuğun "Puan ve rozetler" kartı — seviye, toplam puan, bu hafta kazanılan
 * puan, kendi sırası (okul içi / genel; yalnız sıra ve kapsam büyüklüğü, başka öğrenci yok) ve kazanılmış rozetler ızgarası
 * (ad, ikon, gün). Salt okunur. Çocuk değişince yeniden yüklenir; 404 (bağlantı kaldırılmış olabilir) üst bileşene
 * `notFound` ile bildirilir.
 */
@Component({
  selector: 'app-parent-child-progress',
  standalone: true,
  imports: [MatButtonModule, MatCardModule, MatIconModule, MatProgressSpinnerModule, TranslocoDirective],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './parent-child-progress.component.html',
  styleUrls: ['./parent-child-progress.component.scss'],
})
export class ParentChildProgressComponent {
  readonly studentId = input.required<number>();
  /** Uç 404 döndü (bağlantı kaldırılmış olabilir). */
  readonly notFound = output<void>();

  private readonly dashboardService = inject(ParentDashboardService);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly progress = signal<ParentChildProgress | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly defaultIcon = DEFAULT_BADGE_ICON;

  private readonly locale = computed(() => this.localeService.localeDefinition().angularLocale);

  /** Biçimlendirilmiş görünüm: okul sırası önce (varsa), sonra genel. */
  protected readonly view = computed<ProgressView | null>(() => {
    const p = this.progress();
    if (!p) return null;
    const locale = this.locale();
    return {
      level: p.level,
      totalXp: formatNumber(p.totalXp, locale),
      weeklyXp: formatNumber(p.weeklyXp, locale),
      ranks: [...p.ranks]
        .sort((a, b) => (a.scope === b.scope ? 0 : a.scope === 'school' ? -1 : 1))
        .map((r) => ({ scope: r.scope, rank: formatNumber(r.rank, locale), total: formatNumber(r.totalCount, locale) })),
      badges: p.badges.map((b) => ({
        name: b.name,
        icon: b.icon ?? DEFAULT_BADGE_ICON,
        earnedOn: formatDay(b.earnedOn, locale),
      })),
    };
  });

  private readonly requests = new Subject<number>();

  constructor() {
    this.requests
      .pipe(
        switchMap((id) => this.fetch(id)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();

    // Yalnızca studentId izlenir; gövde untracked (sinyallere yazmak effect'i yeniden tetiklemez).
    effect(() => {
      const studentId = this.studentId();
      untracked(() => this.requests.next(studentId));
    });
  }

  protected reload(): void {
    this.requests.next(this.studentId());
  }

  private fetch(studentId: number): Observable<unknown> {
    return defer(() => {
      this.loading.set(true);
      this.error.set(null);
      this.progress.set(null);
      return this.dashboardService.getChildProgress(studentId).pipe(
        tap((p) => this.progress.set(p)),
        catchError((err: HttpErrorResponse) => {
          if (err?.status === 404) {
            this.error.set(this.text('summaryNotFound'));
            this.notFound.emit();
          } else {
            this.error.set(this.text('progress.loadError'));
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
