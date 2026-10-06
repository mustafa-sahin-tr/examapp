/**
 * Login sonrası yönlendirme hedefi için allowlist (issue #347, open redirect).
 *
 * Kabul edilenler:
 * - Aynı origin'de göreli yol: `/` ile başlar, `//` veya `/\` ile başlamaz (protokol-göreli URL), ters
 *   eğik çizgi ve kontrol karakteri içermez; tarayıcının çözdüğü origin mevcut origin'le aynı kalır.
 * - `allowedOrigins` içinde birebir yer alan bir origin'e mutlak `http(s)` URL (varsayılan liste boş).
 *
 * Reddedilenler: mutlak/dış URL, `//evil.com`, `/\evil.com`, `javascript:`/`data:` vb. şemalar, boşlukla
 * başlayan değerler, login akışının kendi yolları (döngü olmasın). Reddedilen değer için `null` döner;
 * çağıran varsayılan hedefe düşer.
 */

const MAX_REDIRECT_LENGTH = 2048;

/** Login akışının kendi yolları; buraya dönmek yönlendirme döngüsü üretir. */
const AUTH_FLOW_PATHS = ['/oidc-login', '/app/login', '/app/callback', '/app/logout'];

// Kontrol karakterleri (C0 + DEL), ters eğik çizgi ve boşluk benzeri karakterler.
const FORBIDDEN_CHARS = /[\u0000-\u001f\u007f\\\s]/;

function currentOrigin(): string | null {
  return typeof location === 'undefined' ? null : location.origin;
}

function isAuthFlowPath(pathname: string): boolean {
  const normalized = pathname.length > 1 ? pathname.replace(/\/+$/, '') : pathname;
  return AUTH_FLOW_PATHS.some((p) => normalized.toLowerCase() === p);
}

/**
 * `value` güvenli bir login-sonrası hedefse normalleştirilmiş halini (göreli: `path?query#hash`,
 * allowlist'teki origin: tam URL) döner, değilse `null`.
 */
export function safeRedirectTarget(
  value: unknown,
  allowedOrigins: readonly string[] = [],
  origin: string | null = currentOrigin()
): string | null {
  if (typeof value !== 'string' || value.length === 0 || value.length > MAX_REDIRECT_LENGTH) {
    return null;
  }
  if (FORBIDDEN_CHARS.test(value)) {
    return null;
  }

  if (value.startsWith('/')) {
    if (value.startsWith('//')) {
      return null;
    }
    const base = origin ?? 'http://localhost';
    let url: URL;
    try {
      url = new URL(value, base);
    } catch {
      return null;
    }
    if (url.origin !== new URL(base).origin || isAuthFlowPath(url.pathname)) {
      return null;
    }
    return `${url.pathname}${url.search}${url.hash}`;
  }

  // Mutlak URL yalnızca açık allowlist'teki origin'lerden biriyse.
  if (allowedOrigins.length === 0) {
    return null;
  }
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    return null;
  }
  if (url.protocol !== 'https:' && url.protocol !== 'http:') {
    return null;
  }
  if (url.username || url.password) {
    return null;
  }
  if (!allowedOrigins.includes(url.origin) || isAuthFlowPath(url.pathname)) {
    return null;
  }
  return url.href;
}
