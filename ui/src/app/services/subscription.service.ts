import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  AdminSubscriptionUser,
  AdminSubscriptionUserDetail,
  CurrentSubscriptionPlan,
  EntitlementFeature,
  PlanAssignment,
  SchoolPlanAudience,
  SchoolSubscriptionMembers,
  SubscriptionPlan,
  SubscriptionPlanUpdate,
  SubscriptionSearchRole,
} from '../models/subscription';

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

  /** #1093: çağıranın rolüne ait özellikler, kart sırasıyla. */
  features(): Observable<EntitlementFeature[]> {
    return this.http.get<EntitlementFeature[]>(`${this.baseUrl}/features`);
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

  // ---- Issue #1030: admin plan ataması (yalnız Admin / SuperAdmin) ----

  private readonly adminUrl = '/api/exam/admin/subscription-plans';

  /** Etkin ve pasif tüm planlar. */
  adminPlans(): Observable<SubscriptionPlan[]> {
    return this.http.get<SubscriptionPlan[]>(this.adminUrl);
  }

  /** #1086: planın adı, fiyatı, deneme süresi, etkinliği ve bütün sınırları; güncel planı döndürür. */
  updatePlan(planKey: string, update: SubscriptionPlanUpdate): Observable<SubscriptionPlan> {
    return this.http.put<SubscriptionPlan>(`${this.adminUrl}/${encodeURIComponent(planKey)}`, update);
  }

  /** Ad ya da e-postada geçen metin (en az 2 karakter); en fazla 20 sonuç. */
  searchUsers(query: string, role: SubscriptionSearchRole | null): Observable<AdminSubscriptionUser[]> {
    let params = new HttpParams().set('q', query);
    if (role) params = params.set('role', role);
    return this.http.get<AdminSubscriptionUser[]>(`${this.adminUrl}/users`, { params });
  }

  userDetail(userId: number): Observable<AdminSubscriptionUserDetail> {
    return this.http.get<AdminSubscriptionUserDetail>(`${this.adminUrl}/users/${userId}`);
  }

  assignUser(userId: number, assignment: PlanAssignment): Observable<unknown> {
    return this.http.put(`${this.adminUrl}/users/${userId}`, assignment);
  }

  endUserPlan(userId: number): Observable<unknown> {
    return this.http.post(`${this.adminUrl}/users/${userId}/end`, {});
  }

  schoolMembers(schoolId: number): Observable<SchoolSubscriptionMembers> {
    return this.http.get<SchoolSubscriptionMembers>(`${this.adminUrl}/schools/${schoolId}/members`);
  }

  assignSchool(schoolId: number, assignment: PlanAssignment & { audience: SchoolPlanAudience }): Observable<{ assigned: number }> {
    return this.http.put<{ assigned: number }>(`${this.adminUrl}/schools/${schoolId}`, assignment);
  }
}
