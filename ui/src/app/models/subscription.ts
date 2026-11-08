/** Issue #1027 / #1028 (epic #1026): plan kataloğu ve kullanıcının planı. Fiyatlar yer tutucudur; gerçek ödeme yok. */

export type SubscriptionPlanAudience = 'Any' | 'Teacher' | 'Student';

export interface SubscriptionPlan {
  key: string;
  name: string;
  /** `subscription` scope'unda çeviri anahtarı, ör. `subscription.plans.teacherPro`. */
  displayNameKey: string;
  audience: SubscriptionPlanAudience;
  monthlyPrice: number;
  currency: string;
  trialDays: number | null;
  isActive: boolean;
  /** Özellik anahtarı → aylık sınır (null = sınırsız; anahtar yoksa 0). */
  monthlyLimits: Record<string, number | null>;
}

export interface FeatureUsage {
  featureKey: string;
  allowed: boolean;
  limit: number | null;
  used: number;
  remaining: number | null;
  resetsAtUtc: string | null;
}

export interface CurrentSubscriptionPlan {
  planKey: string | null;
  planName: string | null;
  displayNameKey: string | null;
  validToUtc: string | null;
  trialAvailable: boolean;
  features: FeatureUsage[];
}

export const FREE_PLAN_KEY = 'free';
export const TRIAL_PLAN_KEY = 'trial';

/** Kartlarda sırasıyla gösterilen özellikler (#1027 `EntitlementFeatures.All`). */
export const SUBSCRIPTION_FEATURES = ['book-import', 'ai-classification', 'mock-exam'] as const;

/** `subscription.plans.teacherPro` → scope içi `plans.teacherPro` (şablon `subscription` önekiyle çevirir). */
export function planNameKey(displayNameKey: string | null | undefined): string {
  return (displayNameKey ?? '').replace(/^subscription\./, '');
}
