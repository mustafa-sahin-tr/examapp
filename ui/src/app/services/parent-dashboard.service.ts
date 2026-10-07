import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ParentChildSummary } from '../models/parent-dashboard.model';

/** Veli paneli (issue #420). Yetki sunucuda: yalnızca Active bağlantı; diğer her durum 404. */
@Injectable({ providedIn: 'root' })
export class ParentDashboardService {
  // Gateway: /api/exam/{everything} -> backend /api/{everything}
  private readonly baseUrl = '/api/exam/parent/children';
  private readonly http = inject(HttpClient);

  getChildSummary(studentId: number): Observable<ParentChildSummary> {
    return this.http.get<ParentChildSummary>(`${this.baseUrl}/${studentId}/summary`);
  }
}
