/** Issue #692: MEB kazanımı (LearningOutcomeDto). */
export interface LearningOutcome {
  id: number;
  code: string;
  description: string;
  curriculumVersion: 'Previous' | 'Maarif';
  gradeId: number;
  subjectId: number | null;
  subTopicId: number | null;
}
