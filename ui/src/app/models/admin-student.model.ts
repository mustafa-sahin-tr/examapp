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
  /**
   * Sunucuda MASKELENMİŞ e-posta (issue #246, KVKK): `a***@okul.k12.tr`. Tam adres bu listede hiç gelmez —
   * arama/kopyalama/mailto için kullanılamaz. auth-api'den çözümlenemezse boş string.
   */
  email: string;
  /**
   * Sunucuda KISMEN MASKELENMİŞ öğrenci numarası (issue #262): son 4 hane görünür, ör. `****1234`;
   * 4 karakter veya daha kısaysa `****`. Numara yoksa boş string (null gelmez). Tam numara bu listede gelmez.
   */
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
