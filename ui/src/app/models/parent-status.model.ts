import { HttpErrorResponse } from '@angular/common/http';

/**
 * Issue #440 / #441: öğrencinin veli durumu. Backend: `api/ExamApp.Api/Models/Dtos/ParentInvitations/ParentInvitationDtos.cs`
 * (`ParentStatusDto`, `ParentInvitationSummaryDto`). Kaynaklar: `POST /api/exam/auth/refresh` → `student.parentStatus` ve
 * `GET /api/exam/student-parent/status` (ilk çağrı hoşgörü sayacını başlatır). `GET student/profile` bu alanı taşımaz.
 */

/** `Ok` velisi aktif; `AwaitingActivation` davet açık (kısıtlı mod); `Grace` hoşgörü süresi; `Expired` süre doldu. */
export type ParentStatusMode = 'Ok' | 'AwaitingActivation' | 'Grace' | 'Expired';

/**
 * Açık davetin özeti (yalnız `AwaitingActivation`). Yeni / mevcut veli ayrımı bilerek DÖNMEZ — öğrenciye her durumda aynı
 * "Velinize davet gönderildi" metni gösterilir.
 */
export interface ParentInvitationSummary {
  parentName: string;
  /** `a***@g***.com` */
  maskedEmail: string;
  /** UTC. */
  expiresAt: string;
  expired: boolean;
  /** Şu an yeniden gönderilebilir mi (24 saatte 1, toplam 5). */
  canResend: boolean;
  /** Sonraki gönderim anı (UTC); hak bittiyse null. */
  nextResendAt?: string | null;
  resendsLeft: number;
  /** Doluysa bu ana kadar farklı bir e-posta girilemez (1 saatte 3 farklı e-posta). */
  emailChangeLockedUntil?: string | null;
  /** YALNIZ Development: e-posta yerine gösterilen aktivasyon bağlantısı ("dev: aktivasyon bağlantısı"). */
  devActivationUrl?: string | null;
}

export interface ParentStatus {
  mode: ParentStatusMode;
  requiresParent: boolean;
  graceDays: number;
  /** Grace / AwaitingActivation / Expired: hoşgörü süresinin bitişi (UTC). */
  graceEndsAt?: string | null;
  /** Grace / AwaitingActivation: kalan gün (süre dolduysa 0); Expired: 0. */
  daysLeft?: number | null;
  /** Hoşgörünün son günleri (Grace'te kapatılamaz modal, AwaitingActivation'da uyarı kartı) ya da Expired. */
  isFinalWarning: boolean;
  /** Açık davet — AwaitingActivation'da ve (süresi dolmuş davetle, `expired:true`) Expired'da. */
  invitation?: ParentInvitationSummary | null;
}

/** `POST student-parent/invitation` gövdesi ("veli bilgisi ekle" / "e-postayı düzelt"). */
export interface ParentContactPayload {
  parentFullName: string;
  parentEmail: string;
  parentPhone: string;
}

/** Yazım uçlarının yanıtı: `{ message, status }`. */
export interface ParentInvitationResponse {
  message?: string;
  status?: ParentStatus | null;
}

/** Hata gövdesi: `{ message, errorCode }` (429'da `retryAt`); kısıtlama 403'leri `{ success:false, errorCode, message }`. */
export interface ParentInvitationErrorBody {
  message?: string;
  errorCode?: string;
  retryAt?: string | null;
  success?: boolean;
}

export const PARENT_INVITATION_ERROR_CODES = {
  parentInfoRequired: 'ParentInfoRequired',
  invalidParentName: 'InvalidParentName',
  invalidParentEmail: 'InvalidParentEmail',
  invalidParentPhone: 'InvalidParentPhone',
  parentEmailNotUsable: 'ParentEmailNotUsable',
  parentEmailChangeLocked: 'ParentEmailChangeLocked',
  resendLimited: 'ParentInvitationResendLimited',
  parentAlreadyLinked: 'ParentAlreadyLinked',
  parentNotRequired: 'ParentNotRequired',
  noOpenInvitation: 'NoOpenInvitation',
  unavailable: 'ParentInvitationUnavailable',
  /** Veli onayı (my-children): davetin süresi dolmuş. */
  invitationExpired: 'InvitationExpired',
  consentRequired: 'ConsentRequired',
  consentVersionMismatch: 'ConsentVersionMismatch',
  /** 409 — veli şu anda hesabını etkinleştiriyor (e-posta düzeltme yarışı); tekrar denenebilir. */
  busy: 'Busy',
} as const;

/** Yaptırım 403 kodları. */
export const PARENT_ACTIVATION_REQUIRED_ERROR_CODE = 'ParentActivationRequired';
export const PARENT_REQUIRED_ERROR_CODE = 'ParentRequired';

/** "Veli bilgisi ekle" ekranı (#441 süre bitince öğrencinin tek ekranı). */
export const PARENT_REQUIRED_PATH = 'parent-required';
export const PARENT_REQUIRED_URL = `/${PARENT_REQUIRED_PATH}`;

/** Bu rollerden biri olan hesap veli kuralına tabi değildir (sunucu `ParentRequirement.AdultRoles`). */
export const PARENT_RULE_ADULT_ROLES: readonly string[] = ['Teacher', 'Admin', 'SuperAdmin', 'Parent'];

/** Rol listesine göre: öğrenci ve yetişkin rolü yok → veli kuralına tabi. */
export function isSubjectToParentRule(roles: readonly string[]): boolean {
  return roles.includes('Student') && !roles.some((role) => PARENT_RULE_ADULT_ROLES.includes(role));
}

/** Hata gövdesindeki `errorCode` (yoksa null). */
export function parentErrorCode(err: unknown): string | null {
  if (!(err instanceof HttpErrorResponse)) {
    return null;
  }
  const body: unknown = err.error;
  if (!body || typeof body !== 'object') {
    return null;
  }
  const code = (body as ParentInvitationErrorBody).errorCode;
  return typeof code === 'string' ? code : null;
}

/** 403 + verilen yaptırım kodu mu. */
export function isParentRestrictionError(err: unknown, code: string): err is HttpErrorResponse {
  return err instanceof HttpErrorResponse && err.status === 403 && parentErrorCode(err) === code;
}

/** Sunucu UTC `DateTime`'ı bölge eki olmadan da dönebilir; eksiz değer UTC kabul edilir. Geçersizse null. */
export function parseServerDate(iso: string | null | undefined): Date | null {
  if (!iso) {
    return null;
  }
  const hasZone = /(Z|[+-]\d{2}:?\d{2})$/i.test(iso);
  const date = new Date(hasZone ? iso : `${iso}Z`);
  return Number.isNaN(date.getTime()) ? null : date;
}

/** Aktif dilde tarih + saat; geçersizde boş metin. */
export function formatServerDateTime(iso: string | null | undefined, locale: string): string {
  const date = parseServerDate(iso);
  if (!date) {
    return '';
  }
  return new Intl.DateTimeFormat(locale, {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date);
}

/**
 * Dev aktivasyon bağlantısı yalnız Development'ta gelir (`Server:BaseUrl` köklü mutlak ya da göreli). Sunucudan gelse de
 * yalnız göreli yol veya http(s) gösterilir (`javascript:` vb. düşer).
 */
export function safeDevActivationUrl(url: string | null | undefined): string | null {
  const value = (url ?? '').trim();
  if (!value) {
    return null;
  }
  if (value.startsWith('/') && !value.startsWith('//')) {
    return value;
  }
  try {
    const parsed = new URL(value);
    return parsed.protocol === 'http:' || parsed.protocol === 'https:' ? value : null;
  } catch {
    return null;
  }
}
