import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { CheckStudentResponse } from '../models/check-student-response';
import { Router } from '@angular/router';

@Injectable({providedIn: 'root'})

export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private tokenKey = 'auth_token';
  private roleKey = 'user_role';
  private avatarKey = 'user_avatar';

  isAuthenticatedSubject = new BehaviorSubject<boolean>(this.hasToken());
  isAuthenticated$ = this.isAuthenticatedSubject.asObservable(); // 🟢 Diğer bileşenler bunu subscribe edebilir
  

  register(userData: any): Observable<any> {
    return this.http.post('/api/auth/register', userData);
  }

  checkStudentProfile(): Observable<CheckStudentResponse> {
    return this.http.get<CheckStudentResponse>('/api/student/check-student');
  }

  login(credentials: any): Observable<{ token: string; role: string , avatar: string}> {
    return this.http.post<{ token: string; role: string , avatar: string, user: any}>('/api/auth/login', credentials).pipe(
      tap(res => {
        localStorage.setItem(this.tokenKey, res.token);
        localStorage.setItem(this.roleKey, res.role);
        localStorage.setItem(this.avatarKey, res.avatar);
        localStorage.setItem('user', JSON.stringify(res.user));
        this.isAuthenticatedSubject.next(true);
      })
    );
  }

  registerStudent(studentData: any): Observable<any> {
    return this.http.post('/api/students/register-student', studentData);
  }

  logout(): void {
    localStorage.removeItem(this.tokenKey);
    localStorage.removeItem(this.roleKey);
    localStorage.removeItem(this.avatarKey);
    localStorage.removeItem('user');
    localStorage.removeItem('student');
    this.isAuthenticatedSubject.next(false);
    this.router.navigate(['/login']);
  }

  isAuthenticated(): Observable<boolean> {
    return this.isAuthenticatedSubject.asObservable();
  }

  getToken(): string | null {
    return localStorage.getItem(this.tokenKey);
  }

  getUserRole(): string | null {
    return localStorage.getItem(this.roleKey);
  }

  hasRole(role: string): boolean {
    console.log(this.getUserRole(), role);
    return this.getUserRole() === role;
  }

  getUserAvatar(): string | null {
    return localStorage.getItem(this.avatarKey);
  }

  getUser(): any {
    const user = localStorage.getItem('user');
    return user ? JSON.parse(user) : null;
  }

  hasToken(): boolean {
    return !!this.getToken();
  }
}
