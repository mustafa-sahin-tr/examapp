/** Issue #187 — başvurunun durumu (backend `TeacherApprovalStatus` adı). */
export type TeacherApplicationStatus = 'Pending' | 'Approved' | 'Rejected';

/**
 * Issue #187 — liste durum filtresi (`?status=`): `pending` yalnız bekleyenler (en eski önce, varsayılan);
 * `all` her durum (önce bekleyenler en eski önce, sonra karar verilmişler en yeni karar önce).
 */
export type TeacherApplicationStatusFilter = 'pending' | 'all';

/** Issue #187 — GET /api/exam/admin/teacher-applications sorgusu. `page` 1-tabanlı; `pageSize` backend'de 1..100'e kırpılır. */
export interface TeacherApplicationListQuery {
  status: TeacherApplicationStatusFilter;
  page: number;
  pageSize: number;
}

/**
 * Issue #94 / #234 / #187 — GET /api/exam/admin/teacher-applications yanıt elemanı (TeacherApplicationListItemDto;
 * yanıt `Paged<T>`). Başvuru ya bağımsız öğretmen başvurusudur (#94) ya da okul bağlantısı talebidir (#234);
 * onay/red aynı uçlardan. fullName / email auth-api'den çözümlenir; erişilemezse boş string gelir, UI fallback gösterir.
 */
export interface TeacherApplicationListItem {
  /** Başvurunun (Teacher kaydının) id'si. Issue #262: auth-api `userId` artık dönmez. */
  teacherId: number;
  fullName: string;
  /**
   * Sunucuda MASKELENMİŞ e-posta (issue #262, KVKK): `a***@okul.k12.tr`. Tam adres listede hiç gelmez;
   * gerektiğinde satır bazında `TeacherApplicationDetail` (audit'li detay ucu) ile istenir.
   * auth-api'den çözümlenemezse boş string.
   */
  email: string;
  /** ISO tarih (Teacher.CreateTime). */
  appliedAt: string;
  /** Issue #234: true → bağımsız öğretmen başvurusu; false → okul bağlantısı talebi. */
  isIndependentTutor: boolean;
  /**
   * Issue #234: talep edilen okul; bağımsız başvuruda null. Issue #187: onaylanmış okul talebinde öğretmenin
   * (onaylı) okulu.
   */
  requestedSchoolId: number | null;
  /** Issue #234: talep edilen okulun adı; bağımsız başvuruda null. */
  requestedSchoolName: string | null;
  /** Issue #187. */
  status: TeacherApplicationStatus;
  /** Issue #187: yalnız `Rejected` iken dolu. */
  rejectionReason: string | null;
  /**
   * Issue #187: kararın verildiği an (ISO, UTC). Pending'de null; eski (#157 öncesi) kararlarda da null olabilir.
   */
  decidedAt: string | null;
  /**
   * Issue #287: öğretmen hesabı henüz onaylanmamış — bu başvurunun onayı öğretmen özelliklerini açar. Okul talebi
   * olmayan, bağımsız da olmayan ilk kayıtta `isIndependentTutor=false` ve `requestedSchoolId=null` olur (tür:
   * "öğretmen hesabı onayı"). Hesabı zaten onaylı öğretmenin sonraki (bağımsız/okul) başvurusunda false.
   */
  requiresAccountApproval: boolean;
}

/**
 * Issue #262 — GET /api/exam/admin/teacher-applications/{teacherId} yanıtı (TeacherApplicationDetailDto).
 * Liste satırıyla aynı alanlar; tek fark `email` TAM adrestir. Her çağrı backend'de audit'lenir ve liste
 * uçlarıyla aynı rate limit kovasını kullanır (429 + `Retry-After`). Issue #187: her durumdaki başvuru için döner;
 * başvuru olmayan öğretmen → 404.
 */
export interface TeacherApplicationDetail {
  /** Issue #262: auth-api `userId` bu yanıtta da dönmez. */
  teacherId: number;
  /** auth-api'den çözümlenir; erişilemezse boş string. */
  fullName: string;
  /** TAM e-posta (maskesiz); auth-api'den çözümlenemezse boş string. Kalıcı olarak saklanmamalı. */
  email: string;
  /** ISO tarih (Teacher.CreateTime). */
  appliedAt: string;
  isIndependentTutor: boolean;
  requestedSchoolId: number | null;
  requestedSchoolName: string | null;
  /** Issue #187. */
  status: TeacherApplicationStatus;
  /** Issue #187: yalnız `Rejected` iken dolu. */
  rejectionReason: string | null;
  /** Issue #187: karar anı (ISO, UTC); Pending'de null. */
  decidedAt: string | null;
  /** Issue #287: bkz. `TeacherApplicationListItem.requiresAccountApproval`. */
  requiresAccountApproval: boolean;
}

/**
 * BadgeService SignalR `TeacherApplicationSubmitted` push payload'ı (role:Admin grubuna gider).
 * Şekil: Services/BadgeService/Consumers/TeacherApplicationSubmittedConsumer.cs → SendAsync anonim nesnesi.
 */
export interface TeacherApplicationSubmittedPayload {
  notificationId: number;
  teacherId: number;
  userId: number;
  /** Ad çözümlenemezse backend "Bir öğretmen" gönderir; hiçbir zaman boş değil. */
  applicantName: string;
  title: string;
  body: string;
}

/**
 * Issue #277 — BadgeService SignalR `TeacherSchoolRequestSubmitted` push payload'ı (role:Admin grubuna gider):
 * öğretmen bir okul bağlantısı talebi gönderdi (yeni kayıt veya mevcut öğretmenin talebi), admin onayı bekliyor.
 * Şekil: Services/BadgeService/Consumers/TeacherSchoolRequestSubmittedConsumer.cs → SendAsync anonim nesnesi.
 */
export interface TeacherSchoolRequestSubmittedPayload {
  notificationId: number;
  teacherId: number;
  userId: number;
  requestedSchoolId: number;
  /** Ad çözümlenemezse backend yedek metin gönderir; boş gelmez. */
  applicantName: string;
  /** Okul adı çözümlenemezse backend yedek metin gönderir; boş gelmez. */
  schoolName: string;
  title: string;
  body: string;
}

/**
 * BadgeService SignalR `TeacherApplicationDecided` push payload'ı (issue #157 — Clients.User(sub),
 * yalnızca başvuru sahibine gider).
 * Şekil: Services/BadgeService/Consumers/TeacherApplicationDecisionConsumer.cs → SendAsync anonim nesnesi.
 * Güvenlik kararı: ret gerekçesi/admin kimliği YOK — sabit metin.
 */
export interface TeacherApplicationDecidedPayload {
  notificationId: number;
  teacherId: number;
  approved: boolean;
  /**
   * issue #157 review: true → bağımsız öğretmen başvurusu (/tutor-profile sayfası var); false → okul
   * bağlantısı talebi (öğretmenin özel bir profil sayfası yok — UI aksiyon göstermez).
   */
  isIndependentTutor: boolean;
  title: string;
  body: string;
}

/** POST .../teacher-applications/{id}/reject gövdesi (TeacherRejectRequestDto). 1..500 karakter. */
export interface TeacherRejectRequest {
  reason: string;
}

/**
 * approve / reject yanıtı (ResponseBaseDto). Backend success=false durumunu HTTP 404/409/400 ile
 * eşler; 2xx dışı yanıtlarda aynı şekil `HttpErrorResponse.error` içinde gelir.
 */
export interface TeacherApplicationActionResult {
  success: boolean;
  message: string;
  notFound?: boolean;
  conflict?: boolean;
}
