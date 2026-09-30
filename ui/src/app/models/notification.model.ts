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
  /**
   * Issue #305 dilim B: aynı thread'in okunmamış yorum bildirimleri tek satırda birleştirilir; satırın temsil ettiği
   * olay sayısı (başlık sunucuda "N yeni yorum…"). Eski sunucuda yok → 1. Gösterimden önce {@link toCoalescedCount}.
   */
  coalescedCount?: number;
}

/** Birleştirilmiş sayının gösterim üst sınırı ("99+"). */
export const COALESCED_COUNT_DISPLAY_MAX = 99;

/**
 * Güvenilmeyen `coalescedCount` (REST öğesi ya da SignalR payload'ı): yalnız pozitif güvenli tam sayı kabul edilir;
 * yok / bozuk / 0 / negatif / ondalık / string → 1 (tek olay, mevcut davranış).
 */
export function toCoalescedCount(value: unknown): number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : 1;
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
 * Issue #298 — `BookingTeacherUnavailableConsumer.NotificationType`: öğrencinin öğretmeni askıya alındı,
 * planlanmış dersleri etkilendi. Title/body sunucuda yerelleştirilir.
 */
export const BOOKING_TEACHER_UNAVAILABLE_NOTIFICATION_TYPE = 'BookingTeacherUnavailable';

/** `BookingTeacherUnavailable` bildiriminin `data` JSON'u (`{ teacherId, bookingIds }`). */
export interface BookingTeacherUnavailableNotificationData {
  teacherId: number;
  bookingIds: number[];
}

function isPositiveSafeInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
}

/** Tek bildirimde kabul edilen en fazla randevu kimliği (savunma sınırı; backend öğrenci başına gruplar). */
const MAX_BOOKING_IDS = 500;

/**
 * `BookingTeacherUnavailable` `data` alanını güvenilmeyen veri olarak ayrıştırır. JSON bozuksa, `teacherId`
 * pozitif tam sayı değilse, `bookingIds` dizi değilse ya da içinde pozitif tam sayı olmayan bir eleman varsa
 * `null` döner. Fazladan alanlar yok sayılır.
 */
export function parseBookingTeacherUnavailableData(
  data: string | null
): BookingTeacherUnavailableNotificationData | null {
  if (!data) {
    return null;
  }
  let parsed: unknown;
  try {
    parsed = JSON.parse(data);
  } catch {
    return null;
  }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    return null;
  }
  const record = parsed as Record<string, unknown>;
  const teacherId = record['teacherId'];
  const bookingIds = record['bookingIds'];
  if (!isPositiveSafeInteger(teacherId) || !Array.isArray(bookingIds) || bookingIds.length > MAX_BOOKING_IDS) {
    return null;
  }
  if (!bookingIds.every(isPositiveSafeInteger)) {
    return null;
  }
  return { teacherId, bookingIds: [...bookingIds] };
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
