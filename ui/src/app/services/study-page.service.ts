import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Paged } from '../models/test-instance';
import { StudyPage, StudyPageFilter } from '../models/study-page';

@Injectable({
  providedIn: 'root',
})
export class StudyPageService {
  private baseUrl = '/api/exam/study-pages';

  constructor(private http: HttpClient) {}

  getPaged(filter: StudyPageFilter): Observable<Paged<StudyPage>> {
    const params = new URLSearchParams();
    if (filter.search) params.set('search', filter.search);
    if (filter.subjectId) params.set('subjectId', String(filter.subjectId));
    if (filter.topicId) params.set('topicId', String(filter.topicId));
    if (filter.subTopicId) params.set('subTopicId', String(filter.subTopicId));
    params.set('pageNumber', String(filter.pageNumber ?? 1));
    params.set('pageSize', String(filter.pageSize ?? 10));

    return this.http.get<Paged<StudyPage>>(`${this.baseUrl}?${params.toString()}`);
  }

  getById(id: number): Observable<StudyPage> {
    return this.http.get<StudyPage>(`${this.baseUrl}/${id}`);
  }

  create(
    request: {
      title: string;
      description: string;
      subjectId?: number | null;
      topicId?: number | null;
      subTopicId?: number | null;
      isPublished: boolean;
    },
    images: File[]
  ): Observable<StudyPage> {
    const formData = new FormData();
    formData.append('Title', request.title);
    formData.append('Description', request.description || '');
    if (request.subjectId) formData.append('SubjectId', String(request.subjectId));
    if (request.topicId) formData.append('TopicId', String(request.topicId));
    if (request.subTopicId) formData.append('SubTopicId', String(request.subTopicId));
    formData.append('IsPublished', String(request.isPublished));

    images.forEach((file) => formData.append('images', file));

    return this.http.post<StudyPage>(this.baseUrl, formData);
  }

  update(
    id: number,
    request: {
      title: string;
      description: string;
      subjectId?: number | null;
      topicId?: number | null;
      subTopicId?: number | null;
      isPublished: boolean;
      removedImageIds: number[];
    },
    newImages: File[]
  ): Observable<StudyPage> {
    const formData = new FormData();
    formData.append('Title', request.title);
    formData.append('Description', request.description || '');
    if (request.subjectId) formData.append('SubjectId', String(request.subjectId));
    if (request.topicId) formData.append('TopicId', String(request.topicId));
    if (request.subTopicId) formData.append('SubTopicId', String(request.subTopicId));
    formData.append('IsPublished', String(request.isPublished));
    request.removedImageIds.forEach((idValue) => formData.append('RemovedImageIds', String(idValue)));
    newImages.forEach((file) => formData.append('images', file));

    return this.http.put<StudyPage>(`${this.baseUrl}/${id}`, formData);
  }

  delete(id: number): Observable<any> {
    return this.http.delete(`${this.baseUrl}/${id}`);
  }
}
