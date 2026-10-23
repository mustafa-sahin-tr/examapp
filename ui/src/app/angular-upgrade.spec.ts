import {
  ApplicationConfig,
  ChangeDetectionStrategy,
  Component,
  DOCUMENT as CORE_DOCUMENT,
  NgZone,
  VERSION,
  inject,
  input,
  signal,
} from '@angular/core';
import { HttpBackend, HttpXhrBackend } from '@angular/common/http';
import { By, createApplication } from '@angular/platform-browser';
import { DOCUMENT as COMMON_DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { VERSION as CDK_VERSION } from '@angular/cdk';
import { VERSION as MATERIAL_VERSION } from '@angular/material/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatSelect, MatSelectModule } from '@angular/material/select';
import { OverlayContainer } from '@angular/cdk/overlay';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { TRANSLOCO_LOADER } from '@jsverse/transloco';
import { of } from 'rxjs';

import { AppComponent } from './app.component';
import { appConfig } from './app.config';
import { config as serverConfig } from './app.config.server';

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
      @Component({ selector: 'app-state-reader', template: '' })
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
  describe('Angular 21', () => {
    it('frameworkMaterialAndCdk_AreAtLeast21', () => {
      expect(Number(VERSION.major)).toBeGreaterThanOrEqual(21);
    });

    // Angular 21'de change detection sağlayıcısı verilmeyen uygulama zoneless açılıyor. Zoneless'a geçiş bu
    // işin kapsamı dışında (#545): tarayıcı ve sunucu config'i zone tabanlı change detection'ı açıkça istemeli.
    // (bootstrap-options migration'ı main.server.ts'e ikinci bir provideZoneChangeDetection() ekliyordu;
    // config zaten sağladığı için geri alındı — bu test o güvenceyi taşıyor.)
    async function isZoneBased(config: ApplicationConfig): Promise<boolean> {
      // Sözlük isteği (sunucu config'inde ServerXhr) ve hydration uyarısı testin konusu değil.
      spyOn(console, 'warn');
      const appRef = await createApplication({
        providers: [...config.providers, { provide: TRANSLOCO_LOADER, useValue: { getTranslation: () => of({}) } }],
      });
      try {
        const zone = appRef.injector.get(NgZone);
        return zone.run(() => NgZone.isInAngularZone());
      } finally {
        appRef.destroy();
      }
    }

    // CDK 21: bağlı konumlu paneller (mat-select, autocomplete) OverlayContainer yerine tetikleyicinin yanına
    // native popover olarak eklenir ve top layer'da açılır. OverlayContainer'da panel arayan spec'ler bu yüzden
    // document üzerinden aramaya çevrildi (school-filter, school-select).
    it('matSelectPanel_RendersAsOpenNativePopoverNextToTrigger', async () => {
      @Component({
        selector: 'app-select-host',
        imports: [MatSelectModule],
        template: `<mat-select aria-label="seçim"><mat-option value="a">A</mat-option></mat-select>`,
      })
      class SelectHostComponent {}

      TestBed.configureTestingModule({ providers: [provideNoopAnimations()] });
      const fixture = TestBed.createComponent(SelectHostComponent);
      fixture.detectChanges();
      const select = fixture.debugElement.query(By.directive(MatSelect)).componentInstance as MatSelect;

      select.open();
      fixture.detectChanges();
      await fixture.whenStable();

      const option = document.querySelector('mat-option');
      const popover = option?.closest<HTMLElement>('[popover]');
      expect(popover).withContext('panel bir popover host içinde').not.toBeNull();
      expect(popover!.matches(':popover-open')).toBeTrue();
      expect(TestBed.inject(OverlayContainer).getContainerElement().contains(option)).toBeFalse();
      select.close();
    });

    it('browserAppConfig_KeepsZoneChangeDetection', async () => {
      expect(await isZoneBased(appConfig)).toBeTrue();
    });

    it('serverAppConfig_KeepsZoneChangeDetection', async () => {
      expect(await isZoneBased(serverConfig)).toBeTrue();
    });
  });
  describe('Angular 22', () => {
    it('framework_IsAtLeast22', () => {
      expect(Number(VERSION.major)).toBeGreaterThanOrEqual(22);
    });

    // Angular 22'de provideHttpClient() varsayılan olarak fetch backend'i kullanıyor; http-xhr-backend migration'ı
    // mevcut davranış için withXhr() ekledi. Fetch'e geçiş ayrı karar (yükleme ilerlemesi, interceptor davranışı).
    it('browserAppConfig_UsesXhrBackend', async () => {
      spyOn(console, 'warn');
      const appRef = await createApplication({
        providers: [...appConfig.providers, { provide: TRANSLOCO_LOADER, useValue: { getTranslation: () => of({}) } }],
      });
      try {
        expect(appRef.injector.get(HttpBackend)).toBeInstanceOf(HttpXhrBackend);
      } finally {
        appRef.destroy();
      }
    });

    // Angular 22'nin varsayılan change detection stratejisi OnPush; change-detection-eager migration'ı mevcut
    // bileşenlere eski varsayılanı (Eager) açıkça yazdı. Yeni bileşen strateji belirtmezse OnPush olur.
    describe('change detection varsayılanı', () => {
      @Component({ selector: 'app-default-cd', template: '{{ label }}' })
      class DefaultCdComponent {
        label = 'ilk';
      }

      @Component({ selector: 'app-eager-cd', template: '{{ label }}', changeDetection: ChangeDetectionStrategy.Eager })
      class EagerCdComponent {
        label = 'ilk';
      }

      function rendersPlainFieldMutation(type: typeof DefaultCdComponent | typeof EagerCdComponent): string {
        const fixture = TestBed.createComponent(type);
        fixture.detectChanges();
        fixture.componentInstance.label = 'güncel';
        fixture.detectChanges();
        return (fixture.nativeElement as HTMLElement).textContent!.trim();
      }

      it('componentWithoutStrategy_IsOnPushAndIgnoresPlainFieldMutation', () => {
        expect(rendersPlainFieldMutation(DefaultCdComponent)).toBe('ilk');
      });

      it('eagerComponent_RefreshesPlainFieldMutation', () => {
        expect(rendersPlainFieldMutation(EagerCdComponent)).toBe('güncel');
      });

      it('appComponent_KeepsEagerStrategyFromMigration', () => {
        expect((AppComponent as unknown as { ɵcmp: { onPush: boolean } }).ɵcmp.onPush).toBeFalse();
      });
    });

    // Angular 22'de şablondaki `a?.b`, `a` null iken null değil undefined üretir. safe-optional-chaining migration'ı
    // bunu `$safeNavigationMigration(...)` ile sarmıştı; biz kalıcı `?? null` yazdık (labeling, practice-solve,
    // settings, worksheet-list(-enhanced)). Bu iki test farkı ve düzeltmenin eski davranışı koruduğunu gösterir.
    describe('şablonda ?. semantiği', () => {
      @Component({ selector: 'app-probe', template: '' })
      class ProbeComponent {
        readonly value = input<unknown>('başlangıç');
      }

      @Component({
        selector: 'app-safe-nav-host',
        imports: [ProbeComponent],
        template: `<app-probe class="raw" [value]="item()?.endAt" /><app-probe class="fixed" [value]="item()?.endAt ?? null" />`,
      })
      class SafeNavHostComponent {
        readonly item = signal<{ endAt: string } | null>(null);
      }

      function probeValues(): { raw: unknown; fixed: unknown } {
        const fixture = TestBed.createComponent(SafeNavHostComponent);
        fixture.detectChanges();
        const [raw, fixed] = fixture.debugElement
          .queryAll(By.directive(ProbeComponent))
          .map((de) => (de.componentInstance as ProbeComponent).value());
        return { raw, fixed };
      }

      it('optionalChainOnNull_YieldsUndefined', () => {
        expect(probeValues().raw).toBeUndefined();
      });

      it('optionalChainWithNullFallback_KeepsPreV22Null', () => {
        expect(probeValues().fixed).toBeNull();
      });
    });
  });
});
