import { Subject } from 'rxjs';
import { Answer } from './answer';
import { Question } from './question';
import { ResponseBase } from './worksheet-access-request.model';
import { WorksheetKind } from './activity';

export enum TestStatus {
  NotStarted = -1,
  Started = 0,
  Completed = 1,
  Expired = 2,
}

// Backend enum'ları number olarak serialize edilir (string-enum converter yok).
export enum WorksheetTeacherSharing {
  Private = 0,
  PublicView = 1,
  PublicAssignable = 2,
  /** issue #191: yalnızca sahibin okulundaki öğretmenler görür ve atar; aynı okulda PublicAssignable gibi davranır. */
  SchoolOnly = 3,
}

export enum WorksheetStudentVisibility {
  Normal = 0,
  Restricted = 1,
}

// public enum WorksheetInstanceStatus
// {
//     Started = 0,   // 🟢 Test başladı
//     Completed = 1, // ✅ Test tamamlandı
//     Expired = 2    // ⏳ Süre doldu
// }

export interface TestInstanceQuestion {
  id: number;
  question: Question;
  order: number;
  selectedAnswerId: number;
  answerPayload?: string;
  timeTaken: number;
  /**
   * Issue #571/#573: satırın son kontrol sonucu. Etkinlikte yalnız kontrol edilip sonra DEĞİŞMEDİYSE dolu (yeniden
   * yüklemede işaretler geri gelir); testte yalnız bitmiş sonuç ekranında. Diğer durumlarda null/undefined.
   */
  isCorrect?: boolean | null;
  correctCount?: number | null;
  totalCount?: number | null;
  /** JSON string `{"rows":[{"id","correct"}]}` (MCQ'da null). */
  feedback?: string | null;
}
export interface TestInstance {
  id: number;
  testName: string;
  status: TestStatus;
  maxDurationSeconds: number;
  /**
   * Issue #396: sunucunun hesapladığı kalan süre (saniye). `null` = süre sınırı yok; bitmiş oturumda 0. Sayaç bundan
   * başlar — sayfa yenilense ya da sekme arka planda kalsa da süre baştan başlamaz. Eski yanıtta yoksa undefined.
   */
  remainingSeconds?: number | null;
  testInstanceQuestions: TestInstanceQuestion[];
  isPracticeTest: boolean;
  /**
   * Issue #383: sonuç sayfası `/test/{worksheetId}`. Backend `WorksheetInstanceResultDto` bu alanı
   * henüz dönmüyorsa undefined kalır; çözüm ekranı o durumda navigasyon state'ine düşer.
   */
  worksheetId?: number;
  /** Issue #573: sayfa türü (0 Test, 1 Etkinlik). Eski yanıtta yok → Test. */
  kind?: WorksheetKind;
  /** Issue #573: etkinlikte "Cevapları kontrol et" sayısı; testte null. */
  checkCount?: number | null;
  /** Issue #573: sayfa açıklaması — oynatıcıda maddenin yönergesi yoksa yedek yönerge. */
  worksheetDescription?: string | null;
  /** Issue #573: etkinlikte ilk kontrol anı (UTC ISO); testte null. */
  firstCheckedAt?: string | null;
  /** Issue #573: etkinlikte çözümün ilk açıldığı an (UTC ISO); testte null. */
  solutionsRevealedAt?: string | null;
  /** Issue #573: etkinlikte ilk kontroldeki doğru satır sayısı; kontrol yoksa / testte null. */
  firstCheckCorrectCount?: number | null;
  /** Issue #573: etkinlikte ilk kontroldeki toplam satır sayısı; kontrol yoksa / testte null. */
  firstCheckTotalCount?: number | null;
  /**
   * Issue #609: etkinliğin ilk tamamlanma anı (UTC ISO); hiç tamamlanmadıysa / testte null. `status` Started iken doluysa
   * oturum yeniden çözülüyordur.
   */
  firstCompletedAt?: string | null;
}

/** Backend `TestSessionErrorCodes` (issue #367). */
export const TEST_SESSION_ERROR_CODES = {
  /** 409: oturum artık `Started` değil (Completed/Expired); cevap yazılmadı. */
  testNotInProgress: 'TestNotInProgress',
} as const;

/** Backend `TestSessionRejectReasons` (issue #396): `errorCode` aynı kalır, alt neden. */
export const TEST_SESSION_REJECT_REASONS = {
  /** Oturum süre sınırı (+ tolerans) dolduğu için Expired. */
  timeExpired: 'TimeExpired',
} as const;

/**
 * Backend `TestStartResultDto`: `start-test` yanıtı. Issue #367: test artık devam etmiyorsa (Completed/Expired)
 * yeni instance açılmaz; `success: false` + mevcut `instanceId` döner (alreadyCompleted).
 */
export interface TestStartResult extends ResponseBase {
  instanceId: number;
  startTime: string;
}

/** Backend `TestSessionResultDto`: `save-answer` / `end-test` yanıtı (issue #367). */
export interface TestSessionResult extends ResponseBase {
  errorCode?: string | null;
  /** Issue #396: `errorCode` alt nedeni (ör. `TimeExpired`); yoksa null/undefined. */
  reason?: string | null;
}

export interface Exam {
  id: number;
  name: string;
  description: string;
  maxDurationSeconds: number;
  totalQuestions: number;
  instanceStatus: TestStatus;
  testInstanceId: number;
  bookId?: number;
  bookTestId?: number;
}

export interface Paged<T> {
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  items: T[];
}

export interface Test {
  id: number | null;
  name: string;
  description?: string;
  gradeId?: number;
  maxDurationSeconds: number;
  isPracticeTest: boolean;
  imageUrl?: string;
  subtitle?: string;
  badgeText?: string;
  bookId?: number;
  bookTestId?: number;
  questionCount?: number;
  instance?: InstanceSummary;
  instanceCount?: number;
  newBookName?: string;
  newBookTestName?: string;
  subjectId?: number;
  topicId?: number;
  subTopicId?: number;
  /** Backend: (sahibi && CreateUserId>0) || admin */
  canEdit?: boolean;
  createdByUserId?: number | null;
  /** Sadece istek sahibi admin ise dolu gelir. */
  createdByName?: string | null;
  /** Öğretmenler arası paylaşım ekseni (issue #9). Backend listelerde her zaman gönderir. */
  teacherSharing?: WorksheetTeacherSharing;
  /** Öğrenciye görünürlük ekseni (issue #9). Backend listelerde her zaman gönderir. */
  studentVisibility?: WorksheetStudentVisibility;
  /** İstek sahibi bu worksheet'in sahibi mi. */
  isOwner?: boolean;
  /** Tekil detay (GetById) akışında admin VEYA sahip için dolu; liste akışlarında yalnız admin için dolu. */
  ownerName?: string | null;
  /** İstek sahibi bu worksheet'i öğrenciye atayabilir mi. */
  canAssign?: boolean;
  /** Öğrenci discover listesinde: true ise "Atanan sınavlar", false ise "Keşfet" grubunda gösterilir (issue #14). */
  isAssigned?: boolean;
  /**
   * Issue #105: öğrenciler yorum/soru yazabilir mi (worksheet varsayılanı; atama override'ı ayrı). Okumada
   * `WorksheetDto.CommentsEnabled`; kaydederken `ExamDto.CommentsEnabled` — güncellemede gönderilmezse değişmez.
   */
  commentsEnabled?: boolean;
  /**
   * Issue #573: sayfa türü (0 Test, 1 Etkinlik). Okumada `WorksheetDto.Kind`; kaydederken `ExamDto.Kind` — null/undefined
   * = oluştururken Test, güncellerken değişmez. Etkinlikte `instance` skor alanları hesaplanmaz (0) ve gösterilmez.
   */
  kind?: WorksheetKind | null;
}

export interface InstanceSummary {
  id: number;
  name: string;
  imageUrl?: string;
  completedDate: Date;
  score: number;
  durationMinutes: number;
  correctAnswers: number;
  wrongAnswers: number;
  totalQuestions: number;
  status: number;
  /** Issue #573: yalnız etkinlikte — cevaplanmış madde sayısı (mini ilerleme). Testte null. */
  answeredQuestions?: number | null;
  /** Issue #573: yalnız etkinlikte — kontrol sayısı. Testte null. */
  checkCount?: number | null;
  /** Issue #573: yalnız etkinlikte — ilk kontroldeki doğru satır sayısı; kontrol yoksa / testte null. */
  firstCheckCorrectCount?: number | null;
  /** Issue #573: yalnız etkinlikte — ilk kontroldeki toplam satır sayısı; kontrol yoksa / testte null. */
  firstCheckTotalCount?: number | null;
  /**
   * Issue #609: yalnız etkinlikte — ilk tamamlanma anı (UTC ISO); hiç tamamlanmadıysa / testte null. `status` 0 iken doluysa
   * etkinlik yeniden çözülüyordur.
   */
  firstCompletedAt?: string | null;
}
