/**
 * Issue #583: açık uçlu cevap (`InteractionType = openText`, bileşen H) — AI'sız ilk sürüm. Öğrenci görselin altındaki metin
 * alanına yazar; otomatik kontrol yok, öğretmen değerlendirir (yeterli / kısmen / eksik + geri bildirim, puansız). Kurallar
 * sunucudaki `OpenTextGrader` ile aynı:
 * - Plan v1 `{version:1,type:"openText",minWords?,maxWords?,lines?}` (1..2000, min ≤ max; lines 1..30).
 * - Çözüm `{sampleAnswer?,rubric?}` (≤ 4000, düz metin). Öğrenciye (reveal) yalnız `sampleAnswer` gelir.
 * - Payload `{version:1,type:"openText",text}`; text ≤ 8000, \n \r \t dışı kontrol karakteri yok; boş/boşluk = cevapsız.
 *   `maxWords` doluysa `maxWords * 2` sözcüğü aşan metin 400 ile reddedilir; altındaki aşım yalnız sayaç uyarısıdır.
 * Öğrenci metni ve öğretmen geri bildirimi DÜZ METİNDİR: yalnız Angular interpolasyonuyla gösterilir (innerHTML yok).
 */
import { isInteractionType } from './interaction-ids';
import { Paged, TestSessionResult } from './test-instance';

export const OPEN_TEXT = 'openText' as const;

/** Backend `OpenTextGrader` / değerlendirme servisi sınırları. */
export const OPEN_TEXT_LIMITS = {
  minWordLimit: 1,
  maxWordLimit: 2000,
  minLines: 1,
  maxLines: 30,
  /** Planda `lines` yoksa metin kutusunun satır sayısı. */
  defaultLines: 5,
  maxSolutionTextLength: 4000,
  /** Öğrencinin yazabileceği en uzun metin (karakter). */
  maxTextLength: 8000,
  /** `maxWords` doluysa metin en çok bu kat sözcük içerebilir (fazlası kaydedilmez). */
  maxWordsOverflowFactor: 2,
  /** Öğretmen geri bildirimi (karakter). */
  maxFeedbackLength: 1000,
} as const;

export interface OpenTextPlanV1 {
  version: 1;
  type: typeof OPEN_TEXT;
  minWords: number | null;
  maxWords: number | null;
  lines: number | null;
}

export interface OpenTextSolution {
  sampleAnswer: string | null;
  rubric: string | null;
}

export interface OpenTextAnswerPayload {
  version: 1;
  type: typeof OPEN_TEXT;
  text: string;
}

export type OpenTextEvaluation = 'sufficient' | 'partial' | 'insufficient';
export const OPEN_TEXT_EVALUATIONS: readonly OpenTextEvaluation[] = ['sufficient', 'partial', 'insufficient'];

/** `pending` = gönderildi, değerlendirme yok; `reviewed` = şimdiki metin değerlendirildi; `outdated` = metin sonra değişti. */
export type OpenTextReviewStatus = 'pending' | 'reviewed' | 'outdated';

/** Kontrol satırının durumu (`feedback.rows[0].status`) — panel ✓/✗ yerine bunu gösterir. */
export type OpenTextRowStatus = 'pending' | OpenTextEvaluation;

/**
 * Backend `OpenTextStudentReviewDto`: öğrencinin kendi değerlendirmesi — oturum yanıtında `testInstanceQuestions[].openTextReview`,
 * kontrol yanıtında `questions[].openTextReview`. Cevap yok / taslak hiç gönderilmedi → null.
 */
export interface OpenTextStudentReview {
  status: OpenTextReviewStatus;
  evaluation: OpenTextEvaluation | null;
  feedback: string | null;
  reviewedAt: string | null;
}

export function isOpenTextType(type: unknown): boolean {
  return isInteractionType(type, OPEN_TEXT);
}

export function isOpenTextEvaluation(value: unknown): value is OpenTextEvaluation {
  return typeof value === 'string' && (OPEN_TEXT_EVALUATIONS as readonly string[]).includes(value);
}

// ------------------------------------------------------------------ sözcük sayacı (sunucu kuralı)

/**
 * .NET `char.IsWhiteSpace` ile aynı küme (sunucu sayacı bunu kullanır): \t–\r, U+001C–U+001F, boşluk, U+0085, U+00A0, U+1680,
 * U+2000–U+200A, U+2028, U+2029, U+202F, U+205F, U+3000. (JS `\s` U+FEFF'i de sayardı, U+0085'i saymazdı.)
 */
const WHITESPACE_CLASS = '[\\t\\n\\v\\f\\r\\u001C-\\u001F \\u0085\\u00A0\\u1680\\u2000-\\u200A\\u2028\\u2029\\u202F\\u205F\\u3000]';
const WORDS = new RegExp(`[^${WHITESPACE_CLASS.slice(1, -1)}]+`, 'gu');
const EDGE_WHITESPACE = new RegExp(`^${WHITESPACE_CLASS}+|${WHITESPACE_CLASS}+$`, 'gu');
/**
 * Sunucunun düz metin kuralının reddettiği karakterler: C0 kontrol (\t \n \r serbest), DEL, C1 (U+0080–U+009F) ve bidi
 * override / isolate (U+202A–U+202E, U+2066–U+2069; metnin görünen sırasını değiştirip yanıltabilir).
 */
const CONTROL_CHARS = /[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F\u202A-\u202E\u2066-\u2069]/gu;

/** Sözcük sayısı = boşluk olmayan karakterlerin kesintisiz dizileri (sunucu `OpenTextGrader.CountWords`). */
export function countOpenTextWords(text: string | null | undefined): number {
  if (!text) return 0;
  return text.match(WORDS)?.length ?? 0;
}

/** Sunucunun saklayacağı biçim: satır sonları \n, baştaki / sondaki boşluk kırpılmış. */
export function normalizeOpenText(text: string | null | undefined): string {
  return (text ?? '').replace(/\r\n?/g, '\n').replace(EDGE_WHITESPACE, '');
}

/** Yapıştırılan metindeki kontrol / C1 / bidi denetim karakterlerini atar (sunucu reddederdi; ekranda görünmezler). */
export function stripOpenTextControlChars(text: string): string {
  return text.replace(CONTROL_CHARS, '');
}

export function isBlankOpenText(text: string | null | undefined): boolean {
  return normalizeOpenText(text).length === 0;
}

/**
 * Sayaç durumu: `under` en az sözcüğün altında (yalnız ipucu), `over` en çok sözcüğün üstünde (uyarı, kaydedilir), `blocked`
 * en çok sözcüğün 2 katını aştı (sunucu reddeder; kaydedilmez). Boş metin `ok`.
 */
export type OpenTextWordState = 'ok' | 'under' | 'over' | 'blocked';

export function openTextWordState(words: number, plan: Pick<OpenTextPlanV1, 'minWords' | 'maxWords'> | null): OpenTextWordState {
  if (!plan || words === 0) return 'ok';
  if (plan.maxWords != null) {
    if (words > plan.maxWords * OPEN_TEXT_LIMITS.maxWordsOverflowFactor) return 'blocked';
    if (words > plan.maxWords) return 'over';
  }
  if (plan.minWords != null && words < plan.minWords) return 'under';
  return 'ok';
}

// ------------------------------------------------------------------ ayrıştırıcılar

function wordLimit(value: unknown): number | null {
  return typeof value === 'number' &&
    Number.isInteger(value) &&
    value >= OPEN_TEXT_LIMITS.minWordLimit &&
    value <= OPEN_TEXT_LIMITS.maxWordLimit
    ? value
    : null;
}

function lineCount(value: unknown): number | null {
  return typeof value === 'number' && Number.isInteger(value) && value >= OPEN_TEXT_LIMITS.minLines && value <= OPEN_TEXT_LIMITS.maxLines
    ? value
    : null;
}

/** Plan JSON'u — tür farklı / bozuksa null. Geçersiz sınır sınırsız sayılır (sunucunun toleranslı okuması); min > max ise min düşer. */
export function parseOpenTextPlan(json: string | null | undefined): OpenTextPlanV1 | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isOpenTextType(parsed['type'])) return null;
    const maxWords = wordLimit(parsed['maxWords']);
    let minWords = wordLimit(parsed['minWords']);
    if (minWords != null && maxWords != null && minWords > maxWords) minWords = null;
    return { version: 1, type: OPEN_TEXT, minWords, maxWords, lines: lineCount(parsed['lines']) };
  } catch {
    return null;
  }
}

function solutionText(value: unknown): string | null {
  return typeof value === 'string' && value.trim() ? value : null;
}

/** Çözüm JSON'u (`{sampleAnswer?, rubric?}`) — bozuk / boşsa iki alan da null. */
export function parseOpenTextSolution(json: string | null | undefined): OpenTextSolution {
  const empty: OpenTextSolution = { sampleAnswer: null, rubric: null };
  if (!json) return empty;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return empty;
    return { sampleAnswer: solutionText(parsed['sampleAnswer']), rubric: solutionText(parsed['rubric']) };
  } catch {
    return empty;
  }
}

/** Öğrenci cevabının metni (başka tür / bozuksa boş). */
export function parseOpenTextAnswer(json: string | null | undefined): string {
  if (!json) return '';
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isOpenTextType(parsed['type'])) return '';
    return typeof parsed['text'] === 'string' ? parsed['text'] : '';
  } catch {
    return '';
  }
}

/** Payload: metin olduğu gibi gider (sunucu normalize eder); boş metin = cevapsız (Temizle). */
export function buildOpenTextPayload(text: string): string {
  const payload: OpenTextAnswerPayload = { version: 1, type: OPEN_TEXT, text };
  return JSON.stringify(payload);
}

/** Boş / yalnız boşluk metinli openText cevabı: sunucu boş sayar, ekranlar da cevapsız saymalı. Başka tür → false. */
export function isBlankOpenTextPayload(json: string | null | undefined): boolean {
  if (!json) return true;
  try {
    const parsed = JSON.parse(json) as Record<string, unknown> | null;
    if (!parsed || typeof parsed !== 'object' || !isOpenTextType(parsed['type'])) return false;
    return isBlankOpenText(typeof parsed['text'] === 'string' ? parsed['text'] : '');
  } catch {
    return false;
  }
}

/** Sunucunun `openTextReview`'ünü güvenli okur; tanınmayan durum → null. */
export function parseOpenTextStudentReview(value: unknown): OpenTextStudentReview | null {
  if (!value || typeof value !== 'object') return null;
  const raw = value as Record<string, unknown>;
  const status = raw['status'];
  if (status !== 'pending' && status !== 'reviewed' && status !== 'outdated') return null;
  const evaluation = isOpenTextEvaluation(raw['evaluation']) ? raw['evaluation'] : null;
  // reviewed / outdated değerlendirme olmadan anlamsız: bekliyor sayılır.
  return {
    status: status !== 'pending' && !evaluation ? 'pending' : status,
    evaluation: status === 'pending' ? null : evaluation,
    feedback: typeof raw['feedback'] === 'string' && raw['feedback'].trim() ? raw['feedback'] : null,
    reviewedAt: typeof raw['reviewedAt'] === 'string' ? raw['reviewedAt'] : null,
  };
}

/** Kontrol yanıtının `feedback` JSON'undaki tek satırın durumu (`{"rows":[{"id":"text","status":...}]}`); yoksa null. */
export function parseOpenTextFeedbackStatus(json: string | null | undefined): OpenTextRowStatus | null {
  if (!json) return null;
  try {
    const parsed = JSON.parse(json) as { rows?: unknown } | null;
    const row = Array.isArray(parsed?.rows) ? (parsed!.rows[0] as Record<string, unknown> | undefined) : undefined;
    const status = row?.['status'];
    return status === 'pending' || isOpenTextEvaluation(status) ? status : null;
  } catch {
    return null;
  }
}

/** Değerlendirme → kontrol satırı durumu: güncel değerlendirme varsa o, aksi halde (gönderilmiş / eskimiş) bekliyor. */
export function openTextRowStatus(review: OpenTextStudentReview | null): OpenTextRowStatus | null {
  if (!review) return null;
  return review.status === 'reviewed' && review.evaluation ? review.evaluation : 'pending';
}

// ------------------------------------------------------------------ öğretmen kuyruğu (backend OpenTextReviewsController)

/** Kuyruk filtresi: `pending` (vars.) değerlendirmesi olmayan / eskimiş gönderilmiş cevaplar, `reviewed`, `all`. */
export type OpenTextQueueStatus = 'pending' | 'reviewed' | 'all';
export const OPEN_TEXT_QUEUE_STATUSES: readonly OpenTextQueueStatus[] = ['pending', 'reviewed', 'all'];

/** Backend `OpenTextReviewQueueQuery.MaxPageSize`. */
export const OPEN_TEXT_QUEUE_MAX_PAGE_SIZE = 50;

export interface OpenTextQueueQuery {
  status: OpenTextQueueStatus;
  worksheetId?: number | null;
  /** 1 tabanlı. */
  page: number;
  pageSize: number;
}

/** Backend `OpenTextTeacherReviewDto`. */
export interface OpenTextTeacherReview {
  evaluation: OpenTextEvaluation;
  feedback: string | null;
  /** `teacher` (ileride `ai`). */
  source: string;
  reviewedByUserId: number;
  reviewedAt: string;
  /** true = değerlendirme önceki metne ait. */
  stale: boolean;
}

/** Backend `OpenTextReviewItemDto`. `text` öğrencinin düz metnidir (HTML değil). */
export interface OpenTextReviewItem {
  instanceQuestionId: number;
  instanceId: number;
  worksheetId: number;
  worksheetName: string;
  questionId: number;
  /** `WorksheetQuestion.Order` (iç sıra; gösterilmez — madde etiketi `itemNumber`'dan). */
  itemOrder: number;
  /** 1 tabanlı madde numarası (oynatıcının a/b/c sırası); eski yanıtta yok. */
  itemNumber?: number | null;
  questionText: string | null;
  /** İmzalı görsel URL'si. */
  questionImageUrl: string | null;
  studentId: number;
  studentUserId: number;
  studentNumber: string | null;
  gradeName: string | null;
  text: string;
  /** Değerlendirme ucuna aynen geri gönderilir (metin bu arada değiştiyse 409). */
  answerHash: string;
  wordCount: number;
  minWords: number | null;
  maxWords: number | null;
  sampleAnswer: string | null;
  /** Yalnız soru sahibine / admine dolu; diğer öğretmende null (bölüm gizlenir). */
  rubric: string | null;
  answeredAt: string | null;
  /**
   * false = istek sahibi bu satırı değerlendiremez (başka bir öğretmen değerlendirdi): form salt okunur. Eski yanıtta yok →
   * düzenlenebilir sayılır (sunucu yine 403 `OpenTextReviewForbidden` ile korur).
   */
  canEdit?: boolean;
  status: OpenTextReviewStatus;
  review: OpenTextTeacherReview | null;
}

export type OpenTextReviewPage = Paged<OpenTextReviewItem>;

/** `PUT api/exam/open-text-reviews/{instanceQuestionId}` gövdesi. */
export interface OpenTextReviewRequest {
  evaluation: OpenTextEvaluation;
  /** Düz metin, ≤ 1000; boş → null. */
  feedback: string | null;
  answerHash: string;
}

/** Backend `OpenTextReviewResultDto`. */
export interface OpenTextReviewResult extends TestSessionResult {
  invalidInput?: boolean;
  item?: OpenTextReviewItem | null;
}

/** Backend `OpenTextReviewErrorCodes`. */
export const OPEN_TEXT_REVIEW_ERROR_CODES = {
  invalidReview: 'OpenTextInvalidReview',
  notFound: 'OpenTextReviewNotFound',
  /** 403 — satırı başka bir öğretmen değerlendirdi / istek sahibi düzenleyemez. */
  forbidden: 'OpenTextReviewForbidden',
  notSubmitted: 'OpenTextNotSubmitted',
  answerChanged: 'OpenTextAnswerChanged',
  rateLimited: 'RateLimited',
} as const;
