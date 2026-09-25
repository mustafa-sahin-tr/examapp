import { HttpErrorResponse } from '@angular/common/http';
import {
  BadgeDefinitionAdmin,
  BadgeDefinitionErrorBody,
  BadgeRuleFieldSchema,
  BadgeRuleTypeSchema,
} from '../../../models/badge-definition-admin.model';

/**
 * Issue #148 — rozet kural editörünün saf yardımcıları (şema → değer, değer → `ruleConfigJson`, liste özeti,
 * sunucu hata gövdesi). Komponentten ayrı tutulur ki birim testleri DOM'suz yazılabilsin.
 */

/** Ders seçimiyle birlikte doldurulan şema alanları: tek bir ders seçici ikisini de set eder. */
export const SUBJECT_ID_FIELD = 'subjectId';
export const SUBJECT_NAME_FIELD = 'subjectName';
const SUBJECT_FIELDS = new Set([SUBJECT_ID_FIELD, SUBJECT_NAME_FIELD]);

/** i18n'de etiketi/özeti olan RuleType'lar (`admin.badgeDefinitions.ruleTypes.<RuleType>`). */
export const TRANSLATED_RULE_TYPES: readonly string[] = [
  'AnswerCount',
  'CorrectStreak',
  'TotalStudyTimeMinutes',
  'TotalCorrectAnswers',
  'ActiveDays',
  'DailyStreak',
  'SubjectAnswerCount',
  'SubjectCorrectCount',
  'SubjectStudyTimeMinutes',
];

export type RuleFieldValue = number | string | null;

export function isSubjectField(field: BadgeRuleFieldSchema | string): boolean {
  return SUBJECT_FIELDS.has(typeof field === 'string' ? field : field.name);
}

export function isIntegerField(field: BadgeRuleFieldSchema): boolean {
  return field.type.toLowerCase() === 'integer';
}

/** Backend RuleType eşleşmesi büyük/küçük harf duyarsız; kanonik adı taşıyan şemayı döner. */
export function findRuleTypeSchema(
  schemas: readonly BadgeRuleTypeSchema[],
  ruleType: string | null | undefined
): BadgeRuleTypeSchema | null {
  const key = (ruleType ?? '').trim().toLowerCase();
  if (!key) return null;
  return schemas.find((s) => s.ruleType.toLowerCase() === key) ?? null;
}

/** `ruleConfigJson`'ı nesneye çevirir; geçersiz / nesne olmayan JSON → boş nesne. */
export function parseRuleConfig(json: string | null | undefined): Record<string, unknown> {
  if (!json) return {};
  try {
    const parsed: unknown = JSON.parse(json);
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : {};
  } catch {
    return {};
  }
}

/** Alanı büyük/küçük harf duyarsız okur (backend deserializer'ı da duyarsız). */
export function readConfigField(config: Record<string, unknown>, name: string): unknown {
  const key = Object.keys(config).find((k) => k.toLowerCase() === name.toLowerCase());
  return key === undefined ? undefined : config[key];
}

/**
 * Eski (legacy) hedef alan adları — `Services/BadgeService/Services/BadgeRuleEvaluator.cs` `TryReadInt` çağrılarıyla
 * birebir (RuleType başına). Backend migration'ı bunları `target`'a çevirecek; yine de okuma tarafında `target`
 * yoksa sırayla bu adlara bakılır ki eski satırlar listede ve düzenleme formunda doğru görünsün.
 */
const LEGACY_TARGET_ALIASES: Readonly<Record<string, readonly string[]>> = {
  answercount: ['count'],
  correctstreak: ['streak'],
  totalstudytimeminutes: ['minutes', 'targetMinutes'],
  totalcorrectanswers: ['count', 'correct'],
  subjectanswercount: ['count'],
  subjectcorrectcount: ['count', 'correct'],
  subjectstudytimeminutes: ['minutes', 'targetMinutes'],
  activedays: ['days'],
  dailystreak: ['days', 'streak'],
  studystreak: ['days', 'streak'],
  activitystreak: ['days', 'streak'],
};

/** `target`'ı okur; yoksa RuleType'ın legacy alias'larına düşer (evaluator ile aynı sıra). */
export function readTarget(ruleType: string, config: Record<string, unknown>): unknown {
  const direct = readConfigField(config, 'target');
  if (direct !== undefined && direct !== null) return direct;
  for (const alias of LEGACY_TARGET_ALIASES[ruleType.trim().toLowerCase()] ?? []) {
    const value = readConfigField(config, alias);
    if (value !== undefined && value !== null) return value;
  }
  return undefined;
}

/** Mevcut config'ten şema alanlarının form başlangıç değerlerini çıkarır (şemada olmayanlar atılır). */
export function ruleFieldValues(
  schema: BadgeRuleTypeSchema,
  config: Record<string, unknown>
): Record<string, RuleFieldValue> {
  const values: Record<string, RuleFieldValue> = {};
  for (const field of schema.fields) {
    const raw = field.name === 'target' ? readTarget(schema.ruleType, config) : readConfigField(config, field.name);
    if (isIntegerField(field)) {
      const num = typeof raw === 'number' ? raw : typeof raw === 'string' && raw.trim() ? Number(raw) : NaN;
      values[field.name] = Number.isFinite(num) ? num : null;
    } else {
      values[field.name] = typeof raw === 'string' ? raw : raw == null ? null : String(raw);
    }
  }
  return values;
}

/**
 * Formdaki değerlerden `ruleConfigJson` üretir: yalnız şemadaki alanlar, şema sırasıyla; boş alanlar yazılmaz
 * (backend bilinmeyen alanı reddeder, eksik opsiyonel alanı kabul eder). integer → sayı, string → kırpılmış metin.
 */
export function buildRuleConfigJson(schema: BadgeRuleTypeSchema, values: Record<string, unknown>): string {
  const config: Record<string, number | string> = {};
  for (const field of schema.fields) {
    const raw = values[field.name];
    if (raw === null || raw === undefined) continue;
    if (isIntegerField(field)) {
      const num = typeof raw === 'number' ? raw : typeof raw === 'string' && raw.trim() ? Number(raw) : NaN;
      if (Number.isFinite(num)) config[field.name] = num;
    } else {
      const text = String(raw).trim();
      if (text) config[field.name] = text;
    }
  }
  return JSON.stringify(config);
}

/** Liste satırındaki "kural özeti" için çeviri anahtarı + parametreler; bilinmeyen tipte `key` null. */
export interface RuleSummary {
  /** `TRANSLATED_RULE_TYPES` içindeki kanonik ad; yoksa null (ham gösterim). */
  key: string | null;
  params: { target: string; subject: string };
  /** Çevirisi olmayan tip için ham metin: `RuleType {json}`. */
  raw: string;
}

export function ruleSummary(dto: Pick<BadgeDefinitionAdmin, 'ruleType' | 'ruleConfigJson'>): RuleSummary {
  const config = parseRuleConfig(dto.ruleConfigJson);
  const target = readTarget(dto.ruleType, config);
  const subjectName = readConfigField(config, SUBJECT_NAME_FIELD);
  const subjectId = readConfigField(config, SUBJECT_ID_FIELD);
  const subject =
    typeof subjectName === 'string' && subjectName.trim()
      ? subjectName.trim()
      : subjectId != null && subjectId !== ''
        ? `#${String(subjectId)}`
        : '?';
  const key = TRANSLATED_RULE_TYPES.find((t) => t.toLowerCase() === dto.ruleType.trim().toLowerCase()) ?? null;
  return {
    key,
    params: { target: target == null ? '?' : String(target), subject },
    raw: `${dto.ruleType} ${dto.ruleConfigJson}`.trim(),
  };
}

/** Sunucunun 400/409 gövdesindeki `errors` sözlüğünü döner; yoksa null. */
export function serverFieldErrors(err: unknown): Record<string, string[]> | null {
  if (!(err instanceof HttpErrorResponse)) return null;
  const body = err.error as BadgeDefinitionErrorBody | null;
  const errors = body && typeof body === 'object' ? body.errors : undefined;
  if (!errors || typeof errors !== 'object') return null;
  const result: Record<string, string[]> = {};
  for (const [key, value] of Object.entries(errors)) {
    const messages = Array.isArray(value) ? value.filter((m): m is string => typeof m === 'string') : [];
    if (messages.length) result[normalizeErrorKey(key)] = messages;
  }
  return Object.keys(result).length ? result : null;
}

/**
 * ModelState anahtarları "Target", "$.pathOrder", "request.Code" gibi gelebilir; form kontrol adına
 * (camelCase, önek yok) indirger.
 */
export function normalizeErrorKey(key: string): string {
  const last = key.replace(/^\$\.?/, '').split('.').pop() ?? key;
  return last ? last.charAt(0).toLowerCase() + last.slice(1) : key;
}
