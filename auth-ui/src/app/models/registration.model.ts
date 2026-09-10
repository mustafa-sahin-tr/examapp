export interface Grade {
  id: number;
  name: string;
}

/** GET /api/school — anonymous, used to populate school dropdowns before login. */
export interface School {
  id: number;
  name: string;
  city?: string;
}

/** Response shape shared by /api/exam/{student,teacher,parent}/register. */
export interface RegisterProfileResponse {
  accessToken: string;
  expiresIn: number;
  profileId: number;
}

export interface RegisterStudentPayload {
  studentNumber: string;
  schoolId: number | null;
  gradeId: number;
}

/** POST /api/teacher/register — mirrors backend RegisterTeacherDto. */
export interface RegisterTeacherPayload {
  schoolId: number | null;
  /** true → bağımsız özel ders öğretmeni; hesap admin onayı bekler (Pending). */
  isIndependentTutor: boolean;
}
