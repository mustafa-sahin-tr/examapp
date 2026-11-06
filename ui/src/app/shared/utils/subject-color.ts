/**
 * Ders adına göre renk (styles.scss `--ms-subject-*`, beyaz yazıyla AA). Planım takvimi (#725) ve test kartı
 * kapağı (#834) aynı haritayı kullanır; eşleşme büyük/küçük harf duyarsız "içerir" ile yapılır.
 */
export const SUBJECT_COLORS: Readonly<Record<string, string>> = {
  Matematik: 'var(--ms-subject-blue)',
  Fizik: 'var(--ms-subject-red)',
  Kimya: 'var(--ms-subject-green)',
  Biyoloji: 'var(--ms-subject-amber)',
  Türkçe: 'var(--ms-subject-purple)',
  Tarih: 'var(--ms-subject-pink)',
  Coğrafya: 'var(--ms-subject-teal)',
  İngilizce: 'var(--ms-subject-orange)',
};

export const DEFAULT_SUBJECT_COLOR = 'var(--ms-subject-default)';

export function subjectColorFor(text: string | null | undefined): string {
  const haystack = (text ?? '').toLocaleLowerCase('tr');
  if (!haystack) {
    return DEFAULT_SUBJECT_COLOR;
  }
  for (const [subject, color] of Object.entries(SUBJECT_COLORS)) {
    if (haystack.includes(subject.toLocaleLowerCase('tr'))) {
      return color;
    }
  }
  return DEFAULT_SUBJECT_COLOR;
}
