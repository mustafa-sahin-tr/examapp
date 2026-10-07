import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { EMPTY, Observable, Subject, Subscription, catchError, defer, finalize, map, switchMap, tap } from 'rxjs';

import { LinkedChild } from '../../models/parent-link.model';
import { PARENT_CHILD_QUERY_PARAM, ParentChildSummary } from '../../models/parent-dashboard.model';
import { ParentDashboardService } from '../../services/parent-dashboard.service';
import { LocaleService } from '../../services/locale.service';
import { ParentLinkService } from '../../services/parent-link.service';
import { ParentChildAssignmentsComponent } from './child-assignments/parent-child-assignments.component';

/** Veli paneli ekranlarının Transloco scope'u (`public/i18n/parent-dashboard/<lang>.json`). */
export const PARENT_DASHBOARD_SCOPE = 'parent-dashboard';

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
 */
@Component({
  selector: 'app-parent-dashboard',
  standalone: true,
  imports: [
    MatButtonModule,
    MatCardModule,
    MatFormFieldModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    ParentChildAssignmentsComponent,
    RouterLink,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(PARENT_DASHBOARD_SCOPE)],
  templateUrl: './parent-dashboard.component.html',
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
  /** 404 sonrası (bağlantı kaldırılmış olabilir) liste yenilenince gösterilen bilgi notu; seçim değişince kalkar. */
  protected readonly notice = signal<string | null>(null);

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

  ngOnInit(): void {
    this.summaryRequests
      .pipe(
        switchMap((id) => this.fetchSummary(id)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();

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

  /** Ödev listesi 404 döndü: bağlantı kaldırılmış olabilir — özetteki 404 ile aynı davranış (not + listeyi yenile). */
  protected onChildNotFound(): void {
    this.notice.set(this.text('summaryNotFound'));
    this.loadChildren();
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

  /** Haftanın Pazartesi'si ("yyyy-MM-dd") aktif dilde; saat dilimi kayması olmadan. */
  protected formatWeekStart(value: string): string {
    const date = parseLocalDate(value);
    return date ? new Intl.DateTimeFormat(this.locale(), { day: 'numeric', month: 'long' }).format(date) : '';
  }

  /**
   * Son aktivite (sunucu saate kesilmiş UTC gönderir): "Bugün / Dün / <tarih>" + yaklaşık saat (yerel, "14:00").
   * Dakika gösterilmez.
   */
  protected formatLastActivity(iso: string | null): string {
    if (!iso) return this.text('cards.lastActivity.none');
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return this.text('cards.lastActivity.none');

    const locale = this.locale();
    const time = new Intl.DateTimeFormat(locale, { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(
      new Date(date.getFullYear(), date.getMonth(), date.getDate(), date.getHours())
    );
    const dayDiff = Math.round((startOfDay(this.now()).getTime() - startOfDay(date).getTime()) / 86_400_000);
    if (dayDiff === 0) return this.text('cards.lastActivity.today', { time });
    if (dayDiff === 1) return this.text('cards.lastActivity.yesterday', { time });
    const day = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'long', year: 'numeric' }).format(date);
    return this.text('cards.lastActivity.onDate', { date: day, time });
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
