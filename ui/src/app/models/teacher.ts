import { TeacherApplicationStatus } from './teacher-application.model';

export interface Teacher {
  id: number;
  userId: number;
  user?: any; // Eğer gerekirse
  schoolName: string;
  /** `TeacherDto.SchoolId` — okulsuz öğretmende null (issue #191). */
  schoolId?: number | null;
  themePreset?: string; // 🎨 Theme tercihi
  themeCustomConfig?: string; // 🎨 Custom theme config (JSON)
  /**
   * Issue #287 (`TeacherDto.TeacherAccountApproved`): öğretmen hesabı admin tarafından onaylandı mı. false iken
   * öğretmen uçları 403 `TeacherNotApproved` döner. Yalnız exam-api `POST /api/exam/auth/refresh` doldurur;
   * login/exchange (auth-api) yanıtında yoktur → undefined = bilinmiyor.
   */
  teacherAccountApproved?: boolean;
  /**
   * Issue #287: mevcut başvurunun durumu. Hesabı onaylı öğretmenin sonraki (bağımsız/okul) başvurusu da
   * `Pending` olabilir — erişim kararı yalnız `teacherAccountApproved` ile verilir.
   */
  teacherApplicationStatus?: TeacherApplicationStatus;
  /** Issue #287: yalnız `teacherApplicationStatus === 'Rejected'` iken dolu. */
  rejectionReason?: string | null;
}
