import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { Paged } from '../models/test-instance';

/** Issue #1281: duyuru hedefi (backend `SchoolAnnouncementTarget`). */
export type AnnouncementTarget = 'School' | 'Grade' | 'Section' | 'Students';

/** Issue #1281: `GET /api/exam/announcements/options`. */
export interface AnnouncementOptions {
  schoolName: string;
  /** true → okul geneli ve sınıf hedefleri açık (okul yöneticisi). */
  canTargetSchoolAndGrade: boolean;
}

export interface AnnouncementSendRequest {
  target: AnnouncementTarget;
  gradeId?: number;
  sectionId?: number;
  studentIds?: number[];
  title: string;
  body: string;
}

/** Issue #1281: gönderilen duyuru (gönderim yanıtı ve "Gönderilenler" satırı). */
export interface SentAnnouncement {
  id: number;
  target: AnnouncementTarget;
  targetLabel: string | null;
  title: string;
  body: string;
  studentCount: number;
  teacherCount: number;
  parentCount: number;
  createdAt: string;
}

export const ANNOUNCEMENT_TITLE_MAX = 100;
export const ANNOUNCEMENT_BODY_MAX = 500;

/**
 * Issue #1281 (#1071 S6): okul duyuruları. Okul, hedef yetkisi (okul geneli / sınıf yalnız okul yöneticisi) ve alıcılar
 * sunucuda çözülür; bildirimler BadgeService'te yazılır.
 */
@Injectable({ providedIn: 'root' })
export class AnnouncementService {
  private readonly http = inject(HttpClient);
  private readonly url = '/api/exam/announcements';

  options(): Observable<AnnouncementOptions> {
    return this.http.get<AnnouncementOptions>(`${this.url}/options`);
  }

  send(request: AnnouncementSendRequest): Observable<SentAnnouncement> {
    return this.http.post<SentAnnouncement>(this.url, request);
  }

  sent(page: number, pageSize: number): Observable<Paged<SentAnnouncement>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<Paged<SentAnnouncement>>(`${this.url}/sent`, { params });
  }
}
