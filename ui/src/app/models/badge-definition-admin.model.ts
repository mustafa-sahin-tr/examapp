/**
 * Issue #148 — Rozet tanımları admin yönetimi. BadgeService `BadgeDefinitionsAdminController`
 * (`Services/BadgeService/Models/BadgeDefinitionAdminDtos.cs`, `Services/BadgeRuleTypeCatalog.cs`)
 * sözleşmesiyle birebir eşleşir; gateway üzerinden `/api/badge/admin/badge-definitions`.
 */

/** `BadgeDefinitionAdminDto`. */
export interface BadgeDefinitionAdmin {
  id: string;
  code: string;
  name: string;
  description: string;
  /** Eski SVG yolu (`achievements/<dosya>.svg`); #149 geçişi süresince yedek, admin UI'dan düzenlenmez. */
  iconUrl: string | null;
  /** Issue #149 — allowlist'teki Material Symbols adı (`GET .../icons`); null = ikon seçilmemiş. */
  icon: string | null;
  category: string;
  ruleType: string;
  ruleConfigJson: string;
  pathKey: string | null;
  pathName: string | null;
  pathOrder: number | null;
  isActive: boolean;
  /** Keycloak `sub` — kalıcı denetim kimliği (görüntüleme için `createdByName` tercih edilir). */
  createdBy: string | null;
  /** `preferred_username` (best-effort, yalnız görüntüleme). */
  createdByName: string | null;
  createdAtUtc: string;
  updatedBy: string | null;
  updatedByName: string | null;
  updatedAtUtc: string | null;
}

/** `BadgeDefinitionAdminListResponse` — `GET ?includeInactive&skip&take` sayfalı zarfı. */
export interface BadgeDefinitionAdminListResponse {
  items: BadgeDefinitionAdmin[];
  totalCount: number;
}

/** Sunucu `take`'i 1..200'e sıkıştırır (varsayılan 50). */
export const BADGE_DEFINITION_MAX_PAGE_SIZE = 200;
export const BADGE_DEFINITION_DEFAULT_PAGE_SIZE = 50;

/** `UpdateBadgeDefinitionRequest` — Code değiştirilemez, IsActive yalnız activate/deactivate ile değişir. */
export interface UpdateBadgeDefinitionRequest {
  name: string;
  description: string;
  /**
   * Eski SVG yolu. PUT'ta tam üzerine yazılır: düzenlemede mevcut değer AYNEN geri gönderilir (UI düzenletmez),
   * oluşturmada null.
   */
  iconUrl: string | null;
  /**
   * Issue #149 — HER ZAMAN gönderilir: seçili ikon adı ya da null (temizle). Backend PUT'ta alan yoksa eski ikonu
   * korur; UI bu "alan yok" davranışına güvenmez.
   */
  icon: string | null;
  category: string;
  ruleType: string;
  /** RuleType şemasına uyan ham JSON nesnesi (bkz. `GET .../rule-types`). */
  ruleConfigJson: string;
  pathKey: string | null;
  pathName: string | null;
  pathOrder: number | null;
}

/** `CreateBadgeDefinitionRequest`. */
export interface CreateBadgeDefinitionRequest extends UpdateBadgeDefinitionRequest {
  code: string;
}

/** `RuleTypeFieldSchema` — `type` bugün "integer" veya "string"; `allowedValues` ileride kapalı kümeler için. */
export interface BadgeRuleFieldSchema {
  name: string;
  type: string;
  required: boolean;
  min: number | null;
  max: number | null;
  allowedValues: string[] | null;
}

/** `RuleTypeSchema` — `GET /api/badge/admin/badge-definitions/rule-types` eleman tipi. */
export interface BadgeRuleTypeSchema {
  ruleType: string;
  description: string;
  fields: BadgeRuleFieldSchema[];
}

/** ASP.NET `ValidationProblemDetails` (400) ve 409 gövdesinin ortak kısmı: alan → mesajlar. */
export interface BadgeDefinitionErrorBody {
  errors?: Record<string, string[]>;
}

/**
 * Seeder'ın (`Services/BadgeService/Data/BadgeSeeder.cs`) kullandığı kategoriler. Backend kategoriyi
 * serbest metin kabul eder; bu liste yalnız öneri (autocomplete) içindir.
 */
export const KNOWN_BADGE_CATEGORIES: readonly string[] = [
  'Çözüm',
  'Performans',
  'Çalışma Süresi',
  'Ders Bazlı',
  'Streak',
  'Aktivite',
];

/**
 * Eski `iconUrl` biçimi (backend `BadgeIconValidator.IsValid`): yalnız rozet medalyonunun geçiş süresince yedek
 * SVG'yi güvenle göstermesi için (`resolveBadgeIcon`). Admin UI artık serbest metin ikon yolu almaz.
 */
export const BADGE_ICON_PATTERN = /^achievements\/[A-Za-z0-9._-]+\.svg$/;

/**
 * Geçerli eski SVG yolu (baştaki `/` atılmış, backend biçiminde) ya da null. Desene uymayan değer "eski SVG" sayılmaz:
 * medalyon onu göstermez (`resolveBadgeIcon`), admin kaydı da onu geri göndermez (400 ile kilitlenmesin).
 */
export function validLegacyIconUrl(iconUrl: string | null | undefined): string | null {
  const path = iconUrl?.trim().replace(/^\/+/, '');
  return path && BADGE_ICON_PATTERN.test(path) ? path : null;
}

/** Backend `BadgeDefinitionAdminService.CodePattern` ile aynı: küçük harf/rakam, segmentler tek tireyle. */
export const BADGE_CODE_PATTERN = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

/** Backend `BadgeDefinitionAdminService` alan sınırları (400 ile aynı kurallar). */
export const BADGE_DEFINITION_LIMITS = {
  codeMax: 64,
  nameMax: 100,
  descriptionMax: 500,
  categoryMax: 100,
  pathKeyMax: 100,
  pathNameMax: 100,
  pathOrderMin: 1,
  pathOrderMax: 1000,
} as const;

/** Issue #149 — `GET /api/badge/admin/badge-definitions/icons` eleman tipi (`BadgeIconEntry`). */
export interface BadgeIconEntry {
  /** Material Symbols adı. */
  name: string;
  /** Kararlı anahtar; etiketi UI çevirir. Bilinmeyen değer `other` sayılır. */
  category: string;
}

/** Backend `BadgeIconCatalog.Categories` — seçicideki çiplerin sırası. */
export const BADGE_ICON_CATEGORIES = ['achievement', 'learning', 'time', 'streak', 'other'] as const;
export type BadgeIconCategory = (typeof BADGE_ICON_CATEGORIES)[number];
