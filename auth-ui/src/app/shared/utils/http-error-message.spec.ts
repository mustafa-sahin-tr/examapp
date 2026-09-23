import { HttpErrorResponse } from '@angular/common/http';

import { httpErrorMessage } from './http-error-message';

describe('httpErrorMessage', () => {
  const fallback = 'Varsayılan';

  it('gövdedeki message alanını döndürür', () => {
    const error = new HttpErrorResponse({ status: 401, error: { message: 'E-posta veya şifre hatalı.' } });
    expect(httpErrorMessage(error, fallback)).toBe('E-posta veya şifre hatalı.');
  });

  it('gövde yoksa fallback döner', () => {
    expect(httpErrorMessage(new HttpErrorResponse({ status: 500, error: null }), fallback)).toBe(fallback);
  });

  it('message boş veya string değilse fallback döner', () => {
    expect(httpErrorMessage(new HttpErrorResponse({ status: 503, error: { message: '  ' } }), fallback)).toBe(fallback);
    expect(httpErrorMessage(new HttpErrorResponse({ status: 503, error: { message: 42 } }), fallback)).toBe(fallback);
  });

  it('HttpErrorResponse değilse fallback döner', () => {
    expect(httpErrorMessage(new Error('x'), fallback)).toBe(fallback);
  });
});
