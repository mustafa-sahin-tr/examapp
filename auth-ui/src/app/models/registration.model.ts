export interface Grade {
  id: number;
  name: string;
}

/** Response shape shared by /api/exam/{student,teacher,parent}/register. */
export interface RegisterProfileResponse {
  accessToken: string;
  expiresIn: number;
  profileId: number;
}

export interface RegisterStudentPayload {
  studentNumber: string;
  schoolName: string;
  gradeId: number;
}

export interface RegisterTeacherPayload {
  schoolName: string;
}
