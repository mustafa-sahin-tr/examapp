export interface Grade {
  id: number;
  name: string;
}

export interface Student {
  id: number;
  userId: number;
  user?: any; // Eğer gerekirse
  studentNumber: string;
  schoolName: string;
  grade: Grade;
  themePreset?: string; // 🎨 Theme tercihi
  themeCustomConfig?: string; // 🎨 Custom theme config (JSON)
  /** Issue #361: doğrulanmış okul (`POST /api/exam/auth/refresh`); bekleyen üyelikte null. */
  schoolId?: number | null;
  /** Issue #361: öğrencinin seçtiği, okul onayı bekleyen okul; doğrulanmışsa/okulsuzsa null. */
  pendingSchoolId?: number | null;
  /** Issue #361: bekleyen okulun adı ("Okul onayı bekleniyor" bandı). */
  pendingSchoolName?: string | null;
}

export interface StudentLookup {
  id: number;
  userId: number;
  studentNumber: string;
  schoolName: string;
  gradeId?: number | null;
  fullName?: string;
  email?: string;
  avatarUrl?: string;
}
