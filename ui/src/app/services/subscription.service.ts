import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { CurrentSubscriptionPlan, SubscriptionPlan } from '../models/subscription';

/** Issue #1027 / #1028 (epic #1026): plan kataloğu, kullanıcının planı, deneme ve sahte ödeme. */
@Injectable({ providedIn: 'root' })
export class SubscriptionService {
  // Gateway: /api/exam/{everything} -> backend /api/{everything}
  private readonly baseUrl = '/api/exam/subscription-plans';
  private readonly http = inject(HttpClient);

  /** Çağıranın rolüne açık aktif planlar. */
  plans(): Observable<SubscriptionPlan[]> {
    return this.http.get<SubscriptionPlan[]>(this.baseUrl);
  }

  current(): Observable<CurrentSubscriptionPlan> {
    return this.http.get<CurrentSubscriptionPlan>(`${this.baseUrl}/me`);
  }

  /** Hesap başına bir kez; ikinci deneme 409 `subscription.trialUsed`. */
  startTrial(): Observable<CurrentSubscriptionPlan> {
    return this.http.post<CurrentSubscriptionPlan>(`${this.baseUrl}/me/trial`, {});
  }

  /** Sahte ödeme: ücretli planı bir aylık atar, ödeme alınmaz. */
  checkout(planKey: string): Observable<CurrentSubscriptionPlan> {
    return this.http.post<CurrentSubscriptionPlan>(`${this.baseUrl}/me/checkout`, { planKey });
  }
}
