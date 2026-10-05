import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, finalize, map, share, tap } from 'rxjs';
import {
  ConversationMessages,
  ConversationPage,
  DIRECT_MESSAGE_HISTORY_TAKE,
  DIRECT_MESSAGE_PAGE_SIZE,
  DirectMessageBlockResult,
  DirectMessageReportResult,
  DirectMessageSenderRole,
  MarkConversationReadRequest,
  MarkDirectMessagesReadResult,
  MessageableTeacherPage,
  ReportDirectMessageRequest,
  SendDirectMessageResult,
  TEACHER_SEARCH_MIN_LENGTH,
  TeacherInboxFilter,
} from '../models/direct-message.model';

/** Öğrenci rozet sayımında taranan en yeni konuşma sayısı (backend pageSize üst sınırı). */
const STUDENT_UNREAD_SCAN_SIZE = 50;

/**
 * Issue #106 — doğrudan mesajlaşma uçları. Tüm çağrılar gateway üzerinden (`/api/exam/direct-messages/...`).
 *
 * `unreadCount` sidenav rozeti içindir: **okunmamış mesajı olan konuşma sayısı**. Ayrı bir sayım ucu yok;
 * öğretmende `inbox?filter=unread` `totalCount`'u, öğrencide en yeni 50 konuşmadan `unreadCount > 0` olanlar.
 * Polling yok: layout girişte ve pencere odağında, sayfalar liste yüklenince tazeler. Dilim (b) SignalR
 * `DirectMessage*` push'u geldiğinde aynı `refreshUnreadCount` çağrılacak.
 */
@Injectable({ providedIn: 'root' })
export class DirectMessageService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/exam/direct-messages';

  private readonly unreadCountState = signal(0);
  readonly unreadCount = this.unreadCountState.asReadonly();

  // ---- Öğrenci ----

  getMessageableTeachers(search: string, page: number, pageSize = DIRECT_MESSAGE_PAGE_SIZE): Observable<MessageableTeacherPage> {
    let params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    const term = search.trim();
    // Sunucu en az 2 karakter ister; daha kısası aramasız liste demektir.
    if (term.length >= TEACHER_SEARCH_MIN_LENGTH) {
      params = params.set('search', term);
    }
    return this.http.get<MessageableTeacherPage>(`${this.baseUrl}/teachers`, { params });
  }

  sendToTeacher(teacherId: number, body: string): Observable<SendDirectMessageResult> {
    return this.http.post<SendDirectMessageResult>(`${this.baseUrl}/teachers/${teacherId}/messages`, { body });
  }

  getStudentConversations(page: number, pageSize = DIRECT_MESSAGE_PAGE_SIZE): Observable<ConversationPage> {
    const params = new HttpParams().set('page', String(page)).set('pageSize', String(pageSize));
    return this.http.get<ConversationPage>(`${this.baseUrl}/conversations`, { params });
  }

  // ---- Öğretmen ----

  getInbox(filter: TeacherInboxFilter, page: number, pageSize = DIRECT_MESSAGE_PAGE_SIZE): Observable<ConversationPage> {
    const params = new HttpParams()
      .set('filter', filter)
      .set('page', String(page))
      .set('pageSize', String(pageSize));
    return this.http.get<ConversationPage>(`${this.baseUrl}/inbox`, { params });
  }

  block(conversationId: number): Observable<DirectMessageBlockResult> {
    return this.http.post<DirectMessageBlockResult>(`${this.baseUrl}/conversations/${conversationId}/block`, null);
  }

  unblock(conversationId: number): Observable<DirectMessageBlockResult> {
    return this.http.post<DirectMessageBlockResult>(`${this.baseUrl}/conversations/${conversationId}/unblock`, null);
  }

  // ---- İki taraf ----

  /** Bir sayfa mesaj (eskiden yeniye). Okundu işaretlemez — bkz. `markRead`. */
  getMessages(conversationId: number, beforeId?: number | null, take = DIRECT_MESSAGE_HISTORY_TAKE): Observable<ConversationMessages> {
    let params = new HttpParams().set('take', String(take));
    if (beforeId != null) {
      params = params.set('beforeId', String(beforeId));
    }
    return this.http.get<ConversationMessages>(`${this.baseUrl}/conversations/${conversationId}/messages`, { params });
  }

  /** Konuşma görünür olunca en yeni yüklü mesaj Id'siyle çağrılır; eski sayfa yüklemek okundu yapmaz. */
  markRead(conversationId: number, upToMessageId: number): Observable<MarkDirectMessagesReadResult> {
    const body: MarkConversationReadRequest = { upToMessageId };
    return this.http.post<MarkDirectMessagesReadResult>(`${this.baseUrl}/conversations/${conversationId}/read`, body);
  }

  sendToConversation(conversationId: number, body: string): Observable<SendDirectMessageResult> {
    return this.http.post<SendDirectMessageResult>(`${this.baseUrl}/conversations/${conversationId}/messages`, { body });
  }

  /** Şikayet: `messageId` yoksa konuşma şikayeti; boş not gönderilmez. */
  report(conversationId: number, request: ReportDirectMessageRequest): Observable<DirectMessageReportResult> {
    const body: ReportDirectMessageRequest = { reason: request.reason };
    if (request.messageId != null) {
      body.messageId = request.messageId;
    }
    const note = request.note?.trim();
    if (note) {
      body.note = note;
    }
    return this.http.post<DirectMessageReportResult>(`${this.baseUrl}/conversations/${conversationId}/report`, body);
  }

  // ---- Rozet ----

  /** Rol başına süren sayım isteği: eşzamanlı çağrılar tek HTTP isteğini paylaşır. */
  private readonly inFlight = new Map<DirectMessageSenderRole, Observable<number>>();

  /** Okunmamış konuşma sayısını rolüne göre çeker ve `unreadCount`'u günceller (eşzamanlı çağrılar tekilleşir). */
  refreshUnreadCount(role: DirectMessageSenderRole): Observable<number> {
    const pending = this.inFlight.get(role);
    if (pending) {
      return pending;
    }
    const count$ =
      role === 'Teacher'
        ? this.getInbox('unread', 1, 1).pipe(map((page) => page?.totalCount ?? 0))
        : this.getStudentConversations(1, STUDENT_UNREAD_SCAN_SIZE).pipe(
            map((page) => (page?.items ?? []).filter((item) => item.unreadCount > 0).length),
          );
    const shared$ = count$.pipe(
      tap((count) => this.unreadCountState.set(Math.max(0, count))),
      finalize(() => this.inFlight.delete(role)),
      share(),
    );
    this.inFlight.set(role, shared$);
    return shared$;
  }

  /**
   * Öğretmen gelen kutusu `filter=unread`, 1. sayfa yanıtının `totalCount`'u zaten rozet sayısıdır — ikinci istek atmadan
   * buradan yazılır.
   */
  setUnreadCount(count: number): void {
    this.unreadCountState.set(Math.max(0, count));
  }

  /** Okunmamış bir konuşma okundu işaretlenince (`markRead` başarılı) rozeti istek atmadan bir azaltır. */
  markConversationRead(): void {
    this.unreadCountState.update((count) => Math.max(0, count - 1));
  }

  /** Çıkışta rozet sıfırlanır. */
  resetUnreadCount(): void {
    this.unreadCountState.set(0);
  }
}
