import { HttpErrorResponse } from '@angular/common/http';
import { isWorksheetCommentErrorCode } from '../../../models/worksheet-comment.model';

/** Sunucudan gelen düz metin mesajın gösterimde üst sınırı (savunma amaçlı). */
const MAX_MESSAGE_LENGTH = 300;

export type CommentTranslate = (key: string, params?: Record<string, unknown>) => string;

function clean(value: unknown): string {
  return typeof value === 'string' ? value.trim().slice(0, MAX_MESSAGE_LENGTH) : '';
}

/**
 * Yorum uçlarının hata yanıtını kullanıcı mesajına çevirir (issue #105):
 * - 429: gövde yerelleştirilmiş düz metin (`worksheets.comments.rateLimited`) — yalnız `<` içermiyor ve 300 karakteri
 *   aşmıyorsa gösterilir; aksi hâlde Retry-After ile yerel metin.
 * - diğerleri: gövde `{ message, errorCode }` — önce backend `message`, boşsa `errorCode`'un yerel karşılığı.
 * - ağ hatası (status 0): yerel metin.
 * `t` çağrısı `comments` scope'unda çözülen anahtarları alır.
 */
export function commentErrorMessage(error: unknown, t: CommentTranslate, fallbackKey: string): string {
  if (!(error instanceof HttpErrorResponse)) {
    return t(fallbackKey);
  }

  if (error.status === 429) {
    // Gövde düz metin beklenir; gateway/proxy HTML hata sayfası ya da aşırı uzun metin gösterilmez.
    const raw = typeof error.error === 'string' ? error.error.trim() : '';
    const text = raw && !raw.includes('<') && raw.length <= MAX_MESSAGE_LENGTH ? raw : '';
    if (text) {
      return text;
    }
    const retryAfter = Number(error.headers?.get('Retry-After'));
    return Number.isFinite(retryAfter) && retryAfter > 0
      ? t('errors.rateLimitedRetry', { seconds: Math.ceil(retryAfter) })
      : t('errors.rateLimited');
  }

  if (error.status === 0) {
    return t('errors.network');
  }

  const body = error.error && typeof error.error === 'object' ? (error.error as Record<string, unknown>) : null;
  const message = clean(body?.['message']);
  if (message) {
    return message;
  }
  const code = body?.['errorCode'];
  if (isWorksheetCommentErrorCode(code)) {
    return t(`errors.codes.${code}`);
  }
  return t(fallbackKey);
}
