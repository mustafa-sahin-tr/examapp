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

export interface QuestionTransferExportBundle {
  sourceKey: string;
  bundleNo: number;
  questionCount: number;
  fileUrl?: string | null;
}

export interface QuestionTransferImportPreview {
  sourceKey: string;
  questionCount: number;
  alreadyImportedCount: number;
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
    if (sourceKey && sourceKey.trim().length > 0) {
      form.append('sourceKey', sourceKey.trim());
    }
    form.append('file', file);
    return this.http.post<QuestionTransferJob>(`${this.apiUrl}/imports`, form);
  }

  previewImport(file: File, sourceKeyOverride?: string | null): Observable<QuestionTransferImportPreview> {
    const form = new FormData();
    if (sourceKeyOverride && sourceKeyOverride.trim().length > 0) {
      form.append('sourceKey', sourceKeyOverride.trim());
    }
    form.append('file', file);
    return this.http.post<QuestionTransferImportPreview>(`${this.apiUrl}/imports/preview`, form);
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

  listExportBundles(sourceKey: string): Observable<QuestionTransferExportBundle[]> {
    const sk = (sourceKey ?? 'default').trim() || 'default';
    return this.http.get<QuestionTransferExportBundle[]>(`${this.apiUrl}/exports/${encodeURIComponent(sk)}/bundles`);
  }

  listSourceKeys(): Observable<string[]> {
    return this.http.get<string[]>(`${this.apiUrl}/exports/sources`);
  }

  downloadBundle(sourceKey: string, bundleNo: number): Observable<HttpResponse<Blob>> {
    const sk = (sourceKey ?? 'default').trim() || 'default';
    return this.http.get(`${this.apiUrl}/exports/${encodeURIComponent(sk)}/bundles/${bundleNo}/download`, {
      observe: 'response',
      responseType: 'blob',
    });
  }

  downloadBundleMap(sourceKey: string, bundleNo: number): Observable<HttpResponse<Blob>> {
    const sk = (sourceKey ?? 'default').trim() || 'default';
    return this.http.get(`${this.apiUrl}/exports/${encodeURIComponent(sk)}/bundles/${bundleNo}/map`, {
      observe: 'response',
      responseType: 'blob',
    });
  }

  downloadSourceIndex(sourceKey: string): Observable<HttpResponse<Blob>> {
    const sk = (sourceKey ?? 'default').trim() || 'default';
    return this.http.get(`${this.apiUrl}/exports/${encodeURIComponent(sk)}/index`, {
      observe: 'response',
      responseType: 'blob',
    });
  }

  downloadSourcePackage(sourceKey: string): Observable<HttpResponse<Blob>> {
    const sk = (sourceKey ?? 'default').trim() || 'default';
    return this.http.get(`${this.apiUrl}/exports/${encodeURIComponent(sk)}/package`, {
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
