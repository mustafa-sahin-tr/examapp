import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, inject, signal } from '@angular/core';

/**
 * Uygulama geneli renk şeması (dark / light). Issue #81.
 *
 * Worksheet kart görsel stilini yöneten `ThemeConfigService` / `ThemePreset` ile ilgisi yoktur;
 * bu servis yalnızca `<html>` üzerindeki `.dark-theme` / `.light-theme` class'ını ve
 * `localStorage['color-scheme']` anahtarını yönetir.
 *
 * İlk değer index.html'deki inline script tarafından (ilk paint öncesi, FOUC önleme) `<html>`'e
 * yazılan class'tan okunur; SSR/prerender tarafında DOM ve localStorage'a dokunulmaz.
 */
export type ColorScheme = 'dark' | 'light';

export const COLOR_SCHEME_STORAGE_KEY = 'color-scheme';
const DEFAULT_SCHEME: ColorScheme = 'dark';

@Injectable({ providedIn: 'root' })
export class ColorSchemeService {
  private readonly document = inject(DOCUMENT);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  private readonly schemeState = signal<ColorScheme>(this.readInitialScheme());

  /** Aktif renk şeması. */
  readonly colorScheme = this.schemeState.asReadonly();

  setScheme(scheme: ColorScheme): void {
    this.schemeState.set(scheme);
    if (!this.isBrowser) {
      return;
    }
    const classList = this.document.documentElement.classList;
    classList.remove('dark-theme', 'light-theme');
    classList.add(`${scheme}-theme`);
    try {
      localStorage.setItem(COLOR_SCHEME_STORAGE_KEY, scheme);
    } catch {
      // localStorage erişilemez (gizli mod, kota) → tercih yalnızca oturum boyunca korunur
    }
  }

  toggle(): void {
    this.setScheme(this.schemeState() === 'dark' ? 'light' : 'dark');
  }

  private readInitialScheme(): ColorScheme {
    if (!this.isBrowser) {
      return DEFAULT_SCHEME;
    }
    const classList = this.document.documentElement.classList;
    if (classList.contains('light-theme')) {
      return 'light';
    }
    if (classList.contains('dark-theme')) {
      return 'dark';
    }
    // Inline script çalışmadıysa (örn. test ortamı) class'ı burada uygula.
    classList.add(`${DEFAULT_SCHEME}-theme`);
    return DEFAULT_SCHEME;
  }
}
