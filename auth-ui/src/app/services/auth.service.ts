import { inject, Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { BehaviorSubject, catchError, map, Observable, tap, throwError } from 'rxjs';
import { Router } from '@angular/router';
import { jwtDecode } from 'jwt-decode';
import {
  Grade,
  RegisterProfileResponse,
  RegisterStudentPayload,
  RegisterTeacherPayload,
} from '../models/registration.model';

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
  roles: string[];
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

  getRoles(): Observable<any[]> {
    return this.http.get<any[]>('/api/auth/roles');
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

  completeProfile(role: string): Observable<UserProfile> {
    return this.http.post<UserProfile>('/api/auth/complete-profile', { role });
  }

  registerStudent(studentData: any): Observable<any> {
    return this.http.post('/api/exam/students/register-student', studentData);
  }

  /** Grade lookup used by the student registration step. */
  getGrades(): Observable<Grade[]> {
    return this.http.get<Grade[]>('/api/exam/worksheet/grades');
  }

  /** Assigns the Student realm role, creates the Student profile row, and refreshes the session cookie. */
  registerStudentProfile(payload: RegisterStudentPayload): Observable<RegisterProfileResponse> {
    return this.http.post<RegisterProfileResponse>('/api/exam/student/register', payload);
  }

  /** Assigns the Teacher realm role, creates the Teacher profile row, and refreshes the session cookie. */
  registerTeacherProfile(payload: RegisterTeacherPayload): Observable<RegisterProfileResponse> {
    return this.http.post<RegisterProfileResponse>('/api/exam/teacher/register', payload);
  }

  /** Assigns the Parent realm role, creates the Parent profile row, and refreshes the session cookie. */
  registerParentProfile(): Observable<RegisterProfileResponse> {
    return this.http.post<RegisterProfileResponse>('/api/exam/parent/register', {});
  }

  logout(): void {
    console.log('Logging out...');
    // logout işlemi için gerekli olan API çağrısını yapıyoruz
    this.http.post('/api/exam/auth/logout', {}).subscribe(() => {
      console.log('Logout successful');
      localStorage.removeItem(this.tokenKey);
      localStorage.removeItem(this.roleKey);
      localStorage.removeItem(this.avatarKey);
      localStorage.removeItem('user');
      localStorage.removeItem('student');
      this.isAuthenticatedSubject.next(false);
      this.router.navigate(['/login']);
    });
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

  /** Realm roles from the current JWT (`realm_access.roles`) — used to detect an already
   *  role-assigned-but-profile-incomplete account (e.g. admin-created users). */
  getRealmRoles(): string[] {
    const token = this.getToken();
    if (!token) {
      return [];
    }
    try {
      const decoded: any = jwtDecode(token);
      const roles = decoded?.realm_access?.roles;
      return Array.isArray(roles) ? roles : [];
    } catch {
      return [];
    }
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
        localStorage.setItem(this.roleKey, res.roles[0]);
        // localStorage.setItem(this.avatarKey, res.profile.avatar);
        // localStorage.setItem('user', JSON.stringify(res.profile));
        this.isAuthenticatedSubject.next(true);
      })
    );
  }

  isExpiringSoon(token: string): boolean {
    try {
      const decoded: any = jwtDecode(token);
      const now = Math.floor(Date.now() / 1000);
      console.log(' kalan süre : ', decoded.exp - now);
      // token süresi bitmeden 60 saniye içinde yenileme işlemi yap
      return decoded.exp - now < 200; // 60 saniye içinde bitiyorsa yenile
    } catch {
      return true;
    }
  }

  /**
   * Persists a freshly refreshed access token the same way login()/exchangeCodeForToken()
   * persist a brand-new session: updates auth_token, re-derives user_role (and the
   * stored user, when present) from the new JWT's realm_access roles, and flips the
   * authenticated subject. Use this after refreshToken() succeeds instead of writing
   * to localStorage directly, so user_role never goes stale relative to the token.
   */
  applyRefreshedSession(token: string): void {
    localStorage.setItem(this.tokenKey, token);

    try {
      const decoded: any = jwtDecode(token);
      const roles: string[] = decoded?.realm_access?.roles ?? [];
      const relevantRole = roles.find(
        (role: string) => !!role && !role.startsWith('default-roles') && !role.includes('uma_')
      );

      if (relevantRole) {
        localStorage.setItem(this.roleKey, relevantRole);

        const existingUser = this.getUser();
        if (existingUser) {
          localStorage.setItem('user', JSON.stringify({ ...existingUser, role: relevantRole }));
        }
      }
    } catch (error) {
      console.error('Yenilenen token çözümlenemedi:', error);
    }

    this.isAuthenticatedSubject.next(true);
  }

  refreshToken(): Observable<string> {
    return this.http.post<{ accessToken: string }>('/api/auth/refresh-token', {}, { withCredentials: true }).pipe(
      map((res) => res.accessToken),
      catchError((error) => {
        console.error('Token yenileme hatası:', error);
        localStorage.clear();
        this.isAuthenticatedSubject.next(false);
        this.router.navigate(['/login']);
        return throwError(() => new Error('Refresh failed'));
      }) // Hata durumunda null döndür
      // tap((res) => {
    );
  }
}
