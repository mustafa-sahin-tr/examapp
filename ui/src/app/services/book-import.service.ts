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
  BookImportActivity,
  BookAudienceSave,
  BookImportDemo,
  BookImportJob,
  BookImportDeferredJob,
  BookImportJobCounts,
  BookImportJobDetail,
  BookImportKeyMatchResponse,
  BookImportQrRun,
  BookImportPageDraft,
  BookImportPageFix,
  BookImportReviewQuestion,
  BookImportQuota,
  BookImportSetup,
  BookImportStatus,
  BookImportTrainingSummary,
  BookImportUploadEvent,
  BookImportUploadSession,
  ConfirmBookImportSetup,
  StartBookImportRequest,
} from '../models/book-import';
import { LabelingBankBox, LabelingBankPassage, LabelingBankQuestion } from '../models/labeling';
import { WorksheetTeacherSharing } from '../models/test-instance';

/** Aktarım listesinin bir sayfası ("Daha fazla göster" bu kadar ekler). */
export const BOOK_IMPORTS_PAGE = 50;

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

  /** En yeni önce `take` iş; `status` verilirse yalnız o durumdakiler (eski işler de gelir). */
  list(take = BOOK_IMPORTS_PAGE, status?: BookImportStatus): Observable<BookImportJob[]> {
    const params: Record<string, string | number> = { take };
    if (status) params['status'] = status;
    return this.http.get<BookImportJob[]>(this.baseUrl, { params });
  }

  /** Filtre sayıları: bütün görünür işler (listenin yüklenen sayfasından bağımsız). */
  counts(): Observable<BookImportJobCounts> {
    return this.http.get<BookImportJobCounts>(`${this.baseUrl}/counts`);
  }

  /** #1231: sonraya bırakılan işler ve onaylanmamış testleri. */
  deferred(): Observable<BookImportDeferredJob[]> {
    return this.http.get<BookImportDeferredJob[]>(`${this.baseUrl}/deferred`);
  }

  /** #1231: kalan testleri sonraya bırak (`false`: aktarım listesine geri al). */
  setDeferred(id: string, deferred: boolean): Observable<void> {
    const url = `${this.baseUrl}/${id}/defer`;
    return deferred ? this.http.post<void>(url, {}) : this.http.delete<void>(url);
  }

  get(id: string): Observable<BookImportJobDetail> {
    return this.http.get<BookImportJobDetail>(`${this.baseUrl}/${id}`);
  }

  /** #1265: aktarım akışı; yalnız `afterId` / `afterCallId`'den sonraki satırlar. */
  activity(id: string, afterId = 0, calls = false, afterCallId = 0): Observable<BookImportActivity> {
    const params: Record<string, string> = { afterId: String(afterId) };
    if (calls) {
      params['calls'] = 'true';
      params['afterCallId'] = String(afterCallId);
    }
    return this.http.get<BookImportActivity>(`${this.baseUrl}/${id}/events`, { params });
  }

  /** #1266: kurulum onayından sonra testlerin paylaşımı (onaylanmamış taslaklara ve sonrakilere uygulanır). */
  setSharing(
    id: string,
    teacherSharing: WorksheetTeacherSharing,
    audience?: BookAudienceSave,
  ): Observable<{ updatedDrafts: number }> {
    return this.http.put<{ updatedDrafts: number }>(`${this.baseUrl}/${id}/sharing`, { teacherSharing, audience });
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
          ? { fileName: imagesName(images[0]), fileSizeBytes: totalBytes, source: request.loose ? 'LooseImages' : 'Images', imageCount: images.length }
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

  /** Onay ekranında "AI ile bul": anahtarsız testin anahtarını önerir (kaydetmez); bulunamazsa match null. */
  findKeyWithAi(id: string, order: number): Observable<BookImportKeyMatchResponse> {
    return this.http.post<BookImportKeyMatchResponse>(`${this.baseUrl}/${id}/setup/tests/${order}/ai-key`, {});
  }

  /**
   * #1219 "QR'dan al": anahtarsız testlerin (order verilirse yalnız onun) QR çözümlerini arka planda okutur; anahtarlar test
   * satırlarına QR kaynağıyla yazılır, ilerleme kurulumun qrRun alanında.
   */
  startQrKeys(id: string, order?: number): Observable<BookImportQrRun> {
    const params = order != null ? { order } : undefined;
    return this.http.post<BookImportQrRun>(`${this.baseUrl}/${id}/setup/qr-keys`, {}, { params });
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

  approveAll(id: string, collectTestName?: string): Observable<ApproveAllBookImportResult> {
    return this.http.post<ApproveAllBookImportResult>(
      `${this.baseUrl}/${id}/approve`,
      collectTestName ? { collectTestName } : {}
    );
  }

  removeTest(id: string, testId: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}/tests/${testId}`);
  }

  /** #1169: hatalı tespit edilmiş taslak soruyu testten çıkar (son soru çıkarılamaz: 409). */
  removeQuestion(id: string, testId: number, questionId: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}/tests/${testId}/questions/${questionId}`);
  }

  /** #1303: soru sayısı doğru; sayı uyuşmazlığı işareti kalkar (başka işaret yoksa test onaylanabilir). */
  acceptCount(id: string, testId: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/tests/${testId}/accept-count`, {});
  }

  /** Onaylanmamış testi baştan aktar: taslakları silinir, sayfa tespitleri önbellekten gelir. */
  reimportTest(id: string, testId: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/tests/${testId}/reimport`, {});
  }

  /**
   * Issue #1023 "İşaretlemede düzelt": testin bir sayfasının taslaklarını işaretleme ekranında düzeltilenlerle değiştirir.
   * `activities` (#1293): tuvalde kalan etkinlik kutuları; sayfanın bunlarla örtüşmeyen etkinlik taslakları silinir.
   */
  replacePage(
    id: string,
    testId: number,
    filePage: number,
    questions: LabelingBankQuestion[],
    activities?: LabelingBankBox[],
    passages?: LabelingBankPassage[]
  ): Observable<BookImportPageFix> {
    return this.http.put<BookImportPageFix>(`${this.baseUrl}/${id}/tests/${testId}/pages/${filePage}/questions`, {
      questions,
      ...(activities ? { activities } : {}),
      ...(passages?.length ? { passages } : {}),
    });
  }

  /** Issue #1173: düzeltilecek sayfanın taslakları (numara, kutu, doğru şık). */
  pageDrafts(id: string, testId: number, filePage: number): Observable<BookImportPageDraft[]> {
    return this.http.get<BookImportPageDraft[]>(`${this.baseUrl}/${id}/tests/${testId}/pages/${filePage}/questions`);
  }

  /** Sayfa görseli (WebP, #1020); yetkili istek olduğundan <img src> yerine blob. */
  /** `original`: #1234 temizlenmiş sayfanın orijinali. `width`: #1358 küçük resim (64–800 px, sunucuda küçültülür). */
  pageImage(id: string, filePage: number, original = false, width?: number): Observable<Blob> {
    const params: Record<string, string | number | boolean> = {};
    if (original) params['original'] = true;
    if (width != null) params['width'] = width;
    return this.http.get(`${this.baseUrl}/${id}/pages/${filePage}`, { responseType: 'blob', params });
  }

  /** #1234 "Sayfaları temizle": arka planda; işaret rengi verilirse öğrenilmez, bu renk kullanılır. */
  /** Kurulumu baştan çalıştır (tarama + anahtar okuma); onay ekranındaki düzenlemeler gider. */
  rerunSetup(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/setup/rerun`, {});
  }

  startCleaning(id: string, markColour?: number[]): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/setup/clean`, markColour ? { markColour } : {});
  }

  /** #1234 "Orijinal sayfaları kullan". */
  setCleaning(id: string, useOriginal: boolean): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/setup/clean`, { useOriginal });
  }
}
