import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { HintState } from '../models/hints.model';

/** Issue #1113: yanlış ya da boş bırakılan soruda adım adım ipucu. `next` ilk kez açılan adım başına bir hak düşer. */
@Injectable({ providedIn: 'root' })
export class HintsService {
  private readonly baseUrl = '/api/exam/hints/questions';
  private readonly http = inject(HttpClient);

  get(questionId: number): Observable<HintState> {
    return this.http.get<HintState>(`${this.baseUrl}/${questionId}`);
  }

  next(questionId: number): Observable<HintState> {
    return this.http.post<HintState>(`${this.baseUrl}/${questionId}/next`, {});
  }
}
