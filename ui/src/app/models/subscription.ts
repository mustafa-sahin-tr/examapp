import { HttpErrorResponse } from '@angular/common/http';

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
export const SUBSCRIPTION_FEATURES = ['book-import', 'quick-add', 'ai-classification', 'mock-exam'] as const;

/** `subscription.plans.teacherPro` → scope içi `plans.teacherPro` (şablon `subscription` önekiyle çevirir). */
export function planNameKey(displayNameKey: string | null | undefined): string {
  return (displayNameKey ?? '').replace(/^subscription\./, '');
}

/** Issue #1029: plan sınırına takılan her uç noktanın ortak 429 gövdesi (`EntitlementLimitResponse`). */
export const ENTITLEMENT_LIMIT_REACHED_CODE = 'entitlement.limitReached';

export interface EntitlementLimitReached {
  code: typeof ENTITLEMENT_LIMIT_REACHED_CODE;
  feature: string;
  limit: number | null;
  used: number | null;
  resetsAtUtc: string | null;
  planKey: string | null;
  message?: string | null;
}

/** 429 + `entitlement.limitReached` ise gövdeyi döndürür; diğer hatalarda null. */
export function entitlementLimitOf(error: unknown): EntitlementLimitReached | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 429) return null;
  const body = error.error as Partial<EntitlementLimitReached> | null;
  return body?.code === ENTITLEMENT_LIMIT_REACHED_CODE && typeof body.feature === 'string'
    ? (body as EntitlementLimitReached)
    : null;
}

/** Issue #1030: admin plan ataması. E-posta maskelidir. */
export type SubscriptionSearchRole = 'Student' | 'Teacher' | 'Parent';

export interface AdminSubscriptionUser {
  userId: number;
  fullName: string;
  email: string;
  role: string;
  planKey: string | null;
  planName: string | null;
  validToUtc: string | null;
}

export interface AdminSubscriptionAssignment {
  id: number;
  planKey: string;
  planName: string;
  validFromUtc: string;
  validToUtc: string | null;
  status: 'Active' | 'Scheduled' | 'Ended';
}

export interface AdminSubscriptionChange {
  occurredAtUtc: string;
  action: 'Assigned' | 'Ended';
  /** `null` = atama yoktu (Free). */
  fromPlanKey: string | null;
  toPlanKey: string | null;
  validFromUtc: string | null;
  validToUtc: string | null;
  actorName: string;
  bySelf: boolean;
  schoolId: number | null;
}

export interface AdminSubscriptionUserDetail extends AdminSubscriptionUser {
  assignments: AdminSubscriptionAssignment[];
  changes: AdminSubscriptionChange[];
}

export interface PlanAssignment {
  planKey: string;
  validFromUtc: string | null;
  validToUtc: string | null;
}

export type SchoolPlanAudience = 'Teachers' | 'Students' | 'All';

export interface SchoolSubscriptionMembers {
  schoolId: number;
  teachers: number;
  students: number;
}
