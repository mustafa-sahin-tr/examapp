import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';

import { adminActionErrorMessage } from './admin-action-error.util';

describe('adminActionErrorMessage (issue #155/#156)', () => {
  const translate = (key: string, params?: Record<string, unknown>) =>
    params ? `${key}:${params['seconds']}` : key;
  const msg = (err: HttpErrorResponse) => adminActionErrorMessage(err, 'p.errors', translate);

  it('prefersTrimmedBackendMessageFor403_404_502', () => {
    expect(msg(new HttpErrorResponse({ status: 403, error: { message: ' m ' } }))).toBe('m');
    expect(msg(new HttpErrorResponse({ status: 404, error: { message: 'yok' } }))).toBe('yok');
    expect(msg(new HttpErrorResponse({ status: 502, error: { message: 'x' } }))).toBe('x');
  });

  it('fallsBackToPrefixedKeysWithoutBodyMessage', () => {
    expect(msg(new HttpErrorResponse({ status: 403, error: { message: '' } }))).toBe('p.errors.forbidden');
    expect(msg(new HttpErrorResponse({ status: 404, error: 'x' }))).toBe('p.errors.notFound');
    expect(msg(new HttpErrorResponse({ status: 502 }))).toBe('p.errors.upstream');
  });

  it('rateLimit_UsesRetryAfterSecondsWhenNumeric', () => {
    expect(msg(new HttpErrorResponse({ status: 429, headers: new HttpHeaders({ 'Retry-After': '5' }) }))).toBe(
      'p.errors.rateLimitedSeconds:5',
    );
    expect(msg(new HttpErrorResponse({ status: 429, headers: new HttpHeaders({ 'Retry-After': 'abc' }) }))).toBe(
      'p.errors.rateLimited',
    );
    expect(msg(new HttpErrorResponse({ status: 429, error: 'plain' }))).toBe('p.errors.rateLimited');
  });

  it('otherStatuses_AreGenericEvenWithBody', () => {
    expect(msg(new HttpErrorResponse({ status: 0 }))).toBe('p.errors.generic');
    expect(msg(new HttpErrorResponse({ status: 400, error: { message: 'm' } }))).toBe('p.errors.generic');
    expect(msg(new HttpErrorResponse({ status: 500, error: { message: 'internal' } }))).toBe('p.errors.generic');
  });
});
