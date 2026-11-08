import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { BookImportJob, BookImportJobDetail, BookImportQuota, StartBookImportRequest } from '../models/book-import';

/** Issue #1018 (epic #1017): kitaptan aktarım işleri. Pilot: yalnız Admin + QuestionTrainer. */
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

  start(request: StartBookImportRequest): Observable<BookImportJob> {
    const form = new FormData();
    form.append('file', request.file, request.file.name);
    form.append('bookName', request.bookName);
    form.append('gradeId', String(request.gradeId));
    if (request.subjectId != null) form.append('subjectId', String(request.subjectId));
    return this.http.post<BookImportJob>(this.baseUrl, form);
  }
}
