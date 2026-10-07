import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { EMPTY, Observable, Subject, catchError, defer, finalize, switchMap, tap } from 'rxjs';

import { ParentChildSchedule, ParentLessonItem, ParentPlanItem } from '../../../models/parent-dashboard.model';
import { LocaleService } from '../../../services/locale.service';
import { ParentDashboardService } from '../../../services/parent-dashboard.service';
import { dayInTimeZone, formatTime, parseDay, toDayString } from '../parent-format';

/** Veli paneli Transloco scope'u (`public/i18n/parent-dashboard/<lang>.json`). */
const SCOPE = 'parent-dashboard';

/** Görüntülenen aralık ("yyyy-MM-dd", iki uç dahil). */
export interface ScheduleRange {
  from: string;
  to: string;
}

interface ScheduleRequest {
  studentId: number;
  /** Boş = bu hafta (sunucu Europe/Istanbul'a göre belirler). */
  range?: ScheduleRange;
}

/** Haftalık listenin bir günü. */
export interface ScheduleDay {
  date: string;
  isToday: boolean;
  plans: ParentPlanItem[];
  lessons: ParentLessonItem[];
}

/** Şablonun gösterdiği biçimlendirilmiş gün. */
interface DayView {
  date: string;
  label: string;
  isToday: boolean;
  plans: ParentPlanItem[];
  lessons: { teacherName: string | null; time: string; status: ParentLessonItem['status'] }[];
}

/** `from` gününden başlayarak `days` gün kaydırılmış hafta (Pazartesi–Pazar). */
export function shiftWeek(range: ScheduleRange, days: number): ScheduleRange | null {
  const from = parseDay(range.from);
  if (!from) return null;
  const start = new Date(from.getFullYear(), from.getMonth(), from.getDate() + days);
  const end = new Date(start.getFullYear(), start.getMonth(), start.getDate() + 6);
  return { from: toDayString(start), to: toDayString(end) };
}

/**
 * Plan ve dersleri aralıktaki günlere dağıtır (her gün bir satır, boş günler dahil). Gün anahtarları sunucunun Europe/Istanbul
 * günleridir: plan `plannedOn`, ders `startsOn` (aralıktan önce başlayıp taşan ders aralığın ilk günü) — tarayıcının saat
 * dilimi gruplamayı kaydırmaz. `todayKey` de Europe/Istanbul günüdür.
 */
export function buildDays(schedule: ParentChildSchedule, todayKey: string): ScheduleDay[] {
  const from = parseDay(schedule.from);
  const to = parseDay(schedule.to);
  if (!from || !to) return [];
  const days: ScheduleDay[] = [];
  for (let d = from; d <= to; d = new Date(d.getFullYear(), d.getMonth(), d.getDate() + 1)) {
    const key = toDayString(d);
    days.push({
      date: key,
      isToday: key === todayKey,
      plans: schedule.plans.filter((p) => p.plannedOn === key),
      lessons: schedule.lessons
        .filter((l) => l.startsOn === key)
        .sort((a, b) => new Date(a.startAt).getTime() - new Date(b.startAt).getTime()),
    });
  }
  return days;
}

/**
 * Issue #422 (epic #407 V4): veli panelinde seçili çocuğun "Program" bölümü — haftalık liste (Pazartesi–Pazar): "Planım"
 * planları (worksheet adı + gün) ve bekleyen/onaylanan ders randevuları (öğretmen, saat, durum). Saatler ve günler Europe/Istanbul.
 * Önceki/sonraki hafta düğmeleri; ilk açılışta ve çocuk değişince sunucunun "bu hafta"sı. Salt okunur — ders bağlantısı,
 * ücret ya da not gösterilmez. 404 (bağlantı kaldırılmış olabilir) üst bileşene `notFound` ile bildirilir.
 */
@Component({
  selector: 'app-parent-child-schedule',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule, TranslocoDirective],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './parent-child-schedule.component.html',
  styleUrls: ['./parent-child-schedule.component.scss'],
})
export class ParentChildScheduleComponent {
  readonly studentId = input.required<number>();
  /** Uç 404 döndü (bağlantı kaldırılmış olabilir). */
  readonly notFound = output<void>();

  private readonly dashboardService = inject(ParentDashboardService);
  private readonly transloco = inject(TranslocoService);
  private readonly localeService = inject(LocaleService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly schedule = signal<ParentChildSchedule | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  /** Görüntülenen hafta (son başarılı yanıttan); ilk yükleme bitene kadar null. */
  protected readonly range = signal<ScheduleRange | null>(null);
  /** Sunucunun "bu hafta"sı (ilk yanıttan) — "Bu hafta" düğmesi için. */
  protected readonly currentWeek = signal<ScheduleRange | null>(null);

  /** "Şimdi" — testte sabitlenebilir (bugün vurgusu). */
  protected now: () => Date = () => new Date();

  private readonly locale = computed(() => this.localeService.localeDefinition().angularLocale);

  protected readonly days = computed<DayView[]>(() => {
    const s = this.schedule();
    if (!s) return [];
    const locale = this.locale();
    const dayFormat = new Intl.DateTimeFormat(locale, { weekday: 'long', day: 'numeric', month: 'long' });
    return buildDays(s, dayInTimeZone(this.now())).map((d) => ({
      date: d.date,
      label: dayFormat.format(parseDay(d.date)!),
      isToday: d.isToday,
      plans: d.plans,
      lessons: d.lessons.map((l) => ({
        teacherName: l.teacherName,
        time: `${formatTime(l.startAt, locale)}–${formatTime(l.endAt, locale)}`,
        status: l.status,
      })),
    }));
  });
  protected readonly isEmpty = computed(() => {
    const s = this.schedule();
    return !!s && s.plans.length === 0 && s.lessons.length === 0;
  });
  protected readonly isCurrentWeek = computed(() => {
    const current = this.currentWeek();
    const shown = this.range();
    return !!current && !!shown && current.from === shown.from;
  });
  /** Aralık başlığının parametreleri (çeviri şablonda `t('schedule.range', ...)` ile — dil değişimini izler). */
  protected readonly rangeParts = computed<{ from: string; to: string } | null>(() => {
    const r = this.range();
    const from = parseDay(r?.from);
    const to = parseDay(r?.to);
    if (!from || !to) return null;
    const locale = this.locale();
    const short = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'long' });
    const long = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'long', year: 'numeric' });
    return { from: short.format(from), to: long.format(to) };
  });

  private readonly requests = new Subject<ScheduleRequest>();
  private lastRequest: ScheduleRequest | null = null;

  constructor() {
    this.requests
      .pipe(
        switchMap((request) => this.fetch(request)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();

    // Çocuk değişince bu haftaya dönülür. Yalnızca studentId izlenir; gövde untracked.
    effect(() => {
      const studentId = this.studentId();
      untracked(() => {
        this.range.set(null);
        this.currentWeek.set(null);
        this.request({ studentId });
      });
    });
  }

  protected previousWeek(): void {
    this.move(-7);
  }

  protected nextWeek(): void {
    this.move(7);
  }

  protected thisWeek(): void {
    const current = this.currentWeek();
    this.request({ studentId: this.studentId(), range: current ?? undefined });
  }

  protected reload(): void {
    this.request(this.lastRequest ?? { studentId: this.studentId() });
  }

  private move(days: number): void {
    const shown = this.range();
    if (!shown) return;
    const next = shiftWeek(shown, days);
    if (next) this.request({ studentId: this.studentId(), range: next });
  }

  private request(request: ScheduleRequest): void {
    this.lastRequest = request;
    this.requests.next(request);
  }

  private fetch(request: ScheduleRequest): Observable<unknown> {
    return defer(() => {
      this.loading.set(true);
      this.error.set(null);
      return this.dashboardService.getChildSchedule(request.studentId, request.range).pipe(
        tap((s) => {
          this.schedule.set(s);
          const shown = { from: s.from, to: s.to };
          this.range.set(shown);
          if (!request.range && !this.currentWeek()) this.currentWeek.set(shown);
        }),
        catchError((err: HttpErrorResponse) => {
          this.schedule.set(null);
          if (err?.status === 404) {
            this.error.set(this.text('summaryNotFound'));
            this.notFound.emit();
          } else if (err?.status === 400) {
            // Sunucunun aralık sınırı (bugünden en fazla bir yıl) aşıldı.
            this.error.set(this.text('schedule.rangeError'));
          } else {
            this.error.set(this.text('schedule.loadError'));
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
