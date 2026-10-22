import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  LabelingAiDetectRequest,
  LabelingAiDetectResponse,
  LabelingAiModel,
  LabelingPage,
  LabelingPromptState,
  LabelingSaveManualRequest,
  LabelingSavePromptRequest,
} from '../models/labeling';

/** İşaretleme kaynakları (Model / Claude / Manuel). Yalnız Admin + QuestionTrainer. */
@Injectable({ providedIn: 'root' })
export class LabelingService {
  // Gateway: /api/exam/{everything} -> backend /api/{everything}
  private readonly baseUrl = '/api/exam/labeling';
  private readonly http = inject(HttpClient);

  getAiModels(): Observable<LabelingAiModel[]> {
    return this.http.get<LabelingAiModel[]>(`${this.baseUrl}/ai-models`);
  }

  /** Kayıt yoksa 404. */
  getPage(imageSha256: string): Observable<LabelingPage> {
    return this.http.get<LabelingPage>(`${this.baseUrl}/pages/${imageSha256}`);
  }

  aiDetect(request: LabelingAiDetectRequest): Observable<LabelingAiDetectResponse> {
    return this.http.post<LabelingAiDetectResponse>(`${this.baseUrl}/ai-detect`, request);
  }

  saveManual(request: LabelingSaveManualRequest): Observable<LabelingPage> {
    return this.http.put<LabelingPage>(`${this.baseUrl}/pages/manual`, request);
  }

  getPrompt(): Observable<LabelingPromptState> {
    return this.http.get<LabelingPromptState>(`${this.baseUrl}/prompt`);
  }

  /** Yeni sürüm olarak kaydeder; sonraki Claude çağrıları bunu kullanır. */
  savePrompt(request: LabelingSavePromptRequest): Observable<LabelingPromptState> {
    return this.http.put<LabelingPromptState>(`${this.baseUrl}/prompt`, request);
  }
}

/**
 * Ham görsel baytlarının SHA-256'sı (küçük harf hex). `imageData` FileReader data URL'i: baytlar dosyanın kendisi,
 * sunucu ve question-detector aynı girdiyi hash'ler.
 */
export async function sha256OfDataUrl(dataUrl: string): Promise<string> {
  const comma = dataUrl.startsWith('data:') ? dataUrl.indexOf(',') : -1;
  const binary = atob(comma >= 0 ? dataUrl.slice(comma + 1) : dataUrl);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  const digest = await crypto.subtle.digest('SHA-256', bytes);
  return Array.from(new Uint8Array(digest), (b) => b.toString(16).padStart(2, '0')).join('');
}
