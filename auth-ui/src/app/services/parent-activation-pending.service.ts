import { Injectable } from '@angular/core';

/** sessionStorage anahtarı (sekmeye özel): giriş turu sırasında bekleyen davet kabulü. */
export const PARENT_ACTIVATION_PENDING_KEY = 'parent_activation_pending';

/** Giriş formunda geçirilebilecek makul süre (OIDC kaydıyla aynı); sonrası bayat sayılır ve silinir. */
export const PARENT_ACTIVATION_PENDING_MAX_AGE_MS = 30 * 60 * 1000;

/** Girişten önce alınan rıza + davet token'ı. URL'ye hiç yazılmaz. */
export interface ParentActivationPending {
  token: string;
  consentAccepted: boolean;
  consentVersion: string;
  createdAt: number;
}

/**
 * Issue #440 (güvenlik turu): mevcut hesaplı veli daveti giriş yaptıktan sonra kabul eder. Token giriş turunda (Keycloak →
 * callback → `/app/parent-activate`) kaybolmasın diye YALNIZ bu sekmenin sessionStorage'ında, zaman damgasıyla tutulur;
 * kullanılınca / süresi dolunca silinir.
 */
@Injectable({ providedIn: 'root' })
export class ParentActivationPendingService {
  save(token: string, consentVersion: string, now: number = Date.now()): void {
    const value: ParentActivationPending = { token, consentAccepted: true, consentVersion, createdAt: now };
    try {
      sessionStorage.setItem(PARENT_ACTIVATION_PENDING_KEY, JSON.stringify(value));
    } catch {
      /* depolama kapalıysa giriş sonrası kullanıcı bağlantıyı yeniden açar */
    }
  }

  /** Geçerli kayıt (silmeden); bozuk / bayat kayıt silinir ve null döner. */
  peek(now: number = Date.now()): ParentActivationPending | null {
    let raw: string | null = null;
    try {
      raw = sessionStorage.getItem(PARENT_ACTIVATION_PENDING_KEY);
    } catch {
      return null;
    }
    if (!raw) {
      return null;
    }
    const value = parse(raw);
    if (!value || now - value.createdAt > PARENT_ACTIVATION_PENDING_MAX_AGE_MS || value.createdAt > now + 60_000) {
      this.clear();
      return null;
    }
    return value;
  }

  clear(): void {
    try {
      sessionStorage.removeItem(PARENT_ACTIVATION_PENDING_KEY);
    } catch {
      /* yok say */
    }
  }
}

function parse(raw: string): ParentActivationPending | null {
  try {
    const value: unknown = JSON.parse(raw);
    if (!value || typeof value !== 'object') {
      return null;
    }
    const v = value as Partial<ParentActivationPending>;
    if (
      typeof v.token !== 'string' ||
      !v.token ||
      v.token.length > 256 ||
      typeof v.consentVersion !== 'string' ||
      typeof v.consentAccepted !== 'boolean' ||
      typeof v.createdAt !== 'number'
    ) {
      return null;
    }
    return { token: v.token, consentAccepted: v.consentAccepted, consentVersion: v.consentVersion, createdAt: v.createdAt };
  } catch {
    return null;
  }
}
