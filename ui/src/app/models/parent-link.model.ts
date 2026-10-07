/**
 * Issue #419 (epic #407 V1): veli–öğrenci bağlantısı. Backend: `/api/exam/parent-links/...`
 * (gateway `/api/exam/{everything}` → API `/api/parent-links/...`).
 */

/** POST invite-code — düz kod yalnızca bu yanıtta bir kez döner. */
export interface ParentInviteCode {
  code: string;
  /** ISO-8601 UTC. */
  expiresAt: string;
}

export interface LinkedParent {
  linkId: number;
  parentName: string;
  linkedAt: string;
}

/** Öğrencinin onayını bekleyen veli isteği (review: kod tek başına bağlamaz, öğrenci onaylar). */
export interface PendingParentRequest {
  linkId: number;
  parentName: string;
  /** Maskeli e-posta (a***@g***.com) — öğrenci isteği tanıyabilsin; çözülemezse boş. */
  parentEmailMasked: string;
  requestedAt: string;
  expiresAt: string;
}

/** GET my-parents (öğrenci). */
export interface StudentParentLinks {
  items: LinkedParent[];
  pendingRequests: PendingParentRequest[];
  /** Geçerli (kullanılmamış) bir kod varsa bitişi; kodun kendisi tekrar gösterilmez. */
  activeInviteExpiresAt: string | null;
  /** Aktif + bekleyen bağlantı tavanı. */
  maxActiveParents: number;
}

export type LinkedChildStatus = 'Active' | 'Pending';

/**
 * GET my-children / POST redeem (veli). `Pending` (öğrenci onayı bekleniyor) iken öğrenciye ait hiçbir alan dolu gelmez.
 */
export interface LinkedChild {
  linkId: number;
  status: LinkedChildStatus;
  studentName: string | null;
  gradeName: string | null;
  schoolName: string | null;
  linkedAt: string | null;
  requestedAt: string;
  pendingExpiresAt: string | null;
}

/** Hata gövdesi `{ message, errorCode }`. */
export interface ParentLinkErrorBody {
  message?: string;
  errorCode?: string;
}

export const PARENT_LINK_ERROR_CODES = {
  invalidCode: 'InvalidCode',
  studentLimitReached: 'StudentLimitReached',
  parentLimitReached: 'ParentLimitReached',
  alreadyLinked: 'AlreadyLinked',
  rateLimited: 'RateLimited',
} as const;

/** Davet kodu uzunluğu (ayraçsız). */
export const PARENT_INVITE_CODE_LENGTH = 12;

/** Okunabilirlik için "XXXX-XXXX-XXXX" biçimi (sunucu tireyi ve boşluğu yok sayar). */
export function formatInviteCode(code: string): string {
  const clean = (code ?? '').replace(/[\s-]/g, '').toUpperCase();
  return clean.length === PARENT_INVITE_CODE_LENGTH
    ? `${clean.slice(0, 4)}-${clean.slice(4, 8)}-${clean.slice(8)}`
    : clean;
}

/** Girdiden ayraçları atıp büyük harfe çevirir (sunucu da aynı normalize'ı yapar). */
export function normalizeInviteCodeInput(value: string): string {
  return (value ?? '').replace(/[\s-]/g, '').toUpperCase();
}
