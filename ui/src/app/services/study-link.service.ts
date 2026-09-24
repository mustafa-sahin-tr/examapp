import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  CreateStudyLinkRequest,
  QuestionStudyLinkSuggestion,
  ReorderStudyLinksRequest,
  STUDY_LINK_ERROR_CODES,
  StudyLink,
  StudyLinkErrorResponse,
  StudyLinkListResponse,
  StudyLinkQuery,
  UpdateStudyLinkRequest,
} from '../models/study-link';

/** Konu / alt konu çalışma linkleri (issue #61). */
@Injectable({ providedIn: 'root' })
export class StudyLinkService {
  // Gateway: /api/exam/{everything} -> backend /api/{everything}
  private readonly baseUrl = '/api/exam/study-links';
  private readonly http = inject(HttpClient);

  /** Yönetim listesi — `topicId` ya da `subTopicId`'den tam olarak biri gönderilir. */
  list(query: StudyLinkQuery): Observable<StudyLinkListResponse> {
    let params = new HttpParams();
    if (query.subTopicId != null) params = params.set('subTopicId', query.subTopicId);
    else if (query.topicId != null) params = params.set('topicId', query.topicId);
    params = params
      .set('includeInactive', query.includeInactive ?? true)
      .set('skip', query.skip ?? 0)
      .set('take', query.take ?? 50);
    return this.http.get<StudyLinkListResponse>(this.baseUrl, { params });
  }

  getById(id: number): Observable<StudyLink> {
    return this.http.get<StudyLink>(`${this.baseUrl}/${id}`);
  }

  create(request: CreateStudyLinkRequest): Observable<StudyLink> {
    return this.http.post<StudyLink>(this.baseUrl, request);
  }

  update(id: number, request: UpdateStudyLinkRequest): Observable<StudyLink> {
    return this.http.put<StudyLink>(`${this.baseUrl}/${id}`, request);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  /** Sıralamayı toplu günceller; güncel listeyi döner. */
  reorder(request: ReorderStudyLinksRequest): Observable<StudyLinkListResponse> {
    return this.http.put<StudyLinkListResponse>(`${this.baseUrl}/reorder`, request);
  }

  /** Öğrencinin tamamlanmış sınavında yanlış cevapladığı sorular için öneriler (Student). */
  getForResult(testInstanceId: number): Observable<QuestionStudyLinkSuggestion[]> {
    return this.http.get<QuestionStudyLinkSuggestion[]>(`${this.baseUrl}/for-result/${testInstanceId}`);
  }
}

/** Hata gövdesindeki `errorCode` (bkz. `STUDY_LINK_ERROR_CODES`); yoksa null. */
export function studyLinkErrorCode(err: unknown): string | null {
  if (!(err instanceof HttpErrorResponse) || !err.error || typeof err.error !== 'object') return null;
  return (err.error as StudyLinkErrorResponse).errorCode ?? null;
}

/** 409 `ActiveLimitReached` mı (7 aktif link sınırı)? */
export function isActiveLimitError(err: unknown): boolean {
  return (
    err instanceof HttpErrorResponse &&
    err.status === 409 &&
    studyLinkErrorCode(err) === STUDY_LINK_ERROR_CODES.activeLimitReached
  );
}

/** 403 `TeacherNotApproved` mı (öğretmen başvurusu onaylanmamış)? */
export function isTeacherNotApprovedError(err: unknown): boolean {
  return (
    err instanceof HttpErrorResponse &&
    err.status === 403 &&
    studyLinkErrorCode(err) === STUDY_LINK_ERROR_CODES.teacherNotApproved
  );
}

/** 429 (yazma uçları rate limit) → `Retry-After` saniyesi (yoksa null). Başka durumlar için undefined. */
export function rateLimitRetryAfter(err: unknown): number | null | undefined {
  if (!(err instanceof HttpErrorResponse) || err.status !== 429) return undefined;
  const seconds = Number(err.headers?.get('Retry-After'));
  return Number.isFinite(seconds) && seconds > 0 ? seconds : null;
}

/**
 * Sunucu hata gövdesinden gösterilecek metni çıkarır: düz metin gövde (429 rate limit), `ResponseBaseDto.message`,
 * yoksa model doğrulama (`ValidationProblemDetails.errors`) içindeki ilk mesaj; hiçbiri yoksa `null`
 * (çağıran genel metne düşer).
 */
export function studyLinkErrorMessage(err: unknown): string | null {
  if (!(err instanceof HttpErrorResponse)) return null;
  const body: unknown = err.error;
  if (typeof body === 'string') return body.trim() || null;
  if (!body || typeof body !== 'object') return null;
  const message = (body as { message?: unknown }).message;
  if (typeof message === 'string' && message.trim()) return message;
  const errors = (body as { errors?: unknown }).errors;
  if (errors && typeof errors === 'object') {
    for (const value of Object.values(errors as Record<string, unknown>)) {
      if (Array.isArray(value) && typeof value[0] === 'string') return value[0];
    }
  }
  return null;
}
