import { Injectable } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface QuestionTransferJob {
  id: string;
  kind: 'Export' | 'Import' | string;
  status: 'Queued' | 'Running' | 'Completed' | 'Failed' | string;
  sourceKey: string;
  totalItems: number;
  processedItems: number;
  fileUrl?: string | null;
  message?: string | null;
}

@Injectable({ providedIn: 'root' })
export class QuestionTransferService {
  private apiUrl = '/api/exam/question-transfer';

  constructor(private http: HttpClient) {}

  startExport(questionIds: number[], sourceKey?: string | null): Observable<QuestionTransferJob> {
    return this.http.post<QuestionTransferJob>(`${this.apiUrl}/exports`, {
      questionIds,
      sourceKey,
    });
  }

  startImport(file: File, sourceKey?: string | null): Observable<QuestionTransferJob> {
    const form = new FormData();
    form.append('sourceKey', sourceKey ?? 'default');
    form.append('file', file);
    return this.http.post<QuestionTransferJob>(`${this.apiUrl}/imports`, form);
  }

  listJobs(take = 50): Observable<QuestionTransferJob[]> {
    return this.http.get<QuestionTransferJob[]>(`${this.apiUrl}/jobs?take=${take}`);
  }

  getJob(id: string): Observable<QuestionTransferJob> {
    return this.http.get<QuestionTransferJob>(`${this.apiUrl}/jobs/${id}`);
  }

  downloadUrl(id: string): string {
    return `${this.apiUrl}/jobs/${id}/download`;
  }

  download(id: string): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.apiUrl}/jobs/${id}/download`, {
      observe: 'response',
      responseType: 'blob',
    });
  }

  hangfireLogin(): Observable<any> {
    return this.http.post(`${this.apiUrl}/hangfire/login`, {});
  }

  hangfireUrl(): string {
    return `/hangfire`;
  }
}
