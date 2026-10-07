/**
 * Issue #420 (epic #407 V2): veli paneli. Backend: `/api/exam/parent/children/{studentId}/summary`
 * (gateway `/api/exam/{everything}` → API `/api/parent/children/{studentId}/summary`).
 * Yalnızca toplamlar — içerik, cevap anahtarı, mesaj ya da iletişim bilgisi gelmez.
 */

export interface ParentChildAssignmentCounts {
  completed: number;
  overdue: number;
  pending: number;
  /** Teslim tarihi geçmiş atamaların geriye bakış penceresi (gün). */
  windowDays: number;
}

export interface ParentChildSummary {
  studentId: number;
  /** Haftanın Pazartesi'si (Europe/Istanbul), "yyyy-MM-dd". */
  weekStart: string;
  questionsSolvedThisWeek: number;
  assignments: ParentChildAssignmentCounts;
  totalPoints: number;
  /** ISO-8601 UTC; hiç aktivite yoksa null. */
  lastActivityAt: string | null;
}

/** Seçili çocuğu hatırlayan query param (`/parent?child=<studentId>`). */
export const PARENT_CHILD_QUERY_PARAM = 'child';

/**
 * Issue #421 (epic #407 V3): veli ödev/test takibi. Backend: `/api/exam/parent/children/{studentId}/assignments` ve
 * `/api/exam/parent/children/{studentId}/test-results/{testInstanceId}`. Salt okunur; soru metni/görseli, cevap anahtarı ya da
 * çocuğun seçtiği şık gelmez.
 */
export type ParentAssignmentStatus = 'completed' | 'overdue' | 'pending';

/** Filtre çipleri (sıra = ekrandaki sıra). */
export const PARENT_ASSIGNMENT_STATUSES: readonly ParentAssignmentStatus[] = ['pending', 'overdue', 'completed'];

export interface ParentTestScore {
  scorePercent: number;
  correctCount: number;
  wrongCount: number;
  blankCount: number;
  totalCount: number;
  durationSeconds: number;
}

export interface ParentChildAssignmentItem {
  worksheetId: number;
  title: string;
  subject: string | null;
  teacherName: string | null;
  /** ISO-8601 UTC. */
  startAt: string;
  /** ISO-8601 UTC; açık uçlu ödevde null. */
  deadline: string | null;
  status: ParentAssignmentStatus;
  /** Sonucu olan (bitmiş) testin oturumu; yoksa null. */
  testInstanceId: number | null;
  result: ParentTestScore | null;
}

export interface ParentChildAssignmentList {
  studentId: number;
  status: ParentAssignmentStatus | null;
  page: number;
  pageSize: number;
  totalCount: number;
  /** Filtreden bağımsız kova sayıları (V2 özet kartıyla aynı). */
  counts: ParentChildAssignmentCounts;
  items: ParentChildAssignmentItem[];
}

export interface ParentTestTopicResult {
  topicId: number | null;
  name: string;
  correctCount: number;
  wrongCount: number;
  blankCount: number;
  totalCount: number;
}

export interface ParentChildTestResult {
  studentId: number;
  testInstanceId: number;
  worksheetId: number;
  title: string;
  subject: string | null;
  outcome: 'completed' | 'timedOut';
  startedAt: string;
  finishedAt: string | null;
  score: ParentTestScore;
  topics: ParentTestTopicResult[];
}

/**
 * Issue #422 (epic #407 V4): veli "Puan ve rozetler". Backend: `/api/exam/parent/children/{studentId}/progress`.
 * Sıralamada yalnız çocuğun kendi sırası ve kapsamdaki öğrenci sayısı gelir — başka öğrencinin adı/puanı gelmez.
 */
export interface ParentBadge {
  name: string;
  /** Material Symbols adı; yoksa null (varsayılan ikon). */
  icon: string | null;
  /** Kazanıldığı gün (Europe/Istanbul), "yyyy-MM-dd" — saat bilgisi yok. */
  earnedOn: string;
}

export type ParentRankScope = 'global' | 'school';

export interface ParentChildRank {
  scope: ParentRankScope;
  /** 1 tabanlı sıra. */
  rank: number;
  /** Kapsamdaki öğrenci sayısı. */
  totalCount: number;
}

export interface ParentChildProgress {
  studentId: number;
  totalXp: number;
  level: number;
  /** Haftanın Pazartesi'si (Europe/Istanbul), "yyyy-MM-dd". */
  weekStart: string;
  /** Bu hafta (yerel Pazartesi–bugün) kazanılan puan. */
  weeklyXp: number;
  /** Kazanılmış rozetler (en yeni önce); hiç yoksa boş dizi. */
  badges: ParentBadge[];
  /** Her zaman `global`; doğrulanmış okulu varsa `school` da. */
  ranks: ParentChildRank[];
}

/**
 * Issue #422: veli "Program". Backend: `/api/exam/parent/children/{studentId}/schedule?from=&to=` (yerel günler, iki uç dahil,
 * en fazla 31 gün; boşsa bu hafta). Ders bağlantısı, ücret ya da not gelmez.
 */
export interface ParentPlanItem {
  title: string;
  subject: string | null;
  /** Planlanan gün (Europe/Istanbul), "yyyy-MM-dd". */
  plannedOn: string;
}

/** Reddedilen talepler veliye gelmez. */
export type ParentLessonStatus = 'pending' | 'approved';

export interface ParentLessonItem {
  teacherName: string | null;
  /**
   * Listede gösterileceği gün (Europe/Istanbul), "yyyy-MM-dd" — aralığın başından önce başlayıp taşan derste aralığın ilk günü.
   * Gruplama tarayıcının saat dilimine göre değil buna göre yapılır.
   */
  startsOn: string;
  /** ISO-8601 UTC. */
  startAt: string;
  /** ISO-8601 UTC. */
  endAt: string;
  status: ParentLessonStatus;
}

export interface ParentChildSchedule {
  studentId: number;
  /** "yyyy-MM-dd". */
  from: string;
  /** "yyyy-MM-dd" (dahil). */
  to: string;
  plans: ParentPlanItem[];
  lessons: ParentLessonItem[];
}
