/**
 * Issue #305 — yorum gövdesinde kişisel bilgi (telefon / e-posta) sezgisi. Yalnız UYARI içindir: gönderim
 * engellenmez, sunucu bu kontrolü yapmaz. Yanlış pozitif ucuz (kullanıcı "Yine de gönder" der), yanlış negatif
 * kabul edilebilir — bu yüzden desenler dar tutulur (matematik ifadelerindeki sayı dizileri uyarı üretmesin).
 *
 * Bilinçli kapsam dışı (review kararı): TC kimlik no (11 hane, 0 ile başlamaz — telefon sayılmaz), gizlenmiş e-posta
 * (`ali [at] ornek [dot] com`), kullanıcı adları (`@ali_kaya`, sosyal medya hesapları), yabancı numaralar.
 */

export interface PersonalInfoMatch {
  phone: boolean;
  email: boolean;
}

/** Basit e-posta: `ad.soyad+etiket@alan.com.tr`. */
const EMAIL_PATTERN = /[\p{L}\p{N}._%+-]+@[\p{L}\p{N}-]+(?:\.[\p{L}\p{N}-]+)*\.\p{L}{2,}/u;

/**
 * Telefon adayı: rakamla (ya da `+` / `(`) başlayıp rakamla biten, rakam + ayraç (boşluk, `-`, `.`, `(`, `)`) dizisi.
 * Önünde/arkasında rakam yok — daha uzun bir sayının parçası aday sayılmaz; ondalık ayraçtan (`0,53…`) sonra da
 * başlamaz. Doğrulama {@link isTurkishPhone}'da.
 */
const PHONE_CANDIDATE = /(?<![\p{N}+])(?<!\p{N}[.,])(?:\+|\()?\d[\d\s().-]{8,22}\d(?!\p{N})/gu;

/** Ondalık görünümü: rakam + nokta/virgül + 4+ rakam (`0.5321234567`); telefon grupları en çok 3-4 hanedir. */
const DECIMAL_LIKE = /\d[.,]\d{4,}/;

/** Öneksiz cep: bitişik (`5321234567`) ya da 3-3-2-2 grup (`532 123 45 67`, `(532) 123-45-67`). */
const BARE_MOBILE = /^(?:5\d{9}|\(?5\d{2}\)?[\s.-]+\d{3}[\s.-]+\d{2}[\s.-]+\d{2})$/;

/** Üç ve daha uzun ayraç dizisi numara sınırıdır (`0532 … - 0212 …` iki ayrı aday). */
const SEPARATOR_BREAK = /[\s().-]{3,}/;

/**
 * Türkiye numarası mı (rakamlara indirgenmiş aday):
 * - `+90` / `0090` / `90` önekli: kalan 10 hane, alan/operatör kodu 2-5 ile başlar (`+90 532 …`, `+90 212 …`).
 * - `0` önekli: kalan 10 hane, 2-5 ile başlar (`0532 …`, `0212 …`).
 * - öneksiz: yalnız cep — 10 hane, 5 ile başlar, bitişik ya da 3-3-2-2 gruplu ({@link BARE_MOBILE}). Öneksiz sabit
 *   hat, sayı dizileriyle karışmasın diye aranmaz.
 * Ondalık görünümlü ya da iki ve daha fazla tek haneli grup içeren (`0 5 3 2 …`) aday reddedilir.
 */
function isTurkishPhone(raw: string): boolean {
  const trimmed = raw.trim();
  if (DECIMAL_LIKE.test(trimmed)) {
    return false;
  }
  const groups = trimmed.split(/\D+/).filter(Boolean);
  if (groups.filter((group) => group.length === 1).length >= 2) {
    return false;
  }
  const digits = trimmed.replace(/\D/g, '');
  const international = raw.trimStart().startsWith('+') || digits.startsWith('0090');
  let rest = digits;
  if (rest.startsWith('0090')) {
    rest = rest.slice(4);
  } else if ((international || rest.length === 12) && rest.startsWith('90')) {
    rest = rest.slice(2);
  } else if (international) {
    return false;
  }
  const prefixed = rest !== digits;
  if (rest.length === 11 && rest.startsWith('0')) {
    rest = rest.slice(1);
    return /^[2-5]\d{9}$/.test(rest);
  }
  if (rest.length !== 10) {
    return false;
  }
  return prefixed ? /^[2-5]\d{9}$/.test(rest) : BARE_MOBILE.test(trimmed);
}

export function containsPhoneNumber(text: string): boolean {
  if (!text) {
    return false;
  }
  for (const match of text.matchAll(PHONE_CANDIDATE)) {
    if (match[0].split(SEPARATOR_BREAK).some(isTurkishPhone)) {
      return true;
    }
  }
  return false;
}

export function containsEmail(text: string): boolean {
  return !!text && EMAIL_PATTERN.test(text);
}

/** Gövdede telefon ya da e-posta var mı (ikisi ayrı ayrı). */
export function detectPersonalInfo(text: string): PersonalInfoMatch {
  return { phone: containsPhoneNumber(text), email: containsEmail(text) };
}

export function hasPersonalInfo(text: string): boolean {
  const match = detectPersonalInfo(text);
  return match.phone || match.email;
}
