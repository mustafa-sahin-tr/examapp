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
  schedules: UserProgramSchedule[];
  studyPageSchedules: UserProgramStudyPageSchedule[];
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

export interface UserProgramStudyPageSchedule {
  id: number;
  userProgramId: number;
  studyPageId: number;
  studyPageTitle: string;
  studyPageCoverImageUrl?: string | null;
  startDate: string;
  endDate: string;
}

export interface ProgramStudyPageScheduleRequest {
  items: ProgramStudyPageScheduleItem[];
}

export interface ProgramStudyPageScheduleItem {
  studyPageId: number;
  startDate: string;
  endDate: string;
}
