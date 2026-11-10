import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { forkJoin } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import {
  CurrentSubscriptionPlan,
  EntitlementFeature,
  FREE_PLAN_KEY,
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

/** Kart düğmesinin türü: Free (kayıtta atla), deneme, ücretli (sahte ödeme). */
export type PlanAction = 'free' | 'trial' | 'paid';

/**
 * Issue #1028 (epic #1026): plan kartları + deneme başlatma + sahte ödeme. Kayıt sihirbazının son adımı (`signup`: Free kartı
 * "Ücretsiz devam et" ile atlar) ve "Planım"dan yükseltme (`change`: mevcut plan işaretli, Free'ye dönüş yok — iptal kapsam dışı).
 * Ödeme ekranı açıkça demodur: kart alanı yok, tek "Ödemeyi tamamla (demo)" düğmesi.
 */
@Component({
  selector: 'app-plan-picker',
  standalone: true,
  imports: [CurrencyPipe, MatButtonModule, MatIconModule, TranslocoDirective],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './plan-picker.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './plan-picker.component.scss',
})
export class PlanPickerComponent implements OnInit {
  private readonly subscriptions = inject(SubscriptionService);
  private readonly entitlements = inject(EntitlementStateService);

  readonly mode = input<'signup' | 'change'>('signup');
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

  readonly currentKey = computed(() => this.current()?.planKey ?? null);
  /** Issue #1090: plan kilidi açıkken katalogda Free yoktur; açıklama ona göre değişir. */
  readonly offersFree = computed(() => this.plans().some((p) => p.key === FREE_PLAN_KEY));

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);
    forkJoin({
      plans: this.subscriptions.plans(),
      current: this.subscriptions.current(),
      features: this.subscriptions.features(),
    }).subscribe({
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
    this.checkoutPlan.set(plan);
  }

  closeCheckout(): void {
    this.actionError.set(null);
    this.checkoutPlan.set(null);
  }

  completeCheckout(): void {
    const plan = this.checkoutPlan();
    if (!plan || this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);
    this.subscriptions.checkout(plan.key).subscribe({
      next: (current) => this.finish(current),
      error: (err: unknown) => this.fail(err),
    });
  }

  private finish(plan: CurrentSubscriptionPlan): void {
    this.busy.set(false);
    this.current.set(plan);
    this.entitlements.set(plan);
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
