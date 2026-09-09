import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { COLOR_SCHEME_STORAGE_KEY, ColorSchemeService } from './color-scheme.service';

describe('ColorSchemeService', () => {
  afterEach(() => {
    localStorage.removeItem(COLOR_SCHEME_STORAGE_KEY);
    document.documentElement.classList.remove('dark-theme', 'light-theme');
  });

  function createService(): ColorSchemeService {
    TestBed.configureTestingModule({
      providers: [ColorSchemeService, { provide: PLATFORM_ID, useValue: 'browser' }],
    });
    return TestBed.inject(ColorSchemeService);
  }

  describe('in browser platform', () => {
    it('colorScheme_LocalStorageEmptyAndHtmlHasNoThemeClass_DefaultsToDark', () => {
      const service = createService();

      expect(service.colorScheme()).toBe('dark');
      expect(document.documentElement.classList.contains('dark-theme')).toBeTrue();
    });

    it('colorScheme_HtmlAlreadyHasLightThemeClass_ReadsLightFromHtml', () => {
      document.documentElement.classList.add('light-theme');

      const service = createService();

      expect(service.colorScheme()).toBe('light');
    });

    it('colorScheme_HtmlAlreadyHasDarkThemeClass_ReadsDarkFromHtml', () => {
      document.documentElement.classList.add('dark-theme');

      const service = createService();

      expect(service.colorScheme()).toBe('dark');
    });

    it('setScheme_CalledWithLight_UpdatesSignalHtmlClassAndLocalStorage', () => {
      const service = createService();

      service.setScheme('light');

      expect(service.colorScheme()).toBe('light');
      expect(document.documentElement.classList.contains('light-theme')).toBeTrue();
      expect(document.documentElement.classList.contains('dark-theme')).toBeFalse();
      expect(localStorage.getItem(COLOR_SCHEME_STORAGE_KEY)).toBe('light');
    });

    it('setScheme_CalledWithDark_UpdatesSignalHtmlClassAndLocalStorage', () => {
      const service = createService();
      service.setScheme('light');

      service.setScheme('dark');

      expect(service.colorScheme()).toBe('dark');
      expect(document.documentElement.classList.contains('dark-theme')).toBeTrue();
      expect(document.documentElement.classList.contains('light-theme')).toBeFalse();
      expect(localStorage.getItem(COLOR_SCHEME_STORAGE_KEY)).toBe('dark');
    });

    it('toggle_CurrentSchemeIsDark_SwitchesToLight', () => {
      const service = createService();

      service.toggle();

      expect(service.colorScheme()).toBe('light');
    });

    it('toggle_CurrentSchemeIsLight_SwitchesToDark', () => {
      const service = createService();
      service.setScheme('light');

      service.toggle();

      expect(service.colorScheme()).toBe('dark');
    });
  });

  describe('in server (SSR) platform', () => {
    function createServerService(): ColorSchemeService {
      TestBed.configureTestingModule({
        providers: [ColorSchemeService, { provide: PLATFORM_ID, useValue: 'server' }],
      });
      return TestBed.inject(ColorSchemeService);
    }

    it('constructor_ServerPlatform_DoesNotThrowAndDefaultsToDark', () => {
      let service!: ColorSchemeService;

      expect(() => (service = createServerService())).not.toThrow();
      expect(service.colorScheme()).toBe('dark');
    });

    it('setScheme_ServerPlatform_UpdatesSignalWithoutTouchingDomOrLocalStorage', () => {
      const service = createServerService();
      const classListBefore = document.documentElement.className;

      expect(() => service.setScheme('light')).not.toThrow();

      expect(service.colorScheme()).toBe('light');
      expect(document.documentElement.className).toBe(classListBefore);
      expect(localStorage.getItem(COLOR_SCHEME_STORAGE_KEY)).toBeNull();
    });

    it('toggle_ServerPlatform_UpdatesSignalWithoutThrowing', () => {
      const service = createServerService();

      expect(() => service.toggle()).not.toThrow();

      expect(service.colorScheme()).toBe('light');
    });
  });
});
