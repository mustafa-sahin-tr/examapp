/** Issue #1113: haftalık çalışma planı (Pro, `weekly-plan`). */
export const STUDY_PLAN_FEATURE = 'weekly-plan';
export const STUDY_PLAN_ROUTE = '/study-plan';

export type StudyPlanTaskKind = 'assignment' | 'mockExam' | 'practiceTopic' | 'mistakes' | 'flashcards' | 'practice';

export interface StudyPlanTask {
  id: number;
  kind: StudyPlanTaskKind;
  title: string | null;
  subjectName: string | null;
  subjectId: number | null;
  topicId: number | null;
  worksheetId: number | null;
  mockExamId: number | null;
  dueAtUtc: string | null;
  done: boolean;
}

export interface StudyPlan {
  /** "yyyy-MM-dd" Pazartesi. */
  weekStart: string;
  doneCount: number;
  totalCount: number;
  tasks: StudyPlanTask[];
}

export interface StudyPlanTaskLink {
  route: string;
  queryParams?: Record<string, number>;
}

/** Görevin açtığı sayfa; konu görevi pratiği o konuyla açar (`/practice?subjectId=&topicId=`). */
export function studyPlanTaskLink(task: StudyPlanTask): StudyPlanTaskLink {
  switch (task.kind) {
    case 'assignment':
      return { route: '/tests' };
    case 'mockExam':
      return { route: '/my-mock-exams' };
    case 'practiceTopic':
      return task.subjectId && task.topicId
        ? { route: '/practice', queryParams: { subjectId: task.subjectId, topicId: task.topicId } }
        : { route: '/practice' };
    case 'mistakes':
      return { route: '/mistakes' };
    case 'flashcards':
      return { route: '/flashcards' };
    default:
      return { route: '/practice' };
  }
}
