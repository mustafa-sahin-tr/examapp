/**
 * Issue #419 (epic #407 V1) → issue #436 (epic #435, veli-öncelikli model): veli–öğrenci bağlantısı. Backend:
 * `/api/exam/parent-links/...` (gateway `/api/exam/{everything}` → API `/api/parent-links/...`). Bağlantıyı veli kurar; öğrenci
 * yalnızca bağlı velilerini görür. Birincil veli "ikinci veli davet kodu" üretir ve bekleyen isteği onaylar.
 */

/** POST {linkId}/second-parent-code — düz kod yalnızca bu yanıtta bir kez döner. */
export interface ParentInviteCode {
  code: string;
  /** ISO-8601 UTC. */
  expiresAt: string;
}

export interface LinkedParent {
  linkId: number;
  parentName: string;
  linkedAt: string;
  /** Issue #436: bağlantıları yöneten birincil veli. */
  isPrimary: boolean;
}

/** Geçiş dönemi (issue #436): #419'dan kalan, öğrencinin 30 gün daha onaylayabileceği eski veli isteği. */
export interface PendingParentRequest {
  linkId: number;
  parentName: string;
  /** Maskeli e-posta (a***@g***.com) — öğrenci isteği tanıyabilsin; çözülemezse boş. */
  parentEmailMasked: string;
  requestedAt: string;
  expiresAt: string;
}

/** GET my-parents (öğrenci, salt okunur). */
export interface StudentParentLinks {
  items: LinkedParent[];
  /** Yalnız geçiş dönemindeki eski istekler; yeni istekler öğrenciye düşmez. */
  pendingRequests: PendingParentRequest[];
  /** Aktif + bekleyen bağlantı tavanı. */
  maxActiveParents: number;
  /** Issue #437: her öğrencinin velisi olmalı (istisna yok). */
  requiresParent: boolean;
}

/** Issue #436: birincil velinin gördüğü diğer veli ya da bekleyen ikinci veli isteği. */
export interface CoParent {
  linkId: number;
  status: LinkedChildStatus;
  parentName: string;
  /** Yalnız Pending: maskeli e-posta (a***@g***.com) — kimi onayladığını bilsin; çözülemezse boş. */
  parentEmailMasked: string;
  linkedAt: string | null;
  requestedAt: string;
  pendingExpiresAt: string | null;
}

export type LinkedChildStatus = 'Active' | 'Pending';

/**
 * GET my-children / POST redeem (veli). `Pending` (birincil velinin onayı bekleniyor) iken öğrenciye ait hiçbir alan dolu
 * gelmez. Birincil veliye ayrıca diğer veliler/bekleyen istekler ve geçerli ikinci veli kodunun bitişi gelir.
 */
export interface LinkedChild {
  linkId: number;
  status: LinkedChildStatus;
  /** Issue #420: öğrencinin id'si — veli paneli anahtarı (`/parent?child=`). Yalnızca Active iken dolu. */
  studentId?: number | null;
  studentName: string | null;
  gradeName: string | null;
  schoolName: string | null;
  linkedAt: string | null;
  requestedAt: string;
  pendingExpiresAt: string | null;
  /** Issue #436: çağıran bu çocuğun birincil velisi. */
  isPrimary?: boolean;
  /** Issue #436: yalnız birincil veliye dolu. */
  coParents?: CoParent[];
  /** Issue #436: yalnız birincil veliye — kullanılmamış ikinci veli kodunun bitişi (kod dönmez). */
  secondParentCodeExpiresAt?: string | null;
  /** Öğrenci başına açık veli tavanı. */
  maxParents?: number;
  /** Issue #436: yalnız birincil veliye — açık veli sayısı (sunucunun tavan sayımı; eski bekleyen istekler dahil). */
  openParents?: number | null;
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
  notPrimaryParent: 'NotPrimaryParent',
  lastParentCannotLeave: 'LastParentCannotLeave',
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
