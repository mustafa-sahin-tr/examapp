import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  ParentActivationAcceptRequest,
  ParentActivationLookup,
  ParentActivationRequest,
  ParentActivationResult,
} from '../models/parent-activation.model';

/** Anonim aktivasyon uçlarının gateway yolu (`/api/exam/{everything}` değil, kimlik doğrulamasız ayrı route). */
export const PARENT_ACTIVATION_BASE_URL = '/api/exam/parent-activation';

/**
 * Issue #440: velinin e-postasındaki aktivasyon bağlantısı. `lookup`/`activate`/`resend` anonimdir — `authInterceptor` bu
 * yollara Bearer eklemez (`AUTH_EXCLUDED_PATHS`). `accept` ise oturum açmış velinin Bearer'ıyla gider (dışlanmaz). Token her
 * zaman gövdede gider (erişim loglarına düşmesin).
 */
@Injectable({ providedIn: 'root' })
export class ParentActivationService {
  private readonly http = inject(HttpClient);

  lookup(token: string): Observable<ParentActivationLookup> {
    return this.http.post<ParentActivationLookup>(`${PARENT_ACTIVATION_BASE_URL}/lookup`, { token });
  }

  activate(request: ParentActivationRequest): Observable<ParentActivationResult> {
    return this.http.post<ParentActivationResult>(`${PARENT_ACTIVATION_BASE_URL}/activate`, request);
  }

  /** Mevcut hesaplı veli: giriş yaptıktan sonra daveti kabul eder (Bearer). */
  accept(request: ParentActivationAcceptRequest): Observable<ParentActivationResult> {
    return this.http.post<ParentActivationResult>(`${PARENT_ACTIVATION_BASE_URL}/accept`, request);
  }

  resend(token: string): Observable<{ message?: string }> {
    return this.http.post<{ message?: string }>(`${PARENT_ACTIVATION_BASE_URL}/resend`, { token });
  }
}
