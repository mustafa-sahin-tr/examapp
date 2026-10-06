/**
 * OIDC authorization code akışı için PKCE (RFC 7636, S256) ve `state` üretimi (issue #347).
 *
 * - `state`: login başlatılırken üretilen, callback'te sessionStorage'daki kayıtla eşleştirilen rastgele
 *   değer (login CSRF koruması). Yönlendirme hedefi veya rol niyeti TAŞIMAZ.
 * - `code_verifier`: 32 rastgele bayt → 43 karakter base64url (RFC 7636 §4.1: 43-128, `[A-Za-z0-9-._~]`).
 * - `code_challenge`: base64url(sha256(ascii(code_verifier))), padding yok (§4.2).
 *
 * `crypto.getRandomValues` her bağlamda vardır; `crypto.subtle` ise yalnızca güvenli bağlamda (HTTPS /
 * localhost). Uygulama LAN IP'si gibi güvenli olmayan bir origin'den açılırsa login kırılmasın diye
 * SHA-256'nın saf TypeScript yedeği tutulur (aynı RFC test vektörüyle doğrulanır).
 */

/** RFC 7636 §4.1 code_verifier biçimi. */
export const CODE_VERIFIER_PATTERN = /^[A-Za-z0-9\-._~]{43,128}$/;

/** Bizim ürettiğimiz `state` biçimi: 32 bayt → 43 karakter base64url. Callback bunu doğrular. */
export const OIDC_STATE_PATTERN = /^[A-Za-z0-9_-]{43}$/;

/** Baytları padding'siz base64url'e çevirir. */
export function base64UrlEncode(bytes: Uint8Array): string {
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** Kriptografik rastgele `byteLength` bayt, base64url. */
export function randomBase64Url(byteLength = 32): string {
  const bytes = new Uint8Array(byteLength);
  crypto.getRandomValues(bytes);
  return base64UrlEncode(bytes);
}

export function createOidcState(): string {
  return randomBase64Url(32);
}

export function createCodeVerifier(): string {
  return randomBase64Url(32);
}

/** S256 code_challenge = base64url(sha256(ascii(verifier))). */
export async function computeCodeChallenge(codeVerifier: string): Promise<string> {
  if (!CODE_VERIFIER_PATTERN.test(codeVerifier)) {
    throw new Error('Invalid PKCE code_verifier');
  }
  const data = new TextEncoder().encode(codeVerifier);
  const subtle = typeof crypto !== 'undefined' ? crypto.subtle : undefined;
  const digest = subtle ? new Uint8Array(await subtle.digest('SHA-256', data)) : sha256Fallback(data);
  return base64UrlEncode(digest);
}

// --- SHA-256 yedeği (FIPS 180-4); yalnızca crypto.subtle yokken kullanılır. ---

const K = new Uint32Array([
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5, 0xd807aa98,
  0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786,
  0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da, 0x983e5152, 0xa831c66d, 0xb00327c8,
  0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
  0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819,
  0xd6990624, 0xf40e3585, 0x106aa070, 0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a,
  0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7,
  0xc67178f2,
]);

const rotr = (x: number, n: number): number => (x >>> n) | (x << (32 - n));

/** Saf TypeScript SHA-256; `crypto.subtle` olmayan (güvenli olmayan) bağlamlar için. */
export function sha256Fallback(message: Uint8Array): Uint8Array {
  const bitLength = message.length * 8;
  const paddedLength = Math.ceil((message.length + 9) / 64) * 64;
  const padded = new Uint8Array(paddedLength);
  padded.set(message);
  padded[message.length] = 0x80;
  const view = new DataView(padded.buffer);
  // Mesaj uzunluğu (bit) son 8 bayta big-endian; verifier'lar 2^32 bitten çok küçük.
  view.setUint32(paddedLength - 8, Math.floor(bitLength / 0x100000000));
  view.setUint32(paddedLength - 4, bitLength >>> 0);

  const h = new Uint32Array([
    0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19,
  ]);
  const w = new Uint32Array(64);

  for (let offset = 0; offset < paddedLength; offset += 64) {
    for (let i = 0; i < 16; i++) {
      w[i] = view.getUint32(offset + i * 4);
    }
    for (let i = 16; i < 64; i++) {
      const s0 = rotr(w[i - 15], 7) ^ rotr(w[i - 15], 18) ^ (w[i - 15] >>> 3);
      const s1 = rotr(w[i - 2], 17) ^ rotr(w[i - 2], 19) ^ (w[i - 2] >>> 10);
      w[i] = (w[i - 16] + s0 + w[i - 7] + s1) >>> 0;
    }

    let [a, b, c, d, e, f, g, hh] = h;
    for (let i = 0; i < 64; i++) {
      const s1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25);
      const ch = (e & f) ^ (~e & g);
      const t1 = (hh + s1 + ch + K[i] + w[i]) >>> 0;
      const s0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22);
      const maj = (a & b) ^ (a & c) ^ (b & c);
      const t2 = (s0 + maj) >>> 0;
      hh = g;
      g = f;
      f = e;
      e = (d + t1) >>> 0;
      d = c;
      c = b;
      b = a;
      a = (t1 + t2) >>> 0;
    }

    h[0] = (h[0] + a) >>> 0;
    h[1] = (h[1] + b) >>> 0;
    h[2] = (h[2] + c) >>> 0;
    h[3] = (h[3] + d) >>> 0;
    h[4] = (h[4] + e) >>> 0;
    h[5] = (h[5] + f) >>> 0;
    h[6] = (h[6] + g) >>> 0;
    h[7] = (h[7] + hh) >>> 0;
  }

  const out = new Uint8Array(32);
  const outView = new DataView(out.buffer);
  h.forEach((word, i) => outView.setUint32(i * 4, word));
  return out;
}
