import { HttpErrorResponse } from '@angular/common/http';

/** Admin hesap aksiyonu hata metni anahtarları (`<önek>.<anahtar>`). */
export type AdminActionErrorKey = 'forbidden' | 'notFound' | 'upstream' | 'rateLimited' | 'rateLimitedSeconds' | 'generic';

/**
 * Admin hesap aksiyonlarının (şifre sıfırlama #156, hesap durumu #155) ortak HTTP hata yorumu.
 * 403/404/502'de backend'in yerelleştirilmiş `{ message }`'ı varsa o gösterilir; gövdesizse (ör. admin olmayan 403)
 * `<keyPrefix>.forbidden|notFound|upstream`. 429 gövdesi düz metindir → `<keyPrefix>.rateLimited`, `Retry-After`
 * saniye ise `<keyPrefix>.rateLimitedSeconds` (`{ seconds }`). Diğer durumlar (500, ağ hatası, 400) → `<keyPrefix>.generic`.
 *
 * @param keyPrefix Çeviri anahtarı öneki, ör. `admin.passwordReset.errors`.
 * @param translate Tam anahtarı (ve parametreleri) metne çevirir.
 */
export function adminActionErrorMessage(
  err: HttpErrorResponse,
  keyPrefix: string,
  translate: (key: string, params?: Record<string, unknown>) => string,
): string {
  const text = (key: AdminActionErrorKey, params?: Record<string, unknown>) => translate(`${keyPrefix}.${key}`, params);
  switch (err.status) {
    case 403:
      return backendMessage(err) ?? text('forbidden');
    case 404:
      return backendMessage(err) ?? text('notFound');
    case 502:
      return backendMessage(err) ?? text('upstream');
    case 429: {
      const seconds = Number.parseInt(err.headers?.get('Retry-After') ?? '', 10);
      return Number.isFinite(seconds) && seconds > 0 ? text('rateLimitedSeconds', { seconds }) : text('rateLimited');
    }
    default:
      return text('generic');
  }
}

/** Hata gövdesindeki boş olmayan `{ message }` (kırpılmış), yoksa null. */
export function backendMessage(err: HttpErrorResponse): string | null {
  const body: unknown = err.error;
  if (body && typeof body === 'object' && 'message' in body) {
    const message = (body as { message: unknown }).message;
    if (typeof message === 'string' && message.trim()) return message.trim();
  }
  return null;
}
