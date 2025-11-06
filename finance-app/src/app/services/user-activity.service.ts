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

@Injectable({
  providedIn: 'root',
})
export class UserActivityService {
  private readonly badgeApiUrl = '/api/badge';

  constructor(private readonly http: HttpClient) {}

  getUserActivity(userId: number): Observable<UserActivityResponse> {
    const url = `${this.badgeApiUrl}/reports/users/${userId}/activity`;

    return this.http.get<UserActivityResponse>(url).pipe(
      catchError((error) => {
        console.warn('UserActivityService: falling back to mock activity response', error);
        return of(this.buildMockActivityResponse(userId)).pipe(delay(200));
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

  private toUtcDateString(base: Date, deltaDays: number): string {
    const clone = new Date(base);
    clone.setDate(clone.getDate() + deltaDays);
    clone.setUTCHours(0, 0, 0, 0);
    return clone.toISOString();
  }
}
