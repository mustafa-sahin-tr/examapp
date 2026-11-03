import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface QuestionTransferImportResult {
  imported: number;
  matched: number;
  alreadyImported: number;
  conflicts: number;
  failed: number;
  missingTaxonomy: number;
  conflictSamples: string[];
  failedSamples: string[];
  warnings: string[];
}

export interface QuestionTransferJob {
  id: string;
  kind: 'Export' | 'Import' | string;
  status: 'Queued' | 'Running' | 'Completed' | 'Failed' | string;
  sourceKey: string;
  totalItems: number;
  processedItems: number;
  fileUrl?: string | null;
  message?: string | null;
  result?: QuestionTransferImportResult | null;
}

export interface QuestionTransferImportPreview {
  sourceKey: string;
  questionCount: number;
  alreadyImportedCount: number;
  newCount: number;
  matchedCount: number;
  conflictCount: number;
  missingTaxonomyCount: number;
  missingImageCount: number;
  targetUnhashedCount: number;
  conflictSamples: string[];
}

/** Boş bırakılırsa sorular kapsamla seçilir; kapsam da yoksa tüm sorular çıkar. Kaynak adını sunucu üretir. */
export interface QuestionExportRequest {
  questionIds: number[];
  gradeId?: number | null;
  subjectId?: number | null;
  topicId?: number | null;
  bookId?: number | null;
}

export interface CatalogImportCounts {
  created: number;
  updated: number;
  unchanged: number;
}

export interface CatalogImportResult {
  dryRun: boolean;
  grades: CatalogImportCounts;
  subjects: CatalogImportCounts;
  gradeSubjects: CatalogImportCounts;
  topics: CatalogImportCounts;
  subTopics: CatalogImportCounts;
  learningOutcomes: CatalogImportCounts;
  errors: string[];
}

@Injectable({ providedIn: 'root' })
export class QuestionTransferService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = '/api/exam/question-transfer';
  private readonly catalogUrl = '/api/exam/admin/catalog';

  startExport(request: QuestionExportRequest): Observable<QuestionTransferJob> {
    return this.http.post<QuestionTransferJob>(`${this.apiUrl}/exports`, request);
  }

  /** Kaynak adı gönderilmez: sunucu paketin içinden okur. */
  startImport(file: File): Observable<QuestionTransferJob> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<QuestionTransferJob>(`${this.apiUrl}/imports`, form);
  }

  previewImport(file: File): Observable<QuestionTransferImportPreview> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<QuestionTransferImportPreview>(`${this.apiUrl}/imports/preview`, form);
  }

  listJobs(take = 50): Observable<QuestionTransferJob[]> {
    return this.http.get<QuestionTransferJob[]>(`${this.apiUrl}/jobs?take=${take}`);
  }

  downloadPackage(sourceKey: string): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.apiUrl}/exports/${encodeURIComponent(sourceKey)}/package`, {
      observe: 'response',
      responseType: 'blob',
    });
  }

  hangfireLogin(): Observable<unknown> {
    return this.http.post(`${this.apiUrl}/hangfire/login`, {});
  }

  hangfireUrl(): string {
    return `/hangfire`;
  }

  downloadCatalog(): Observable<HttpResponse<Blob>> {
    return this.http.get(`${this.catalogUrl}/export`, {
      observe: 'response',
      responseType: 'blob',
    });
  }

  /** `dryRun`: hiçbir şey yazmadan ne değişeceğini sayar. Geçersiz dosyada 400 + `errors` döner. */
  importCatalog(bundle: unknown, dryRun: boolean): Observable<CatalogImportResult> {
    return this.http.post<CatalogImportResult>(`${this.catalogUrl}/import`, bundle, {
      params: new HttpParams().set('dryRun', dryRun),
    });
  }
}
