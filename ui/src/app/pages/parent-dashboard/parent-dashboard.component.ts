import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, OnInit, computed, inject, signal, viewChild, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTabsModule } from '@angular/material/tabs';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { EMPTY, Observable, Subject, Subscription, catchError, defer, finalize, map, of, switchMap, tap } from 'rxjs';

import { LinkedChild } from '../../models/parent-link.model';
import { PARENT_CHILD_QUERY_PARAM, PARENT_TAB_QUERY_PARAM, ParentChildSummary } from '../../models/parent-dashboard.model';
import { ParentDashboardService } from '../../services/parent-dashboard.service';
import { LocaleService } from '../../services/locale.service';
import { ParentLinkService } from '../../services/parent-link.service';
import { ParentBookingRequestsComponent } from './booking-requests/parent-booking-requests.component';
import { ParentRescheduleRequestsComponent } from './reschedule-requests/parent-reschedule-requests.component';
import { ParentChildAssignmentsComponent } from './child-assignments/parent-child-assignments.component';
import { ParentChildProgressComponent } from './child-progress/parent-child-progress.component';
import { ParentChildScheduleComponent } from './child-schedule/parent-child-schedule.component';
import { ParentChildPlanComponent } from './child-plan/parent-child-plan.component';
import { OutcomeMasteryCardComponent } from '../../shared/components/outcome-mastery-card/outcome-mastery-card.component';
import { TargetGoalCardComponent } from '../../shared/components/target-goal-card/target-goal-card.component';
import { CoachGoalsCardComponent } from '../../shared/components/coach-goals-card/coach-goals-card.component';
import { WeakTopicsCardComponent } from '../../shared/components/weak-topics-card/weak-topics-card.component';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';

/** Veli paneli ekranlarının Transloco scope'u (`public/i18n/parent-dashboard/<lang>.json`). */
export const PARENT_DASHBOARD_SCOPE = 'parent-dashboard';

/** #883: ayrıntı sekmelerinin sırası (şablondaki `mat-tab` sırasıyla aynı). */
export const PARENT_DASHBOARD_TABS = { progress: 0, outcomes: 1, goal: 2, schedule: 3, assignments: 4, plan: 5 } as const;

/** Bağlı çocuk + özet anahtarı (yalnızca Active ve studentId dolu satırlar). */
type ActiveChild = LinkedChild & { studentId: number };

/**
 * "yyyy-MM-dd" (saat dilimsiz takvim günü) → YEREL gece yarısı. `new Date('2026-10-05')` UTC gece yarısı olarak çözülür ve
 * UTC'nin gerisindeki bölgelerde bir önceki güne kayar; bileşenlerden kurmak her bölgede aynı günü verir.
 */
export function parseLocalDate(value: string | null | undefined): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value ?? '');
  if (!match) return null;
  const date = new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
  return Number.isNaN(date.getTime()) ? null : date;
}

/** `?child=` değerini pozitif tamsayıya çevirir; geçersizse null. */
export function parseChildParam(value: string | null): number | null {
  if (!value || !/^\d+$/.test(value)) return null;
  const id = Number(value);
  return Number.isSafeInteger(id) && id > 0 ? id : null;
}

/**
 * Issue #420 (epic #407 V2): veli paneli iskeleti. Aktif (onaylanmış) çocuklar arasından seçim — varsayılan ilk çocuk,
 * seçim `?child=<studentId>` ile URL'de hatırlanır (yenileme/geri tuşu aynı çocuğu açar; geçersiz/başkasının id'si ilk
 * çocuğa düşer). Seçili çocuğun özet kartları: bu hafta çözülen soru, ödev durumları, toplam puan, son aktivite. Bağlı
 * çocuk yoksa "Çocuk ekle" ile Çocuklarım sayfasına yönlendirir. Sunucu yalnızca toplam döner (içerik/iletişim bilgisi yok).
 * Issue #421 (V3): aynı seçili çocuk için "Ödevler ve testler" bölümü (`app-parent-child-assignments`).
 * Issue #422 (V4): "Puan ve rozetler" kartı (`app-parent-child-progress`) ve haftalık "Program" (`app-parent-child-schedule`).
 * Issue #439: "Onay bekleyen ders talepleri" (`app-parent-booking-requests`) — seçili çocuktan bağımsız, tüm Active çocuklar.
 * Issue #883: özet kartları yerine haftanın tek cümlesi + sayı şeridi, "Dikkat" listesi (geciken ödev, zorlandığı konu) ve
 * alt alta yedi bölüm yerine sekmeler (içerik sekme açılınca yüklenir). Birden çok çocukta seçici çip satırı.
 */
@Component({
  selector: 'app-parent-dashboard',
  standalone: true,
  imports: [
    PageHeaderComponent,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTabsModule,
    ParentBookingRequestsComponent,
    ParentRescheduleRequestsComponent,
    ParentChildAssignmentsComponent,
    ParentChildProgressComponent,
    ParentChildScheduleComponent,
    ParentChildPlanComponent,
    OutcomeMasteryCardComponent, TargetGoalCardComponent, CoachGoalsCardComponent, WeakTopicsCardComponent,
    RouterLink,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(PARENT_DASHBOARD_SCOPE)],
  templateUrl: './parent-dashboard.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./parent-dashboard.component.scss'],
})
export class ParentDashboardComponent implements OnInit {
  private readonly linkService = inject(ParentLinkService);
  private readonly dashboardService = inject(ParentDashboardService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly localeService = inject(LocaleService);

  protected readonly childrenLoading = signal(true);
  protected readonly childrenError = signal<string | null>(null);
  protected readonly children = signal<ActiveChild[]>([]);
  /** Onay bekleyen istek var mı (boş durumda bilgi notu). */
  protected readonly hasPending = signal(false);
  protected readonly selectedId = signal<number | null>(null);

  protected readonly summary = signal<ParentChildSummary | null>(null);
  protected readonly summaryLoading = signal(false);
  protected readonly summaryError = signal<string | null>(null);
  /**
   * Issue #1089: çocuğun planında ayrıntılı analiz var mı (zayıf konular, kazanım haritası). Okunana dek (null) kartlar istek
   * atmaz; okunamazsa açık kalır (sunucu karar verir).
   */
  protected readonly childAnalytics = signal<boolean | null>(null);
  /** 404 sonrası (bağlantı kaldırılmış olabilir) liste yenilenince gösterilen bilgi notu; seçim değişince kalkar. */
  protected readonly notice = signal<string | null>(null);

  protected readonly tabs = PARENT_DASHBOARD_TABS;
  /** Açık ayrıntı sekmesi; çocuk değişince korunur. */
  protected readonly tab = signal<number>(PARENT_DASHBOARD_TABS.progress);
  private readonly detailTabs = viewChild('detailTabs', { read: ElementRef });

  /** "Şimdi" — testte sabitlenebilir (bugün/dün ayrımı). */
  protected now: () => Date = () => new Date();

  protected readonly selectedChild = computed(
    () => this.children().find((c) => c.studentId === this.selectedId()) ?? null
  );
  protected readonly isEmpty = computed(
    () => !this.childrenLoading() && !this.childrenError() && this.children().length === 0
  );

  /** URL'de istenen çocuk (son okunan `?child=`). */
  private requestedId: number | null = null;
  private childrenLoaded = false;
  /** Süren çocuk listesi isteği (çift yenilemeyi önler). */
  private childrenRequest?: Subscription;
  private readonly summaryRequests = new Subject<number>();
  /** Issue #1089 / #1091: çocuğun plan özellikleri (çocuk seçilince ve plan alınınca). */
  private readonly planFeatureRequests = new Subject<number>();

  ngOnInit(): void {
    this.summaryRequests
      .pipe(
        switchMap((id) => this.fetchSummary(id)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();

    this.summaryRequests.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id) => this.planFeatureRequests.next(id));
    this.planFeatureRequests
      .pipe(
        switchMap((id) => {
          this.childAnalytics.set(null);
          return this.dashboardService.getChildPlanFeatures(id).pipe(
            map((plan) => plan.features['analytics'] !== false),
            catchError(() => of(true))
          );
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((analytics) => this.childAnalytics.set(analytics));

    // Issue #1091: `?tab=plan` (bildirimden) ilgili sekmeyi açar.
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const tab = params.get(PARENT_TAB_QUERY_PARAM);
      if (tab && tab in PARENT_DASHBOARD_TABS) this.tab.set(PARENT_DASHBOARD_TABS[tab as keyof typeof PARENT_DASHBOARD_TABS]);
    });

    this.route.queryParamMap
      .pipe(
        map((params) => parseChildParam(params.get(PARENT_CHILD_QUERY_PARAM))),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((id) => {
        this.requestedId = id;
        this.resolveSelection();
      });

    this.loadChildren();
  }

  protected loadChildren(): void {
    // m5: özet ve ödev listesi aynı anda 404 alırsa ikisi de yenileme ister — süren istek varken ikincisi açılmaz.
    if (this.childrenRequest && !this.childrenRequest.closed) return;
    this.childrenLoading.set(true);
    this.childrenError.set(null);
    this.childrenRequest = this.linkService
      .getMyChildren()
      .pipe(
        finalize(() => this.childrenLoading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (items) => {
          const list = items ?? [];
          this.children.set(
            list.filter((c): c is ActiveChild => c.status === 'Active' && typeof c.studentId === 'number')
          );
          this.hasPending.set(list.some((c) => c.status === 'Pending'));
          this.childrenLoaded = true;
          this.resolveSelection();
        },
        error: (err: HttpErrorResponse) =>
          this.childrenError.set(this.linkService.extractError(err, this.text('childrenLoadError'))),
      });
  }

  /** Seçiciden: URL güncellenir, seçim query param akışından çözülür (tek kaynak). */
  protected selectChild(studentId: number): void {
    if (studentId === this.selectedId()) return;
    this.notice.set(null);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { [PARENT_CHILD_QUERY_PARAM]: studentId },
      queryParamsHandling: 'merge',
    });
  }

  /** Issue #1091: veli çocuğa plan aldı — planın açtığı bölümler (analiz) yeniden okunur. */
  protected onPlanChanged(): void {
    const id = this.selectedId();
    if (id != null) this.planFeatureRequests.next(id);
  }

  /** Bir bölüm (ödevler, puan, program) 404 döndü: bağlantı kaldırılmış olabilir — özetteki 404 ile aynı davranış (not + listeyi yenile). */
  protected onChildNotFound(): void {
    this.notice.set(this.text('summaryNotFound'));
    this.loadChildren();
  }

  /** "Dikkat" satırından: ilgili sekmeyi açar ve sekmelere kaydırır. */
  protected openTab(index: number): void {
    this.tab.set(index);
    const el = this.detailTabs()?.nativeElement as HTMLElement | undefined;
    setTimeout(() => el?.scrollIntoView?.({ behavior: 'smooth', block: 'start' }));
  }

  /** "Ayşe bu hafta 4 gün çalıştı ve 62 soru çözdü." — ad yalnız ilk kelime (sıcak, kısa). */
  protected weekSentence(s: ParentChildSummary): string {
    const name = (this.selectedChild()?.studentName ?? '').trim().split(/\s+/)[0];
    if (s.questionsSolvedThisWeek === 0) return this.text('week.sentenceNone', { name });
    // Eski API gün sayısı vermez: en az bir gün çalışılmıştır.
    const days = Math.max(1, s.activeDaysThisWeek ?? 1);
    return this.text('week.sentence', { name, days, count: s.questionsSolvedThisWeek });
  }

  /** Bu hafta cevaplananlarda doğru oranı; cevap yoksa ya da eski API ise "—". */
  protected correctPercent(s: ParentChildSummary): string {
    if (s.correctThisWeek == null || s.questionsSolvedThisWeek === 0) return '—';
    return this.percent(Math.round((s.correctThisWeek * 100) / s.questionsSolvedThisWeek));
  }

  /** 0–100 → dile göre yüzde ("%71" / "71%"). */
  protected percent(value: number): string {
    return new Intl.NumberFormat(this.locale(), { style: 'percent', maximumFractionDigits: 0 }).format(value / 100);
  }

  protected attentionCount(s: ParentChildSummary): number {
    return (s.assignments.overdue > 0 ? 1 : 0) + (s.attention?.weakTopic ? 1 : 0);
  }

  /** "5 – 11 Ekim": haftanın Pazartesi'si → Pazar. */
  protected weekRange(weekStart: string): string {
    const start = parseLocalDate(weekStart);
    if (!start) return '';
    const end = new Date(start.getFullYear(), start.getMonth(), start.getDate() + 6);
    return new Intl.DateTimeFormat(this.locale(), { day: 'numeric', month: 'long' }).formatRange(start, end);
  }

  protected retrySummary(): void {
    const id = this.selectedId();
    if (id !== null) this.summaryRequests.next(id);
  }

  /**
   * İstenen çocuk aktif listede varsa o, yoksa ilk çocuk. URL istenenden farklıysa (yok/geçersiz/artık bağlı değil)
   * geçmişe yeni kayıt eklemeden düzeltilir.
   */
  private resolveSelection(): void {
    if (!this.childrenLoaded) return;
    const list = this.children();
    if (list.length === 0) {
      this.selectedId.set(null);
      this.summary.set(null);
      return;
    }

    const requested = this.requestedId;
    const id = list.some((c) => c.studentId === requested) ? requested! : list[0].studentId;
    if (id !== this.selectedId()) {
      this.selectedId.set(id);
      this.summaryRequests.next(id);
    }
    if (requested !== id) {
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { [PARENT_CHILD_QUERY_PARAM]: id },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    }
  }

  private fetchSummary(studentId: number): Observable<unknown> {
    return defer(() => {
      this.summaryLoading.set(true);
      this.summaryError.set(null);
      this.summary.set(null);
      return this.dashboardService.getChildSummary(studentId).pipe(
        tap((summary) => this.summary.set(summary)),
        catchError((err: HttpErrorResponse) => {
          if (err?.status === 404) {
            // Bağlantı bu arada kaldırılmış olabilir: listeyi yenile (seçim ilk aktif çocuğa ya da boş duruma düşer).
            this.summaryError.set(this.text('summaryNotFound'));
            this.notice.set(this.text('summaryNotFound'));
            this.loadChildren();
          } else {
            this.summaryError.set(this.text('summaryLoadError'));
          }
          return EMPTY;
        }),
        finalize(() => this.summaryLoading.set(false))
      );
    });
  }

  /**
   * Son aktivite (sunucu saate kesilmiş UTC gönderir): "Bugün / Dün / <tarih>" + yaklaşık saat (yerel, "14:00").
   * Dakika gösterilmez.
   */
  protected formatLastActivity(iso: string | null): string {
    if (!iso) return this.text('week.lastActivity.none');
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return this.text('week.lastActivity.none');

    const locale = this.locale();
    const time = new Intl.DateTimeFormat(locale, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(
      new Date(date.getFullYear(), date.getMonth(), date.getDate(), date.getHours())
    );
    const dayDiff = Math.round((startOfDay(this.now()).getTime() - startOfDay(date).getTime()) / 86_400_000);
    if (dayDiff === 0) return this.text('week.lastActivity.today', { time });
    if (dayDiff === 1) return this.text('week.lastActivity.yesterday', { time });
    const day = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'long', year: 'numeric' }).format(date);
    return this.text('week.lastActivity.onDate', { date: day, time });
  }

  private locale(): string {
    return this.localeService.localeDefinition().angularLocale;
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${PARENT_DASHBOARD_SCOPE}.${key}`, params) ?? '';
  }
}

/** Yerel gece yarısı (gün farkı hesabı). */
function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}
