import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { CreateChildAccountRequest, CreatedChildAccount } from '../models/parent-child-account.model';

/** Issue #438: veli panelinden çocuk hesabı açma ve şifre sıfırlama. */
@Injectable({ providedIn: 'root' })
export class ParentChildAccountService {
  // Gateway: /api/exam/{everything} -> backend /api/{everything}
  private readonly baseUrl = '/api/exam/parent/children';
  private readonly http = inject(HttpClient);

  create(request: CreateChildAccountRequest): Observable<CreatedChildAccount> {
    return this.http.post<CreatedChildAccount>(this.baseUrl, request);
  }

  resetPassword(studentId: number, newPassword: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${studentId}/reset-password`, { newPassword });
  }
}
