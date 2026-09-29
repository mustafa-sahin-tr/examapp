/**
 * Issue #146 — BadgeService kalıcı bildirimleri.
 * `Services/BadgeService/Controllers/NotificationsController.cs` içindeki `NotificationDto` ile birebir eşleşir;
 * gateway üzerinden `/api/badge/notifications/...`.
 *
 * `title` ve `body` sunucuda kullanıcının diline göre üretilir; UI çevirmez.
 */
export interface AppNotification {
  id: number;
  /** Bildirim türü (ör. `BadgeEarned`, `WorksheetReminderDue`, `BookingRequestCreated`). */
  type: string;
  title: string;
  body: string;
  /** Türe özgü JSON string; şeması türe göre değişir. Bilinmeyen türde yorumlanmaz. */
  data: string | null;
  isRead: boolean;
  /** ISO 8601, UTC. */
  createdAt: string;
}

import { BADGE_ICON_PATTERN } from './badge-definition-admin.model';

/** `BadgeEvaluator.NotificationType` — rozet kazanımı bildirimi. */
export const BADGE_EARNED_NOTIFICATION_TYPE = 'BadgeEarned';

/** `BadgeEarned` bildiriminin `data` JSON'u (`BadgeEvaluator.AddBadgeEarnedNotificationsAsync`). */
export interface BadgeEarnedNotificationData {
  badgeDefinitionId: string;
  badgeCode: string;
  iconUrl: string | null;
}

/**
 * `BadgeEarned` `data` alanını güvenli biçimde ayrıştırır. JSON bozuksa ya da beklenen alanlar
 * yoksa `null` döner — çağıran genel görünüme düşer.
 */
export function parseBadgeEarnedData(data: string | null): BadgeEarnedNotificationData | null {
  if (!data) {
    return null;
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(data);
  } catch {
    return null;
  }
  if (!parsed || typeof parsed !== 'object') {
    return null;
  }
  const record = parsed as Record<string, unknown>;
  if (typeof record['badgeDefinitionId'] !== 'string') {
    return null;
  }
  const iconUrl = record['iconUrl'];
  return {
    badgeDefinitionId: record['badgeDefinitionId'],
    badgeCode: typeof record['badgeCode'] === 'string' ? record['badgeCode'] : '',
    iconUrl: typeof iconUrl === 'string' && iconUrl.trim() ? iconUrl : null,
  };
}

/**
 * Savunma katmanı: `iconUrl` yalnızca uygulamanın kendi rozet ikon yoluna (`achievements/<dosya>.svg`,
 * backend `BadgeIconValidator` ile aynı kural) uyuyorsa kök-göreli `src` olarak döner; aksi hâlde `null`
 * (dış URL / path traversal gösterilmez, çağıran varsayılan ikona düşer).
 */
export function badgeIconSrc(iconUrl: string | null | undefined): string | null {
  const value = iconUrl?.trim();
  return value && BADGE_ICON_PATTERN.test(value) ? `/${value}` : null;
}
