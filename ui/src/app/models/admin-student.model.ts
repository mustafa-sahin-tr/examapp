import { AdminSchoolPagedQuery } from './admin-paged-query.model';

/**
 * Issue #153 — GET /api/exam/admin/students yanıt elemanı.
 * Zarf: `Paged<AdminStudentListItem>` (models/test-instance.ts).
 */
export interface AdminStudentListItem {
  /** Student kaydının id'si. */
  id: number;
  /** auth-api'den çözümlenir; erişilemezse boş string. */
  fullName: string;
  /** auth-api'den çözümlenir; erişilemezse boş string. */
  email: string;
  /** Numara yoksa boş string (null gelmez). */
  studentNumber: string;
  /** Okula bağlı olmayan öğrenci için null. */
  schoolId: number | null;
  schoolName: string | null;
  gradeId: number | null;
  gradeName: string | null;
  /** Keycloak hesap durumu: true aktif, false devre dışı, null bilinmiyor (auth-api erişilemedi). */
  isEnabled: boolean | null;
}

/** Sorgu parametreleri; `schoolId` ile `unassigned=true` birlikte gönderilemez. */
export type AdminStudentListQuery = AdminSchoolPagedQuery;
