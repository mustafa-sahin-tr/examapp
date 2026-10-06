import { Injectable } from '@angular/core';
import {
  CODE_VERIFIER_PATTERN,
  OIDC_STATE_PATTERN,
  computeCodeChallenge,
  createCodeVerifier,
  createOidcState,
} from '../shared/utils/pkce.util';
import { safeRedirectTarget } from '../shared/utils/safe-redirect.util';

/** Kayıt bağlantılarından gelen rol niyeti; gateway bunu Keycloak `registrations` ucuna çevirir. */
export type RegisterIntent = 'student' | 'teacher' | 'parent';

const REGISTER_INTENTS: readonly RegisterIntent[] = ['student', 'teacher', 'parent'];

/** Login başlatılırken saklanan, callback'te tek seferlik tüketilen kayıt (issue #347). */
export interface OidcLoginRecord {
  state: string;
  codeVerifier: string;
  intent: RegisterIntent | null;
  /** Login sonrası gidilmek istenen yol (yalnızca allowlist'ten geçmiş değer). */
  returnPath: string | null;
  createdAt: number;
}

export interface BeginLoginOptions {
  intent?: string | null;
  returnPath?: string | null;
}

/** sessionStorage anahtarı: `oidc_flow:<state>` — kayıt state ile anahtarlanır. */
export const OIDC_FLOW_KEY_PREFIX = 'oidc_flow:';

/** Keycloak giriş/kayıt formunda geçirilebilecek makul süre; sonrası bayat sayılır. */
export const OIDC_FLOW_MAX_AGE_MS = 30 * 60 * 1000;

export function toRegisterIntent(value: unknown): RegisterIntent | null {
  const normalized = typeof value === 'string' ? value.trim().toLowerCase() : '';
  return (REGISTER_INTENTS as readonly string[]).includes(normalized) ? (normalized as RegisterIntent) : null;
}

/**
 * OIDC authorization code + PKCE akışının istemci tarafı (issue #347).
 *
 * `begin()` her login'de rastgele `state` + `code_verifier` üretir, sessionStorage'a yazar ve gateway'in
 * `/oidc-login` ucuna giden URL'yi (state, S256 code_challenge, intent) döner. `consume()` callback'te
 * dönen `state` ile kaydı bulur ve siler (tek kullanımlık); kayıt yoksa/bayatsa `null` → kod değişimi yapılmaz.
 *
 * Bilinen davranış — başka sekmede e-posta doğrulama: sessionStorage sekmeye özeldir. Keycloak kayıt
 * akışında doğrulama bağlantısı yeni bir sekmede açılıp oradan `/app/callback`'e dönülürse o sekmede kayıt
 * yoktur → "Oturum doğrulanamadı" + `/login`. Yeni login Keycloak SSO oturumu sayesinde form göstermeden
 * tamamlanır, ancak kayıt niyeti (`intent`) o sekmede kaybolur; kullanıcı rol ön seçimi olmadan profil
 * tamamlama ekranına düşer. Bilinçli tercih: state/verifier'ı localStorage gibi sekmeler arası paylaşılan
 * bir yere koymak login CSRF korumasını zayıflatırdı.
 */
@Injectable({ providedIn: 'root' })
export class OidcFlowService {
  async begin(options: BeginLoginOptions = {}): Promise<string> {
    this.pruneExpired();

    const state = createOidcState();
    const codeVerifier = createCodeVerifier();
    const codeChallenge = await computeCodeChallenge(codeVerifier);
    const intent = toRegisterIntent(options.intent);
    const record: OidcLoginRecord = {
      state,
      codeVerifier,
      intent,
      returnPath: safeRedirectTarget(options.returnPath),
      createdAt: Date.now(),
    };
    sessionStorage.setItem(OIDC_FLOW_KEY_PREFIX + state, JSON.stringify(record));

    const params = new URLSearchParams({
      state,
      code_challenge: codeChallenge,
      code_challenge_method: 'S256',
    });
    if (intent) {
      params.set('intent', intent);
    }
    return `/oidc-login?${params.toString()}`;
  }

  /** Callback'teki `state` ile kaydı bulur, siler ve doğrular; eşleşme yoksa `null`. */
  consume(state: unknown): OidcLoginRecord | null {
    if (typeof state !== 'string' || !OIDC_STATE_PATTERN.test(state)) {
      return null;
    }
    const key = OIDC_FLOW_KEY_PREFIX + state;
    const raw = sessionStorage.getItem(key);
    sessionStorage.removeItem(key);
    if (!raw) {
      return null;
    }
    const record = this.parse(raw);
    if (!record || record.state !== state || Date.now() - record.createdAt > OIDC_FLOW_MAX_AGE_MS) {
      return null;
    }
    return record;
  }

  private parse(raw: string): OidcLoginRecord | null {
    try {
      const value: unknown = JSON.parse(raw);
      if (typeof value !== 'object' || value === null) {
        return null;
      }
      const r = value as Partial<OidcLoginRecord>;
      if (
        typeof r.state !== 'string' ||
        typeof r.codeVerifier !== 'string' ||
        !CODE_VERIFIER_PATTERN.test(r.codeVerifier) ||
        typeof r.createdAt !== 'number'
      ) {
        return null;
      }
      return {
        state: r.state,
        codeVerifier: r.codeVerifier,
        intent: toRegisterIntent(r.intent),
        returnPath: safeRedirectTarget(r.returnPath),
        createdAt: r.createdAt,
      };
    } catch {
      return null;
    }
  }

  private pruneExpired(): void {
    const now = Date.now();
    for (let i = sessionStorage.length - 1; i >= 0; i--) {
      const key = sessionStorage.key(i);
      if (!key?.startsWith(OIDC_FLOW_KEY_PREFIX)) {
        continue;
      }
      const record = this.parse(sessionStorage.getItem(key) ?? '');
      if (!record || now - record.createdAt > OIDC_FLOW_MAX_AGE_MS) {
        sessionStorage.removeItem(key);
      }
    }
  }
}
