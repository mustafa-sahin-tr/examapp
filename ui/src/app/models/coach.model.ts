import { TopicPerformanceItem } from './topic-performance.model';
import { MockExamResult } from '../services/mock-exam.service';

/** Issue #700: öğretmenin kendi okulundan ya da onaylı özel ders verdiği öğrenci. */
export type CoachRelationship = 'School' | 'PrivateLesson';

export interface CoachStudent {
  id: number;
  fullName: string;
  gradeId: number | null;
  relationship: CoachRelationship;
}

export interface CoachGoal {
  id: number;
  /** Haftanın Pazartesi'si (`yyyy-MM-dd`). */
  weekStart: string;
  title: string;
  updatedAt: string;
}

export interface SaveCoachGoal {
  title: string;
  /** Boşsa bu hafta; güncellemede sunucu yok sayar. */
  weekStart?: string;
}

export interface CoachNote {
  id: number;
  body: string;
  createdAt: string;
  updatedAt: string;
}

export interface CoachPlanCompletion {
  weekStart: string;
  planned: number;
  completed: number;
  /** false: kişisel program okunamadı, sayılar yalnız "Planım" testleri. */
  includesProgram: boolean;
}

/** Soru ve cevap içeriği yok: yalnız sayılar ve konu adları. */
export interface CoachOverview {
  studentId: number;
  fullName: string;
  plan: CoachPlanCompletion;
  recentMockExams: MockExamResult[];
  weakTopics: TopicPerformanceItem[];
  activeMistakeCount: number;
  /** Yalnız bu öğretmenin bu haftaki hedefleri. */
  goals: CoachGoal[];
}

export interface StudentCoachGoal {
  weekStart: string;
  title: string;
  coachName: string | null;
}

/** Veli yanıtı: not ve öğretmen kimliği yok. */
export interface ParentCoachSummary {
  weekStart: string;
  plannedCount: number;
  completedCount: number;
  goals: { title: string }[];
}

/** Sunucu sınırları (`CoachWeeklyGoal.MaxTitleLength`, `CoachNote.MaxBodyLength`, `CoachService.MaxGoalsPerWeek`). */
export const COACH_GOAL_MAX_LENGTH = 300;
export const COACH_NOTE_MAX_LENGTH = 2000;
export const COACH_GOALS_PER_WEEK = 10;
/** Hedef bu hafta ile 4 hafta sonrası arasında yazılabilir. */
export const COACH_GOAL_WEEKS_AHEAD = 4;
