import { TopicPerformanceItem } from './topic-performance.model';
import { MockExamResult } from '../services/mock-exam.service';

/** Issue #700: öğretmenin kendi okulundan ya da onaylı özel ders verdiği öğrenci. */
export type CoachRelationship = 'School' | 'PrivateLesson';

export interface CoachStudent {
  id: number;
  fullName: string;
  gradeId: number | null;
  relationship: CoachRelationship;
  /** Issue #893: son etkin gün (`yyyy-MM-dd`, sunucunun yerel takvimi); hiç yoksa null. */
  lastActiveDay: string | null;
  /** Çözülmemiş yanlış; kalabalık listede hesaplanmayanlar null. */
  mistakeCount: number | null;
  /** Bu haftanın bu koça ait hedefleri ve öğrencinin işaretledikleri. */
  goalCount: number;
  goalsDone: number;
  /** Sunucu "dikkat isteyen önce" sıralar: 7+ gün etkinlik yok, 10+ yanlış ya da bu hafta hedef yok. */
  needsAttention: boolean;
}

export interface CoachGoal {
  id: number;
  /** Haftanın Pazartesi'si (`yyyy-MM-dd`). */
  weekStart: string;
  title: string;
  updatedAt: string;
  /** Issue #893: öğrenci "yaptım" diye işaretlediyse an. */
  completedAt: string | null;
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
  id: number;
  weekStart: string;
  title: string;
  coachName: string | null;
  /** Issue #893: öğrencinin "yaptım" işareti. */
  done: boolean;
}

/** Veli yanıtı: not ve öğretmen kimliği yok. */
export interface ParentCoachSummary {
  weekStart: string;
  plannedCount: number;
  completedCount: number;
  goals: { title: string; done: boolean }[];
}

/** Issue #893: sunucunun `CoachService.InactiveDays` / `ManyMistakes` eşikleri (satırdaki uyarı rengi). */
export const COACH_INACTIVE_DAYS = 7;
export const COACH_MANY_MISTAKES = 10;

/** Sunucu sınırları (`CoachWeeklyGoal.MaxTitleLength`, `CoachNote.MaxBodyLength`, `CoachService.MaxGoalsPerWeek`). */
export const COACH_GOAL_MAX_LENGTH = 300;
export const COACH_NOTE_MAX_LENGTH = 2000;
export const COACH_GOALS_PER_WEEK = 10;
/** Hedef bu hafta ile 4 hafta sonrası arasında yazılabilir. */
export const COACH_GOAL_WEEKS_AHEAD = 4;
