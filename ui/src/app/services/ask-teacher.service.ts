import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  AskTeacherQuestion,
  AskTeacherQuota,
  AskTeacherSendRequest,
  AskTeacherStatus,
  AskTeacherTeacher,
} from '../models/ask-teacher.model';

/** Issue #1111: öğretmene sor — öğrenci uçları ve öğretmen kuyruğu (`queue`). Görseller yalnız bu uçlardan (blob). */
@Injectable({ providedIn: 'root' })
export class AskTeacherService {
  private readonly baseUrl = '/api/exam/ask-teacher';
  private readonly http = inject(HttpClient);

  teachers(subjectId: number | null = null): Observable<AskTeacherTeacher[]> {
    const params = subjectId ? new HttpParams().set('subjectId', subjectId) : undefined;
    return this.http.get<AskTeacherTeacher[]>(`${this.baseUrl}/teachers`, { params });
  }

  quota(): Observable<AskTeacherQuota> {
    return this.http.get<AskTeacherQuota>(`${this.baseUrl}/quota`);
  }

  list(): Observable<AskTeacherQuestion[]> {
    return this.http.get<AskTeacherQuestion[]>(this.baseUrl);
  }

  send(request: AskTeacherSendRequest): Observable<AskTeacherQuestion> {
    const form = new FormData();
    form.append('photo', request.photo, 'question.jpg');
    form.append('teacherId', String(request.teacherId));
    if (request.subjectId) form.append('subjectId', String(request.subjectId));
    if (request.note.trim()) form.append('note', request.note.trim());
    return this.http.post<AskTeacherQuestion>(this.baseUrl, form);
  }

  cancel(id: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/cancel`, {});
  }

  rate(id: number, rating: number): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/rating`, { rating });
  }

  notebook(): Observable<AskTeacherQuestion[]> {
    return this.http.get<AskTeacherQuestion[]>(`${this.baseUrl}/notebook`);
  }

  setNotebook(id: number, saved: boolean): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/notebook`, { saved });
  }

  image(id: number, answer: boolean): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/${id}/${answer ? 'answer-image' : 'image'}`, { responseType: 'blob' });
  }

  queue(status: AskTeacherStatus | null = null): Observable<AskTeacherQuestion[]> {
    const params = status ? new HttpParams().set('status', status) : undefined;
    return this.http.get<AskTeacherQuestion[]>(`${this.baseUrl}/queue`, { params });
  }

  answer(id: number, text: string, image: Blob | null): Observable<AskTeacherQuestion> {
    const form = new FormData();
    if (text.trim()) form.append('text', text.trim());
    if (image) form.append('image', image, 'answer.jpg');
    return this.http.post<AskTeacherQuestion>(`${this.baseUrl}/queue/${id}/answer`, form);
  }
}
