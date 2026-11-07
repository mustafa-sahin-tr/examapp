import { CUSTOM_ELEMENTS_SCHEMA, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import adminTr from '../../../../../public/i18n/admin/tr.json';
import { ClassifierStatus } from '../../../models/classifier-status.model';
import { ClassifierStatusService } from '../../../services/classifier-status.service';
import { LocaleService } from '../../../services/locale.service';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import { ClassifierCacheComponent } from '../classifier-cache/classifier-cache.component';
import { ClassifierStatusComponent, classifierState } from './classifier-status.component';

const status = (overrides: Partial<ClassifierStatus> = {}): ClassifierStatus => ({
  provider: 'Claude',
  model: 'claude-haiku-5-5',
  active: true,
  apiKeyConfigured: true,
  bookTocMode: 'Hybrid',
  since: '2026-11-01T00:00:00Z',
  classified: 38,
  skipped: 1,
  failed: 0,
  lastErrorAt: null,
  lastErrorKind: null,
  ...overrides,
});

describe('ClassifierStatusComponent (issue #897)', () => {
  function create(response: Observable<ClassifierStatus>): ComponentFixture<ClassifierStatusComponent> {
    TestBed.configureTestingModule({
      imports: [
        ClassifierStatusComponent,
        translocoTestingModule({ langs: { 'admin/tr': adminTr }, translocoConfig: { scopes: { keepCasing: true } } }),
      ],
      providers: [
        { provide: ClassifierStatusService, useValue: { get: () => response } },
        { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: 'tr-TR' }) } },
      ],
    });
    // Gemini önbellek kartının kendi servisleri var; burada yalnız görünüp görünmediği önemli.
    TestBed.overrideComponent(ClassifierStatusComponent, {
      remove: { imports: [ClassifierCacheComponent] },
      add: { schemas: [CUSTOM_ELEMENTS_SCHEMA] },
    });
    const fixture = TestBed.createComponent(ClassifierStatusComponent);
    fixture.detectChanges();
    return fixture;
  }

  const text = (fixture: ComponentFixture<ClassifierStatusComponent>, test: string) =>
    (fixture.nativeElement.querySelector(`[data-test="${test}"]`)?.textContent ?? '').replace(/\s+/g, ' ').trim();

  it('claude_ShowsProviderModelBookModeAndCounts_WithoutTheGeminiCache', () => {
    const fixture = create(of(status()));

    expect(text(fixture, 'classifier-provider')).toBe('Claude');
    expect(text(fixture, 'classifier-model')).toBe('claude-haiku-5-5');
    expect(text(fixture, 'classifier-state')).toContain('Etkin');
    expect(text(fixture, 'classifier-book-mode')).toBe('Karma (içindekiler önce)');
    expect(text(fixture, 'classifier-counts')).toBe('38 soru sınıflandı · 1 atlandı');
    expect(text(fixture, 'classifier-last-error')).toBe('Yok');
    expect(fixture.nativeElement.querySelector('app-classifier-cache')).toBeNull();
    expect(text(fixture, 'classifier-gemini-unused')).toContain('Gemini kullanılmıyor');
  });

  it('gemini_ShowsItsCacheCard', () => {
    const fixture = create(of(status({ provider: 'Gemini', model: 'models/gemini-3.5-flash-lite' })));

    expect(fixture.nativeElement.querySelector('app-classifier-cache')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[data-test="classifier-gemini-unused"]')).toBeNull();
  });

  it('state_SaysOffOrMissingKey_AndShowsTheLastErrorType', () => {
    expect(classifierState(status({ active: false, apiKeyConfigured: false }))).toBe('disabled');
    expect(classifierState(status({ apiKeyConfigured: false }))).toBe('noKey');

    const fixture = create(of(status({ failed: 2, lastErrorAt: '2026-11-07T03:00:00Z', lastErrorKind: 'HttpRequestException' })));
    expect(text(fixture, 'classifier-last-error')).toContain('HttpRequestException (2 başarısız deneme)');
  });

  it('loadError_ShowsAMessage', () => {
    const fixture = create(throwError(() => new Error('down')));

    expect(text(fixture, 'classifier-error')).toBe('Sınıflandırıcı durumu okunamadı.');
  });
});
