import { DOCUMENT as CORE_DOCUMENT, NgZone, VERSION } from '@angular/core';
import { HttpBackend, HttpXhrBackend } from '@angular/common/http';
import { createApplication } from '@angular/platform-browser';
import { DOCUMENT as COMMON_DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { VERSION as CDK_VERSION } from '@angular/cdk';
import { VERSION as MATERIAL_VERSION } from '@angular/material/core';

import { AppComponent } from './app.component';
import { appConfig } from './app.config';

/**
 * Issue #545 — Angular major yükseltmelerinin (19 → 20 → 21 → 22) auth-ui'ye dokunan kırıcı değişiklikleri.
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

    // `document-core` migration'ı parent-activate'teki DOCUMENT import'unu @angular/core'a taşıdı.
    it('documentToken_FromCoreAndCommon_ResolveToTheSameDocument', () => {
      expect(CORE_DOCUMENT).toBe(COMMON_DOCUMENT);
      expect(TestBed.inject(CORE_DOCUMENT)).toBe(document);
    });
  });
  describe('Angular 21', () => {
    it('framework_IsAtLeast21', () => {
      expect(Number(VERSION.major)).toBeGreaterThanOrEqual(21);
    });

    // Angular 21'de change detection sağlayıcısı verilmeyen uygulama zoneless açılıyor; zoneless'a geçiş #545'in
    // kapsamı dışında, config zone tabanlı change detection'ı açıkça istemeli.
    it('appConfig_KeepsZoneChangeDetection', async () => {
      const appRef = await createApplication(appConfig);
      try {
        const zone = appRef.injector.get(NgZone);
        expect(zone.run(() => NgZone.isInAngularZone())).toBeTrue();
      } finally {
        appRef.destroy();
      }
    });
  });
  describe('Angular 22', () => {
    it('framework_IsAtLeast22', () => {
      expect(Number(VERSION.major)).toBeGreaterThanOrEqual(22);
    });

    // Angular 22 varsayılanı fetch; http-xhr-backend migration'ı withXhr() ekledi (mevcut davranış korunur).
    it('appConfig_UsesXhrBackend', async () => {
      const appRef = await createApplication(appConfig);
      try {
        expect(appRef.injector.get(HttpBackend)).toBeInstanceOf(HttpXhrBackend);
      } finally {
        appRef.destroy();
      }
    });

    // Angular 22 varsayılanı OnPush; change-detection-eager migration'ı mevcut bileşenlere Eager yazdı.
    it('appComponent_KeepsEagerStrategyFromMigration', () => {
      expect((AppComponent as unknown as { ɵcmp: { onPush: boolean } }).ɵcmp.onPush).toBeFalse();
    });
  });
});
