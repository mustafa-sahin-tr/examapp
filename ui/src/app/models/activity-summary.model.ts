/**
 * Issue #572: BadgeService `GET /api/badge/reports/users/{userId}/activity-summary` (`ActivitySummaryDto`).
 * Etkinlikler puansızdır (#569): yalnız sayı, süre, seri ve rozet ilerlemesi.
 */
export interface ActivityTodayStats {
  /** Bugün (Europe/Istanbul) tamamlanan etkinlik sayısı. */
  activityCount: number;
  /** Bugün çalışılan etkin süre (dakika, aşağı yuvarlanmış). */
  activeMinutes: number;
  /** Ardışık etkinlik günü. */
  streakDays: number;
}

export interface ActivityBadgeProgress {
  code: string;
  name: string;
  current: number;
  target: number;
  earned: boolean;
}

export interface ActivitySummaryResponse {
  today: ActivityTodayStats;
  /** En çok 3 kazanılmamış etkinlik rozeti, en yakını önce; hepsi kazanıldıysa boş. */
  badges: ActivityBadgeProgress[];
}

/** Ekranda gösterilecek hal: "Bugün" kartı yalnız bugün etkinlik varsa, rozet kartı en yakın kazanılmamış rozet. */
export interface ActivityProgressView {
  today: ActivityTodayStats | null;
  badge: ActivityBadgeProgress | null;
}

export function toActivityProgressView(response: ActivitySummaryResponse | null | undefined): ActivityProgressView {
  const today = response?.today;
  const badge = (response?.badges ?? []).find((b) => !!b && !b.earned && b.target > 0) ?? null;
  return {
    today: today && today.activityCount > 0 ? today : null,
    badge,
  };
}
