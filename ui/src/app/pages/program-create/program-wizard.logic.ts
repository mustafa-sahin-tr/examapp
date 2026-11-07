/**
 * Program oluşturma sihirbazının saf kuralları. #885: dört sabit adım (Ne kadar? · Hangi günler? · Hangi dersler? · Ne
 * zamana kadar?) ve canlı hafta önizlemesi. Gönderilen `userSelections` biçimi sunucunun `ProgramService.ParseUserSelections`
 * sözleşmesiyle aynıdır (adım 1 tür, 2 süre, 3 günlük soru, 5 günlük ders, 6 dinlenme günleri; "8" = yok).
 */

import { UserSelection } from '../../models/program.interfaces';

export type StudyType = 'time' | 'question';

/** Süreli çalışmada "çalış-ara" dakikaları (sunucu ilk sayıyı ders süresi olarak okur). */
export const STUDY_DURATIONS = ['25-5', '30-10', '40-10', '50-10'] as const;
/** Soru takipli çalışmada günde toplam soru (sunucu günün derslerine böler). */
export const QUESTIONS_PER_DAY = [12, 24, 36] as const;
export const SUBJECTS_PER_DAY = [1, 2, 3] as const;

/** ISO gün numaraları: 1 = Pazartesi … 7 = Pazar (sunucunun dinlenme günü değerleri). */
export const WEEK_DAYS = [1, 2, 3, 4, 5, 6, 7] as const;

/** Sunucunun "dinlenme günü yok" değeri (6. adım). */
const NO_REST_DAY = '8';

export interface WizardChoices {
  studyType: StudyType;
  duration: string;
  questionsPerDay: number;
  subjectsPerDay: number;
  /** Çalışılan günler (ISO). */
  studyDays: readonly number[];
}

export interface PreviewSubject {
  id: number;
  name: string;
}

export interface PreviewItem {
  subject: string;
  /** Süreli çalışmada dakika, soru takipliyse soru sayısı. */
  amount: number;
}

export interface PreviewDay {
  date: Date;
  /** ISO gün numarası. */
  weekday: number;
  /** Boşsa dinlenme günü. */
  items: PreviewItem[];
}

/** Sunucuya giden seçimler (eski sihirbaz adım id'leriyle). */
export function toUserSelections(choices: WizardChoices): UserSelection[] {
  const restDays = WEEK_DAYS.filter((d) => !choices.studyDays.includes(d)).map(String);
  return [
    { stepId: 1, selectedValues: [choices.studyType] },
    choices.studyType === 'time'
      ? { stepId: 2, selectedValues: [choices.duration] }
      : { stepId: 3, selectedValues: [String(choices.questionsPerDay)] },
    { stepId: 5, selectedValues: [String(choices.subjectsPerDay)] },
    { stepId: 6, selectedValues: restDays.length > 0 ? restDays : [NO_REST_DAY] },
  ];
}

/** Zorlandığı dersin takvimdeki ağırlığı (sunucu `ProgramService.DifficultSubjectWeight`). */
export const DIFFICULT_SUBJECT_WEIGHT = 2;

/**
 * Günün dersleri — sunucu `ProgramService.PickDailySubjects` ile aynı kural: ağırlığına göre en az planlanmış `count`
 * ders (planlanan / ağırlık); eşitlikte ağırlığı büyük olan, sonra sıra (ders adı). Sonuç sıra düzeninde.
 */
export function pickDailySubjects(planned: readonly number[], weights: readonly number[], count: number): number[] {
  return planned
    .map((_, i) => i)
    .sort((a, b) => planned[a] / weights[a] - planned[b] / weights[b] || weights[b] - weights[a] || a - b)
    .slice(0, count)
    .sort((a, b) => a - b);
}

/**
 * Başlangıçtan itibaren `days` günün önizlemesi; sunucunun takvim üretimiyle aynı dağılım. `subjects` sunucunun sıralamasıyla
 * (ders adı) gelir.
 */
export function buildPreview(
  choices: WizardChoices,
  subjects: readonly PreviewSubject[],
  difficultIds: ReadonlySet<number>,
  start: Date,
  days = 7,
): PreviewDay[] {
  const weights = subjects.map((s) => (difficultIds.has(s.id) ? DIFFICULT_SUBJECT_WEIGHT : 1));
  const planned = subjects.map(() => 0);
  const perDay = Math.min(Math.max(choices.subjectsPerDay, 1), subjects.length);
  const minutes = Number(choices.duration.split('-')[0]) || 0;
  const questions = perDay > 0 ? Math.floor(choices.questionsPerDay / perDay) : 0;

  const result: PreviewDay[] = [];
  for (let offset = 0; offset < days; offset++) {
    const date = new Date(start.getFullYear(), start.getMonth(), start.getDate() + offset);
    const weekday = date.getDay() === 0 ? 7 : date.getDay();
    const items: PreviewItem[] = [];
    if (choices.studyDays.includes(weekday) && perDay > 0) {
      for (const index of pickDailySubjects(planned, weights, perDay)) {
        planned[index]++;
        items.push({ subject: subjects[index].name, amount: choices.studyType === 'time' ? minutes : questions });
      }
    }
    result.push({ date, weekday, items });
  }
  return result;
}

/**
 * Son adım tarih kuralı: bitiş, başlangıçtan sonraki bir takvim gününde olmalı (bitiş <= başlangıç geçersiz).
 * Gün bazında karşılaştırılır; datepicker gece yarısı, varsayılan başlangıç ise saatli "bugün" verir.
 */
export function isEndAfterStart(start: Date, end: Date): boolean {
  const day = (d: Date): number => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  return day(end) > day(start);
}

/** Yerel takvim günü `yyyy-MM-dd` (backend `DateTime.TryParse` + `.Date` ile aynı günü okur). */
export function toLocalDateString(date: Date): string {
  const pad = (n: number): string => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}
