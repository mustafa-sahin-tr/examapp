import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ClassifierStatus } from '../models/classifier-status.model';

/** Issue #897: sınıflandırıcı BadgeService'te çalışır; durumu oradan, gateway'in `/api/badge` yolundan okunur. */
@Injectable({ providedIn: 'root' })
export class ClassifierStatusService {
  private readonly http = inject(HttpClient);

  get(): Observable<ClassifierStatus> {
    return this.http.get<ClassifierStatus>('/api/badge/admin/classifier-status');
  }
}
