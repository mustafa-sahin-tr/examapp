/** Issue #703: TYMM kavramsal beceri katmanı (`SkillCategory`, sayı olarak gelir). */
export enum SkillCategory {
  /** KB2 bütünleşik beceriler. */
  Integrated = 2,
  /** KB3 üst düzey düşünme becerileri. */
  HigherOrder = 3,
}

/** Issue #703: etiketlenebilir beceri. Ekran adı `code` ile çevrilir; `name` kılavuzdaki Türkçe addır. */
export interface Skill {
  id: number;
  code: string;
  category: SkillCategory;
  name: string;
}

/** Issue #703: sorunun bağlam temelli bayrağı ve beceri etiketleri. */
export interface QuestionContextTags {
  isContextBased: boolean;
  skills: Skill[];
}

/** En çok beceri sayısı (sunucu `SetQuestionContextTagsDto.MaxSkills`). */
export const MAX_QUESTION_SKILLS = 10;

/**
 * Çeviri anahtarı: `KB2.8` → `skills.KB2.8`. Sözlükte `skills.KB2` nesnesinin `8` anahtarıdır, nokta yol ayırıcısı olarak
 * çalışır.
 */
export function skillLabelKey(code: string): string {
  return `skills.${code}`;
}
