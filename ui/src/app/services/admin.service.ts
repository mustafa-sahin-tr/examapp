import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  ApiResult,
  ClassifierCacheRefreshResult,
  ClassifierCacheStatus,
  School,
  TaxonomyTree,
} from '../models/taxonomy';

interface UpsertSubject {
  name: string;
}
interface UpsertTopic {
  name: string;
  subjectId: number;
  gradeId: number;
}
interface UpsertSubTopic {
  name: string;
  topicId: number;
}
interface UpsertSchool {
  name: string;
  city?: string | null;
}

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/exam/admin';

  // ---- taxonomy ----
  getTaxonomy(): Observable<TaxonomyTree> {
    return this.http.get<TaxonomyTree>(`${this.baseUrl}/taxonomy`);
  }

  createSubject(body: UpsertSubject) {
    return this.http.post<ApiResult>(`${this.baseUrl}/subjects`, body);
  }
  updateSubject(id: number, body: UpsertSubject) {
    return this.http.put<ApiResult>(`${this.baseUrl}/subjects/${id}`, body);
  }
  deleteSubject(id: number) {
    return this.http.delete<ApiResult>(`${this.baseUrl}/subjects/${id}`);
  }

  createTopic(body: UpsertTopic) {
    return this.http.post<ApiResult>(`${this.baseUrl}/topics`, body);
  }
  updateTopic(id: number, body: UpsertTopic) {
    return this.http.put<ApiResult>(`${this.baseUrl}/topics/${id}`, body);
  }
  deleteTopic(id: number) {
    return this.http.delete<ApiResult>(`${this.baseUrl}/topics/${id}`);
  }

  createSubTopic(body: UpsertSubTopic) {
    return this.http.post<ApiResult>(`${this.baseUrl}/subtopics`, body);
  }
  updateSubTopic(id: number, body: UpsertSubTopic) {
    return this.http.put<ApiResult>(`${this.baseUrl}/subtopics/${id}`, body);
  }
  deleteSubTopic(id: number) {
    return this.http.delete<ApiResult>(`${this.baseUrl}/subtopics/${id}`);
  }

  // ---- schools ----
  getSchools(): Observable<School[]> {
    return this.http.get<School[]>(`${this.baseUrl}/schools`);
  }
  createSchool(body: UpsertSchool) {
    return this.http.post<ApiResult>(`${this.baseUrl}/schools`, body);
  }
  updateSchool(id: number, body: UpsertSchool) {
    return this.http.put<ApiResult>(`${this.baseUrl}/schools/${id}`, body);
  }
  deleteSchool(id: number) {
    return this.http.delete<ApiResult>(`${this.baseUrl}/schools/${id}`);
  }

  // ---- classifier cache ----
  getClassifierCache(): Observable<ClassifierCacheStatus> {
    return this.http.get<ClassifierCacheStatus>(`${this.baseUrl}/classifier-cache`);
  }
  refreshClassifierCache(): Observable<ClassifierCacheRefreshResult> {
    return this.http.post<ClassifierCacheRefreshResult>(`${this.baseUrl}/classifier-cache/refresh`, {});
  }
}
