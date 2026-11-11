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
  /** Issue #1108: öğrenci planının fiyat tablosu (yalnız admin kataloğunda). */
  prices?: PlanPriceRow[] | null;
}

/** Issue #1108: sınıf bandı, kapsam ve süre (API'de enum adları). */
export type StudentGradeBand = 'Primary' | 'Middle' | 'Lgs' | 'HighSchool' | 'Yks';
export type PlanScope = 'AllSubjects' | 'SingleSubject';
export type PlanDuration = 'Monthly' | 'SixMonths' | 'TwelveMonths' | 'Summer';

export const GRADE_BANDS: readonly StudentGradeBand[] = ['Primary', 'Middle', 'Lgs', 'HighSchool', 'Yks'];
export const PLAN_SCOPES: readonly PlanScope[] = ['AllSubjects', 'SingleSubject'];
export const PLAN_DURATIONS: readonly PlanDuration[] = ['Monthly', 'SixMonths', 'TwelveMonths', 'Summer'];

/** Bandın sınıfları (admin fiyat tablosunda sınıfa özel satır için). */
export const GRADE_BAND_LEVELS: Record<StudentGradeBand, readonly number[]> = {
  Primary: [1, 2, 3, 4],
  Middle: [5, 6, 7],
  Lgs: [8],
  HighSchool: [9, 10, 11],
  Yks: [12],
};

/** Issue #1108: admin fiyat tablosunun satırı; `gradeLevel` doluysa yalnız o sınıfın fiyatı. */
export interface PlanPriceRow {
  band: StudentGradeBand;
  gradeLevel: number | null;
  scope: PlanScope;
  duration: PlanDuration;
  price: number;
}

/** Issue #1108: velinin gördüğü fiyat (süre boyunca toplam, indirimsiz). */
export interface StudentPlanPrice {
  planKey: string;
  scope: PlanScope;
  duration: PlanDuration;
  amount: number;
}

/** Issue #1108: çocuğun bandı, tek ders için dersleri, fiyatları ve kardeş indirimi; `band` null = sınıfı yok (aylık, bütün dersler). */
export interface StudentPlanOffer {
  band: StudentGradeBand | null;
  gradeLevel: number | null;
  prices: StudentPlanPrice[];
  subjects: { id: number; name: string }[];
  summerOnSale: boolean;
  siblingDiscountPercent: number;
}

/** Issue #1108: satın alınan seçenek; boşsa aylık, bütün dersler. */
export interface PlanPurchase {
  subjectId: number | null;
  duration: PlanDuration;
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
  /** Issue #1092: plan okulun lisansından geliyor (`validToUtc` lisansın bitişi). */
  schoolLicense?: boolean;
  /** Issue #1116: süren satın alma (iptal koşuluyla); Free, deneme, admin ataması ve okul lisansında null. */
  purchase?: PurchaseStatus | null;
  features: FeatureUsage[];
}

/**
 * Issue #1116: süren satın alma. Aylık planda `fullRefundUntilUtc` null (iade yok, erişim dönem sonuna kadar); 6 / 12 ay / yaz
 * planında tam iade bu ana kadar.
 */
export interface PurchaseStatus {
  duration: PlanDuration;
  pricePaid: number;
  currency: string;
  cancelledAtUtc: string | null;
  fullRefundUntilUtc: string | null;
}

/** Issue #1116: iptalin sonucu; `refundAmount` yalnız cayma süresindeki iptalde (plan hemen biter). */
export interface PlanCancellation {
  accessUntilUtc: string | null;
  refundAmount: number | null;
  currency: string | null;
}

/** Issue #1086: admin plan düzenleyicisinin gövdesi; `monthlyLimits`'te olmayan özellik planda yoktur (null = sınırsız / dahil). */
export interface SubscriptionPlanUpdate {
  name: string;
  monthlyPrice: number;
  trialDays: number | null;
  isActive: boolean;
  monthlyLimits: Record<string, number | null>;
  /** Issue #1108: öğrenci planının bütün fiyat tablosu; verilmezse değişmez. */
  prices?: PlanPriceRow[];
}

/** Issue #1091: veli panelinde çocuğun planı ve velinin alabileceği öğrenci planları. */
export interface ChildSubscriptionPlan {
  planKey: string | null;
  displayNameKey: string | null;
  validToUtc: string | null;
  isTrial: boolean;
  /** Issue #1092: çocuğun planı okulunun lisansından geliyor. */
  schoolLicense?: boolean;
  plans: SubscriptionPlan[];
  /** Öğrenci özellikleri (kart satırları). */
  features: EntitlementFeature[];
  /** Issue #1108: band, kapsam, süre ve fiyat seçenekleri. */
  offer?: StudentPlanOffer | null;
  /** Issue #1108: şu anki planın tek dersi; bütün derslerde null. */
  scopeSubjectId?: number | null;
  scopeSubjectName?: string | null;
  /** Issue #1116: süren satın alma (veli iptal edebilir). */
  purchase?: PurchaseStatus | null;
}

/** Issue #1091: öğrenci kendine plan alamaz (403); planı velisi alır. */
export const PARENT_PAYS_CODE = 'subscription.parentPays';

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

/** Issue #1109: tek ders planında başka dersin içeriği (403); `scopeSubjectId` planın dersi. */
export const ENTITLEMENT_OUT_OF_SCOPE_CODE = 'entitlement.outOfScope';

export interface EntitlementLimitReached {
  code: typeof ENTITLEMENT_LIMIT_REACHED_CODE | typeof ENTITLEMENT_NOT_IN_PLAN_CODE | typeof ENTITLEMENT_OUT_OF_SCOPE_CODE;
  scopeSubjectId?: number | null;
  feature: string;
  limit: number | null;
  used: number | null;
  resetsAtUtc: string | null;
  planKey: string | null;
  message?: string | null;
}

/**
 * 429 + `entitlement.limitReached` ya da 403 + `entitlement.notInPlan` / `entitlement.outOfScope` (#1109) ise gövdeyi döndürür;
 * diğer hatalarda null.
 */
export function entitlementLimitOf(error: unknown): EntitlementLimitReached | null {
  if (!(error instanceof HttpErrorResponse)) return null;
  const body = error.error as Partial<EntitlementLimitReached> | null;
  const expected: string[] =
    error.status === 429
      ? [ENTITLEMENT_LIMIT_REACHED_CODE]
      : error.status === 403
        ? [ENTITLEMENT_NOT_IN_PLAN_CODE, ENTITLEMENT_OUT_OF_SCOPE_CODE]
        : [];
  return typeof body?.code === 'string' && expected.includes(body.code) && typeof body.feature === 'string'
    ? (body as EntitlementLimitReached)
    : null;
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
  /** Issue #1116: `Cancelled` = kullanıcı / veli iptal etti (cayma süresindeyse `toPlanKey` null, plan bitti). */
  action: 'Assigned' | 'Ended' | 'Cancelled';
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

/** Issue #1092: okul lisansı; `exists` false ise lisans hiç kurulmadı. */
export interface SchoolLicense {
  schoolId: number;
  exists: boolean;
  active: boolean;
  teacherPlanKey: string | null;
  studentPlanKey: string | null;
  validFromUtc: string | null;
  validToUtc: string | null;
  note: string | null;
  history: SchoolLicenseHistory[];
}

export interface SchoolLicenseHistory {
  action: 'Created' | 'Updated' | 'Ended';
  actorUserId: number;
  teacherPlanKey: string | null;
  studentPlanKey: string | null;
  validFromUtc: string | null;
  validToUtc: string | null;
  note: string | null;
  occurredAtUtc: string;
}

/** Boş plan = o kitlenin Pro'su, boş başlangıç = şimdi, boş bitiş = süresiz. */
export interface SaveSchoolLicense {
  teacherPlanKey: string | null;
  studentPlanKey: string | null;
  validFromUtc: string | null;
  validToUtc: string | null;
  note: string | null;
}
