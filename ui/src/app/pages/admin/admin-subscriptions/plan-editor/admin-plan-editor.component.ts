import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslocoDirective } from '@jsverse/transloco';

import { EntitlementFeature, SubscriptionPlan, TRIAL_PLAN_KEY } from '../../../../models/subscription';
import { SubscriptionService } from '../../../../services/subscription.service';

/** Satırın durumu: planda yok, aylık sınırlı, sınırsız (açık / kapalı özellikte "dahil"). */
export type LimitMode = 'none' | 'limited' | 'unlimited';

export interface LimitRow {
  feature: EntitlementFeature;
  mode: LimitMode;
  limit: number | null;
}

export interface FeatureGroup {
  audience: EntitlementFeature['audience'];
  rows: LimitRow[];
}

const GROUP_ORDER: EntitlementFeature['audience'][] = ['Teacher', 'Student', 'Both'];

/**
 * Issue #1086: admin plan düzenleyicisi. Plan seçilir; ad, aylık fiyat, deneme süresi (yalnız deneme planı), etkinlik ve
 * her özelliğin sınırı (yok / ayda N / sınırsız) değiştirilip kaydedilir. Özellikler planın kitlesine göre süzülür ve kitleye göre
 * gruplanır. Sunucu her kaydı audit'ler; sınır sonraki hak denetiminde geçerlidir.
 */
@Component({
  selector: 'app-admin-plan-editor',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    TranslocoDirective,
  ],
  templateUrl: './admin-plan-editor.component.html',
  styleUrl: './admin-plan-editor.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminPlanEditorComponent implements OnInit {
  private readonly subscriptions = inject(SubscriptionService);

  readonly plans = input.required<SubscriptionPlan[]>();
  /** Plan adı (kitle önekiyle); üst komponentin `planLabel`'ı. */
  readonly label = input.required<(planKey: string) => string>();
  readonly saved = output<SubscriptionPlan>();

  readonly features = signal<EntitlementFeature[]>([]);
  readonly planKey = signal<string | null>(null);
  readonly name = signal('');
  readonly monthlyPrice = signal<number | null>(0);
  readonly trialDays = signal<number | null>(null);
  readonly isActive = signal(true);
  readonly rows = signal<LimitRow[]>([]);

  readonly busy = signal(false);
  readonly done = signal(false);
  /** Kapsam içi hata anahtarı (`admin.planEditor.errors.*`). */
  readonly error = signal<string | null>(null);

  readonly plan = computed(() => this.plans().find((p) => p.key === this.planKey()) ?? null);
  readonly isTrial = computed(() => this.planKey() === TRIAL_PLAN_KEY);

  readonly groups = computed<FeatureGroup[]>(() =>
    GROUP_ORDER.map((audience) => ({ audience, rows: this.rows().filter((r) => r.feature.audience === audience) })).filter(
      (g) => g.rows.length > 0,
    ),
  );

  /** Sınırlı satırda 1 ve üstü tam sayı; fiyat 0 ve üstü; deneme planında 1–365 gün. */
  readonly valid = computed(() => {
    const price = this.monthlyPrice();
    const days = this.trialDays();
    return (
      this.name().trim().length > 0 &&
      price != null &&
      price >= 0 &&
      (!this.isTrial() || (days != null && Number.isInteger(days) && days >= 1 && days <= 365)) &&
      this.rows().every((r) => r.mode !== 'limited' || (r.limit != null && Number.isInteger(r.limit) && r.limit >= 1))
    );
  });

  ngOnInit(): void {
    this.subscriptions.features().subscribe({
      next: (features) => {
        this.features.set(features);
        const first = this.plans()[0];
        if (first && !this.planKey()) this.selectPlan(first.key);
      },
      error: () => this.error.set('admin.planEditor.errors.load'),
    });
  }

  selectPlan(planKey: string): void {
    const plan = this.plans().find((p) => p.key === planKey);
    if (!plan) return;
    this.planKey.set(planKey);
    this.name.set(plan.name);
    this.monthlyPrice.set(plan.monthlyPrice);
    this.trialDays.set(plan.trialDays);
    this.isActive.set(plan.isActive);
    this.rows.set(this.rowsFor(plan));
    this.error.set(null);
    this.done.set(false);
  }

  setMode(row: LimitRow, mode: LimitMode): void {
    this.patch(row, { mode, limit: mode === 'limited' ? (row.limit ?? 1) : row.limit });
  }

  setLimit(row: LimitRow, limit: number | null): void {
    this.patch(row, { limit });
  }

  save(): void {
    const planKey = this.planKey();
    if (!planKey || !this.valid() || this.busy()) return;
    const monthlyLimits: Record<string, number | null> = {};
    for (const row of this.rows()) {
      if (row.mode === 'unlimited') monthlyLimits[row.feature.key] = null;
      else if (row.mode === 'limited') monthlyLimits[row.feature.key] = row.limit;
    }
    this.busy.set(true);
    this.error.set(null);
    this.done.set(false);
    this.subscriptions
      .updatePlan(planKey, {
        name: this.name().trim(),
        monthlyPrice: this.monthlyPrice() ?? 0,
        trialDays: this.isTrial() ? this.trialDays() : null,
        isActive: this.isActive(),
        monthlyLimits,
      })
      .subscribe({
        next: (plan) => {
          this.busy.set(false);
          this.done.set(true);
          this.saved.emit(plan);
        },
        error: (err: HttpErrorResponse) => {
          this.busy.set(false);
          this.error.set(errorKey(err));
        },
      });
  }

  /** Planın kitlesine uyan özellikler, kayıt sırasıyla; 0 ya da satırı olmayan "yok"tur. */
  private rowsFor(plan: SubscriptionPlan): LimitRow[] {
    return this.features()
      .filter((f) => plan.audience === 'Any' || f.audience === 'Both' || f.audience === plan.audience)
      .map((feature) => {
        const has = Object.prototype.hasOwnProperty.call(plan.monthlyLimits, feature.key);
        const limit = has ? plan.monthlyLimits[feature.key] : 0;
        const mode: LimitMode = limit === null ? 'unlimited' : limit > 0 && feature.kind === 'Metered' ? 'limited' : 'none';
        return { feature, mode, limit: limit && limit > 0 ? limit : null };
      });
  }

  private patch(row: LimitRow, change: Partial<LimitRow>): void {
    this.done.set(false);
    this.rows.update((rows) => rows.map((r) => (r.feature.key === row.feature.key ? { ...r, ...change } : r)));
  }
}

function errorKey(err: HttpErrorResponse): string {
  const code = (err.error as { code?: string } | null)?.code;
  switch (code) {
    case 'subscription.invalidName':
    case 'subscription.invalidPrice':
    case 'subscription.invalidTrialDays':
    case 'subscription.unknownFeature':
    case 'subscription.featureAudienceMismatch':
    case 'subscription.invalidLimit':
    case 'subscription.planRequired':
    case 'subscription.unknownPlan':
      return `admin.planEditor.errors.${code.replace('subscription.', '')}`;
    default:
      return 'admin.planEditor.errors.generic';
  }
}
