import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface ResetResponse {
  jobId: string;
  message: string;
}

@Injectable({ providedIn: 'root' })
export class StudentResetService {
  constructor(private readonly http: HttpClient) {}

  resetMyData(): Observable<ResetResponse> {
    return this.http.post<ResetResponse>('/api/exam/student/me/reset', {});
  }
}
