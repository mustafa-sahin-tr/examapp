/**
 * Issue #440: veli aktivasyon bağlantısı (`/app/parent-activate?token=...`) ve öğrenci kaydındaki veli bilgisi sözleşmeleri.
 * Backend: `api/ExamApp.Api/Models/Dtos/ParentInvitations/ParentInvitationDtos.cs`, `ParentActivationController` (anonim,
 * gateway `/api/exam/parent-activation/{lookup|activate|resend}`). Token URL'de değil gövdede taşınır.
 */

/** `POST api/exam/parent-activation/lookup` yanıtı (`ParentActivationLookupDto`). */
export interface ParentActivationLookup {
  /** Öğrencinin kısa adı ("Ali K."); çözülemezse boş. */
  studentName: string;
  maskedParentEmail: string;
  kind: string;
  expiresAt: string;
  expired: boolean;
  /** Token kullanılmış ya da davet kapanmış (düzeltilmiş/iptal). */
  used: boolean;
  /** true → yeni hesap, şifre belirlenir. `accountExists` iken her zaman false. */
  requiresPasswordSetup: boolean;
  /**
   * Bu e-postayla zaten bir hesap var: anonim aktivasyon mevcut hesaba bağlamaz — veli giriş yapıp daveti
   * `POST .../accept` ile (Bearer) kabul eder.
   */
  accountExists: boolean;
  consentVersion: string;
  consentTextKey: string;
  canResend: boolean;
}

/** `POST .../activate` gövdesi. Şifre yalnız `requiresPasswordSetup` iken gönderilir. */
export interface ParentActivationRequest {
  token: string;
  password?: string;
  consentAccepted: boolean;
  consentVersion: string;
}

/** `POST .../activate` (her zaman `passwordSet:true`) ve `POST .../accept` (`passwordSet:false`) yanıtı. */
export interface ParentActivationResult {
  activated: boolean;
  passwordSet: boolean;
  message?: string;
}

/** `POST .../accept` gövdesi (oturum açmış veli; Bearer gateway üzerinden). */
export interface ParentActivationAcceptRequest {
  token: string;
  consentAccepted: boolean;
  consentVersion: string;
}

/** Hata gövdesi `{ message, errorCode }`; 429'da `retryAt` (UTC) eklenir. */
export interface ParentInvitationErrorBody {
  message?: string;
  errorCode?: string;
  retryAt?: string | null;
}

export const PARENT_INVITATION_ERROR_CODES = {
  parentInfoRequired: 'ParentInfoRequired',
  invalidParentName: 'InvalidParentName',
  invalidParentEmail: 'InvalidParentEmail',
  invalidParentPhone: 'InvalidParentPhone',
  parentEmailNotUsable: 'ParentEmailNotUsable',
  parentEmailChangeLocked: 'ParentEmailChangeLocked',
  resendLimited: 'ParentInvitationResendLimited',
  unavailable: 'ParentInvitationUnavailable',
  invalidActivationToken: 'InvalidActivationToken',
  activationTokenExpired: 'ActivationTokenExpired',
  activationTokenUsed: 'ActivationTokenUsed',
  consentRequired: 'ConsentRequired',
  consentVersionMismatch: 'ConsentVersionMismatch',
  passwordRejected: 'PasswordRejected',
  /** 409 (activate) — bu e-postayla hesap var; giriş yapıp `accept` ile kabul edilir. */
  loginRequired: 'LoginRequired',
  /** 403 (accept) — giriş yapılan hesabın e-postası davetinkiyle aynı değil. */
  invitationEmailMismatch: 'InvitationEmailMismatch',
  /** 403 (accept) — giriş yapan hesap veli değil. */
  parentAccountRequired: 'ParentAccountRequired',
  /** 409 (accept) — öğrencinin veli tavanı dolu. */
  studentLimitReached: 'StudentLimitReached',
  /** 409 — kilit/yarış (veli hesabını etkinleştirirken e-posta düzeltildi); tekrar denenebilir. */
  busy: 'Busy',
} as const;

/** Sunucu parola kuralı (`ChildAccountRules.IsValidPassword`): 8-128, harf + rakam, baş/son boşluk yok. */
export const PARENT_PASSWORD_MIN_LENGTH = 8;
export const PARENT_PASSWORD_MAX_LENGTH = 128;

/**
 * Rıza metinleri — sunucunun `consentTextKey`'i (`parentConsent.kvkk`). auth-ui'de çeviri altyapısı yok; bilinmeyen anahtar
 * varsayılan metne düşer.
 */
export const PARENT_CONSENT_TEXTS: Readonly<Record<string, string>> = {
  'parentConsent.kvkk':
    'Çocuğumun platformu kullanmasına veli olarak izin veriyorum. Çocuğuma ve bana ait kişisel verilerin, 6698 sayılı ' +
    'Kişisel Verilerin Korunması Kanunu (KVKK) kapsamında Gizlilik Politikası’nda açıklanan amaçlarla işlenmesini ' +
    'okudum ve açık rıza veriyorum.',
};

export function parentConsentText(key: string | null | undefined): string {
  return (key && PARENT_CONSENT_TEXTS[key]) || PARENT_CONSENT_TEXTS['parentConsent.kvkk'];
}

/** Daveti kabul eden velinin gideceği veli paneli (ana uygulama; callback'in veli varsayılanıyla aynı). */
export const PARENT_HOME_URL = '/parent';

/** Gizlilik politikası ana uygulamada (`ui`) — auth-ui'nin `/app/` tabanının dışında, mutlak yol. */
export const PRIVACY_POLICY_URL = '/privacy-policy';

/** Sunucu UTC `DateTime`'ı bölge eki olmadan da dönebilir; eksiz değer UTC kabul edilir. */
export function parseServerDate(iso: string | null | undefined): Date | null {
  if (!iso) {
    return null;
  }
  const hasZone = /(Z|[+-]\d{2}:?\d{2})$/i.test(iso);
  const date = new Date(hasZone ? iso : `${iso}Z`);
  return Number.isNaN(date.getTime()) ? null : date;
}

/** Türkçe tarih + saat ("21 Ekim 2026 14:30"); geçersizde boş. */
export function formatServerDateTime(iso: string | null | undefined): string {
  const date = parseServerDate(iso);
  if (!date) {
    return '';
  }
  return new Intl.DateTimeFormat('tr-TR', {
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date);
}

/**
 * Öğrenci kaydı / veli formu hata kodu → kullanıcı metni (auth-ui). Bilinmeyen kodda sunucu mesajı, o da yoksa `fallback`.
 */
export function parentContactErrorMessage(body: ParentInvitationErrorBody | null | undefined, fallback: string): string {
  const code = body?.errorCode;
  const retry = formatServerDateTime(body?.retryAt);
  switch (code) {
    case PARENT_INVITATION_ERROR_CODES.parentInfoRequired:
      return 'Veli adı, e-postası ve telefonu zorunludur.';
    case PARENT_INVITATION_ERROR_CODES.invalidParentName:
      return 'Veli adı yalnız harf içerebilir.';
    case PARENT_INVITATION_ERROR_CODES.invalidParentEmail:
      return 'Geçerli bir veli e-posta adresi gir.';
    case PARENT_INVITATION_ERROR_CODES.invalidParentPhone:
      return 'Geçerli bir cep telefonu gir (ör. 0532 123 45 67).';
    case PARENT_INVITATION_ERROR_CODES.parentEmailNotUsable:
      return 'Bu e-posta veli hesabı olarak kullanılamaz.';
    case PARENT_INVITATION_ERROR_CODES.parentEmailChangeLocked:
      return retry
        ? `Çok sayıda farklı e-posta denendi. ${retry} sonrasında tekrar deneyebilirsin.`
        : 'Çok sayıda farklı e-posta denendi. Lütfen daha sonra tekrar dene.';
    case PARENT_INVITATION_ERROR_CODES.resendLimited:
      return retry
        ? `Bu e-postaya davet yakın zamanda gönderildi. ${retry} sonrasında tekrar gönderebilirsin.`
        : 'Bu e-postaya yeniden gönderim hakkı doldu.';
    case PARENT_INVITATION_ERROR_CODES.unavailable:
      return 'Veli daveti şu anda gönderilemedi. Lütfen birkaç dakika sonra tekrar dene.';
    case PARENT_INVITATION_ERROR_CODES.busy:
      return 'Veliniz şu anda hesabını etkinleştiriyor, biraz sonra tekrar deneyin.';
    default:
      return body?.message?.trim() || fallback;
  }
}
