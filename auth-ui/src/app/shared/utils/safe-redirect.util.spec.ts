import { safeRedirectTarget } from './safe-redirect.util';

/** Issue #347: login sonrası yönlendirme allowlist'i (open redirect). */
describe('safeRedirectTarget', () => {
  const origin = 'https://app.example.test';

  describe('kabul edilen aynı-origin göreli yollar', () => {
    const cases: [string, string][] = [
      ['/dashboard', '/dashboard'],
      ['/tests/42?tab=results#q3', '/tests/42?tab=results#q3'],
      ['/app/complete-profile?role=student', '/app/complete-profile?role=student'],
      ['/a/../admin/dashboard', '/admin/dashboard'],
    ];
    for (const [input, expected] of cases) {
      it(`${input} → ${expected}`, () => {
        expect(safeRedirectTarget(input, [], origin)).toBe(expected);
      });
    }
  });

  describe('reddedilen değerler', () => {
    const rejected: unknown[] = [
      null,
      undefined,
      42,
      '',
      'https://evil.example',
      'http://localhost:5678/dashboard', // mutlak, allowlist boş
      '//evil.example/dashboard',
      '///evil.example',
      '/\\evil.example',
      '\\\\evil.example',
      '/\tevil',
      ' /dashboard',
      'javascript:alert(1)',
      'JavaScript:alert(1)',
      'data:text/html,<script>alert(1)</script>',
      'dashboard', // şemasız göreli değil, / ile başlamıyor
      '/oidc-login?intent=student', // akışın kendi yolları (döngü)
      '/app/login',
      '/app/callback?code=x',
      '/app/logout/',
      '/' + 'a'.repeat(2048),
    ];
    for (const value of rejected) {
      it(`${JSON.stringify(value)?.slice(0, 60)} → null`, () => {
        expect(safeRedirectTarget(value, [], origin)).toBeNull();
      });
    }
  });

  describe('açık origin allowlist', () => {
    const allowed = ['https://admin.example.test'];

    it('allowlist origin → tam URL', () => {
      expect(safeRedirectTarget('https://admin.example.test/panel?x=1', allowed, origin)).toBe(
        'https://admin.example.test/panel?x=1'
      );
    });

    it('allowlist dışı / alt alan adı / kullanıcı bilgili / farklı şema → null', () => {
      expect(safeRedirectTarget('https://evil.example/panel', allowed, origin)).toBeNull();
      expect(safeRedirectTarget('https://admin.example.test.evil.example/', allowed, origin)).toBeNull();
      expect(safeRedirectTarget('https://admin.example.test@evil.example/', allowed, origin)).toBeNull();
      expect(safeRedirectTarget('https://user:pw@admin.example.test/', allowed, origin)).toBeNull();
      expect(safeRedirectTarget('http://admin.example.test/', allowed, origin)).toBeNull();
      expect(safeRedirectTarget('javascript://admin.example.test/%0aalert(1)', allowed, origin)).toBeNull();
    });
  });

  it('origin verilmezse location.origin kullanılır', () => {
    expect(safeRedirectTarget('/dashboard')).toBe('/dashboard');
    expect(safeRedirectTarget(`${location.origin}/dashboard`)).toBeNull();
  });
});
