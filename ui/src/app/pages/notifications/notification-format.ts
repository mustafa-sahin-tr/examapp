import { PARENT_CHILD_QUERY_PARAM } from '../../models/parent-dashboard.model';
import { PARENT_HOME_URL } from '../../shared/guards/parent.guard';
import {
  AppNotification,
  BADGE_EARNED_NOTIFICATION_TYPE,
  BOOKING_TEACHER_UNAVAILABLE_NOTIFICATION_TYPE,
  parseBadgeEarnedData,
  parseBookingTeacherUnavailableData,
} from '../../models/notification.model';
import {
  AppRouteLink,
  isWorksheetCommentNotificationType,
  parseWorksheetCommentNotificationData,
  worksheetCommentLink,
} from '../../models/worksheet-comment.model';
import {
  DIRECT_MESSAGE_RECEIVED_TYPE,
  DIRECT_MESSAGE_REPORTED_TYPE,
  directMessageLink,
  parseDirectMessageNotificationData,
} from '../../models/direct-message.model';

/**
 * Issue #146 — bildirim listesi için göreli tarih ("3 dakika önce", "dün").
 * Saf fonksiyon: `now` ve `locale` dışarıdan verilir, test edilebilir.
 */

const UNITS: ReadonlyArray<{ unit: Intl.RelativeTimeFormatUnit; seconds: number }> = [
  { unit: 'year', seconds: 365 * 24 * 60 * 60 },
  { unit: 'month', seconds: 30 * 24 * 60 * 60 },
  { unit: 'week', seconds: 7 * 24 * 60 * 60 },
  { unit: 'day', seconds: 24 * 60 * 60 },
  { unit: 'hour', seconds: 60 * 60 },
  { unit: 'minute', seconds: 60 },
];

/**
 * Sunucu `DateTime` UTC değerini ISO string olarak döner; saat dilimi eki yoksa UTC kabul edilir
 * (tarayıcı eksiz ISO'yu yerel saat sayardı).
 */
export function parseUtcDate(iso: string): Date {
  const hasZone = /(Z|[+-]\d{2}:?\d{2})$/i.test(iso);
  return new Date(hasZone ? iso : `${iso}Z`);
}

/** `createdAt` için göreli tarih metni; geçersiz tarihte boş string. */
export function formatRelativeTime(iso: string, now: number, locale: string): string {
  const date = parseUtcDate(iso);
  const time = date.getTime();
  if (Number.isNaN(time)) {
    return '';
  }
  const diffSeconds = Math.round((time - now) / 1000);
  const formatter = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' });
  for (const { unit, seconds } of UNITS) {
    if (Math.abs(diffSeconds) >= seconds) {
      return formatter.format(Math.trunc(diffSeconds / seconds), unit);
    }
  }
  return formatter.format(0, 'second');
}

/**
 * Issue #105 — bildirim türüne göre ikon ve derin link. Yalnızca bilinen türlerin `data`'sı yorumlanır;
 * bozuk/eksik veri genel görünüme düşer (navigasyon yok).
 */
/** Issue #149: rozet bildiriminin medalyon girdileri (ham; biçim doğrulaması `resolveBadgeIcon`'da). */
export interface NotificationBadgeIcon {
  icon: string | null;
  iconUrl: string | null;
}

export interface NotificationPresentation {
  icon: string;
  /**
   * Issue #149: yalnız `BadgeEarned` — satırda 40px "Kazanıldı" medalyonu (ikon yoksa `military_tech`);
   * diğer türlerde `null` → Material ikonu (`icon`).
   */
  badge: NotificationBadgeIcon | null;
  /** Tıklanınca gidilecek hedef; `null` = yalnızca okundu işaretlenir. */
  route: AppRouteLink | null;
  /**
   * Issue #309: soru yorumu bildiriminde sorunun 1 tabanlı sırası (`data.questionOrder`) — listede "Soru {n}"
   * etiketi; yoksa/geçersizse null. Snackbar başlığını etkilemez.
   */
  questionOrder: number | null;
}

/** Rozet bildirimine tıklanınca gidilen öğrencinin rozet/ilerleme sayfası (`BadgeThropyComponent`). */
export const BADGE_PROGRESS_ROUTE = '/certificates';

/** Issue #298: öğrencinin randevu listesi (`StudentBookingsComponent`, route `my-bookings`). */
export const STUDENT_BOOKINGS_ROUTE = '/my-bookings';

export function describeNotification(notification: Pick<AppNotification, 'type' | 'data'>): NotificationPresentation {
  if (notification.type === BADGE_EARNED_NOTIFICATION_TYPE) {
    const data = parseBadgeEarnedData(notification.data);
    return {
      icon: 'emoji_events',
      badge: { icon: data?.icon ?? null, iconUrl: data?.iconUrl ?? null },
      route: { commands: [BADGE_PROGRESS_ROUTE] },
      questionOrder: null,
    };
  }
  if (isWorksheetCommentNotificationType(notification.type)) {
    const ref = parseWorksheetCommentNotificationData(notification.data);
    // Sıra yalnız soru yorumunda anlamlı (worksheet seviyesinde questionId null).
    const questionOrder = ref?.questionId != null ? (ref.questionOrder ?? null) : null;
    return { icon: 'forum', badge: null, route: ref ? worksheetCommentLink(ref) : null, questionOrder };
  }
  if (notification.type === DIRECT_MESSAGE_RECEIVED_TYPE) {
    // Issue #106 b: `data` doğrulanır; bozuksa navigasyon yok. Link gönderen rolüne göre doğru gelen kutusuna gider.
    const ref = parseDirectMessageNotificationData(notification.data);
    return { icon: 'chat', badge: null, route: ref ? directMessageLink(ref) : null, questionOrder: null };
  }
  if (notification.type === DIRECT_MESSAGE_REPORTED_TYPE) {
    // Admin şikayet bildirimi: şikayet listesi için ayrı bir admin sayfası henüz yok → hedef yok.
    return { icon: 'report', badge: null, route: null, questionOrder: null };
  }
  if (notification.type === BOOKING_TEACHER_UNAVAILABLE_NOTIFICATION_TYPE) {
    // Issue #298: `data` yalnız doğrulanır, linke taşınmaz; bozuksa navigasyon yok.
    const data = parseBookingTeacherUnavailableData(notification.data);
    return {
      icon: 'event_busy',
      badge: null,
      route: data ? { commands: [STUDENT_BOOKINGS_ROUTE] } : null,
      questionOrder: null,
    };
  }
  const parentPresentation = describeParentNotification(notification);
  if (parentPresentation) {
    return parentPresentation;
  }
  return { icon: 'notifications', badge: null, route: null, questionOrder: null };
}




/**
 * Issue #423: veli bildirim türleri → ikon + link. Veliye giden türler `/parent` açar, `data.studentId` geçerli pozitif
 * tamsayıysa `?child=<studentId>` eklenir (panel geçersiz/başkasının id'sini zaten yok sayar). Öğrenciye giden türler
 * (`ParentLinkedToStudent`, `ParentUnlinkedToStudent`) yalnız okundu işaretler (hedef yok). Bozuk `data` → çocuk seçimsiz `/parent`.
 */
const PARENT_NOTIFICATION_PRESENTATION: Readonly<Record<string, { icon: string; toParentPage: boolean }>> = {
  ParentLinkedToParent: { icon: 'family_restroom', toParentPage: true },
  ParentLinkedToStudent: { icon: 'family_restroom', toParentPage: false },
  ParentUnlinkedToParent: { icon: 'link_off', toParentPage: true },
  ParentUnlinkedToStudent: { icon: 'link_off', toParentPage: false },
  // Issue #436: diğer veli ayrıldı → birincil velinin paneli o çocuğu açar.
  ParentCoParentLeftToPrimary: { icon: 'link_off', toParentPage: true },
  ParentHomeworkOverdue: { icon: 'assignment_late', toParentPage: true },
  ParentChildTestCompleted: { icon: 'task_alt', toParentPage: true },
};

function parseParentNotificationStudentId(data: string | null): number | null {
  if (!data) {
    return null;
  }
  try {
    const value = (JSON.parse(data) as { studentId?: unknown } | null)?.studentId;
    return typeof value === 'number' && Number.isInteger(value) && value > 0 ? value : null;
  } catch {
    return null;
  }
}

function describeParentNotification(notification: Pick<AppNotification, 'type' | 'data'>): NotificationPresentation | null {
  const entry = PARENT_NOTIFICATION_PRESENTATION[notification.type];
  if (!entry) {
    return null;
  }
  if (!entry.toParentPage) {
    return { icon: entry.icon, badge: null, route: null, questionOrder: null };
  }
  const studentId = parseParentNotificationStudentId(notification.data);
  return {
    icon: entry.icon,
    badge: null,
    route: studentId === null
      ? { commands: [PARENT_HOME_URL] }
      : { commands: [PARENT_HOME_URL], queryParams: { [PARENT_CHILD_QUERY_PARAM]: studentId } },
    questionOrder: null,
  };
}
