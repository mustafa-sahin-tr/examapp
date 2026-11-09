import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, finalize, of, shareReplay, tap } from 'rxjs';

import { CurrentSubscriptionPlan, FeatureUsage } from '../models/subscription';
import { AuthService } from './auth.service';
import { SubscriptionService } from './subscription.service';

/**
 * Issue #1087: kullanıcının planındaki özellikler (`GET subscription-plans/me`), uygulama boyunca tek kopya. Menü, rota ve
 * düğmeler "bu özellik planımda var mı" sorusunu buradan sorar; asıl kapı sunucudur (403 `entitlement.notInPlan`).
 * Rol muafiyetleri (içerik ekibi, admin) sunucunun hesapladığı sınıra zaten yansır.
 */
@Injectable({ providedIn: 'root' })
export class EntitlementStateService {
  private readonly subscriptions = inject(SubscriptionService);
  private readonly auth = inject(AuthService);

  private readonly current = signal<CurrentSubscriptionPlan | null>(null);
  /** Yüklenemezse (ağ hatası) kapı açık kalır: sunucu karar verir. */
  private readonly failed = signal(false);
  private inflight: Observable<CurrentSubscriptionPlan | null> | null = null;

  readonly plan = this.current.asReadonly();
  readonly loaded = computed(() => this.current() !== null || this.failed());

  /** Planı bir kez yükler (eş zamanlı çağrılar tek istek paylaşır); `force` ile yeniler (plan değişince). */
  load(force = false): Observable<CurrentSubscriptionPlan | null> {
    if (!force && this.loaded()) return of(this.current());
    if (!force && this.inflight) return this.inflight;
    this.inflight = this.subscriptions.current().pipe(
      tap((plan) => {
        this.current.set(plan);
        this.failed.set(false);
      }),
      catchError(() => {
        this.failed.set(true);
        return of(null);
      }),
      finalize(() => (this.inflight = null)),
      shareReplay(1),
    );
    return this.inflight;
  }

  /** Plan değişti (deneme, ödeme): sunucunun döndürdüğü yeni plan kapıları hemen açar. */
  set(plan: CurrentSubscriptionPlan): void {
    this.current.set(plan);
    this.failed.set(false);
  }

  usage(feature: string): FeatureUsage | null {
    return this.current()?.features.find((f) => f.featureKey === feature) ?? null;
  }

  /**
   * Özellik planda var mı (sınır 0 değil). Plan yüklenmediyse ya da yüklenemediyse true (sunucu kapısı geçerli).
   * Rolün kitlesinde olmayan özellik listede yoktur: admin için true, diğerleri için false.
   */
  has(feature: string): boolean {
    const plan = this.current();
    if (!plan) return true;
    const usage = plan.features.find((f) => f.featureKey === feature);
    if (!usage) return this.auth.hasRealmRole('Admin');
    return usage.limit !== 0;
  }
}
