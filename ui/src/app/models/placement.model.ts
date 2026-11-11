import { StudentGradeBand } from './subscription';

/** Issue #1114: ücretsiz seviye tespiti. */
export interface PlacementStatus {
  available: boolean;
  taken: boolean;
  gradeName: string | null;
  questionCount: number;
}

export interface PlacementAnswerOption {
  id: number;
  text: string | null;
  imageUrl: string | null;
}

export interface PlacementQuestion {
  id: number;
  subjectName: string | null;
  text: string | null;
  imageUrl: string | null;
  options: PlacementAnswerOption[];
}

export interface PlacementAnswer {
  questionId: number;
  answerId: number | null;
}

export interface PlacementSubjectScore {
  subjectId: number;
  subjectName: string;
  correct: number;
  total: number;
  percent: number;
}

export interface PlacementTopic {
  topicId: number;
  topicName: string;
  subjectName: string | null;
  correct: number;
  total: number;
}

export type PlacementTier = 'basic' | 'essential' | 'pro';

export interface PlacementSuggestion {
  tier: PlacementTier;
  planKey: string;
  /** Doluysa tek ders yeter (#1109). */
  subjectId: number | null;
  subjectName: string | null;
  band: StudentGradeBand | null;
}

export interface PlacementReport {
  gradeName: string | null;
  submittedAtUtc: string;
  correct: number;
  total: number;
  percent: number;
  subjects: PlacementSubjectScore[];
  strongTopics: PlacementTopic[];
  weakTopics: PlacementTopic[];
  suggestion: PlacementSuggestion;
}

export interface AdminPlacementTest {
  gradeId: number;
  gradeName: string;
  questionCount: number;
  generatedAtUtc: string | null;
  publishedAtUtc: string | null;
  attemptCount: number;
  subjects: PlacementSubjectScore[];
}
