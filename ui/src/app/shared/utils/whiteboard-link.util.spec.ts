import { classifyWhiteboardLink, openInNewTab } from './whiteboard-link.util';

describe('whiteboard-link.util (issue #332)', () => {
  const ORIGIN = 'https://app.example.com';

  it('Classify_SameOrigin_IncludingCaseAndDefaultPort', () => {
    expect(classifyWhiteboardLink('https://app.example.com/programs/1', ORIGIN)).toEqual({
      kind: 'sameOrigin',
      url: 'https://app.example.com/programs/1',
    });
    expect(classifyWhiteboardLink('HTTPS://APP.example.com:443/x', ORIGIN)).toEqual({
      kind: 'sameOrigin',
      url: 'https://app.example.com/x',
    });
  });

  it('Classify_DifferentHostPortOrScheme_IsExternalWithNormalizedUrlAndHost', () => {
    expect(classifyWhiteboardLink('https://evil.example.org/login?a=1', ORIGIN)).toEqual({
      kind: 'external',
      url: 'https://evil.example.org/login?a=1',
      host: 'evil.example.org',
    });
    // Alt alan adı, port ve şema farkı ayrı origin'dir.
    expect(classifyWhiteboardLink('https://sub.app.example.com/', ORIGIN).kind).toBe('external');
    expect(classifyWhiteboardLink('https://app.example.com:8443/', ORIGIN)).toEqual({
      kind: 'external',
      url: 'https://app.example.com:8443/',
      host: 'app.example.com:8443',
    });
    expect(classifyWhiteboardLink('http://app.example.com/', ORIGIN).kind).toBe('external');
  });

  it('Classify_Userinfo_IsBlocked', () => {
    for (const link of [
      'https://app.example.com@evil.example.org/',
      'https://user:pass@evil.example.org/',
      'https://user@app.example.com/programs',
      'https://:secret@app.example.com/',
    ]) {
      expect(classifyWhiteboardLink(link, ORIGIN)).withContext(link).toEqual({ kind: 'blocked' });
    }
  });

  it('Classify_SameOriginSensitivePaths_RequireConfirmation', () => {
    for (const path of [
      '/app/login',
      '/app',
      '/realms/exam/protocol/openid-connect/auth',
      '/auth/logout',
      '/oidc-login?x=1',
      '/token',
      '/api/users/me',
      '/hub/whiteboard',
      '/hangfire/jobs',
      '/API/users',
      '//api/users',
      '/%61pi/users',
      '/%E0%A4%A',
    ]) {
      expect(classifyWhiteboardLink(`${ORIGIN}${path}`, ORIGIN).kind).withContext(path).toBe('external');
    }
  });

  it('Classify_SameOriginUiRoutes_StayDirect', () => {
    for (const path of ['/', '/programs/3/detail', '/test/5?reminder=edit', '/applications', '/notifications']) {
      expect(classifyWhiteboardLink(`${ORIGIN}${path}`, ORIGIN).kind).withContext(path).toBe('sameOrigin');
    }
  });

  it('Classify_NonHttpSchemesRelativeAndInvalid_AreBlocked', () => {
    for (const link of [
      'javascript:alert(1)',
      ' JavaScript:alert(1)',
      'data:text/html;base64,PGI+',
      'file:///etc/passwd',
      'vbscript:msgbox(1)',
      'blob:https://app.example.com/uuid',
      'mailto:a@b.c',
      '/relative/path',
      '//evil.example.org',
      'not a url',
      '',
      `https://e.org/${'a'.repeat(2048)}`,
      null,
      undefined,
      42,
    ]) {
      expect(classifyWhiteboardLink(link, ORIGIN)).withContext(String(link)).toEqual({ kind: 'blocked' });
    }
  });

  it('OpenInNewTab_UsesBlankTargetWithNoopenerNoreferrer', () => {
    const open = jasmine.createSpy('open');
    openInNewTab('https://example.org/', open);
    expect(open).toHaveBeenCalledOnceWith('https://example.org/', '_blank', 'noopener,noreferrer');
  });
});
