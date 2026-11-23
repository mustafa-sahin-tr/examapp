import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import {
  ActionResult,
  AssistantError,
  AssistantStatus,
  AssistantStreamEvent,
  AssistantUsage,
  ChatRequest,
  FeedbackRating,
  FeedbackReason,
} from '../models/assistant';
import { AuthService } from './auth.service';

/**
 * Uygulama içi asistan API'si (#507). Gateway `/api/assistant/*` → AssistantService. Sohbet SSE akışıdır:
 * HttpClient akış vermediği için `fetch` ile okunur, Bearer elle eklenir, istekten önce token yenilenir.
 */
@Injectable({ providedIn: 'root' })
export class AssistantService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly baseUrl = '/api/assistant';

  status(): Observable<AssistantStatus> {
    return this.http.get<AssistantStatus>(`${this.baseUrl}/status`);
  }

  /** #523: öğrencinin velisi asistanı kapattı mı (exam API). */
  studentAccess(): Observable<{ allowed: boolean }> {
    return this.http.get<{ allowed: boolean }>('/api/exam/student/assistant-access');
  }

  usage(): Observable<AssistantUsage> {
    return this.http.get<AssistantUsage>(`${this.baseUrl}/usage/me`);
  }

  confirmAction(id: string): Observable<ActionResult> {
    return this.http.post<ActionResult>(`${this.baseUrl}/actions/${encodeURIComponent(id)}/confirm`, null);
  }

  rejectAction(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/actions/${encodeURIComponent(id)}/reject`, null);
  }

  feedback(messageId: number, rating: FeedbackRating, reason: FeedbackReason | null = null): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/messages/${messageId}/feedback`, { rating, reason });
  }

  /**
   * Bir tur gönderir ve olayları sırayla verir. Akış öncesi hata `AssistantError` (HTTP kodu + sunucu kodu) olarak atılır;
   * 401'de token bir kez yenilenip istek tekrarlanır. Akış içi `error` olayı olay olarak gelir (çağıran karar verir).
   */
  async *chat(request: ChatRequest, signal?: AbortSignal): AsyncGenerator<AssistantStreamEvent> {
    let response = await this.post(request, await this.freshToken(), signal);
    if (response.status === 401) {
      response = await this.post(request, await this.refreshedToken(), signal);
    }
    if (!response.ok || !response.body) {
      throw await this.errorOf(response);
    }

    const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
    let buffer = '';
    try {
      for (;;) {
        const { value, done } = await reader.read();
        if (done) break;
        buffer += value;
        let boundary = buffer.indexOf('\n\n');
        while (boundary >= 0) {
          const block = buffer.slice(0, boundary);
          buffer = buffer.slice(boundary + 2);
          const parsed = parseSseBlock(block);
          if (parsed) yield parsed;
          boundary = buffer.indexOf('\n\n');
        }
      }
      const tail = parseSseBlock(buffer);
      if (tail) yield tail;
    } finally {
      reader.releaseLock();
    }
  }

  /** `TokenExpired` olayından sonra: token'ı yenile (aynı mesaj çağıran tarafından yeniden gönderilir). */
  async refreshAfterExpiry(): Promise<void> {
    await this.refreshedToken();
  }

  private post(request: ChatRequest, token: string | null, signal?: AbortSignal): Promise<Response> {
    const headers: Record<string, string> = { 'Content-Type': 'application/json', Accept: 'text/event-stream' };
    if (token) headers['Authorization'] = `Bearer ${token}`;
    return fetch(`${this.baseUrl}/chat`, {
      method: 'POST',
      headers,
      body: JSON.stringify(request),
      credentials: 'include',
      signal,
    });
  }

  private async freshToken(): Promise<string | null> {
    const token = this.auth.getToken();
    if (token && this.auth.isExpiringSoon(token)) return this.refreshedToken();
    return token;
  }

  private async refreshedToken(): Promise<string> {
    try {
      const token = await firstValueFrom(this.auth.refreshToken());
      localStorage.setItem('auth_token', token);
      return token;
    } catch {
      throw new AssistantError('Unauthenticated', 401);
    }
  }

  private async errorOf(response: Response): Promise<AssistantError> {
    let code = response.status === 401 ? 'Unauthenticated' : 'AssistantUnavailable';
    let limit: string | null = null;
    try {
      const body = (await response.json()) as { code?: string; limit?: string } | null;
      if (body?.code) code = body.code;
      if (body?.limit) limit = body.limit;
    } catch {
      // Gövde JSON değil (gateway hatası): genel kod kalır.
    }
    return new AssistantError(code, response.status, limit);
  }
}

/** Tek SSE bloğu → olay. Yorum satırı (`: keep-alive`) ve bilinmeyen biçim null. */
export function parseSseBlock(block: string): AssistantStreamEvent | null {
  let event = 'message';
  const data: string[] = [];
  for (const raw of block.split('\n')) {
    const line = raw.endsWith('\r') ? raw.slice(0, -1) : raw;
    if (!line || line.startsWith(':')) continue;
    if (line.startsWith('event:')) event = line.slice(6).trim();
    else if (line.startsWith('data:')) data.push(line.slice(5).replace(/^ /, ''));
  }
  if (!data.length) return null;
  try {
    return { event, data: JSON.parse(data.join('\n')) } as AssistantStreamEvent;
  } catch {
    return null;
  }
}
