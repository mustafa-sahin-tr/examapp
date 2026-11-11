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
  BookImportTrainingSummary,
  BookImportUploadEvent,
  BookImportUploadSession,
  ConfirmBookImportSetup,
  StartBookImportRequest,
} from '../models/book-import';
import { LabelingBankQuestion } from '../models/labeling';

/** Issue #1122: ağ ya da sunucu hatasında bir parça en fazla bu kadar yeniden denenir (bekleme her denemede uzar). */
export const BOOK_IMPORT_PART_RETRIES = 3;
export const BOOK_IMPORT_PART_RETRY_MS = 1000;

/** Görsellerle aktarımda işin dosya adı: klasör seçildiyse klasörün adı, yoksa ilk görselin adı. */
function imagesName(first: File): string {
  const path = first.webkitRelativePath || first.name;
  return path.includes('/') ? path.split('/')[0] : path;
}

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
   * Issue #1144: `images` doluysa PDF yerine sayfa görselleri gider; her görsel bir parça (1. görsel = 1. sayfa).
   */
  start(request: StartBookImportRequest): Observable<BookImportUploadEvent> {
    const images = request.images ?? [];
    const isImages = images.length > 0;
    const totalBytes = isImages ? images.reduce((sum, image) => sum + image.size, 0) : request.file!.size;
    return this.http
      .post<BookImportUploadSession>(`${this.baseUrl}/uploads`, {
        bookName: request.bookName,
        gradeId: request.gradeId,
        subjectId: request.subjectId,
        ...(isImages
          ? { fileName: imagesName(images[0]), fileSizeBytes: totalBytes, source: 'Images', imageCount: images.length }
          : { fileName: request.file!.name, fileSizeBytes: totalBytes }),
      })
      .pipe(
        switchMap((session) => {
          const parts = session.partCount;
          // Parçanın blob'u ve kendinden önceki parçaların toplam baytı.
          const blobAt = (part: number): { blob: Blob; before: number } => {
            if (isImages) {
              let before = 0;
              for (let i = 0; i < part - 1; i++) before += images[i].size;
              return { blob: images[part - 1], before };
            }
            const from = (part - 1) * session.partSizeBytes;
            return {
              blob: request.file!.slice(from, Math.min(from + session.partSizeBytes, totalBytes)),
              before: from,
            };
          };
          const progress = (part: number, sentBytes: number): BookImportUploadEvent => ({
            type: 'progress',
            part,
            parts,
            sentBytes,
            totalBytes,
          });
          const sent = range(1, parts).pipe(
            concatMap((part) => {
              const { blob, before } = blobAt(part);
              return this.sendPart(session, blob, part).pipe(
                map((loaded) =>
                  loaded === null ? progress(part, before + blob.size) : progress(part - 1, before + loaded)
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
  private sendPart(session: BookImportUploadSession, blob: Blob, part: number): Observable<number | null> {
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

  /** #1133 (Admin): eğitim verisi özeti; dosya üretmez. */
  trainingSummary(): Observable<BookImportTrainingSummary> {
    return this.http.get<BookImportTrainingSummary>(`${this.baseUrl}/training-export`);
  }

  /** #1133 (Admin): sayfa görselleri + manifest.json ZIP'i (question-detector `training/import_book_export.py`). */
  trainingZip(): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/training-export`, { params: { dryRun: false }, responseType: 'blob' });
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
