import { DOCUMENT as CORE_DOCUMENT, VERSION } from '@angular/core';
import { DOCUMENT as COMMON_DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { VERSION as CDK_VERSION } from '@angular/cdk';
import { VERSION as MATERIAL_VERSION } from '@angular/material/core';

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
});
