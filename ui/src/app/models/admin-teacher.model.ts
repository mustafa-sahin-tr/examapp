import { AdminSchoolPagedQuery } from './admin-paged-query.model';

/**
 * Issue #152 — GET /api/exam/admin/teachers yanıt elemanı (AdminTeacherListItemDto).
 * Zarf: `Paged<AdminTeacherListItem>` (models/test-instance.ts).
 */

/** Backend string olarak serialize eder (tutor.model.ts'deki sayısal enum'dan farklı). */
export type AdminTeacherApprovalStatus = 'Pending' | 'Approved' | 'Rejected';

export interface AdminTeacherListItem {
  /** Teacher kaydının id'si. (issue #262: auth-api `userId` artık bu listede dönmez.) */
  id: number;
  /** auth-api'den çözümlenir; erişilemezse boş string. */
  fullName: string;
  /**
   * Sunucuda MASKELENMİŞ e-posta (issue #246, KVKK): `a***@okul.k12.tr`. Tam adres bu listede hiç gelmez —
   * arama/kopyalama/mailto için kullanılamaz. auth-api'den çözümlenemezse boş string.
   */
  email: string;
  /** Bağımsız/okulsuz öğretmen için null. */
  schoolId: number | null;
  /** School.Name ya da legacy Teacher.SchoolName; ikisi de yoksa null. */
  schoolName: string | null;
  isIndependentTutor: boolean;
  approvalStatus: AdminTeacherApprovalStatus;
  /** Keycloak hesap durumu: true aktif, false devre dışı, null bilinmiyor (auth-api erişilemedi). */
  isEnabled: boolean | null;
  /**
   * Issue #289: öğretmen HESABI onaylı ve askıda değil — öğretmen özellikleri açık. "Askıya al" aksiyonu yalnız
   * bu true iken gösterilir. (`approvalStatus` başvuru durumudur; askıdaki öğretmende `Approved` kalabilir.)
   */
  accountApproved: boolean;
  /** Issue #289: hesap onayı askıda. "Askıyı kaldır" aksiyonu bu true iken gösterilir. */
  accountSuspended: boolean;
  /** Issue #289: askıya alma anı (ISO-8601 UTC); askıda değilse null. */
  accountSuspendedAt: string | null;
  /** Issue #289: admin'in girdiği askı nedeni; askıda değilse null. Yalnız admin'e döner. */
  accountSuspensionReason: string | null;
}

/** Sorgu parametreleri; `schoolId` ile `unassigned=true` birlikte gönderilemez. */
export type AdminTeacherListQuery = AdminSchoolPagedQuery;
