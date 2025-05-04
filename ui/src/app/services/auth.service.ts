import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { CheckStudentResponse } from '../models/check-student-response';
import { Router } from '@angular/router';
import { CheckkTeacherResponse } from '../models/check-teacher-response';

export interface UserProfile {
  email: string;
  avatar: string;
  fullName: string;
  id: number;
  keycloakId: string;
  profileId: number;
  role: string;
}

export interface TokenResponse {
  token: string;
  profile: UserProfile;
}

@Injectable({ providedIn: 'root' })
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
    return this.http.get<CheckStudentResponse>('/api/exam/student/check-student');
  }

  checkTeacherProfile(): Observable<CheckkTeacherResponse> {
    return this.http.get<CheckkTeacherResponse>('/api/exam/teacher/check-teacher');
  }

  login(credentials: any): Observable<TokenResponse> {
    // const body = new HttpParams()
    //   .set('grant_type', 'password')
    //   .set('client_id', 'exam-client')
    //   .set('username', credentials.email)
    //   .set('password', credentials.password)
    //   .set('client_secret', 'yD3joUPCJesjf2Z4NnW1GJqc5wMGJtlg'); // sadece gerekiyorsa

    // return this.http
    //   .post<TokenResponse>('http://localhost:8081/realms/exam-realm/protocol/openid-connect/token', body.toString(), {
    //     headers: {
    //       'Content-Type': 'application/x-www-form-urlencoded',
    //     },
    //   })
    //   .pipe(
    //     tap((res) => {
    //       localStorage.setItem(this.tokenKey, res.access_token);
    //       // localStorage.setItem(this.roleKey, res.role);
    //       // localStorage.setItem(this.avatarKey, res.avatar);
    //       // localStorage.setItem('user', JSON.stringify(res.user));
    //       this.isAuthenticatedSubject.next(true);
    //     })
    //   );

    return this.http.post<TokenResponse>('/api/auth/login', credentials).pipe(
      tap((res) => {
        localStorage.setItem(this.tokenKey, res.token);
        localStorage.setItem(this.roleKey, res.profile.role);
        localStorage.setItem(this.avatarKey, res.profile.avatar);
        localStorage.setItem('user', JSON.stringify(res.profile));
        this.isAuthenticatedSubject.next(true);
      })
    );
  }

  registerStudent(studentData: any): Observable<any> {
    return this.http.post('/api/exam/students/register-student', studentData);
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

  exchangeCodeForToken(code: string) {
    return this.http.post<TokenResponse>(`/api/auth/exchange`, { code: code }).pipe(
      tap((res) => {
        localStorage.setItem(this.tokenKey, res.token);
        localStorage.setItem(this.roleKey, res.profile.role);
        localStorage.setItem(this.avatarKey, res.profile.avatar);
        localStorage.setItem('user', JSON.stringify(res.profile));
        this.isAuthenticatedSubject.next(true);
      })
    );
  }
}
