import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';

import { AppLocale } from '../../models/locale';
import { LocaleService } from '../../services/locale.service';
import { localeInterceptor } from './locale.interceptor';

describe('localeInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let locale: ReturnType<typeof signal<AppLocale>>;

  beforeEach(() => {
    locale = signal<AppLocale>('tr');
    const localeServiceStub = { locale: locale.asReadonly() } as Pick<LocaleService, 'locale'>;

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([localeInterceptor])),
        provideHttpClientTesting(),
        { provide: LocaleService, useValue: localeServiceStub },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('intercept_ApiRequest_AddsActiveLocaleAsAcceptLanguage', () => {
    http.get('/api/exam/dashboard').subscribe();

    const request = httpMock.expectOne('/api/exam/dashboard');

    expect(request.request.headers.get('Accept-Language')).toBe('tr');
    request.flush({});
  });

  it('intercept_ActiveLocaleChanged_SendsNewLocale', () => {
    locale.set('en');
    http.get('/api/exam/dashboard').subscribe();

    const request = httpMock.expectOne('/api/exam/dashboard');

    expect(request.request.headers.get('Accept-Language')).toBe('en');
    request.flush({});
  });

  it('intercept_AbsoluteApiUrl_AddsAcceptLanguage', () => {
    http.get('http://localhost:5678/api/exam/dashboard').subscribe();

    const request = httpMock.expectOne('http://localhost:5678/api/exam/dashboard');

    expect(request.request.headers.get('Accept-Language')).toBe('tr');
    request.flush({});
  });

  it('intercept_NonApiRequest_LeavesHeadersUntouched', () => {
    http.get('/i18n/tr.json').subscribe();

    const request = httpMock.expectOne('/i18n/tr.json');

    expect(request.request.headers.has('Accept-Language')).toBeFalse();
    request.flush({});
  });

  it('intercept_CallerSetAcceptLanguage_DoesNotOverwriteIt', () => {
    http.get('/api/exam/dashboard', { headers: { 'Accept-Language': 'de' } }).subscribe();

    const request = httpMock.expectOne('/api/exam/dashboard');

    expect(request.request.headers.get('Accept-Language')).toBe('de');
    request.flush({});
  });
});
