import { DEFAULT_SUBJECT_COLOR, subjectColorFor } from './subject-color';

describe('subjectColorFor', () => {
  it('matchesSubjectNameCaseInsensitively_issue834', () => {
    expect(subjectColorFor('Matematik')).toBe('var(--ms-subject-blue)');
    expect(subjectColorFor('5. sınıf türkçe tekrar')).toBe('var(--ms-subject-purple)');
    expect(subjectColorFor('İNGİLİZCE')).toBe('var(--ms-subject-orange)');
  });

  it('fallsBackToDefaultColor_issue834', () => {
    expect(subjectColorFor('Felsefe')).toBe(DEFAULT_SUBJECT_COLOR);
    expect(subjectColorFor('')).toBe(DEFAULT_SUBJECT_COLOR);
    expect(subjectColorFor(null)).toBe(DEFAULT_SUBJECT_COLOR);
  });
});
