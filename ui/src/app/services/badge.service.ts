import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, delay, of } from 'rxjs';

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
  iconUrl: string;
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

  getUserActivity(userId: number): Observable<UserActivityResponse> {
    const url = `${this.badgeApiUrl}/reports/users/${userId}/activity`;

    return this.http.get<UserActivityResponse>(url).pipe(
      catchError((error) => {
        console.warn('BadgeService: falling back to mock user activity response', error);
        return of(this.buildMockActivityResponse(userId)).pipe(delay(200));
      })
    );
  }

  getUserBadgeProgress(userId: number): Observable<BadgeProgressResponse> {
    const url = `${this.badgeApiUrl}/reports/users/${userId}/badge-progress`;

    return this.http.get<BadgeProgressResponse>(url).pipe(
      catchError((error) => {
        console.warn('BadgeService: falling back to mock badge progress response', error);
        return of(this.buildMockBadgeProgressResponse(userId)).pipe(delay(200));
      })
    );
  }

  private buildMockActivityResponse(userId: number): UserActivityResponse {
    const today = new Date();
    today.setHours(0, 0, 0, 0);

    const endDate = new Date(today);
    const startDate = new Date(today);
    startDate.setDate(startDate.getDate() - 30);

    return {
      userId,
      startDateUtc: startDate.toISOString(),
      endDateUtc: endDate.toISOString(),
      days: [
        {
          dateUtc: this.toUtcDateString(endDate, -6),
          questionCount: 3,
          correctCount: 1,
          totalTimeSeconds: 704,
          totalPoints: 0,
          activityScore: 46,
        },
        {
          dateUtc: this.toUtcDateString(endDate, -2),
          questionCount: 14,
          correctCount: 12,
          totalTimeSeconds: 414,
          totalPoints: 0,
          activityScore: 206,
        },
        {
          dateUtc: this.toUtcDateString(endDate, 0),
          questionCount: 2,
          correctCount: 0,
          totalTimeSeconds: 13,
          totalPoints: 0,
          activityScore: 20,
        },
      ],
    };
  }

  private buildMockBadgeProgressResponse(userId: number): BadgeProgressResponse {
    return {
      summary: {
        userId,
        totalQuestions: 287,
        correctQuestions: 202,
        accuracyPercentage: 70.38,
        totalPoints: 0,
        currentCorrectStreak: 2,
        bestCorrectStreak: 13,
        totalTimeSeconds: 5853,
        totalActiveDays: 11,
        currentActivityStreak: 3,
        bestActivityStreak: 3,
        lastAnsweredAtUtc: new Date().toISOString(),
        lastUpdatedUtc: new Date().toISOString(),
      },
      badgeProgress: [
        {
          badgeDefinitionId: '44b5a21b-5d05-487f-8d89-4b4a5cc88d14',
          name: '5 Doğru Üst Üste',
          description: '5 doğru cevap arka arkaya verdin.',
          iconUrl: 'achievements/disabled-dark.041736.svg',
          pathKey: 'correct-streak',
          pathName: 'Doğru Yolu Bul',
          pathOrder: 1,
          currentValue: 5,
          targetValue: 5,
          isCompleted: true,
          earnedDateUtc: new Date().toISOString(),
        },
        {
          badgeDefinitionId: '49f14d0b-5729-4202-807d-f56f62dca798',
          name: 'Akademik Yolculuk',
          description: 'Toplam 25 saat çalıştın.',
          iconUrl: 'achievements/disabled-dark.1e1b53.svg',
          pathKey: 'study-time',
          pathName: 'Çalışma Serüveni',
          pathOrder: 3,
          currentValue: 97,
          targetValue: 1500,
          isCompleted: false,
          earnedDateUtc: null,
        },
        {
          badgeDefinitionId: '715ae387-d53b-4c6a-bec7-eb1c1624ac65',
          name: 'Hızlı Başlangıç',
          description: 'İlk 5 dakikalık çalışma tamamlandı.',
          iconUrl: 'achievements/disabled-dark.0085b3.svg',
          pathKey: 'study-time',
          pathName: 'Çalışma Serüveni',
          pathOrder: 1,
          currentValue: 5,
          targetValue: 5,
          isCompleted: true,
          earnedDateUtc: new Date().toISOString(),
        },
        {
          badgeDefinitionId: '3a2eb355-377c-4a21-aa25-d76da0b425a1',
          name: 'Soru Avcısı V',
          description: 'Toplam 500 soru çözdün.',
          iconUrl: 'achievements/disabled-dark.1e1b53.svg',
          pathKey: 'question-hunter',
          pathName: 'Soru Avcısı',
          pathOrder: 5,
          currentValue: 287,
          targetValue: 500,
          isCompleted: false,
          earnedDateUtc: null,
        },
        {
          badgeDefinitionId: '64ab0cc6-7c6d-49ba-b599-f3c1a3e33129',
          name: 'Doğru Yolu Bul II',
          description: 'Toplam 50 doğru cevap verdin.',
          iconUrl: 'achievements/disabled-dark.0085b3.svg',
          pathKey: 'correct-streak',
          pathName: 'Doğru Yolu Bul',
          pathOrder: 2,
          currentValue: 38,
          targetValue: 50,
          isCompleted: false,
          earnedDateUtc: null,
        },
        {
          badgeDefinitionId: 'b55e59bb-a98c-4d2a-a9d9-4eaa2b852a49',
          name: 'Soru Avcısı I',
          description: 'Toplam 10 soru çözdün.',
          iconUrl: 'achievements/disabled-dark.0b4480.svg',
          pathKey: 'question-hunter',
          pathName: 'Soru Avcısı',
          pathOrder: 1,
          currentValue: 10,
          targetValue: 10,
          isCompleted: true,
          earnedDateUtc: new Date().toISOString(),
        },
      ],
      subjectBreakdown: [
        {
          subjectId: 1,
          subjectName: 'Fen Bilimleri',
          totalQuestions: 120,
          correctQuestions: 77,
          accuracyPercentage: 64.16,
          totalTimeSeconds: 1512,
        },
        {
          subjectId: 2,
          subjectName: 'Matematik',
          totalQuestions: 102,
          correctQuestions: 73,
          accuracyPercentage: 71.57,
          totalTimeSeconds: 1481,
        },
      ],
    };
  }

  private toUtcDateString(base: Date, deltaDays: number): string {
    const clone = new Date(base);
    clone.setDate(clone.getDate() + deltaDays);
    clone.setUTCHours(0, 0, 0, 0);
    return clone.toISOString();
  }
}
