import { BadgeProgressItem, BadgeProgressSummary } from '../../services/badge.service';

/**
 * Günlük seri bilgisi (issue #99). Yeni veri çekilmez: dashboard'un zaten okuduğu rozet ilerleme
 * cevabından türetilir.
 */
export interface DailyStreakInfo {
  /** Üst üste aktif gün sayısı (`summary.currentActivityStreak`). */
  count: number;
  /** Sıradaki seri rozetinin hedef gün sayısı; tüm seri rozetleri kazanıldıysa null. */
  nextTarget: number | null;
  /** Sıradaki rozete kalan gün; `nextTarget` null ise null. */
  remaining: number | null;
}

/**
 * Rozet ilerleme DTO'su kural tipini taşımaz; `DailyStreak` rozetleri BadgeSeeder'da
 * `streak-path` ("İstikrar Yolu") yoluna bağlıdır, bu yüzden yol anahtarıyla seçilir.
 */
export const DAILY_STREAK_PATH_KEY = 'streak-path';

/**
 * Seri sayısı rozetin `currentValue`'sundan alınmaz: o değer en iyi seriye dayanır ve hedefle
 * tavanlanır, bozulmuş seride yüksek kalır. Sayı `summary.currentActivityStreak`'ten gelir;
 * rozet listesi yalnız "sıradaki hedef" için kullanılır.
 * Seri rozeti yoksa ya da güncel seri 0 ise null: chip/not hiç render edilmez (karar 1).
 */
export function dailyStreakFrom(
  summary: BadgeProgressSummary | null | undefined,
  progress: readonly BadgeProgressItem[] | null | undefined
): DailyStreakInfo | null {
  const streakBadges = (progress ?? []).filter((badge) => badge.pathKey === DAILY_STREAK_PATH_KEY);
  if (!streakBadges.length) return null;

  const count = Math.max(0, summary?.currentActivityStreak ?? 0);
  if (count <= 0) return null;

  const next = streakBadges
    .filter((badge) => !badge.isCompleted && (badge.targetValue ?? 0) > count)
    .sort((a, b) => a.targetValue - b.targetValue)[0];

  return next
    ? { count, nextTarget: next.targetValue, remaining: next.targetValue - count }
    : { count, nextTarget: null, remaining: null };
}
