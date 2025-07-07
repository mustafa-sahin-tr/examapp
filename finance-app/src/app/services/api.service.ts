import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root',
})
export class ApiService {
  private baseUrl = environment.apiUrl || 'http://localhost:5678';

  constructor(private http: HttpClient) {}

  get<T>(endpoint: string) {
    const url = `${this.baseUrl}${endpoint}`;
    console.log('🌐 API GET:', url);
    return this.http.get<T>(url);
  }

  post<T>(endpoint: string, data: any) {
    const url = `${this.baseUrl}${endpoint}`;
    console.log('🌐 API POST:', url, data);
    return this.http.post<T>(url, data);
  }

  put<T>(endpoint: string, data: any) {
    return this.http.put<T>(`${this.baseUrl}${endpoint}`, data);
  }

  patch<T>(endpoint: string, data: any) {
    return this.http.patch<T>(`${this.baseUrl}${endpoint}`, data);
  }

  delete<T>(endpoint: string) {
    return this.http.delete<T>(`${this.baseUrl}${endpoint}`);
  }
}
