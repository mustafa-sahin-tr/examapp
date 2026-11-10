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

/** Issue #1093: özellik listesinden bir satır (API `GET subscription-plans/features`, sıra kartlardaki sıradır). */
export type EntitlementFeatureKind = 'Toggle' | 'Metered';

export interface EntitlementFeature {
  key: string;
  audience: 'Teacher' | 'Student' | 'Both';
  kind: EntitlementFeatureKind;
  /** Sayılan özelliğin birimi (`subscription` scope'unda `units.<unit>`); açık / kapalı özellikte null. */
  unit: string | null;
}

export interface FeatureUsage {
  featureKey: string;
  kind: EntitlementFeatureKind;
  unit: string | null;
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
  /** Issue #1090: deneme bitti, plan yok; uygulama yalnız plan sayfasını açar. Eski yanıtlarda yok (false). */
  locked?: boolean;
  features: FeatureUsage[];
}

/** Issue #1086: admin plan düzenleyicisinin gövdesi; `monthlyLimits`'te olmayan özellik planda yoktur (null = sınırsız / dahil). */
export interface SubscriptionPlanUpdate {
  name: string;
  monthlyPrice: number;
  trialDays: number | null;
  isActive: boolean;
  monthlyLimits: Record<string, number | null>;
}

export const FREE_PLAN_KEY = 'free';
export const TRIAL_PLAN_KEY = 'trial';


/** `subscription.plans.teacherPro` → scope içi `plans.teacherPro` (şablon `subscription` önekiyle çevirir). */
export function planNameKey(displayNameKey: string | null | undefined): string {
  return (displayNameKey ?? '').replace(/^subscription\./, '');
}

/** Issue #1029: plan sınırına takılan her uç noktanın ortak 429 gövdesi (`EntitlementLimitResponse`). */
export const ENTITLEMENT_LIMIT_REACHED_CODE = 'entitlement.limitReached';
/** Issue #1087: özellik planda yok (sınır 0) — 403, aynı gövde ve aynı yükseltme penceresi. */
export const ENTITLEMENT_NOT_IN_PLAN_CODE = 'entitlement.notInPlan';

export interface EntitlementLimitReached {
  code: typeof ENTITLEMENT_LIMIT_REACHED_CODE | typeof ENTITLEMENT_NOT_IN_PLAN_CODE;
  feature: string;
  limit: number | null;
  used: number | null;
  resetsAtUtc: string | null;
  planKey: string | null;
  message?: string | null;
}

/** 429 + `entitlement.limitReached` ya da 403 + `entitlement.notInPlan` ise gövdeyi döndürür; diğer hatalarda null. */
export function entitlementLimitOf(error: unknown): EntitlementLimitReached | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  const body = error.error as Partial<EntitlementLimitReached> | null;
  const expected =
    error.status === 429 ? ENTITLEMENT_LIMIT_REACHED_CODE : error.status === 403 ? ENTITLEMENT_NOT_IN_PLAN_CODE : null;
  return expected && body?.code === expected && typeof body.feature === 'string' ? (body as EntitlementLimitReached) : null;
}

/**
 * Issue #1088: iki taraflı özellikte (mesaj, randevu, canlı ders) karşı tarafın planı izin vermiyor — 403. Çağıranın
 * yükseltmesi işe yaramaz; yükseltme penceresi açılmaz, sunucunun sabit sözlük mesajı gösterilir.
 */
export const ENTITLEMENT_COUNTERPART_NOT_IN_PLAN_CODE = 'entitlement.counterpartNotInPlan';

/** 403 + `entitlement.counterpartNotInPlan` ise gösterilecek mesaj; diğer hatalarda null. */
export function entitlementCounterpartMessageOf(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse) || error.status !== 403) return null;
  const body = error.error as { code?: unknown; message?: unknown } | null;
  return body?.code === ENTITLEMENT_COUNTERPART_NOT_IN_PLAN_CODE && typeof body.message === 'string' && body.message
    ? body.message
    : null;
}

/** Issue #1090: planı olmayan (deneme bitmiş) öğretmen / öğrencinin her kilitli uçtaki 402 yanıtı. */
export const SUBSCRIPTION_REQUIRED_CODE = 'subscription.required';

/** 402 + `subscription.required` ise true. */
export function isSubscriptionRequired(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse) || error.status !== 402) return false;
  return (error.error as { code?: unknown } | null)?.code === SUBSCRIPTION_REQUIRED_CODE;
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
