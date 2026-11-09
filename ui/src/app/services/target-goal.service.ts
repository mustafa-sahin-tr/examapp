import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  AdminTargetProgram,
  SaveTargetProgram,
  TargetCatalogSummary,
  TargetExam,
  TargetGoal,
  TargetImportPreview,
  TargetImportRequest,
  TargetImportResult,
  TargetMockResult,
  TargetProgram,
  TargetProgramsForScore,
  TargetProgramPage,
  TargetProgramQuery,
} from '../models/target-goal.model';

const toParams = (query: TargetProgramQuery): HttpParams => {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== '') {
      params = params.set(key, value);
    }
  }
  return params;
};

const toForm = (request: TargetImportRequest): FormData => {
  const form = new FormData();
  form.append('Exam', request.exam);
  form.append('Year', String(request.year));
  form.append('Source', request.source);
  form.append('File', request.file);
  return form;
};

/** Issue #695: hedef okul ve taban puanlar. Ayar kapalıyken (`TargetGoals:Enabled`) durum ucu dışında her uç 404 döner. */
@Injectable({ providedIn: 'root' })
export class TargetGoalService {
  private readonly http = inject(HttpClient);

  getStatus(): Observable<{ enabled: boolean }> {
    return this.http.get<{ enabled: boolean }>('/api/exam/target-goals/status');
  }

  getMine(): Observable<TargetGoal> {
    return this.http.get<TargetGoal>('/api/exam/student/me/target-goal');
  }

  getChild(studentId: number): Observable<TargetGoal> {
    return this.http.get<TargetGoal>(`/api/exam/parent/children/${studentId}/target-goal`);
  }

  setMine(targetProgramId: number): Observable<TargetGoal> {
    return this.http.put<TargetGoal>('/api/exam/student/me/target-goal', { targetProgramId });
  }

  clearMine(): Observable<void> {
    return this.http.delete<void>('/api/exam/student/me/target-goal');
  }

  /** #891: il seçimi için katalogdaki iller. */
  programCities(exam: TargetExam): Observable<string[]> {
    return this.http.get<string[]>('/api/exam/student/me/target-goal/programs/cities', { params: { exam } });
  }

  /** Puan türünün şablonlarında son denemeler, eskiden yeniye. */
  history(scoreType: string): Observable<TargetMockResult[]> {
    return this.http.get<TargetMockResult[]>('/api/exam/student/me/target-goal/history', { params: { scoreType } });
  }

  programsForScore(scoreType: string, score: number, city?: string): Observable<TargetProgramsForScore> {
    return this.http.get<TargetProgramsForScore>('/api/exam/student/me/target-goal/programs/for-score', {
      params: toParams({ scoreType, score, city } as TargetProgramQuery),
    });
  }

  searchPrograms(query: TargetProgramQuery): Observable<TargetProgramPage<TargetProgram>> {
    return this.http.get<TargetProgramPage<TargetProgram>>('/api/exam/student/me/target-goal/programs', { params: toParams(query) });
  }

  adminList(query: TargetProgramQuery): Observable<TargetProgramPage<AdminTargetProgram>> {
    return this.http.get<TargetProgramPage<AdminTargetProgram>>('/api/exam/admin/target-programs', { params: toParams(query) });
  }

  adminSummary(): Observable<TargetCatalogSummary[]> {
    return this.http.get<TargetCatalogSummary[]>('/api/exam/admin/target-programs/summary');
  }

  adminSave(program: SaveTargetProgram, id: number | null): Observable<AdminTargetProgram> {
    return id === null
      ? this.http.post<AdminTargetProgram>('/api/exam/admin/target-programs', program)
      : this.http.put<AdminTargetProgram>(`/api/exam/admin/target-programs/${id}`, program);
  }

  adminDelete(id: number): Observable<void> {
    return this.http.delete<void>(`/api/exam/admin/target-programs/${id}`);
  }

  previewImport(request: TargetImportRequest): Observable<TargetImportPreview> {
    return this.http.post<TargetImportPreview>('/api/exam/admin/target-programs/import/preview', toForm(request));
  }

  confirmImport(request: TargetImportRequest): Observable<TargetImportResult> {
    return this.http.post<TargetImportResult>('/api/exam/admin/target-programs/import/confirm', toForm(request));
  }
}
