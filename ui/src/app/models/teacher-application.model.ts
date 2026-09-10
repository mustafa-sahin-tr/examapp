/**
 * Issue #94 — GET /api/exam/admin/teacher-applications yanıt elemanı (PendingTeacherApplicationDto).
 * fullName / email auth-api'den çözümlenir; erişilemezse boş string gelir, UI fallback gösterir.
 */
export interface PendingTeacherApplication {
  teacherId: number;
  userId: number;
  fullName: string;
  email: string;
  /** ISO tarih (Teacher.CreateTime). */
  appliedAt: string;
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
