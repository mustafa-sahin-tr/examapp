import { BADGE_ICON_PATTERN } from '../../../models/badge-definition-admin.model';

/**
 * Issue #149 — rozet medalyonunun 4 durumu (tasarım notu tablosu).
 * Durum yalnız renkle anlatılmaz: kenar biçimi + ikon dolgusu + köşe glifi + üst öğedeki metin.
 */
export type BadgeMedallionState = 'earned' | 'new' | 'in-progress' | 'locked';

/** "Yeni kazanıldı" penceresi: `earnedDateUtc` son 7 gün (karar 1). */
export const BADGE_NEW_WINDOW_DAYS = 7;
const BADGE_NEW_WINDOW_MS = BADGE_NEW_WINDOW_DAYS * 24 * 60 * 60 * 1000;

/** Material Symbols adı — backend allowlist'i ile aynı biçim; bilinmeyen biçim font ligatürü olarak ham metin görünürdü. */
export const BADGE_ICON_NAME_PATTERN = /^[a-z][a-z0-9_]{1,63}$/;

/** `icon` ve `iconUrl` yoksa (ya da görsel yüklenemezse) gösterilen varsayılan glif. */
export const BADGE_FALLBACK_ICON = 'military_tech';

export interface BadgeStateSource {
  isCompleted: boolean;
  earnedDateUtc?: string | null;
  currentValue: number;
}

/** Medalyonun neyi çizeceği: Material Symbols glifi ya da (geçiş süresince) eski SVG yolu. */
export type BadgeIconSource = { kind: 'symbol'; name: string } | { kind: 'image'; src: string };

/** Saat dilimi eki olmayan sunucu ISO değeri UTC kabul edilir (tarayıcı eksiz ISO'yu yerel saat sayardı). */
export function parseBadgeUtcDate(iso: string | null | undefined): Date | null {
  const value = iso?.trim();
  if (!value) {
    return null;
  }
  const hasZone = /(Z|[+-]\d{2}:?\d{2})$/i.test(value);
  const date = new Date(hasZone ? value : `${value}Z`);
  return Number.isNaN(date.getTime()) ? null : date;
}

/**
 * Durum türetimi (devir notu): earned = isCompleted; new = earned && earnedDateUtc son 7 gün (sınır dahil;
 * saat kayması nedeniyle gelecekteki tarih de "yeni"); locked = !earned && currentValue === 0 (karar 5);
 * aksi in-progress.
 */
export function deriveBadgeState(source: BadgeStateSource, now: number = Date.now()): BadgeMedallionState {
  if (source.isCompleted) {
    const earnedAt = parseBadgeUtcDate(source.earnedDateUtc);
    if (earnedAt && now - earnedAt.getTime() <= BADGE_NEW_WINDOW_MS) {
      return 'new';
    }
    return 'earned';
  }
  return (source.currentValue ?? 0) <= 0 ? 'locked' : 'in-progress';
}

export function isEarnedState(state: BadgeMedallionState): boolean {
  return state === 'earned' || state === 'new';
}

/**
 * İkon çözümü: geçerli Material Symbols adı → glif; yoksa eski `iconUrl` (`achievements/<dosya>.svg`, baştaki `/`
 * olsun olmasın kök-göreli normalize edilir — derin rotada göreli çözülmesin); o da yoksa {@link BADGE_FALLBACK_ICON}.
 * Desene uymayan `iconUrl` (dış URL, path traversal) gösterilmez.
 */
export function resolveBadgeIcon(icon: string | null | undefined, iconUrl: string | null | undefined): BadgeIconSource {
  const name = icon?.trim();
  if (name && BADGE_ICON_NAME_PATTERN.test(name)) {
    return { kind: 'symbol', name };
  }
  const path = iconUrl?.trim().replace(/^\/+/, '');
  if (path && BADGE_ICON_PATTERN.test(path)) {
    return { kind: 'image', src: `/${path}` };
  }
  return { kind: 'symbol', name: BADGE_FALLBACK_ICON };
}

export type BadgeTranslate = (key: string, params?: Record<string, unknown>) => string;

export interface BadgeAriaLabelInput {
  state: BadgeMedallionState;
  name: string;
  /** Biçimlenmiş (ya da ham) mevcut değer. */
  current: string | number;
  /** Biçimlenmiş (ya da ham) hedef değer. */
  target: string | number;
  earnedDateUtc?: string | null;
  /** Kazanım tarihinin biçimi için BCP 47 dil kodu (ör. `tr-TR`). */
  locale: string;
}

/**
 * Öğenin erişilebilir adı (tasarım notu): "Kilitli: Soru Avcısı V, 0/500", "İlerlemede: …, 180/250",
 * "Kazanıldı: …, 12 Eyl", "Yeni kazanıldı: …". Medalyon `aria-hidden`; bu metin üst öğeye verilir.
 */
export function badgeAriaLabel(translate: BadgeTranslate, input: BadgeAriaLabelInput): string {
  const base = 'shared.badgeMedallion.aria';
  switch (input.state) {
    case 'new':
      return translate(`${base}.new`, { name: input.name });
    case 'earned': {
      const date = formatBadgeEarnedDate(input.earnedDateUtc, input.locale);
      return date
        ? translate(`${base}.earned`, { name: input.name, date })
        : translate(`${base}.earnedNoDate`, { name: input.name });
    }
    case 'locked':
      return translate(`${base}.locked`, { name: input.name, current: input.current, target: input.target });
    default:
      return translate(`${base}.inProgress`, { name: input.name, current: input.current, target: input.target });
  }
}

/**
 * Medalyonun yanında HER ZAMAN görünen durum metni (renk körlüğü için durum yalnız renkle anlatılmaz):
 * "Kazanıldı · 12 Eyl", "Yeni kazanıldı", "180 / 250", "Kilitli · 0 / 500".
 */
export function badgeStateText(translate: BadgeTranslate, input: Omit<BadgeAriaLabelInput, 'name'>): string {
  const base = 'shared.badgeMedallion.text';
  switch (input.state) {
    case 'new':
      return translate(`${base}.new`);
    case 'earned': {
      const date = formatBadgeEarnedDate(input.earnedDateUtc, input.locale);
      return date ? translate(`${base}.earned`, { date }) : translate(`${base}.earnedNoDate`);
    }
    case 'locked':
      return translate(`${base}.locked`, { current: input.current, target: input.target });
    default:
      return translate(`${base}.inProgress`, { current: input.current, target: input.target });
  }
}

/** "12 Eyl" / "Sep 12" — geçersiz/boş tarihte boş metin. */
export function formatBadgeEarnedDate(iso: string | null | undefined, locale: string): string {
  const date = parseBadgeUtcDate(iso);
  if (!date) {
    return '';
  }
  try {
    return new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short' }).format(date);
  } catch {
    return new Intl.DateTimeFormat(undefined, { day: 'numeric', month: 'short' }).format(date);
  }
}
