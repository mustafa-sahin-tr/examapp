export type WorksheetSortBy = 'newest' | 'popular' | 'duration' | 'questionCount' | 'alphabetical' | 'recent';

export type WorksheetSortDir = 'asc' | 'desc';

/** -1 = başlanmadı, 0 = devam ediyor, 1 = tamamlandı (yalnızca öğrenci). */
export type WorksheetStatus = -1 | 0 | 1;

export type WorksheetListTab = 'discover' | 'assigned' | 'inprogress' | 'completed';

export type DurationBucket = 'lt15' | '15to30' | 'gt30';

export type QuestionBucket = 'lt10' | '10to20' | 'gt20';

export interface WorksheetListFilter {
  search?: string;
  subjectIds?: number[];
  gradeIds?: number[];
  statuses?: WorksheetStatus[];
  minQuestionCount?: number;
  maxQuestionCount?: number;
  minDurationSeconds?: number;
  maxDurationSeconds?: number;
  isPracticeTest?: boolean;
  bookIds?: number[];
  bookTestId?: number;
  sortBy?: WorksheetSortBy;
  sortDir?: WorksheetSortDir;
  pageNumber?: number;
  pageSize?: number;
}

export interface WorksheetSortOption {
  value: WorksheetSortBy;
  label: string;
  icon: string;
  studentOnly?: boolean;
}

export const WORKSHEET_SORT_OPTIONS: WorksheetSortOption[] = [
  { value: 'newest', label: 'En yeni', icon: 'auto_awesome' },
  { value: 'popular', label: 'Popüler', icon: 'trending_up' },
  { value: 'duration', label: 'Süre', icon: 'schedule' },
  { value: 'questionCount', label: 'Soru sayısı', icon: 'format_list_numbered' },
  { value: 'alphabetical', label: 'A–Z', icon: 'sort_by_alpha' },
  { value: 'recent', label: 'Son çalıştığım', icon: 'history', studentOnly: true },
];

export const DURATION_BUCKET_RANGES: Record<DurationBucket, { min?: number; max?: number; label: string }> = {
  lt15: { max: 15 * 60, label: '15 dk altı' },
  '15to30': { min: 15 * 60, max: 30 * 60, label: '15–30 dk' },
  gt30: { min: 30 * 60, label: '30 dk üzeri' },
};

export const QUESTION_BUCKET_RANGES: Record<QuestionBucket, { min?: number; max?: number; label: string }> = {
  lt10: { max: 9, label: '10 sorudan az' },
  '10to20': { min: 10, max: 20, label: '10–20 soru' },
  gt20: { min: 21, label: '20 sorudan fazla' },
};
