import { StudyPageContentType, StudyPageLinkPlatform } from './study-page';

export interface CreateProgramRequest {
  programName: string;
  description: string;
  startDate?: string;
  endDate?: string;
  userSelections: UserSelection[];
}

export interface UserSelection {
  stepId: number;
  selectedValues: string[];
}

export interface UserProgram {
  id: number;
  userId: string;
  programName: string;
  description: string;
  createdDate: string;
  startDate?: string;
  endDate?: string;
  isActive: boolean;
  studyType: string;
  studyDuration?: string;
  questionsPerDay?: number;
  subjectsPerDay: number;
  restDays: string;
  difficultSubjects: string;
  completedPageCount: number;
  totalPageCount: number;
  progressPercentage: number;
  schedules: UserProgramSchedule[];
  studyItemSchedules: UserProgramStudyPageSchedule[];
}

export interface UserProgramSchedule {
  id: number;
  userProgramId: number;
  scheduleDate: string;
  subjectId: number;
  subjectName: string;
  studyDurationMinutes?: number;
  questionCount?: number;
  isCompleted: boolean;
  completedDate?: string;
  notes?: string;
}

/**
 * Programa eklenmiş çalışma etkinliği planı — backend `UserProgramStudyPageScheduleDto` JSON'u
 * (camelCase, enum'lar sayısal). Tip adı geçmişten kaldı; alanlar `studyItem*`.
 * Tipe özgü alanlar (url/platform, kitap alanları) yalnızca ilgili `contentType` için dolar.
 */
export interface UserProgramStudyPageSchedule {
  id: number;
  userProgramId: number;
  studyItemId: number;
  studyItemTitle: string;
  studyItemCoverImageUrl?: string | null;
  startDate: string;
  endDate: string;
  isCompleted: boolean;
  completedDate?: string | null;
  contentType: StudyPageContentType;
  // Link
  url?: string | null;
  platform?: StudyPageLinkPlatform | null;
  // BookPageRange
  bookName?: string | null;
  bookTestName?: string | null;
  startPage?: number | null;
  endPage?: number | null;
}

export interface ProgramStudyPageScheduleRequest {
  items: ProgramStudyPageScheduleItem[];
}

export interface ProgramStudyPageScheduleItem {
  studyItemId: number;
  startDate: string;
  endDate: string;
}
