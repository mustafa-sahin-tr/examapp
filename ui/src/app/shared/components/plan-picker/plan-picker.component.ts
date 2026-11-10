import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, forkJoin, map } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import {
  ChildSubscriptionPlan,
  CurrentSubscriptionPlan,
  EntitlementFeature,
  FREE_PLAN_KEY,
  PLAN_DURATIONS,
  PlanDuration,
  PlanScope,
  StudentPlanOffer,
  TRIAL_PLAN_KEY,
  SubscriptionPlan,
  planNameKey,
} from '../../../models/subscription';
import { SubscriptionService } from '../../../services/subscription.service';
import { EntitlementStateService } from '../../../services/entitlement-state.service';

const SCOPE = 'subscription';

export interface PlanFeatureLine {
  key: string;
  kind: EntitlementFeature['kind'];
  unit: string | null;
  limit: number | null;
}

/** Issue #1108: ödeme ekranında bir süre seçeneği; `price` süre boyunca toplam (indirimsiz). */
export interface DurationOption {
  duration: PlanDuration;
  price: number;
  /** İndirim sonrası. */
  finalPrice: number;
  months: number | null;
}

const DURATION_MONTHS: Record<PlanDuration, number | null> = { Monthly: 1, SixMonths: 6, TwelveMonths: 12, Summer: null };

/** Kart düğmesinin türü: Free (kayıtta atla), deneme, ücretli (sahte ödeme). */
export type PlanAction = 'free' | 'trial' | 'paid';

/**
 * Issue #1028 (epic #1026): plan kartları + deneme başlatma + sahte ödeme. Kayıt sihirbazının son adımı (`signup`: Free kartı
 * "Ücretsiz devam et" ile atlar) ve "Planım"dan yükseltme (`change`: mevcut plan işaretli, Free'ye dönüş yok — iptal kapsam dışı).
 * Ödeme ekranı açıkça demodur: kart alanı yok, tek "Ödemeyi tamamla (demo)" düğmesi.
 * Issue #1091: öğrencinin planını velisi alır — öğrencide (`parentPays`) ücretli kartlarda düğme yerine "velin seçer" notu; veli
 * panelinde (`childId`) çocuğun planı ve öğrenci planları gösterilir, ödeme çocuk adına yapılır.
 * Issue #1108: veli ödeme ekranında kapsamı (bütün dersler / tek ders), dersi ve süreyi seçer; fiyat çocuğun sınıf bandından,
 * kardeş indirimi varsa düşülmüş gösterilir. Çocuğun sınıfı yoksa seçenek yoktur (aylık, bütün dersler).
 */
@Component({
  selector: 'app-plan-picker',
  standalone: true,
  imports: [CurrencyPipe, MatButtonModule, MatFormFieldModule, MatIconModule, MatRadioModule, MatSelectModule, TranslocoDirective],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './plan-picker.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './plan-picker.component.scss',
})
export class PlanPickerComponent implements OnInit {
  private readonly subscriptions = inject(SubscriptionService);
  private readonly entitlements = inject(EntitlementStateService);

  readonly mode = input<'signup' | 'change'>('signup');
  /** Issue #1091: öğrenci kendine plan alamaz; ücretli kartlar velisine yönlendirir. */
  readonly parentPays = input(false);
  /** Issue #1091: veli panelinde çocuğun (Students.Id) planı; doluysa katalog ve ödeme çocuğa aittir. */
  readonly childId = input<number | null>(null);
  /** Plan seçildi (deneme / ödeme sonrası yeni plan) ya da Free / kendiliğinden başlayan deneme ile devam edildi (null). */
  readonly completed = output<CurrentSubscriptionPlan | null>();

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly plans = signal<SubscriptionPlan[]>([]);
  /** #1093: özellik sırası, türü ve birimi. */
  readonly features = signal<EntitlementFeature[]>([]);
  readonly current = signal<CurrentSubscriptionPlan | null>(null);
  /** Sahte ödeme ekranındaki plan; null → kartlar. */
  readonly checkoutPlan = signal<SubscriptionPlan | null>(null);
  readonly busy = signal(false);
  /** `subscription` scope'unda hata anahtarı. */
  readonly actionError = signal<string | null>(null);

  /** Issue #1108: çocuğun bandı ve fiyat seçenekleri (veli panelinde). */
  readonly offer = signal<StudentPlanOffer | null>(null);
  readonly scope = signal<PlanScope>('AllSubjects');
  readonly subjectId = signal<number | null>(null);
  readonly duration = signal<PlanDuration>('Monthly');

  /** Ödeme ekranındaki planın band fiyatları; band yoksa boş (seçenek gösterilmez). */
  private readonly planPrices = computed(() => {
    const plan = this.checkoutPlan();
    return plan ? (this.offer()?.prices ?? []).filter((p) => p.planKey === plan.key) : [];
  });
  readonly hasOptions = computed(() => this.planPrices().length > 0);
  readonly singleSubjectOnSale = computed(
    () => (this.offer()?.subjects.length ?? 0) > 0 && this.planPrices().some((p) => p.scope === 'SingleSubject'),
  );
  readonly discountPercent = computed(() => this.offer()?.siblingDiscountPercent ?? 0);
  readonly durationOptions = computed<DurationOption[]>(() => {
    const scope = this.scope();
    const summer = this.offer()?.summerOnSale ?? false;
    const discount = this.discountPercent();
    return PLAN_DURATIONS.flatMap((duration) => {
      const row = this.planPrices().find((p) => p.scope === scope && p.duration === duration);
      if (!row || (duration === 'Summer' && !summer)) return [];
      return [{ duration, price: row.amount, finalPrice: discounted(row.amount, discount), months: DURATION_MONTHS[duration] }];
    });
  });
  readonly selectedOption = computed(() => this.durationOptions().find((o) => o.duration === this.duration()) ?? null);
  /** Seçenekli ödemede tek ders seçildiyse ders de seçilmiş olmalı. */
  readonly canComplete = computed(
    () => !this.hasOptions() || (this.selectedOption() != null && (this.scope() === 'AllSubjects' || this.subjectId() != null)),
  );

  readonly currentKey = computed(() => this.current()?.planKey ?? null);
  /** Issue #1090: plan kilidi açıkken katalogda Free yoktur; açıklama ona göre değişir. */
  readonly offersFree = computed(() => this.plans().some((p) => p.key === FREE_PLAN_KEY));

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);
    const childId = this.childId();
    const source: Observable<{ plans: SubscriptionPlan[]; current: CurrentSubscriptionPlan; features: EntitlementFeature[] }> =
      childId != null
        ? this.subscriptions.childPlan(childId).pipe(
            map((child) => {
              this.offer.set(child.offer ?? null);
              return { plans: child.plans, current: asCurrent(child), features: child.features };
            }),
          )
        : forkJoin({
            plans: this.subscriptions.plans(),
            current: this.subscriptions.current(),
            features: this.subscriptions.features(),
          });
    source.subscribe({
      next: ({ plans, current, features }) => {
        this.plans.set(plans);
        this.features.set(features);
        this.current.set(current);
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set(true);
        this.loading.set(false);
      },
    });
  }

  actionOf(plan: SubscriptionPlan): PlanAction {
    if (plan.trialDays != null) return 'trial';
    return plan.monthlyPrice > 0 ? 'paid' : 'free';
  }

  /** Kartta listelenen özellikler: planda olanlar (sınırı 0 olmayan), özellik listesinin sırasıyla. */
  featuresOf(plan: SubscriptionPlan): PlanFeatureLine[] {
    return this.features()
      .filter((f) => f.key in plan.monthlyLimits && plan.monthlyLimits[f.key] !== 0)
      .map((f) => ({ key: f.key, kind: f.kind, unit: f.unit, limit: plan.monthlyLimits[f.key] }));
  }

  nameKey(plan: SubscriptionPlan): string {
    return planNameKey(plan.displayNameKey);
  }

  continueFree(): void {
    this.completed.emit(null);
  }

  startTrial(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);
    this.subscriptions.startTrial().subscribe({
      next: (plan) => this.finish(plan),
      error: (err: unknown) => this.fail(err),
    });
  }

  openCheckout(plan: SubscriptionPlan): void {
    this.actionError.set(null);
    this.scope.set('AllSubjects');
    this.subjectId.set(null);
    this.duration.set('Monthly');
    this.checkoutPlan.set(plan);
  }

  setScope(scope: PlanScope): void {
    this.scope.set(scope);
    if (scope === 'AllSubjects') this.subjectId.set(null);
    // Seçili süre bu kapsamda satılmıyorsa aylığa dön.
    if (!this.selectedOption()) this.duration.set(this.durationOptions()[0]?.duration ?? 'Monthly');
  }

  /** Süre boyunca toplamın aylık karşılığı (6 / 12 ay); aylıkta ve yazda null. */
  perMonth(option: DurationOption): number | null {
    return option.months && option.months > 1 ? Math.round(option.finalPrice / option.months) : null;
  }

  closeCheckout(): void {
    this.actionError.set(null);
    this.checkoutPlan.set(null);
  }

  completeCheckout(): void {
    const plan = this.checkoutPlan();
    if (!plan || this.busy() || !this.canComplete()) return;
    this.busy.set(true);
    this.actionError.set(null);
    const childId = this.childId();
    const request = childId != null
      ? this.subscriptions
          .childCheckout(childId, plan.key, this.hasOptions() ? { subjectId: this.subjectId(), duration: this.duration() } : undefined)
          .pipe(
            map((child) => {
              this.offer.set(child.offer ?? null);
              return asCurrent(child);
            }),
          )
      : this.subscriptions.checkout(plan.key);
    request.subscribe({
      next: (current) => this.finish(current),
      error: (err: unknown) => this.fail(err),
    });
  }

  private finish(plan: CurrentSubscriptionPlan): void {
    this.busy.set(false);
    this.current.set(plan);
    // Veli çocuğuna aldığında oturumun (velinin) plan durumu değişmez.
    if (this.childId() == null) this.entitlements.set(plan);
    this.checkoutPlan.set(null);
    this.completed.emit(plan);
  }

  private fail(err: unknown): void {
    this.busy.set(false);
    const code = err instanceof HttpErrorResponse ? (err.error as { code?: string } | null)?.code : null;
    this.actionError.set(code === 'subscription.trialUsed' ? 'errors.trialUsed' : 'errors.generic');
    if (code === 'subscription.trialUsed') this.current.update((c) => (c ? { ...c, trialAvailable: false } : c));
  }

  protected readonly freeKey = FREE_PLAN_KEY;
  protected readonly trialKey = TRIAL_PLAN_KEY;
}

/** Sunucuyla aynı: kuruşa yuvarlanmış indirimli fiyat. */
function discounted(price: number, percent: number): number {
  return percent > 0 ? Math.round(price * (100 - percent)) / 100 : price;
}

/** Issue #1091: çocuğun planı seçicinin "mevcut plan" biçiminde (deneme ve kullanım veli tarafında yok). */
function asCurrent(child: ChildSubscriptionPlan): CurrentSubscriptionPlan {
  return {
    planKey: child.planKey,
    planName: null,
    displayNameKey: child.displayNameKey,
    validToUtc: child.validToUtc,
    trialAvailable: false,
    features: [],
  };
}
