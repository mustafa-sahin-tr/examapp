import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, BehaviorSubject, tap, catchError, of, map } from 'rxjs';
import { environment } from '../../environments/environment';
import { ThemeConfigService, ThemePreset, WorksheetCardThemeConfig } from './theme-config.service';
import { AuthService, UserProfile } from './auth.service';

export interface UpdateThemeRequest {
  themePreset: string;
  themeCustomConfig?: string | null;
}

export interface UpdateThemeResponse {
  message: string;
  themePreset: string;
  themeCustomConfig?: string | null;
}

@Injectable({
  providedIn: 'root',
})
export class UserThemeService {
  private readonly http = inject(HttpClient);
  private readonly themeConfigService = inject(ThemeConfigService);
  private readonly authService = inject(AuthService);

  // Current user theme state
  private userThemeSubject = new BehaviorSubject<{ themePreset: string; themeCustomConfig?: string | null } | null>(
    null
  );
  public userTheme$ = this.userThemeSubject.asObservable();

  constructor() {
    // Theme yükleme artık enhanced layout component'inde yapılıyor
  }

  /**
   * User profile'dan tema tercihini yükler (authentication refresh'den sonra)
   */
  loadUserThemeFromProfile(): void {
    const user = this.authService.getUser();
    if (user) {
      const themeData = this.extractThemeFromUserProfile(user);
      if (themeData) {
        this.userThemeSubject.next(themeData);
        this.applyTheme(themeData.themePreset, themeData.themeCustomConfig);
      } else {
        // Default tema kullan
        const defaultTheme = environment.worksheetCardTheme || 'standard';
        this.themeConfigService.setTheme(defaultTheme as ThemePreset);
      }
    }
  }

  /**
   * User profile'dan tema bilgisini çıkarır
   */
  private extractThemeFromUserProfile(
    user: UserProfile
  ): { themePreset: string; themeCustomConfig?: string | null } | null {
    // Student veya Teacher'dan tema bilgisini al
    if (user.student?.themePreset) {
      return {
        themePreset: user.student.themePreset,
        themeCustomConfig: user.student.themeCustomConfig,
      };
    } else if (user.teacher?.themePreset) {
      return {
        themePreset: user.teacher.themePreset,
        themeCustomConfig: user.teacher.themeCustomConfig,
      };
    }
    return null;
  }

  /**
   * Tema konfigürasyonunu uygular
   */
  private applyTheme(themePreset: string, themeCustomConfig?: string | null): void {
    if (themeCustomConfig) {
      try {
        const customConfig = JSON.parse(themeCustomConfig);
        this.themeConfigService.setCustomTheme(customConfig);
      } catch (error) {
        console.warn('Invalid theme custom config, using preset:', error);
        this.themeConfigService.setTheme(themePreset as ThemePreset);
      }
    } else {
      this.themeConfigService.setTheme(themePreset as ThemePreset);
    }
  }

  /**
   * Kullanıcının tema tercihini günceller
   */
  saveUserTheme(
    themePreset: ThemePreset,
    customConfig?: Partial<WorksheetCardThemeConfig>
  ): Observable<UpdateThemeResponse> {
    const user = this.authService.getUser();
    if (!user) {
      throw new Error('User not authenticated');
    }

    const request: UpdateThemeRequest = {
      themePreset,
      themeCustomConfig: customConfig ? JSON.stringify(customConfig) : null,
    };

    // User role'üne göre doğru endpoint'i seç
    const endpoint = user.role === 'Student' ? '/api/exam/student/update-theme' : '/api/exam/teacher/update-theme';

    return this.http.post<UpdateThemeResponse>(endpoint, request).pipe(
      tap((response) => {
        // Local state'i güncelle
        this.userThemeSubject.next({
          themePreset: response.themePreset,
          themeCustomConfig: response.themeCustomConfig,
        });

        // Theme config service'i güncelle
        this.applyTheme(response.themePreset, response.themeCustomConfig);

        // localStorage'daki user bilgisini de güncelle
        this.updateUserProfileInStorage(response.themePreset, response.themeCustomConfig);

        // Tüm component'lere theme değişikliğini duyur
        this.notifyThemeChange(response.themePreset, response.themeCustomConfig);
      })
    );
  }

  /**
   * LocalStorage'daki user profile'ı tema bilgisiyle günceller
   */
  private updateUserProfileInStorage(themePreset: string, themeCustomConfig?: string | null): void {
    const userStr = localStorage.getItem('user');
    if (userStr) {
      try {
        const user = JSON.parse(userStr);
        if (user.student) {
          user.student.themePreset = themePreset;
          user.student.themeCustomConfig = themeCustomConfig;
        } else if (user.teacher) {
          user.teacher.themePreset = themePreset;
          user.teacher.themeCustomConfig = themeCustomConfig;
        }
        localStorage.setItem('user', JSON.stringify(user));
      } catch (error) {
        console.warn('Failed to update user profile in storage:', error);
      }
    }
  }

  /**
   * Preset tema seçer ve kaydeder
   */
  selectPreset(preset: ThemePreset): Observable<UpdateThemeResponse> {
    return this.saveUserTheme(preset);
  }

  /**
   * Özel tema konfigürasyonu seçer ve kaydeder
   */
  selectCustomTheme(customConfig: Partial<WorksheetCardThemeConfig>): Observable<UpdateThemeResponse> {
    return this.saveUserTheme('standard', customConfig); // Base preset olarak standard kullan
  }

  /**
   * Kullanıcının mevcut tema tercihini döndürür
   */
  getCurrentUserTheme(): { themePreset: string; themeCustomConfig?: string | null } | null {
    return this.userThemeSubject.value;
  }

  /**
   * Tema tercihini sıfırlar (default'a döndürür)
   */
  resetUserTheme(): Observable<UpdateThemeResponse> {
    const defaultTheme = environment.worksheetCardTheme || 'standard';
    return this.saveUserTheme(defaultTheme as ThemePreset);
  }

  /**
   * Theme güncellemesini tüm component'lere duyur
   */
  notifyThemeChange(themePreset: string, themeCustomConfig?: string | null): void {
    this.userThemeSubject.next({
      themePreset,
      themeCustomConfig,
    });
  }
}
