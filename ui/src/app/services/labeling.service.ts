import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  DetectionSource,
  LabelingActivityPrompt,
  LabelingActivityPromptsState,
  LabelingActivityStructureRequest,
  LabelingActivityStructureResponse,
  LabelingActivitySolutionList,
  LabelingActivitySolutionRequest,
  LabelingActivitySolutionResponse,
  LabelingAnswerKeyBatchLookupResponse,
  LabelingAnswerKeyEntry,
  LabelingAnswerKeyLookupRequest,
  LabelingAnswerKeyEntryUpdate,
  LabelingAnswerKeyLookupResponse,
  LabelingAnswerKeyReadRequest,
  LabelingAnswerKeyReadResponse,
  LabelingAiDetectRequest,
  LabelingAiDetectResponse,
  LabelingAiModel,
  LabelingBankSaveRequest,
  LabelingBankSaveResponse,
  LabelingPage,
  LabelingPromptKind,
  LabelingPromptState,
  LabelingResult,
  LabelingSaveManualRequest,
  LabelingSaveActivityPromptRequest,
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

  /** Soruları teste bağlamadan soru bankasına kaydeder; sayfa daha önce kaydedildiyse (ve `force` değilse) 409. */
  saveToBank(request: LabelingBankSaveRequest): Observable<LabelingBankSaveResponse> {
    return this.http.post<LabelingBankSaveResponse>(`${this.baseUrl}/pages/bank`, request);
  }

  getPrompt(): Observable<LabelingPromptState> {
    return this.http.get<LabelingPromptState>(`${this.baseUrl}/prompt`);
  }

  /** Yeni sürüm olarak kaydeder; sonraki Claude çağrıları bunu kullanır. */
  savePrompt(request: LabelingSavePromptRequest): Observable<LabelingPromptState> {
    return this.http.put<LabelingPromptState>(`${this.baseUrl}/prompt`, request);
  }

  /**
   * Issue #579: etkinlik bloklarının taslak etkileşim planları (blok başına bir Claude çağrısı, sunucuda paralel; kayıtlı
   * sonuç `force` değilse ücretsiz döner). 429 `LabelingRateLimited` / `LabelingBudgetExceeded`, anahtar yoksa 503.
   */
  activityStructure(request: LabelingActivityStructureRequest): Observable<LabelingActivityStructureResponse> {
    return this.http.post<LabelingActivityStructureResponse>(`${this.baseUrl}/activity-structure`, request);
  }

  getActivityPrompts(): Observable<LabelingActivityPromptsState> {
    return this.http.get<LabelingActivityPromptsState>(`${this.baseUrl}/activity-prompts`);
  }

  /** Tipin talimatını yeni sürüm olarak kaydeder (etkin sürümü döner); yalnız o tipin önbelleği geçersiz olur. */
  saveActivityPrompt(
    type: LabelingPromptKind,
    request: LabelingSaveActivityPromptRequest
  ): Observable<LabelingActivityPrompt> {
    return this.http.put<LabelingActivityPrompt>(
      `${this.baseUrl}/activity-prompts/${encodeURIComponent(type)}`,
      request
    );
  }

  // ---------------- Issue #580: cevap anahtarı ve etkinlik çözümü ----------------

  /** Cevap anahtarı sayfasını Claude ile okur (kayıtlı sonuç `force` değilse ücretsiz); anahtar aralığı dışında 400. */
  readAnswerKey(request: LabelingAnswerKeyReadRequest): Observable<LabelingAnswerKeyReadResponse> {
    return this.http.post<LabelingAnswerKeyReadResponse>(`${this.baseUrl}/answer-key/read`, request);
  }

  /** Kitabın anahtar satırları; basılı sayfa / etkinlik etiketiyle süzülür (boş alanlar gönderilmez). */
  getAnswerKey(
    bookId: number,
    filter: { printedPage?: number | null; activityLabel?: string | null; filePage?: number | null } = {}
  ): Observable<LabelingAnswerKeyLookupResponse> {
    let params = new HttpParams();
    // Etkinliğin DOSYA sayfası: fasiküllü kitapta tekrar eden basılı numarayı ayırır.
    if (filter.filePage != null) {
      params = params.set('filePage', filter.filePage);
    }
    if (filter.printedPage != null) {
      params = params.set('printedPage', filter.printedPage);
    }
    if (filter.activityLabel?.trim()) {
      params = params.set('activityLabel', filter.activityLabel.trim());
    }
    return this.http.get<LabelingAnswerKeyLookupResponse>(`${this.baseUrl}/books/${bookId}/answer-key`, { params });
  }

  /** Sayfadaki blokların (soru + etkinlik) anahtar satırları tek istekte; sonuçlar `key` ile döner. */
  lookupAnswerKey(bookId: number, request: LabelingAnswerKeyLookupRequest): Observable<LabelingAnswerKeyBatchLookupResponse> {
    return this.http.post<LabelingAnswerKeyBatchLookupResponse>(`${this.baseUrl}/books/${bookId}/answer-key/lookup`, request);
  }

  updateAnswerKeyEntry(id: number, request: LabelingAnswerKeyEntryUpdate): Observable<LabelingAnswerKeyEntry> {
    return this.http.put<LabelingAnswerKeyEntry>(`${this.baseUrl}/answer-key/entries/${id}`, request);
  }

  deleteAnswerKeyEntry(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/answer-key/entries/${id}`);
  }

  /** Etkinlik bloğunun taslak çözümü (anahtardan ya da AI); 429 tavan / bütçe, 503 anahtar yok. */
  solveActivity(request: LabelingActivitySolutionRequest): Observable<LabelingActivitySolutionResponse> {
    return this.http.post<LabelingActivitySolutionResponse>(`${this.baseUrl}/activity-solution`, request);
  }

  /**
   * Elle düzeltilen çözüm (taslak planına göre geçerli olmalı). `approve` false: onay bekler; true: düzelten onaylar (kaynak
   * `Human`, onaylayan + zaman yazılır).
   */
  updateActivitySolution(
    id: number,
    solution: Record<string, unknown>,
    approve = false
  ): Observable<LabelingActivitySolutionResponse> {
    return this.http.put<LabelingActivitySolutionResponse>(`${this.baseUrl}/activity-solution/${id}`, { solution, approve });
  }

  /** Kırpmanın kayıtlı çözüm taslakları (sayfa yeniden açılınca geri yüklemek için), en son güncellenen önce. */
  getActivitySolutions(cropSha256: string): Observable<LabelingActivitySolutionList> {
    const params = new HttpParams().set('cropSha256', cropSha256);
    return this.http.get<LabelingActivitySolutionList>(`${this.baseUrl}/activity-solution`, { params });
  }

  /** Yalnız Admin + QuestionTrainer (diğerleri 403). */
  approveActivitySolution(id: number): Observable<LabelingActivitySolutionResponse> {
    return this.http.post<LabelingActivitySolutionResponse>(`${this.baseUrl}/activity-solution/${id}/approve`, {});
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

/** Manuel kayıttan açılan tuvalin kaynağı; yeniden eğitimde kaydın başlangıç kaynağı korunur. */
export function manualDetectionSource(manual: LabelingResult): DetectionSource {
  const base =
    manual.baseSource === 'Model' || manual.baseSource === 'Ai'
      ? { source: manual.baseSource, modelName: manual.baseModelName, promptVersion: manual.basePromptVersion ?? null }
      : null;
  return { kind: 'Manual', base };
}
