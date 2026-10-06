import {
  CODE_VERIFIER_PATTERN,
  OIDC_STATE_PATTERN,
  base64UrlEncode,
  computeCodeChallenge,
  createCodeVerifier,
  createOidcState,
  sha256Fallback,
} from './pkce.util';

/** Issue #347: PKCE S256 ve state üretimi. */
describe('pkce.util', () => {
  // RFC 7636 Appendix B test vektörü.
  const RFC_VERIFIER = 'dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk';
  const RFC_CHALLENGE = 'E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM';

  function hex(bytes: Uint8Array): string {
    return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
  }

  it('computeCodeChallenge_Rfc7636Vector_ReturnsBase64UrlSha256', async () => {
    expect(await computeCodeChallenge(RFC_VERIFIER)).toBe(RFC_CHALLENGE);
  });

  it('computeCodeChallenge_RandomVerifier_EqualsBase64UrlOfSubtleSha256', async () => {
    const verifier = createCodeVerifier();
    const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier)));

    const challenge = await computeCodeChallenge(verifier);

    expect(challenge).toBe(base64UrlEncode(digest));
    expect(challenge).toMatch(/^[A-Za-z0-9_-]{43}$/);
  });

  it('computeCodeChallenge_InvalidVerifier_Throws', async () => {
    await expectAsync(computeCodeChallenge('too-short')).toBeRejected();
  });

  it('sha256Fallback_MatchesKnownVectorsAndRfcChallenge', () => {
    const enc = new TextEncoder();
    expect(hex(sha256Fallback(enc.encode('')))).toBe(
      'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
    );
    expect(hex(sha256Fallback(enc.encode('abc')))).toBe(
      'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad'
    );
    // 56 bayt: padding'in ikinci bloğa taştığı sınır durumu.
    expect(hex(sha256Fallback(enc.encode('abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq')))).toBe(
      '248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1'
    );
    expect(base64UrlEncode(sha256Fallback(enc.encode(RFC_VERIFIER)))).toBe(RFC_CHALLENGE);
  });

  it('createCodeVerifier_ProducesRfcCompliantRandomValues', () => {
    const a = createCodeVerifier();
    const b = createCodeVerifier();

    expect(a).toMatch(CODE_VERIFIER_PATTERN);
    expect(a.length).toBe(43);
    expect(a).not.toBe(b);
  });

  it('createOidcState_ProducesUniqueBase64UrlValues', () => {
    const a = createOidcState();
    const b = createOidcState();

    expect(a).toMatch(OIDC_STATE_PATTERN);
    expect(a).not.toBe(b);
  });

  it('base64UrlEncode_HasNoPaddingOrUnsafeChars', () => {
    expect(base64UrlEncode(new Uint8Array([0xfb, 0xff, 0xfe]))).toBe('-__-');
    expect(base64UrlEncode(new Uint8Array([0x01]))).toBe('AQ');
  });
});
