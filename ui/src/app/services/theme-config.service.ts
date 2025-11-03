import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface WorksheetCardThemeConfig {
  borders: boolean;
  gradient: boolean;
  iconBadges: boolean;
  ribbons: boolean;
  glowEffects: boolean;
  transformEffects: boolean;
  progressBar: boolean;
  typographyEffects: boolean;
}

export type ThemePreset = 'minimal' | 'standard' | 'enhanced' | 'full';

@Injectable({
  providedIn: 'root',
})
export class ThemeConfigService {
  private readonly themePresets: Record<ThemePreset, WorksheetCardThemeConfig> = {
    minimal: {
      borders: true,
      gradient: false,
      iconBadges: false,
      ribbons: false,
      glowEffects: false,
      transformEffects: false,
      progressBar: false,
      typographyEffects: false,
    },
    standard: {
      borders: true,
      gradient: true,
      iconBadges: true,
      ribbons: false,
      glowEffects: false,
      transformEffects: false,
      progressBar: true,
      typographyEffects: false,
    },
    enhanced: {
      borders: true,
      gradient: true,
      iconBadges: true,
      ribbons: true,
      glowEffects: true,
      transformEffects: false,
      progressBar: true,
      typographyEffects: true,
    },
    full: {
      borders: true,
      gradient: true,
      iconBadges: true,
      ribbons: true,
      glowEffects: true,
      transformEffects: true,
      progressBar: true,
      typographyEffects: true,
    },
  };

  private currentTheme: WorksheetCardThemeConfig;
  private currentThemeSubject: BehaviorSubject<WorksheetCardThemeConfig>;
  public currentTheme$: Observable<WorksheetCardThemeConfig>;

  constructor() {
    // Öncelik sırası: Environment > LocalStorage > Default
    const envTheme = this.loadThemeFromEnvironment();
    const savedTheme = this.loadThemeFromStorage();

    this.currentTheme = envTheme || savedTheme || this.themePresets.standard;
    this.currentThemeSubject = new BehaviorSubject<WorksheetCardThemeConfig>(this.currentTheme);
    this.currentTheme$ = this.currentThemeSubject.asObservable();
  }
  getCurrentTheme(): WorksheetCardThemeConfig {
    return { ...this.currentTheme };
  }

  setTheme(preset: ThemePreset): void {
    this.currentTheme = { ...this.themePresets[preset] };
    this.currentThemeSubject.next(this.currentTheme);
    this.saveThemeToStorage();
  }

  setCustomTheme(customConfig: Partial<WorksheetCardThemeConfig>): void {
    this.currentTheme = { ...this.themePresets.standard, ...customConfig };
    this.currentThemeSubject.next(this.currentTheme);
    this.saveThemeToStorage();
  }

  getAvailablePresets(): ThemePreset[] {
    return Object.keys(this.themePresets) as ThemePreset[];
  }

  getPresetConfig(preset: ThemePreset): WorksheetCardThemeConfig {
    return { ...this.themePresets[preset] };
  }

  private loadThemeFromEnvironment(): WorksheetCardThemeConfig | null {
    // Angular environment'dan tema yükle
    const envTheme = (environment as any)?.worksheetCardTheme;
    if (envTheme && typeof envTheme === 'string' && this.themePresets[envTheme as ThemePreset]) {
      return this.themePresets[envTheme as ThemePreset];
    }

    // Browser window'dan da yükleyebilir (runtime'da değiştirmek için)
    if (typeof window !== 'undefined') {
      const windowTheme = (window as any)?.['WORKSHEET_CARD_THEME'];
      if (windowTheme && typeof windowTheme === 'string') {
        try {
          return JSON.parse(windowTheme);
        } catch (e) {
          console.warn('Invalid WORKSHEET_CARD_THEME window variable:', e);
        }
      }

      // Preset ismi olarak da gelebilir
      const windowPreset = (window as any)?.['WORKSHEET_CARD_THEME_PRESET'] as ThemePreset;
      if (windowPreset && this.themePresets[windowPreset]) {
        return this.themePresets[windowPreset];
      }
    }

    return null;
  }

  private loadThemeFromStorage(): WorksheetCardThemeConfig | null {
    if (typeof localStorage !== 'undefined') {
      try {
        const saved = localStorage.getItem('worksheetCardTheme');
        return saved ? JSON.parse(saved) : null;
      } catch (e) {
        console.warn('Failed to load theme from localStorage:', e);
      }
    }
    return null;
  }

  private saveThemeToStorage(): void {
    if (typeof localStorage !== 'undefined') {
      try {
        localStorage.setItem('worksheetCardTheme', JSON.stringify(this.currentTheme));
      } catch (e) {
        console.warn('Failed to save theme to localStorage:', e);
      }
    }
  }

  // Debug için
  resetToDefault(): void {
    this.currentTheme = { ...this.themePresets.standard };
    this.currentThemeSubject.next(this.currentTheme);
    this.saveThemeToStorage();
  }
}
