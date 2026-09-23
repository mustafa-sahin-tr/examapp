import { inject, Injectable, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { BehaviorSubject, catchError, finalize, map, Observable, of, shareReplay, tap, throwError, timeout } from 'rxjs';
import { CheckStudentResponse } from '../models/check-student-response';
import { Router } from '@angular/router';
import { CheckkTeacherResponse } from '../models/check-teacher-response';
import { jwtDecode } from 'jwt-decode';
import { Student } from '../models/student';
import { Teacher } from '../models/teacher';
import { LocaleService } from './locale.service';

export interface UserProfile {
  email: string;
  avatar: string;
  fullName: string;
  id: number;
  keycloakId: string;
  profileId: number;
  role: string;
  /** Kullanıcının kayıtlı dil tercihi (issue #181): "tr" | "en". Eski token yanıtlarında olmayabilir. */
  preferredLocale?: string;
  /**
   * Sunucu tarafında doğrulanmış okul kimliği (issue #189, `UserProfileDto.SchoolId`).
   * null/undefined = okulsuz/bağımsız kullanıcı veya admin.
   * DİKKAT: login/exchange yanıtı (auth-api) bu alanı taşımaz; yalnızca exam-api
   * `POST /api/exam/auth/refresh` doldurur (`enhanced-layout` asenkron tetikler). Bu gelene kadar
   * `teacher?.schoolId` yedeği kullanılır — bkz. `AuthService.getSchoolId()`.
   */
  schoolId?: number | null;
  student?: Student; // Opsiyonel olarak öğrenci bilgisi
  teacher?: Teacher; // Opsiyonel olarak öğretmen bilgisi
}

export interface TokenResponse {
  token: string;
  profile: UserProfile;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  // LocaleService AuthService'e bağımlı değildir → döngü yok.
  private readonly localeService = inject(LocaleService);
  private tokenKey = 'auth_token';
  private roleKey = 'user_role';
  private avatarKey = 'user_avatar';
  private baseUrl = '/api/exam/auth'; // Backend API URL

  isAuthenticatedSubject = new BehaviorSubject<boolean>(this.hasToken());
  isAuthenticated$ = this.isAuthenticatedSubject.asObservable(); // 🟢 Diğer bileşenler bunu subscribe edebilir

  /**
   * Önbellekteki kullanıcı profili — reaktif kaynak (issue #191). `localStorage['user']`'a yazan her yer
   * `setUser()` üzerinden geçer; böylece profil sonradan (örn. refresh ile `schoolId`) güncellenince
   * bileşenler `computed` ile yeniden hesaplanır. Doğrudan `localStorage.setItem('user', …)` yazma.
   */
  readonly user = signal<UserProfile | null>(this.getUser());

  /** Profili hem localStorage'a hem `user` signal'ına yazar; null → önbellek temizlenir. */
  setUser(profile: UserProfile | null): void {
    try {
      if (profile) {
        localStorage.setItem('user', JSON.stringify(profile));
      } else {
        localStorage.removeItem('user');
      }
    } catch (error) {
      console.warn('Kullanıcı profili localStorage a yazılamadı', error);
    }
    this.user.set(profile);
  }

  register(userData: any): Observable<any> {
    return this.http.post(`${this.baseUrl}/register`, userData);
  }

  checkStudentProfile(): Observable<CheckStudentResponse> {
    return this.http.get<CheckStudentResponse>('/api/exam/student/check-student');
  }

  checkTeacherProfile(): Observable<CheckkTeacherResponse> {
    return this.http.get<CheckkTeacherResponse>('/api/exam/teacher/check-teacher');
  }

  login(credentials: any): Observable<TokenResponse> {
    return this.http.post<TokenResponse>('/api/auth/login', credentials).pipe(
      tap((res) => {
        localStorage.setItem(this.tokenKey, res.token);
        localStorage.setItem(this.roleKey, res.profile.role);
        localStorage.setItem(this.avatarKey, res.profile.avatar);
        this.setUser(res.profile);
        this.isAuthenticatedSubject.next(true);
        // Profildeki dil tercihi aktif dilden farklıysa uygulanır (issue #181).
        // Aynıysa no-op olduğu için reload döngüsü oluşmaz.
        this.localeService.syncFromProfile(res.profile.preferredLocale);
      })
    );
  }

  goLogin(): void {
    // Giriş yapma işlemi için gerekli olan API çağrısını yapıyoruzw
    window.location.href = '/app/login';
  }

  registerStudent(studentData: any): Observable<any> {
    return this.http.post('/api/exam/students/register-student', studentData);
  }

  getUserIdFromLocalStorage(): number | null {
    if (typeof window === 'undefined') {
      return null;
    }

    try {
      const stored = window.localStorage.getItem('user');
      console.log('LocalStorage user verisi:', stored);
      if (!stored) {
        return null;
      }

      const parsed = JSON.parse(stored);
      const userId = Number(parsed?.id);
      const finalId = Number.isFinite(userId) && userId > 0 ? userId : null;
      console.log('Çözümlenen userId:', finalId);
      return finalId;
    } catch (error) {
      console.warn('LocalStorage user bilgisi okunamadı', error);
      return null;
    }
  }

  /** Oturum anahtarlarını siler ve authenticated akışını kapatır; yönlendirme yapmaz. */
  private clearSession(): void {
    localStorage.removeItem(this.tokenKey);
    localStorage.removeItem(this.roleKey);
    localStorage.removeItem(this.avatarKey);
    localStorage.removeItem('student');
    this.setUser(null);
    this.isAuthenticatedSubject.next(false);
  }

  clearLocalStorage(): void {
    this.clearSession();
    this.goLogin();
  }

  logout(): void {
    // Token temizlikten ÖNCE alınır: istek interceptor'dan muaf olduğu için Authorization
    // başlığını elle taşımak zorundayız, aksi halde backend 401 döner ve Keycloak oturumu kapanmaz.
    const token = this.getToken();
    // Yerel oturum ÖNCE senkron temizlenir; sunucu logout'u best-effort denenir.
    // İstek başarısız olsa da yeniden girişte eski kullanıcı bilgisi kalmaz.
    this.clearSession();
    this.http
      .post(
        '/api/exam/auth/logout',
        {},
        {
          withCredentials: true,
          headers: token ? { Authorization: `Bearer ${token}` } : {},
        }
      )
      .pipe(
        timeout(2000),
        catchError(() => of(null))
      )
      .subscribe(() => this.goLogin());
  }

  isAuthenticated(): Observable<boolean> {
    return this.isAuthenticatedSubject.asObservable();
  }

  getToken(): string | null {
    return readStoredValue(this.tokenKey);
  }

  getUserRole(): string | null {
    return readStoredValue(this.roleKey);
  }

  hasRole(role: string): boolean {
    console.log(this.getUserRole(), role);
    return this.getUserRole() === role;
  }

  /** Realm roles carried in the Keycloak access token (realm_access.roles). */
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

  hasRealmRole(role: string): boolean {
    return this.getRealmRoles().includes(role);
  }

  getUserAvatar(): string | null {
    return readStoredValue(this.avatarKey);
  }

  getUser(): any {
    const user = readStoredValue('user');
    return user ? JSON.parse(user) : null;
  }

  /**
   * Önbellekteki profilden kullanıcının okul kimliğini döner (issue #191).
   * Önce `schoolId` (yalnızca exam-api refresh ile gelir), yoksa `teacher.schoolId` (login yanıtında da var).
   * Kayıt yoksa, alan yoksa veya geçersizse null (= okulsuz kullanıcı). Reaktif kullanım için `user` signal'ı.
   */
  getSchoolId(): number | null {
    return AuthService.schoolIdOf(this.getUser());
  }

  /** Profilden okul kimliğini çözer; `user` signal'ı ile `computed` içinde kullanılabilir. */
  static schoolIdOf(profile: UserProfile | null | undefined): number | null {
    const schoolId = Number(profile?.schoolId ?? profile?.teacher?.schoolId);
    return Number.isFinite(schoolId) && schoolId > 0 ? schoolId : null;
  }

  /**
   * Önbellekteki `user` kaydının, elimizdeki access token ile aynı kullanıcıya ait olup
   * olmadığını söyler. Token'ın `sub` claim'i ile kayıttaki `keycloakId` karşılaştırılır.
   * Token yoksa, kayıt yoksa, JSON bozuksa veya kimlikler farklıysa `false` döner.
   */
  isCachedUserCurrent(): boolean {
    const token = this.getToken();
    if (!token) {
      return false;
    }

    const stored = localStorage.getItem('user');
    if (!stored) {
      return false;
    }

    try {
      const decoded: { sub?: string } = jwtDecode(token);
      const sub = decoded?.sub;
      const cachedKeycloakId = JSON.parse(stored)?.keycloakId;
      return !!sub && !!cachedKeycloakId && sub === cachedKeycloakId;
    } catch {
      return false;
    }
  }

  /** Sadece kullanıcıya ait önbellek anahtarlarını siler; token'a dokunmaz, yönlendirme yapmaz. */
  clearCachedUser(): void {
    localStorage.removeItem(this.avatarKey);
    localStorage.removeItem('student');
    this.setUser(null);
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
        this.setUser(res.profile);
        this.isAuthenticatedSubject.next(true);
        // Profildeki dil tercihi aktif dilden farklıysa uygulanır (issue #181).
        // Aynıysa no-op olduğu için reload döngüsü oluşmaz.
        this.localeService.syncFromProfile(res.profile.preferredLocale);
      })
    );
  }

  isExpiringSoon(token: string): boolean {
    try {
      const decoded: any = jwtDecode(token);
      const now = Math.floor(Date.now() / 1000);
      console.log(' kalan süre : ', decoded.exp - now);
      // token süresi bitmeden 60 saniye içinde yenileme işlemi yap
      return decoded.exp - now < 100; // 60 saniye içinde bitiyorsa yenile
    } catch {
      return true;
    }
  }

  refresh(): Observable<UserProfile | null> {
    return this.http.post<UserProfile | null>('/api/exam/auth/refresh', {}, { withCredentials: true });
  }

  /** Devam eden token yenilemesi; eşzamanlı çağıranlar aynı isteği paylaşır (issue #241). */
  private refreshInFlight$: Observable<string> | null = null;

  /**
   * Access token'ı yeniler. Aynı anda birden fazla 401/expiring-soon tetiklemesi olursa tek bir
   * `/api/auth/refresh-token` isteği atılır (refresh token rotasyonunda paralel istekler birbirini
   * geçersiz kılabilir). Hata durumunda oturum temizlenip login'e yönlendirilir.
   */
  refreshToken(): Observable<string> {
    if (!this.refreshInFlight$) {
      this.refreshInFlight$ = this.http
        .post<{ accessToken: string }>('/api/auth/refresh-token', {}, { withCredentials: true })
        .pipe(
          map((res) => {
            // Boş token da başarısızlıktır: temizlik aşağıdaki catchError'da, paylaşılan akışta bir kez yapılır.
            if (!res?.accessToken) {
              throw new Error('Refresh response has no accessToken');
            }
            return res.accessToken;
          }),
          // Asılı kalan refresh tüm bekleyen istekleri dondurmasın; zaman aşımı da temizlik yoluna düşer.
          timeout(10_000),
          catchError((error) => {
            console.error('Token yenileme hatası:', error);
            // clearSession isAuthenticatedSubject'i de kapatır; dil tercihi gibi oturum dışı anahtarlar korunur.
            this.clearLocalStorage();
            return throwError(() => new Error('Refresh failed'));
          }),
          finalize(() => (this.refreshInFlight$ = null)),
          shareReplay({ bufferSize: 1, refCount: false })
        );
    }
    return this.refreshInFlight$;
  }
}

/**
 * SSR/prerender sirasinda `localStorage` yoktur; okuma tarafi bu yuzden guvenli sarmalayiciyi
 * kullanir (issue #182: landing navbar'daki dil secici sunucuda da olusturuluyor).
 */
function readStoredValue(key: string): string | null {
  if (typeof localStorage === 'undefined') {
    return null;
  }

  try {
    return localStorage.getItem(key);
  } catch {
    // Gizli mod / kota: depolama okunamiyorsa oturum yokmus gibi davranilir
    return null;
  }
}
