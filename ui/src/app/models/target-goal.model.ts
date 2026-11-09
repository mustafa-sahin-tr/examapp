/** Issue #695: sunucu `TargetGoalDtos` karşılığı. */
export type TargetExam = 'lgs' | 'yks';

export interface TargetProgram {
  id: number;
  scoreType: string;
  name: string;
  institution: string;
  city: string;
  district: string;
  year: number;
  minimumScore: number;
  percentile: number | null;
  /** Veli yanıtında boştur. */
  sourceReference: string | null;
}

/** "Bu puanla hangi okullar": puan türünün son yılı; katalog boşsa `year` null. */
export interface TargetProgramsForScore {
  year: number | null;
  safe: TargetProgram[];
  borderline: TargetProgram[];
  stretch: TargetProgram[];
}

export interface AdminTargetProgram extends TargetProgram {
  inUse: boolean;
}

export interface TargetProgramPage<T> {
  total: number;
  items: T[];
}

export interface TargetProgramQuery {
  exam?: TargetExam;
  /** #891: il süzgeci (katalogdaki yazımla). */
  city?: string;
  scoreType?: string;
  score?: number;
  year?: number;
  search?: string;
  skip?: number;
  take?: number;
}

export interface SaveTargetProgram {
  scoreType: string;
  name: string;
  institution: string;
  city: string;
  district: string;
  year: number;
  minimumScore: number;
  percentile: number | null;
  sourceReference: string;
}

export interface TargetMockSection {
  groupName: string | null;
  name: string;
  questionCount: number;
  correct: number;
  wrong: number;
  blank: number;
  net: number;
  pending: boolean;
}

export interface TargetMockResult {
  id: number;
  name: string;
  templateName: string;
  templateCode: string;
  wrongAnswersPerCorrect: number;
  complete: boolean;
  sections: TargetMockSection[];
  net: number;
  /** Yalnız geçmiş listesinde dolu. */
  finishedAt?: string | null;
}

export interface TargetGoal {
  target: TargetProgram | null;
  results: TargetMockResult[];
  /** #891: sınıf düzeyi (5–12); bilinmiyorsa null, eski sunucuda yok. */
  gradeLevel?: number | null;
}

export interface TargetImportRow {
  line: number;
  scoreType: string;
  name: string;
  institution: string;
  city: string;
  district: string;
  minimumScore: number;
  percentile: number | null;
}

export interface TargetImportChange extends TargetImportRow {
  previousMinimumScore: number;
  previousPercentile: number | null;
}

export interface TargetImportError {
  line: number;
  code: string;
  name: string | null;
}

/** #900: sınav başına katalog durumu; `total` 0 ise öğrencinin hedef araması boş döner. */
export interface TargetCatalogSummary {
  exam: TargetExam;
  total: number;
  latestYear: number | null;
  latestYearCount: number;
}

export interface TargetImportPreview {
  newCount: number;
  changedCount: number;
  unchangedCount: number;
  invalidCount: number;
  new: TargetImportRow[];
  changed: TargetImportChange[];
  invalid: TargetImportError[];
}

export interface TargetImportResult {
  created: number;
  updated: number;
  unchanged: number;
  skipped: number;
}

export interface TargetImportRequest {
  exam: TargetExam;
  year: number;
  source: string;
  file: File;
}
