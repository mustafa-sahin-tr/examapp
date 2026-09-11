/**
 * Issue #95 — bağımsız öğretmen (özel ders) profili ve öğrenci araması.
 * Backend karşılığı: api/ExamApp.Api/Models/Dtos/Tutors/TutorProfileDtos.cs
 */

/** Backend enum: ExamApp.Api.Data.TeacherApprovalStatus (JSON'a sayı olarak serialize edilir). */
export enum TeacherApprovalStatus {
  Pending = 0,
  Approved = 1,
  Rejected = 2,
}

/** TutorSubjectDto — öğretmenin verdiği ders (Subject tablosundan). */
export interface TutorSubject {
  subjectId: number;
  name: string;
}

/** GET /api/exam/teacher/tutor-profile yanıtı (TutorProfileDto). Sadece kaydın sahibi görür. */
export interface TutorProfile {
  teacherId: number;
  approvalStatus: TeacherApprovalStatus;
  subjects: TutorSubject[];
  hourlyRate: number | null;
  teachesOnline: boolean;
  teachesInPerson: boolean;
  bio: string | null;
}

/**
 * PUT /api/exam/teacher/tutor-profile gövdesi (UpdateTutorProfileDto).
 * İş kuralı: en az 1 ders, ücret > 0, online/yüz yüze'den en az biri true, bio en fazla 500 karakter.
 */
export interface UpdateTutorProfileRequest {
  subjectIds: number[];
  hourlyRate: number;
  teachesOnline: boolean;
  teachesInPerson: boolean;
  bio?: string | null;
}

/** GET /api/exam/teacher/search query parametreleri (TeacherSearchFilterDto). Tümü opsiyonel. */
export interface TutorSearchFilter {
  subjectId?: number | null;
  minPrice?: number | null;
  maxPrice?: number | null;
  /** true ise sadece online ders verenler; null/false filtre uygulamaz. */
  online?: boolean | null;
  /** true ise sadece yüz yüze ders verenler; null/false filtre uygulamaz. */
  inPerson?: boolean | null;
  skip?: number;
  take?: number;
}

/** Arama sonucu satırı (TeacherSearchResultDto). Bio ilk 160 karaktere kısaltılmış gelir. */
export interface TutorSearchResult {
  teacherId: number;
  fullName: string;
  subjects: TutorSubject[];
  hourlyRate: number | null;
  teachesOnline: boolean;
  teachesInPerson: boolean;
  bio: string | null;
}

/** GET /api/exam/teacher/{id}/public-profile yanıtı (TeacherPublicProfileDto). Bio tam metin. */
export interface TutorPublicProfile {
  teacherId: number;
  fullName: string;
  avatar: string;
  subjects: TutorSubject[];
  hourlyRate: number | null;
  teachesOnline: boolean;
  teachesInPerson: boolean;
  bio: string | null;
}
