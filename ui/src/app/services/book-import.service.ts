import { HttpClient, HttpErrorResponse, HttpEventType } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  Observable,
  catchError,
  concat,
  concatMap,
  defer,
  filter,
  map,
  of,
  range,
  retry,
  switchMap,
  throwError,
  timer,
} from 'rxjs';

import {
  ApproveAllBookImportResult,
  BookImportDemo,
  BookImportJob,
  BookImportJobDetail,
  BookImportPageFix,
  BookImportReviewQuestion,
  BookImportQuota,
  BookImportSetup,
  BookImportUploadEvent,
  BookImportUploadSession,
  ConfirmBookImportSetup,
  StartBookImportRequest,
} from '../models/book-import';
import { LabelingBankQuestion } from '../models/labeling';

/** Issue #1122: ağ ya da sunucu hatasında bir parça en fazla bu kadar yeniden denenir (bekleme her denemede uzar). */
export const BOOK_IMPORT_PART_RETRIES = 3;
export const BOOK_IMPORT_PART_RETRY_MS = 1000;

/** Issue #1018 (epic #1017): kitaptan aktarım işleri. Pilot: Admin + QuestionTrainer; öğretmen yalnız demo (#1032). */
@Injectable({ providedIn: 'root' })
export class BookImportService {
  // Gateway: /api/exam/{everything} -> backend /api/{everything}
  private readonly baseUrl = '/api/exam/book-imports';
  private readonly http = inject(HttpClient);

  list(): Observable<BookImportJob[]> {
    return this.http.get<BookImportJob[]>(this.baseUrl);
  }

  get(id: string): Observable<BookImportJobDetail> {
    return this.http.get<BookImportJobDetail>(`${this.baseUrl}/${id}`);
  }

  quota(): Observable<BookImportQuota> {
    return this.http.get<BookImportQuota>(`${this.baseUrl}/quota`);
  }

  /** Issue #1032: örnek kitapla demo. */
  demo(): Observable<BookImportDemo> {
    return this.http.get<BookImportDemo>(`${this.baseUrl}/demo`);
  }

  startDemo(): Observable<BookImportJob> {
    return this.http.post<BookImportJob>(`${this.baseUrl}/demo`, {});
  }

  setDemoSource(id: string, isDemoSource: boolean): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/demo-source`, { isDemoSource });
  }

  /**
   * Issue #1122: PDF parça parça gider (Cloudflare 100 MB, gateway 30 MB istek sınırı). Oturum açılır, parçalar sırayla gönderilir
   * (ağ / sunucu hatasında parça yeniden denenir), sonra sunucu birleştirip işi başlatır. Hata olursa oturum bırakılır.
   */
  start(request: StartBookImportRequest): Observable<BookImportUploadEvent> {
    const { file } = request;
    return this.http
      .post<BookImportUploadSession>(`${this.baseUrl}/uploads`, {
        bookName: request.bookName,
        gradeId: request.gradeId,
        subjectId: request.subjectId,
        fileName: file.name,
        fileSizeBytes: file.size,
      })
      .pipe(
        switchMap((session) => {
          const parts = session.partCount;
          const progress = (part: number, sentBytes: number): BookImportUploadEvent => ({
            type: 'progress',
            part,
            parts,
            sentBytes,
            totalBytes: file.size,
          });
          const sent = range(1, parts).pipe(
            concatMap((part) => {
              const before = (part - 1) * session.partSizeBytes;
              return this.sendPart(session, file, part).pipe(
                map((loaded) =>
                  loaded === null
                    ? progress(part, Math.min(part * session.partSizeBytes, file.size))
                    : progress(part - 1, before + loaded)
                )
              );
            })
          );
          const complete = defer(() =>
            this.http.post<BookImportJob>(`${this.baseUrl}/uploads/${session.id}/complete`, {})
          ).pipe(map((job): BookImportUploadEvent => ({ type: 'done', job })));
          return concat(of(progress(0, 0)), sent, of<BookImportUploadEvent>({ type: 'assembling' }), complete).pipe(
            catchError((error) => {
              this.http.delete<void>(`${this.baseUrl}/uploads/${session.id}`).subscribe({ error: () => undefined });
              return throwError(() => error);
            })
          );
        })
      );
  }

  /** Parçanın gönderilen baytları (yükleme olaylarından), bitince `null`. Yeniden denemede sayaç sıfırdan başlar. */
  private sendPart(session: BookImportUploadSession, file: File, part: number): Observable<number | null> {
    const from = (part - 1) * session.partSizeBytes;
    const blob = file.slice(from, Math.min(from + session.partSizeBytes, file.size));
    return defer(() =>
      this.http.put<void>(`${this.baseUrl}/uploads/${session.id}/parts/${part}`, blob, {
        headers: { 'Content-Type': 'application/octet-stream' },
        observe: 'events',
        reportProgress: true,
      })
    ).pipe(
      retry({
        count: BOOK_IMPORT_PART_RETRIES,
        delay: (error: HttpErrorResponse, attempt) =>
          error.status === 0 || error.status >= 500
            ? timer(attempt * BOOK_IMPORT_PART_RETRY_MS)
            : throwError(() => error),
      }),
      filter((event) => event.type === HttpEventType.UploadProgress || event.type === HttpEventType.Response),
      map((event) => (event.type === HttpEventType.UploadProgress ? event.loaded : null))
    );
  }

  /** Issue #1021: kurulum onay ekranı. */
  setup(id: string): Observable<BookImportSetup> {
    return this.http.get<BookImportSetup>(`${this.baseUrl}/${id}/setup`);
  }

  confirmSetup(id: string, request: ConfirmBookImportSetup): Observable<BookImportJob> {
    return this.http.post<BookImportJob>(`${this.baseUrl}/${id}/setup/confirm`, request);
  }

  cancel(id: string, deleteDrafts = false): Observable<void> {
    const params = deleteDrafts ? { deleteDrafts: 'true' } : undefined;
    return this.http.post<void>(`${this.baseUrl}/${id}/cancel`, {}, { params });
  }

  /** Issue #1024: Admin — maliyet tavanında bekleyen işi sürdür. */
  continue(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/continue`, {});
  }

  /** #1025: hatalı ya da takılmış işi kaldığı yerden yeniden dene. */
  retry(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/retry`, {});
  }

  /** #1025: hatalı testi yeniden aktar. */
  retryTest(id: string, testId: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/tests/${testId}/retry`, {});
  }

  /** #1025: bütün hatalı testleri yeniden aktar. */
  retryFailedTests(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/tests/retry`, {});
  }

  /** Issue #1023: inceleme — testin soruları, cevap seçimi, onay ve silme. */
  reviewQuestions(id: string, testId: number): Observable<BookImportReviewQuestion[]> {
    return this.http.get<BookImportReviewQuestion[]>(`${this.baseUrl}/${id}/tests/${testId}/questions`);
  }

  setAnswer(id: string, testId: number, questionId: number, optionIndex: number): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/tests/${testId}/questions/${questionId}/answer`, { optionIndex });
  }

  approveTest(id: string, testId: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/tests/${testId}/approve`, {});
  }

  approveAll(id: string): Observable<ApproveAllBookImportResult> {
    return this.http.post<ApproveAllBookImportResult>(`${this.baseUrl}/${id}/approve`, {});
  }

  removeTest(id: string, testId: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}/tests/${testId}`);
  }

  /** Onaylanmamış testi baştan aktar: taslakları silinir, sayfa tespitleri önbellekten gelir. */
  reimportTest(id: string, testId: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/tests/${testId}/reimport`, {});
  }

  /** Issue #1023 "İşaretlemede düzelt": testin bir sayfasının taslaklarını işaretleme ekranında düzeltilenlerle değiştirir. */
  replacePage(
    id: string,
    testId: number,
    filePage: number,
    questions: LabelingBankQuestion[]
  ): Observable<BookImportPageFix> {
    return this.http.put<BookImportPageFix>(`${this.baseUrl}/${id}/tests/${testId}/pages/${filePage}/questions`, {
      questions,
    });
  }

  /** Sayfa görseli (WebP, #1020); yetkili istek olduğundan <img src> yerine blob. */
  pageImage(id: string, filePage: number): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/${id}/pages/${filePage}`, { responseType: 'blob' });
  }
}
