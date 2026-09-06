import { Test } from './test-instance';

export interface WorksheetStats {
  solverCount: number;
  averageScorePercent: number | null;
}

export interface WorksheetTopicBreakdown {
  topicId: number | null;
  name: string;
  questionCount: number;
  weightPercent: number;
}

export interface WorksheetSampleQuestion {
  id: number;
  text: string | null;
  imageUrl: string | null;
}

export interface WorksheetAttempt {
  instanceId: number;
  completedDate: string | null;
  durationSeconds: number;
  correctCount: number;
  totalCount: number;
  scorePercent: number;
}

export interface SimilarWorksheet {
  id: number;
  name: string;
  questionCount: number;
  isPracticeTest: boolean;
  averageScorePercent: number | null;
}

export interface WorksheetHardestQuestion {
  questionId: number;
  order: number;
  text: string | null;
  subtopicName: string | null;
  answeredCount: number;
  correctPercent: number;
}

export interface WorksheetDifficultyDistribution {
  easy: number;
  medium: number;
  hard: number;
}

export interface WorksheetTeacherInsights {
  hardestQuestions: WorksheetHardestQuestion[];
  difficultyDistribution: WorksheetDifficultyDistribution;
  classifiedCount: number;
  totalQuestionCount: number;
  unclassifiedCount: number;
}

export interface WorksheetTopicSuccess {
  topicId: number | null;
  name: string;
  correctCount: number;
  totalCount: number;
  successPercent: number;
}

export interface WorksheetRank {
  position: number;
  totalStudents: number;
  classAveragePercent: number;
}

export interface WorksheetCompletedResult {
  instanceId: number;
  scorePercent: number;
  correctCount: number;
  wrongCount: number;
  emptyCount: number;
  durationSeconds: number;
  topicSuccess: WorksheetTopicSuccess[];
  rank: WorksheetRank | null;
}

export type WorksheetReminderStatus = 'Pending' | 'Sent' | 'Cancelled';

export interface WorksheetReminder {
  worksheetId: number;
  scheduledFor: string;
  remindBeforeMinutes: number;
  status: WorksheetReminderStatus;
}

export interface WorksheetReminderRequest {
  scheduledFor: string;
  remindBeforeMinutes: number;
}

export interface WorksheetDetail {
  worksheet: Test;
  plannedReminder: WorksheetReminder | null;
  stats: WorksheetStats;
  topicBreakdown: WorksheetTopicBreakdown[];
  outcomes: string[];
  rewardBadgeText: string | null;
  sampleQuestion: WorksheetSampleQuestion | null;
  attempts: WorksheetAttempt[];
  improvementPoints: number | null;
  similarWorksheets: SimilarWorksheet[];
  teacherInsights: WorksheetTeacherInsights | null;
  completedResult: WorksheetCompletedResult | null;
}

export interface WorksheetFromMistakesResult {
  worksheetId: number;
}

export interface CopyWorksheetResult {
  worksheetId: number;
}
