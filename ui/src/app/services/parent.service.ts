import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root',
})
export class ParentService {
  constructor(private http: HttpClient) {}

  register(): Observable<any> {
    return this.http.post<any>(`/api/exam/parent/register`, {});
  }
}
