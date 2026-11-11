/** Issue #1111: öğretmene sor — `api/exam/ask-teacher` yanıtları. */
export type AskTeacherStatus = 'Open' | 'Answered' | 'Expired' | 'Cancelled';

export interface AskTeacherTeacher {
  teacherId: number;
  fullName: string;
  /** Seçilen dersi okutuyor mu (liste başta bunları verir). */
  teachesSubject: boolean;
}

export interface AskTeacherQuestion {
  id: number;
  status: AskTeacherStatus;
  teacherId: number;
  teacherName: string;
  /** Yalnız öğretmen görünümünde dolu ("Ad S."). */
  studentName: string;
  subjectId: number | null;
  subjectName: string | null;
  note: string | null;
  createdAt: string;
  dueAt: string;
  answeredAt: string | null;
  answerText: string | null;
  hasAnswerImage: boolean;
  rating: number | null;
  /** Yanlışlar defterinde mi ("Öğretmene sorduklarım"). */
  inNotebook: boolean;
}

export interface AskTeacherQuota {
  /** null = sınırsız; 0 = planda yok. */
  limit: number | null;
  used: number;
  /** Cevap bekleyen sorular (hakkı rezerve eder). */
  pending: number;
  resetsAtUtc: string | null;
  planKey: string | null;
}

export interface AskTeacherSendRequest {
  teacherId: number;
  subjectId: number | null;
  note: string;
  photo: Blob;
}

export const ASK_TEACHER_FEATURE = 'ask-teacher';
export const ASK_TEACHER_ROUTE = '/ask-teacher';
export const ASK_TEACHER_QUEUE_ROUTE = '/teacher/ask-queue';

/** Bildirim `data` alanı (`{ questionId }`) güvenilmeyen veridir; id pozitif tam sayı değilse `null`. */
export function parseAskTeacherNotificationData(data: string | null): number | null {
  if (!data) return null;
  try {
    const id = (JSON.parse(data) as { questionId?: unknown }).questionId;
    return typeof id === 'number' && Number.isInteger(id) && id > 0 ? id : null;
  } catch {
    return null;
  }
}

/** Sunucu hata kodu (`askTeacher.*`); yoksa null. */
export function askTeacherErrorCode(error: unknown): string | null {
  const code = (error as { error?: { code?: unknown } } | null)?.error?.code;
  return typeof code === 'string' && code.startsWith('askTeacher.') ? code.slice('askTeacher.'.length) : null;
}
