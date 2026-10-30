import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/**
 * Issue #440: veli iletişim bilgilerinin istemci doğrulaması — sunucudaki `ExamApp.Foundation.Security.ParentContactRules` ile
 * aynı kurallar (asıl kapı sunucu). auth-ui ayrı bir uygulama olduğundan `ui/src/app/shared/utils/parent-contact.util.ts` ile aynı
 * içerik burada tutulur; değişiklikte ikisini birlikte güncelleyin.
 */
export const PARENT_EMAIL_MAX_LENGTH = 254;
export const PARENT_NAME_MIN_LENGTH = 2;
export const PARENT_NAME_MAX_LENGTH = 100;
/** Ham telefon girdisi (boşluk/tire/parantez dahil) için üst sınır — sunucu DTO `MaxLength(32)`. */
export const PARENT_PHONE_INPUT_MAX_LENGTH = 32;

const EMAIL_SHAPE = /^[^@\s]+@[^@\s]+\.[^@\s.]{2,}$/;
const TR_MOBILE_NATIONAL = /^5\d{9}$/;
const PHONE_SEPARATORS = /[\s\-.()]/g;

/** Kırpılmış + küçük harf e-posta; geçersizse null. */
export function normalizeParentEmail(email: string | null | undefined): string | null {
  const value = (email ?? '').trim().toLowerCase();
  if (value.length === 0 || value.length > PARENT_EMAIL_MAX_LENGTH || !EMAIL_SHAPE.test(value)) {
    return null;
  }
  return value;
}

/**
 * TR cep telefonu → `+905xxxxxxxxx`; geçersizse null. Kabul: `05xxxxxxxxx`, `5xxxxxxxxx`, `905xxxxxxxxx`, `+905xxxxxxxxx`
 * (boşluk, tire, nokta ve parantez yok sayılır).
 */
export function normalizeParentPhone(phone: string | null | undefined): string | null {
  const raw = (phone ?? '').trim().replace(PHONE_SEPARATORS, '');
  if (!raw) {
    return null;
  }
  let national: string;
  if (raw.startsWith('+90')) {
    national = raw.slice(3);
  } else if (raw.startsWith('90') && raw.length === 12) {
    national = raw.slice(2);
  } else if (raw.startsWith('0') && raw.length === 11) {
    national = raw.slice(1);
  } else {
    national = raw;
  }
  return TR_MOBILE_NATIONAL.test(national) ? `+90${national}` : null;
}

/**
 * Gezinti notu (K1): veli telefonu alanı yazarken `05xx xxx xx xx` biçimine sokulur — rakam dışı atılır, 11 haneyle sınırlanır,
 * `5` ile başlayana `0` eklenir; `+90` / `90` ile başlayan (yapıştırılan) numara 12 hane dolunca `0`'lı biçime çevrilir.
 * Doğrulama değişmedi ({@link normalizeParentPhone}); bu yalnız giriş kolaylığı.
 */
export function formatParentPhoneInput(raw: string | null | undefined): string {
  let digits = (raw ?? '').replace(/\D/g, '');
  if (digits.startsWith('90')) {
    // Ülke kodlu numara tamamlanana dek olduğu gibi kalır (erken kesilmesin), tamamlanınca ulusal biçime döner.
    if (digits.length < 12) return digits;
    digits = `0${digits.slice(2, 12)}`;
  } else if (digits.startsWith('5')) {
    digits = `0${digits}`;
  }
  digits = digits.slice(0, 11);
  return [digits.slice(0, 4), digits.slice(4, 7), digits.slice(7, 9), digits.slice(9, 11)].filter(Boolean).join(' ');
}

/** Ad karakterleri: harf (Türkçe dahil), birleşik işaret, boşluk, `.`, `-`, `'`, `’` (sunucu `NameCharacters`). */
const NAME_CHARACTERS = /^[\p{L}\p{M} .'’-]+$/u;
/** Alan adı / bağlantı benzeri: "www." ya da harf + nokta + en az iki harf ("site.com", "x.co") — sunucu `LinkLike`. */
const LINK_LIKE = /www\.|\p{L}\.\p{L}{2,}/iu;

/**
 * Kırpılmış, iç boşlukları teke indirilmiş ad; geçersizse null (2-100 karakter, en az bir harf, yalnız izinli karakterler,
 * bağlantı benzeri değil).
 */
export function normalizeParentName(name: string | null | undefined): string | null {
  const value = (name ?? '').trim().replace(/\s+/g, ' ');
  if (value.length < PARENT_NAME_MIN_LENGTH || value.length > PARENT_NAME_MAX_LENGTH || !/\p{L}/u.test(value)) {
    return null;
  }
  if (!NAME_CHARACTERS.test(value) || LINK_LIKE.test(value)) {
    return null;
  }
  return value;
}

function textValidator(errorKey: string, normalize: (value: string) => string | null): ValidatorFn {
  return (control: AbstractControl<string | null>): ValidationErrors | null => {
    const value = control.value ?? '';
    // Boş değer `Validators.required`'ın işi; burada yalnız biçim.
    if (!value.trim()) {
      return null;
    }
    return normalize(value) ? null : { [errorKey]: true };
  };
}

/** Form doğrulayıcıları — hata anahtarları: `parentName`, `parentEmail`, `parentPhone`. */
export const parentNameValidator = textValidator('parentName', normalizeParentName);
export const parentEmailValidator = textValidator('parentEmail', normalizeParentEmail);
export const parentPhoneValidator = textValidator('parentPhone', normalizeParentPhone);
