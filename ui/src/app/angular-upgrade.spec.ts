import { Component, DOCUMENT as CORE_DOCUMENT, VERSION, inject } from '@angular/core';
import { DOCUMENT as COMMON_DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { VERSION as CDK_VERSION } from '@angular/cdk';
import { VERSION as MATERIAL_VERSION } from '@angular/material/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

/**
 * Issue #545 — Angular major yükseltmelerinin (19 → 20 → 21 → 22) bizim koda dokunan kırıcı değişiklikleri.
 * Her major için ayrı bir `describe`; sonraki geçişlerde öncekiler yeşil kalmalı.
 */
describe('Angular yükseltmesi (#545)', () => {
  describe('Angular 20', () => {
    it('frameworkMaterialAndCdk_AreOnTheSameMajorAtLeast20', () => {
      const major = Number(VERSION.major);

      expect(major).toBeGreaterThanOrEqual(20);
      expect(Number(MATERIAL_VERSION.major)).toBe(major);
      expect(Number(CDK_VERSION.major)).toBe(major);
    });

    // `document-core` migration'ı DOCUMENT import'larını @angular/core'a taşıdı; iki token aynı olmalı ki
    // kalan @angular/common import'ları (üçüncü parti kod dahil) aynı belgeyi alsın.
    it('documentToken_FromCoreAndCommon_ResolveToTheSameDocument', () => {
      expect(CORE_DOCUMENT).toBe(COMMON_DOCUMENT);
      expect(TestBed.inject(CORE_DOCUMENT)).toBe(document);
    });

    // `router-current-navigation` migration'ı `getCurrentNavigation()` → `currentNavigation()` signal'i.
    // Bileşenlerimiz (soru tuvali, önizleme, test çözme) navigasyon state'ini bu signal'den, yoksa
    // history.state'ten okuyor; navigasyon sürerken (rota bileşeni oluşturulurken) state'i vermeli.
    it('routerCurrentNavigation_DuringActivation_ExposesNavigationStateThenClears', async () => {
      @Component({ template: '' })
      class StateReaderComponent {
        readonly state = inject(Router).currentNavigation()?.extras.state;
      }

      TestBed.configureTestingModule({
        providers: [provideRouter([{ path: 'reader', component: StateReaderComponent }])],
      });
      const harness = await RouterTestingHarness.create();
      const router = TestBed.inject(Router);

      await router.navigate(['/reader'], { state: { worksheetId: 42, returnUrl: '/tests' } });
      harness.detectChanges();

      const reader = harness.routeDebugElement!.componentInstance as StateReaderComponent;
      expect(reader.state).toEqual(jasmine.objectContaining({ worksheetId: 42, returnUrl: '/tests' }));
      expect(router.currentNavigation()).toBeNull();
    });

    // Material 20 `--mdc-snackbar-*` token'larını kaldırdı; styles.scss'teki yeni ad `--mat-snack-bar-*`
    // (migration `--mat-snackbar-*` yazmıştı, o ad hiçbir şeye bağlanmıyor).
    describe('snackbar renk token\'ları', () => {
      function surfaceOf(panelClass: string): HTMLElement {
        TestBed.configureTestingModule({ providers: [provideNoopAnimations()] });
        TestBed.inject(MatSnackBar).open('mesaj', undefined, { panelClass });
        const surface = document.querySelector<HTMLElement>(`.${panelClass} .mdc-snackbar__surface`);
        expect(surface).withContext(`.${panelClass} yüzeyi`).not.toBeNull();
        return surface!;
      }

      afterEach(() => TestBed.inject(MatSnackBar).dismiss());

      it('successSnackbar_UsesGreenContainerColor', () => {
        expect(getComputedStyle(surfaceOf('success-snackbar')).backgroundColor).toBe('rgb(76, 175, 80)');
      });

      it('errorSnackbar_UsesRedContainerColor', () => {
        expect(getComputedStyle(surfaceOf('error-snackbar')).backgroundColor).toBe('rgb(244, 67, 54)');
      });

      it('badgeToast_UsesStrongSurfaceToken', () => {
        // --ms-* token'ları tema sınıfı altında tanımlı (ColorSchemeService <html>'e ekler).
        const themeAdded = !document.documentElement.classList.contains('light-theme');
        document.documentElement.classList.add('light-theme');
        const surface = surfaceOf('badge-toast');
        const probe = document.createElement('div');
        probe.style.backgroundColor = 'var(--ms-surface-strong)';
        document.body.appendChild(probe);

        try {
          const expected = getComputedStyle(probe).backgroundColor;
          expect(expected).withContext('--ms-surface-strong tanımlı olmalı').not.toBe('rgba(0, 0, 0, 0)');
          expect(getComputedStyle(surface).backgroundColor).toBe(expected);
        } finally {
          probe.remove();
          if (themeAdded) {
            document.documentElement.classList.remove('light-theme');
          }
        }
      });
    });
  });
});
