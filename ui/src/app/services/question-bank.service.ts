import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { QuestionBankFilter, QuestionBankPage } from '../models/question-bank';

/** issue #471: soru bankası araması. */
@Injectable({ providedIn: 'root' })
export class QuestionBankService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = '/api/exam/questions/bank';

  search(filter: QuestionBankFilter): Observable<QuestionBankPage> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value === null || value === undefined || value === '') continue;
      if (Array.isArray(value)) {
        for (const item of value) params = params.append(key, String(item));
      } else {
        params = params.set(key, String(value));
      }
    }
    return this.http.get<QuestionBankPage>(this.apiUrl, { params });
  }
}
