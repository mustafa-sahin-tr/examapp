import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { EMPTY, Observable, Subject, catchError, defer, finalize, switchMap, tap } from 'rxjs';

import { ParentWeeklyReport } from '../../../models/parent-dashboard.model';
import { LocaleService } from '../../../services/locale.service';
import { ParentDashboardService } from '../../../services/parent-dashboard.service';
import { parseDay, toDayString } from '../parent-format';

const SCOPE = 'parent-dashboard';

/** Sunucunun geriye izin verdiği hafta sayısı (son biten hafta dahil; `ParentDashboardController.MaxWeeklyReportWeeks`). */
export const MAX_REPORT_WEEKS = 12;

/** Material ikon adı değilse (ör. emoji / yol) varsayılan rozet ikonu. */
const BADGE_ICON = 'military_tech';

/** "yyyy-MM-dd" gününü `days` gün kaydırır. */
export function shiftDay(day: string, days: number): string | null {
  const d = parseDay(day);
  return d ? toDayString(new Date(d.getFullYear(), d.getMonth(), d.getDate() + days)) : null;
}

/**
 * Issue #1112: veli panelinde seçili çocuğun "Rapor" sekmesi — haftalık rapor (son biten hafta; önceki 11 haftaya geri gidilir).
 * Sayılar sunucuda panelle aynı hesaptır. Plan dışı (403) kilit notu + Plan sekmesine düğme (`openPlan`); 404 üst bileşene
 * `notFound` ile bildirilir.
 */
@Component({
  selector: 'app-parent-child-report',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule, TranslocoDirective],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './parent-child-report.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './parent-child-report.component.scss',
})
export class ParentChildReportComponent {
  readonly studentId = input.required<number>();
  readonly notFound = output<void>();
  /** Kilit notundaki "Planı gör" düğmesi. */
  readonly openPlan = output<void>();

  private readonly dashboardService = inject(ParentDashboardService);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly report = signal<ParentWeeklyReport | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly locked = signal(false);
  /** Son biten hafta (ilk yanıttan); ileri düğmesinin sınırı. */
  protected readonly latestWeek = signal<string | null>(null);

  private readonly locale = computed(() => this.localeService.localeDefinition().angularLocale);

  protected readonly accuracy = computed(() => {
    const r = this.report();
    return r && r.questionsSolved > 0 ? Math.round((r.correct * 100) / r.questionsSolved) : null;
  });
  protected readonly weekLabel = computed(() => {
    const r = this.report();
    const from = parseDay(r?.weekStart);
    if (!r || !from) return null;
    const to = new Date(from.getFullYear(), from.getMonth(), from.getDate() + 6);
    const locale = this.locale();
    return {
      from: new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'long' }).format(from),
      to: new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'long', year: 'numeric' }).format(to),
    };
  });
  protected readonly canGoBack = computed(() => {
    const r = this.report();
    const latest = this.latestWeek();
    const oldest = latest ? shiftDay(latest, -7 * (MAX_REPORT_WEEKS - 1)) : null;
    return !!r && !!oldest && r.weekStart > oldest;
  });
  protected readonly canGoForward = computed(() => {
    const r = this.report();
    const latest = this.latestWeek();
    return !!r && !!latest && r.weekStart < latest;
  });
  protected readonly isQuiet = computed(() => {
    const r = this.report();
    return !!r && r.questionsSolved === 0 && r.mockExams.length === 0;
  });

  private readonly requests = new Subject<{ studentId: number; weekStart?: string }>();
  private lastWeek: string | undefined;

  constructor() {
    this.requests
      .pipe(
        switchMap((request) => this.fetch(request.studentId, request.weekStart)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();

    // Çocuk değişince son biten haftaya dönülür.
    effect(() => {
      const studentId = this.studentId();
      untracked(() => {
        this.latestWeek.set(null);
        this.report.set(null);
        this.request(undefined, studentId);
      });
    });
  }

  protected previousWeek(): void {
    this.move(-7);
  }

  protected nextWeek(): void {
    this.move(7);
  }

  protected reload(): void {
    this.request(this.lastWeek);
  }

  protected badgeIcon(icon: string | null): string {
    return icon && /^[a-z_]+$/.test(icon) ? icon : BADGE_ICON;
  }

  protected net(value: number): string {
    return new Intl.NumberFormat(this.locale(), { maximumFractionDigits: 2 }).format(value);
  }

  private move(days: number): void {
    const shown = this.report()?.weekStart;
    const next = shown ? shiftDay(shown, days) : null;
    if (next) this.request(next);
  }

  private request(weekStart: string | undefined, studentId = this.studentId()): void {
    this.lastWeek = weekStart;
    this.requests.next({ studentId, weekStart });
  }

  private fetch(studentId: number, weekStart?: string): Observable<unknown> {
    return defer(() => {
      this.loading.set(true);
      this.error.set(null);
      this.locked.set(false);
      return this.dashboardService.getChildWeeklyReport(studentId, weekStart).pipe(
        tap((r) => {
          this.report.set(r);
          if (!weekStart && !this.latestWeek()) this.latestWeek.set(r.weekStart);
        }),
        catchError((err: HttpErrorResponse) => {
          this.report.set(null);
          if (err?.status === 403) {
            this.locked.set(true);
          } else if (err?.status === 404) {
            this.error.set(this.text('summaryNotFound'));
            this.notFound.emit();
          } else {
            this.error.set(this.text('report.loadError'));
          }
          return EMPTY;
        }),
        finalize(() => this.loading.set(false))
      );
    });
  }

  private text(key: string): string {
    return this.transloco.translate<string>(`${SCOPE}.${key}`) ?? '';
  }
}
