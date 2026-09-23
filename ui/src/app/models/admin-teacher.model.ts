/**
 * Issue #152 — GET /api/exam/admin/teachers yanıt elemanı (AdminTeacherListItemDto).
 * Zarf: `Paged<AdminTeacherListItem>` (models/test-instance.ts).
 */

/** Backend string olarak serialize eder (tutor.model.ts'deki sayısal enum'dan farklı). */
export type AdminTeacherApprovalStatus = 'Pending' | 'Approved' | 'Rejected';

export interface AdminTeacherListItem {
  /** Teacher kaydının id'si. */
  id: number;
  /** auth-api kullanıcı id'si. */
  userId: number;
  /** auth-api'den çözümlenir; erişilemezse boş string. */
  fullName: string;
  /** auth-api'den çözümlenir; erişilemezse boş string. */
  email: string;
  /** Bağımsız/okulsuz öğretmen için null. */
  schoolId: number | null;
  /** School.Name ya da legacy Teacher.SchoolName; ikisi de yoksa null. */
  schoolName: string | null;
  isIndependentTutor: boolean;
  approvalStatus: AdminTeacherApprovalStatus;
  /** Keycloak hesap durumu: true aktif, false devre dışı, null bilinmiyor (auth-api erişilemedi). */
  isEnabled: boolean | null;
}

/**
 * Sorgu parametreleri. `schoolId` ile `unassigned=true` birlikte gönderilemez (backend 400 döner).
 * `page` 1-tabanlı; `pageSize` backend'de 1..100 aralığına kırpılır.
 */
export interface AdminTeacherListQuery {
  page: number;
  pageSize: number;
  schoolId?: number | null;
  unassigned?: boolean;
}
