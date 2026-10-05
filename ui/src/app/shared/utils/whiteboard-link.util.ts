import { isSafeHttpUrl } from './whiteboard-sync.util';

/**
 * Tahtadaki bir bağlantının açılma kararı (issue #332). Excalidraw'a bağımlı değildir; tuval (`onLinkOpen` ve bağlantı
 * balonundaki anchor koruması) bağlantıyı komponente iletir, komponent bu sınıflandırmaya göre doğrudan açar, onay
 * ister ya da engeller.
 *
 * - `sameOrigin`: uygulamanın kendi origin'inde sıradan bir UI rotası — onaysız açılır.
 * - `external`: başka bir origin YA DA aynı origin'de hassas bir yol (bkz. `WHITEBOARD_LINK_CONFIRM_PATH_PREFIXES`) —
 *   hedef adres gösterilip onay istenir (phishing riski; kullanıcıların çoğu reşit değil).
 * - `blocked`: http(s) dışı şema (`javascript:`, `data:`, `file:` ...), göreli/bozuk adres, 2048 karakteri aşan adres ya
 *   da kullanıcı bilgisi (`user:pass@host`) taşıyan adres — hiç açılmaz.
 */
export type WhiteboardLinkTarget =
  | { readonly kind: 'sameOrigin'; readonly url: string }
  | { readonly kind: 'external'; readonly url: string; readonly host: string }
  | { readonly kind: 'blocked' };

/**
 * Aynı origin'de olsa da onaysız açılmayan yol önekleri: gateway arkasındaki API/hub/auth uçları ve Keycloak
 * (OIDC/token) yolları, auth-ui (`/app/`) ve Hangfire paneli. Bu yollar tahtadan tek tıkla açılırsa oturum/tokene
 * dokunan istekler (ör. OIDC giriş akışı) kullanıcı fark etmeden tetiklenebilir. Eşleşme küçük harfe çevrilmiş,
 * yüzde-kodlaması çözülmüş ve ardışık eğik çizgileri tekilleştirilmiş yol üzerinde, önek olarak yapılır (fazladan
 * onay zararsızdır).
 */
export const WHITEBOARD_LINK_CONFIRM_PATH_PREFIXES: readonly string[] = [
  '/app/',
  '/realms/',
  '/auth/',
  '/oidc-login',
  '/token',
  '/api/',
  '/hub/',
  '/hangfire',
];

export function classifyWhiteboardLink(link: unknown, currentOrigin: string): WhiteboardLinkTarget {
  if (!isSafeHttpUrl(link)) {
    return { kind: 'blocked' };
  }
  const url = new URL(link);
  // `https://app.example.com@evil.example.org/` gibi kullanıcı bilgisi taşıyan adresler gerçek host'u gizler.
  if (url.username !== '' || url.password !== '') {
    return { kind: 'blocked' };
  }
  // `URL.origin` şema + host + (varsayılan olmayan) port'u normalize eder; büyük/küçük harf ve varsayılan port farkı
  // aynı origin sayılır.
  if (url.origin === currentOrigin && !requiresConfirmation(url.pathname)) {
    return { kind: 'sameOrigin', url: url.href };
  }
  return { kind: 'external', url: url.href, host: url.host };
}

/** Aynı origin'deki yol onay gerektiriyor mu (hassas önek ya da çözülemeyen kodlama). */
function requiresConfirmation(pathname: string): boolean {
  let path: string;
  try {
    path = decodeURIComponent(pathname);
  } catch {
    return true;
  }
  path = path.replace(/\\/g, '/').replace(/\/{2,}/g, '/').toLowerCase();
  return WHITEBOARD_LINK_CONFIRM_PATH_PREFIXES.some((prefix) => {
    const base = prefix.endsWith('/') ? prefix.slice(0, -1) : prefix;
    return path === base || path.startsWith(prefix) || (!prefix.endsWith('/') && path.startsWith(base));
  });
}

/** `window.open` imzasının kullandığımız kısmı (testte sahte verilir). */
export type OpenWindowFn = (url: string, target: string, features: string) => unknown;

/** Bağlantıyı her zaman yeni sekmede, açan sayfaya erişimi olmadan (`noopener,noreferrer`) açar. */
export function openInNewTab(url: string, open: OpenWindowFn): void {
  open(url, '_blank', 'noopener,noreferrer');
}
