import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import { ParentSettings } from '../models/account-settings.model';

@Injectable({
  providedIn: 'root',
})
export class ParentService {
  constructor(private http: HttpClient) {}

  register(): Observable<any> {
    return this.http.post<any>(`/api/exam/parent/register`, {});
  }

  /** Issue #1112: velinin kendi ayarları (haftalık rapor e-postası). */
  getSettings(): Observable<ParentSettings> {
    return this.http.get<ParentSettings>(`/api/exam/parent/settings`);
  }

  updateSettings(settings: ParentSettings): Observable<ParentSettings> {
    return this.http.put<ParentSettings>(`/api/exam/parent/settings`, settings);
  }
}
