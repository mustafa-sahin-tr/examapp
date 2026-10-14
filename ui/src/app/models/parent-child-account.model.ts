/**
 * Issue #438 (epic #435): veli panelinden çocuk hesabı açma ve çocuk şifresini sıfırlama. Backend:
 * `POST /api/exam/parent/children` ve `POST /api/exam/parent/children/{studentId}/reset-password` (gateway `/api/exam/{everything}`).
 * Şifre yalnız istek gövdesinde gider; hiçbir yanıtta dönmez, ekranda tekrar gösterilmez.
 */

/** Velinin onayladığı rıza metninin sürümü — sunucudaki `ParentConsentVersions.Current` ile aynı olmalı. */
export const PARENT_CONSENT_VERSION = 'kvkk-2026-10';

/** Kullanıcı adı: 4–30 karakter, küçük harf / rakam / nokta / alt çizgi (sunucu kırpıp küçük harfe çevirir). */
export const CHILD_USERNAME_PATTERN = /^[a-z0-9._]{4,30}$/;

export const CHILD_PASSWORD_MIN_LENGTH = 8;
export const CHILD_PASSWORD_MAX_LENGTH = 128;

/** Veli başına açılabilecek çocuk hesabı tavanı (sunucu da uygular). */
export const MAX_CHILD_ACCOUNTS = 10;

export interface CreateChildAccountRequest {
  firstName: string;
  lastName: string;
  username: string;
  password: string;
  gradeId: number;
  schoolId: number | null;
  studentNumber: string | null;
  consentAccepted: boolean;
  consentVersion: string;
}

/** 201 gövdesi — şifre YOK. */
export interface CreatedChildAccount {
  studentId: number;
  linkId: number;
  username: string;
  studentName: string;
}

export const CHILD_ACCOUNT_ERROR_CODES = {
  usernameTaken: 'UsernameTaken',
  invalidUsername: 'InvalidUsername',
  passwordRejected: 'PasswordRejected',
  invalidName: 'InvalidName',
  consentRequired: 'ConsentRequired',
  consentVersionMismatch: 'ConsentVersionMismatch',
  invalidGrade: 'InvalidGrade',
  schoolNotFound: 'SchoolNotFound',
  childAccountLimitReached: 'ChildAccountLimitReached',
  notParentManaged: 'NotParentManaged',
  /** Şifre sıfırlama yalnız birincil veliye açık — 403. */
  onlyPrimaryParent: 'OnlyPrimaryParent',
  unavailable: 'Unavailable',
} as const;

/** Girdiyi sunucunun normalize ettiği biçime getirir (kırp + küçük harf). */
export function normalizeChildUsername(value: string | null | undefined): string {
  return (value ?? '').trim().toLowerCase();
}

/** Yerel şifre tabanı (sunucu ve Keycloak ayrıca doğrular): 8–128, en az bir harf ve bir rakam, baş/son boşluksuz. */
export function isAcceptableChildPassword(value: string | null | undefined): boolean {
  const v = value ?? '';
  if (v.length < CHILD_PASSWORD_MIN_LENGTH || v.length > CHILD_PASSWORD_MAX_LENGTH) return false;
  if (v.trim() !== v) return false;
  return /\p{L}/u.test(v) && /\d/.test(v);
}
