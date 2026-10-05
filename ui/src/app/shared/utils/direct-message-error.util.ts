import { HttpErrorResponse } from '@angular/common/http';
import { DirectMessageErrorCode, isDirectMessageErrorCode } from '../../models/direct-message.model';

/** `direct-messages` scope'unda (önek: `errors.`) çözülen anahtarlar. */
export type DirectMessageTranslate = (key: string, params?: Record<string, unknown>) => string;

export interface DirectMessageErrorInfo {
  /** Gösterilecek düz metin. */
  message: string;
  /** Tanınan `errorCode`; yoksa null. */
  code: DirectMessageErrorCode | null;
  status: number;
}

/** Hata gövdesindeki `errorCode` (tanınıyorsa). */
export function directMessageErrorCode(error: unknown): DirectMessageErrorCode | null {
  if (!(error instanceof HttpErrorResponse)) {
    return null;
  }
  const body = error.error && typeof error.error === 'object' ? (error.error as Record<string, unknown>) : null;
  const code = body?.['errorCode'];
  return isDirectMessageErrorCode(code) ? code : null;
}

/**
 * Issue #106 — DM uçlarının hata yanıtını kullanıcı metnine çevirir.
 * - Tanınan `errorCode` → **her zaman yerel metin** (backend `message` değil). Böylece `CannotMessageTeacher`
 *   nötr kalır: öğrenciye engellendiği/ilişkinin bittiği söylenmez; metin sözlükte sabittir.
 * - 429: `Retry-After` varsa saniyeli metin.
 * - Ağ hatası (status 0): yerel metin.
 * - Bilinmeyen ya da eksik kod: her zaman `fallbackKey` (backend `message` asla gösterilmez).
 */
export function directMessageError(error: unknown, t: DirectMessageTranslate, fallbackKey = 'errors.generic'): DirectMessageErrorInfo {
  if (!(error instanceof HttpErrorResponse)) {
    return { message: t(fallbackKey), code: null, status: -1 };
  }
  const status = error.status;
  if (status === 429) {
    const retryAfter = Number(error.headers?.get('Retry-After'));
    return {
      message:
        Number.isFinite(retryAfter) && retryAfter > 0
          ? t('errors.rateLimitedRetry', { seconds: Math.ceil(retryAfter) })
          : t('errors.rateLimited'),
      code: 'RateLimited',
      status,
    };
  }
  if (status === 0) {
    return { message: t('errors.network'), code: null, status };
  }
  const code = directMessageErrorCode(error);
  if (code) {
    return { message: t(`errors.codes.${code}`), code, status };
  }
  // Güvenlik (#106 D2): tanınmayan ya da eksik kodda sunucu `message`'ı gösterilmez (iç ayrıntı / nötrlük sızıntısı).
  return { message: t(fallbackKey), code: null, status };
}
