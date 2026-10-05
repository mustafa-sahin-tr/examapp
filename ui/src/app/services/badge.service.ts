import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface UserActivityDay {
  dateUtc: string;
  questionCount: number;
  correctCount: number;
  totalTimeSeconds: number;
  totalPoints: number;
  activityScore: number;
}

export interface UserActivityResponse {
  userId: number;
  startDateUtc: string;
  endDateUtc: string;
  days: UserActivityDay[];
}

export interface BadgeProgressSummary {
  userId: number;
  totalQuestions: number;
  correctQuestions: number;
  accuracyPercentage: number;
  totalPoints: number;
  currentCorrectStreak: number;
  bestCorrectStreak: number;
  totalTimeSeconds: number;
  totalActiveDays: number;
  currentActivityStreak: number;
  bestActivityStreak: number;
  lastAnsweredAtUtc: string | null;
  lastUpdatedUtc: string | null;
}

export interface BadgeProgressItem {
  badgeDefinitionId: string;
  name: string;
  description: string;
  /**
   * Issue #149: Material Symbols adı (`^[a-z][a-z0-9_]{1,63}$`). Eski sunucuda alan yok → undefined;
   * UI `icon` → `iconUrl` → `military_tech` sırasıyla düşer (`resolveBadgeIcon`).
   */
  icon?: string | null;
  /** Eski SVG yolu (`achievements/<dosya>.svg`); geçiş süresince yedek, null olabilir. */
  iconUrl: string | null;
  pathKey?: string | null;
  pathName?: string | null;
  pathOrder?: number | null;
  currentValue: number;
  targetValue: number;
  isCompleted: boolean;
  earnedDateUtc: string | null;
}

export interface BadgeSubjectBreakdownItem {
  subjectId: number;
  subjectName: string;
  totalQuestions: number;
  correctQuestions: number;
  accuracyPercentage: number;
  totalTimeSeconds: number;
}

export interface BadgeProgressResponse {
  summary: BadgeProgressSummary;
  badgeProgress: BadgeProgressItem[];
  subjectBreakdown: BadgeSubjectBreakdownItem[];
}

@Injectable({
  providedIn: 'root',
})
export class BadgeService {
  private readonly badgeApiUrl = '/api/badge';

  constructor(private readonly http: HttpClient) {}

  // Hata durumunda mock fallback yok; hata olduğu gibi subscriber'a iletilir (issue #144).
  getUserActivity(userId: number): Observable<UserActivityResponse> {
    const url = `${this.badgeApiUrl}/reports/users/${userId}/activity`;
    return this.http.get<UserActivityResponse>(url);
  }

  getUserBadgeProgress(userId: number): Observable<BadgeProgressResponse> {
    const url = `${this.badgeApiUrl}/reports/users/${userId}/badge-progress`;
    return this.http.get<BadgeProgressResponse>(url);
  }
}
