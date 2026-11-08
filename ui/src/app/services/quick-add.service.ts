import { HttpClient, HttpEvent } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { BookImportJob, BookImportJobDetail, BookImportQuota } from '../models/book-import';
import { StartQuickAddRequest } from '../models/quick-add';

/**
 * Issue #1053 (epic #1048): hızlı soru ekleme — fotoğraf yükle, işleri listele, bir işi takip et. İşler kitap aktarımı işleridir
 * (`kind = QuickAdd`); sunucu bu uçlarda yalnız kullanıcının hızlı eklemelerini döndürür.
 */
@Injectable({ providedIn: 'root' })
export class QuickAddService {
  // Gateway: /api/exam/{everything} -> backend /api/{everything}
  private readonly baseUrl = '/api/exam/quick-add';
  private readonly http = inject(HttpClient);

  list(take = 20): Observable<BookImportJob[]> {
    return this.http.get<BookImportJob[]>(this.baseUrl, { params: { take } });
  }

  get(id: string): Observable<BookImportJobDetail> {
    return this.http.get<BookImportJobDetail>(`${this.baseUrl}/${id}`);
  }

  /** Bu ayki hak (fotoğraf sayısı); `limit` null → sınırsız. */
  quota(): Observable<BookImportQuota> {
    return this.http.get<BookImportQuota>(`${this.baseUrl}/quota`);
  }

  /** Yükleme ilerlemesi için olaylarla; `answers[i]` i. fotoğrafın cevap anahtarı (boş → anahtar yok). */
  start(request: StartQuickAddRequest): Observable<HttpEvent<BookImportJob>> {
    const form = new FormData();
    request.photos.forEach((photo) => {
      form.append('photos', photo.file, photo.file.name);
      form.append('answers', photo.answers.trim());
    });
    form.append('gradeId', String(request.gradeId));
    if (request.subjectId != null) form.append('subjectId', String(request.subjectId));
    return this.http.post<BookImportJob>(this.baseUrl, form, { reportProgress: true, observe: 'events' });
  }

  retry(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/retry`, {});
  }

  /** Yüklenen (yönü düzeltilmiş) fotoğraf; yetkili istek olduğundan <img src> yerine blob. */
  photo(id: string, photo: number): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/${id}/photos/${photo}`, { responseType: 'blob' });
  }
}
