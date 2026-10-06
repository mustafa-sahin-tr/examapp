import { TestBed } from '@angular/core/testing';
import {
  OIDC_FLOW_KEY_PREFIX,
  OIDC_FLOW_MAX_AGE_MS,
  OidcFlowService,
  OidcLoginRecord,
  toRegisterIntent,
} from './oidc-flow.service';
import { computeCodeChallenge } from '../shared/utils/pkce.util';

/** Issue #347: state + PKCE kaydının üretimi ve tek kullanımlık doğrulanması. */
describe('OidcFlowService', () => {
  let service: OidcFlowService;

  function storedRecords(): OidcLoginRecord[] {
    const out: OidcLoginRecord[] = [];
    for (let i = 0; i < sessionStorage.length; i++) {
      const key = sessionStorage.key(i)!;
      if (key.startsWith(OIDC_FLOW_KEY_PREFIX)) {
        out.push(JSON.parse(sessionStorage.getItem(key)!));
      }
    }
    return out;
  }

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({});
    service = TestBed.inject(OidcFlowService);
  });

  afterEach(() => sessionStorage.clear());

  it('begin_StoresRecordKeyedByStateAndReturnsGatewayUrlWithS256Challenge', async () => {
    const url = new URL(await service.begin(), 'http://localhost');

    const state = url.searchParams.get('state')!;
    const raw = sessionStorage.getItem(OIDC_FLOW_KEY_PREFIX + state);
    expect(raw).not.toBeNull();
    const record = JSON.parse(raw!) as OidcLoginRecord;

    expect(url.pathname).toBe('/oidc-login');
    expect(url.searchParams.get('code_challenge_method')).toBe('S256');
    expect(url.searchParams.get('code_challenge')).toBe(await computeCodeChallenge(record.codeVerifier));
    // Verifier asla URL'ye çıkmaz.
    expect(url.search).not.toContain(record.codeVerifier);
    expect(url.searchParams.has('intent')).toBeFalse();
    expect(record.state).toBe(state);
    expect(record.intent).toBeNull();
    expect(record.returnPath).toBeNull();
  });

  it('begin_EachCallUsesFreshStateAndVerifier', async () => {
    const a = new URL(await service.begin(), 'http://localhost');
    const b = new URL(await service.begin(), 'http://localhost');

    expect(a.searchParams.get('state')).not.toBe(b.searchParams.get('state'));
    expect(a.searchParams.get('code_challenge')).not.toBe(b.searchParams.get('code_challenge'));
    expect(storedRecords().length).toBe(2);
  });

  it('begin_ValidIntent_IsForwardedAndStoredButNotEmbeddedInState', async () => {
    const url = new URL(await service.begin({ intent: 'Teacher' }), 'http://localhost');

    expect(url.searchParams.get('intent')).toBe('teacher');
    expect(url.searchParams.get('state')).not.toContain('~');
    expect(storedRecords()[0].intent).toBe('teacher');
  });

  it('begin_UnknownIntentAndUnsafeReturnPath_AreDropped', async () => {
    const url = new URL(
      await service.begin({ intent: 'admin', returnPath: '//evil.example/x' }),
      'http://localhost'
    );

    expect(url.searchParams.has('intent')).toBeFalse();
    expect(storedRecords()[0].intent).toBeNull();
    expect(storedRecords()[0].returnPath).toBeNull();
  });

  it('begin_SafeReturnPath_IsStored', async () => {
    await service.begin({ returnPath: '/tests/12?tab=results' });

    expect(storedRecords()[0].returnPath).toBe('/tests/12?tab=results');
  });

  it('consume_MatchingState_ReturnsRecordOnce', async () => {
    const state = new URL(await service.begin({ intent: 'student' }), 'http://localhost').searchParams.get('state')!;

    const first = service.consume(state);
    const second = service.consume(state);

    expect(first?.state).toBe(state);
    expect(first?.intent).toBe('student');
    expect(second).toBeNull();
    expect(sessionStorage.getItem(OIDC_FLOW_KEY_PREFIX + state)).toBeNull();
  });

  it('consume_UnknownOrMalformedState_ReturnsNull', async () => {
    await service.begin();

    expect(service.consume('A'.repeat(43))).toBeNull();
    expect(service.consume('http://evil.example~student')).toBeNull();
    expect(service.consume('')).toBeNull();
    expect(service.consume(undefined)).toBeNull();
    expect(service.consume(['x'])).toBeNull();
    // Gerçek kayıt dokunulmadan kalır.
    expect(storedRecords().length).toBe(1);
  });

  it('consume_ExpiredRecord_ReturnsNull', async () => {
    const state = new URL(await service.begin(), 'http://localhost').searchParams.get('state')!;
    const key = OIDC_FLOW_KEY_PREFIX + state;
    const record = JSON.parse(sessionStorage.getItem(key)!) as OidcLoginRecord;
    sessionStorage.setItem(key, JSON.stringify({ ...record, createdAt: Date.now() - OIDC_FLOW_MAX_AGE_MS - 1 }));

    expect(service.consume(state)).toBeNull();
  });

  it('consume_TamperedRecord_ReturnsNull', async () => {
    const state = new URL(await service.begin(), 'http://localhost').searchParams.get('state')!;
    const key = OIDC_FLOW_KEY_PREFIX + state;
    const record = JSON.parse(sessionStorage.getItem(key)!) as OidcLoginRecord;
    sessionStorage.setItem(key, JSON.stringify({ ...record, state: 'B'.repeat(43) }));

    expect(service.consume(state)).toBeNull();
  });

  it('consume_StoredUnsafeReturnPath_IsRevalidated', async () => {
    const state = new URL(await service.begin(), 'http://localhost').searchParams.get('state')!;
    const key = OIDC_FLOW_KEY_PREFIX + state;
    const record = JSON.parse(sessionStorage.getItem(key)!) as OidcLoginRecord;
    sessionStorage.setItem(key, JSON.stringify({ ...record, returnPath: 'https://evil.example' }));

    expect(service.consume(state)?.returnPath).toBeNull();
  });

  it('begin_PrunesExpiredRecords', async () => {
    sessionStorage.setItem(
      OIDC_FLOW_KEY_PREFIX + 'C'.repeat(43),
      JSON.stringify({ state: 'C'.repeat(43), codeVerifier: 'v'.repeat(43), createdAt: 0 })
    );
    sessionStorage.setItem('unrelated', 'keep');

    await service.begin();

    expect(sessionStorage.getItem(OIDC_FLOW_KEY_PREFIX + 'C'.repeat(43))).toBeNull();
    expect(sessionStorage.getItem('unrelated')).toBe('keep');
    expect(storedRecords().length).toBe(1);
  });

  it('toRegisterIntent_AcceptsOnlyKnownRoles', () => {
    expect(toRegisterIntent(' Parent ')).toBe('parent');
    expect(toRegisterIntent('admin')).toBeNull();
    expect(toRegisterIntent(null)).toBeNull();
  });
});
