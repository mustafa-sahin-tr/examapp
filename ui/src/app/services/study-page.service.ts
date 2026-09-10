import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Paged } from '../models/test-instance';
import {
  StudyPage,
  StudyPageContentType,
  StudyPageFilter,
  StudyPageUpdateRequest,
  StudyPageWriteRequest,
} from '../models/study-page';

@Injectable({
  providedIn: 'root',
})
export class StudyPageService {
  // Gateway: /api/exam/{everything} -> backend /api/{everything}
  private baseUrl = '/api/exam/study-items';
  private http = inject(HttpClient);

  getPaged(filter: StudyPageFilter): Observable<Paged<StudyPage>> {
    const params = new URLSearchParams();
    if (filter.search) params.set('search', filter.search);
    if (filter.subjectId) params.set('subjectId', String(filter.subjectId));
    if (filter.topicId) params.set('topicId', String(filter.topicId));
    if (filter.subTopicId) params.set('subTopicId', String(filter.subTopicId));
    if (filter.contentType !== null && filter.contentType !== undefined) {
      params.set('contentType', String(filter.contentType));
    }
    params.set('pageNumber', String(filter.pageNumber ?? 1));
    params.set('pageSize', String(filter.pageSize ?? 10));

    return this.http.get<Paged<StudyPage>>(`${this.baseUrl}?${params.toString()}`);
  }

  getById(id: number): Observable<StudyPage> {
    return this.http.get<StudyPage>(`${this.baseUrl}/${id}`);
  }

  create(request: StudyPageWriteRequest, images: File[]): Observable<StudyPage> {
    const formData = this.buildFormData(request);
    images.forEach((file) => formData.append('images', file));
    return this.http.post<StudyPage>(this.baseUrl, formData);
  }

  update(id: number, request: StudyPageUpdateRequest, newImages: File[]): Observable<StudyPage> {
    const formData = this.buildFormData(request);
    request.removedImageIds.forEach((idValue) => formData.append('RemovedImageIds', String(idValue)));
    newImages.forEach((file) => formData.append('images', file));
    return this.http.put<StudyPage>(`${this.baseUrl}/${id}`, formData);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  /**
   * Backend [FromForm] model binding PascalCase alan adlarını bekler.
   * Sadece seçili ContentType'a ait alanlar gönderilir; diğerlerini backend zaten temizler.
   */
  private buildFormData(request: StudyPageWriteRequest): FormData {
    const formData = new FormData();
    formData.append('Title', request.title);
    formData.append('Description', request.description || '');
    if (request.gradeId) formData.append('GradeId', String(request.gradeId));
    if (request.subjectId) formData.append('SubjectId', String(request.subjectId));
    if (request.topicId) formData.append('TopicId', String(request.topicId));
    if (request.subTopicId) formData.append('SubTopicId', String(request.subTopicId));
    formData.append('IsPublished', String(request.isPublished));
    formData.append('ContentType', String(request.contentType));

    switch (request.contentType) {
      case StudyPageContentType.Link:
        formData.append('Url', (request.url ?? '').trim());
        if (request.platform !== null && request.platform !== undefined) {
          formData.append('Platform', String(request.platform));
        }
        break;

      case StudyPageContentType.BookPageRange:
        if (request.bookId) formData.append('BookId', String(request.bookId));
        if (request.bookTestId) formData.append('BookTestId', String(request.bookTestId));
        if (request.newBookName?.trim()) formData.append('NewBookName', request.newBookName.trim());
        if (request.newBookTestName?.trim()) formData.append('NewBookTestName', request.newBookTestName.trim());
        if (request.startPage !== null && request.startPage !== undefined) {
          formData.append('StartPage', String(request.startPage));
        }
        if (request.endPage !== null && request.endPage !== undefined) {
          formData.append('EndPage', String(request.endPage));
        }
        break;

      case StudyPageContentType.Image:
      default:
        if (request.minioImages && request.minioImages.length > 0) {
          formData.append('MinioImages', JSON.stringify(request.minioImages));
        }
        break;
    }

    return formData;
  }
}
