import { isSlotAnswerCorrect, normalizeSlotText, parseTrNumber, turkishLower } from './slot-normalizer';
import { SlotNumberSolution } from '../../models/slot-fill';

const num = (value: string, extra: Partial<SlotNumberSolution> = {}): SlotNumberSolution => ({
  value,
  equivalence: true,
  requireSimplest: false,
  units: [],
  unitMode: 'none',
  ...extra,
});

describe('slot normalizer (#575)', () => {
  it('parsesTurkishNumberForms', () => {
    expect(parseTrNumber('0,75')).toMatchObject({ num: 3n, den: 4n, form: 'decimal' });
    expect(parseTrNumber('1.250')).toMatchObject({ num: 1250n, den: 1n, form: 'integer' });
    expect(parseTrNumber('1.25')).toMatchObject({ num: 5n, den: 4n, form: 'decimal' });
    expect(parseTrNumber('2 tam 3/5')).toMatchObject({ num: 13n, den: 5n, form: 'mixed', simplest: true });
    expect(parseTrNumber('% 75')).toMatchObject({ num: 3n, den: 4n, form: 'percent' });
    expect(parseTrNumber('75%')).toMatchObject({ form: 'percent' });
    expect(parseTrNumber('5cm')).toMatchObject({ num: 5n, unit: 'cm' });
    expect(parseTrNumber('0.750')).toBeNull();
    expect(parseTrNumber('2 5/3')).toBeNull();
    expect(parseTrNumber('3/0')).toBeNull();
  });

  it('equivalenceToggle_ControlsFractionDecimalPercent', () => {
    for (const answer of ['3/4', '0,75', '%75', '6/8']) {
      expect(isSlotAnswerCorrect('number', num('3/4'), answer)).toBe(true);
    }
    expect(isSlotAnswerCorrect('number', num('3/4', { equivalence: false }), '0,75')).toBe(false);
    expect(isSlotAnswerCorrect('number', num('3/4', { equivalence: false }), '%75')).toBe(false);
    expect(isSlotAnswerCorrect('number', num('3/4', { equivalence: false }), '3/4')).toBe(true);
    expect(isSlotAnswerCorrect('number', num('3/4', { requireSimplest: true }), '6/8')).toBe(false);
  });

  it('units_NoneOptionalRequired', () => {
    expect(isSlotAnswerCorrect('number', num('5'), '5 cm')).toBe(false);
    expect(isSlotAnswerCorrect('number', num('5', { unitMode: 'optional', units: ['cm'] }), '5')).toBe(true);
    expect(isSlotAnswerCorrect('number', num('5', { unitMode: 'optional', units: ['cm'] }), '5 CM')).toBe(true);
    expect(isSlotAnswerCorrect('number', num('5', { unitMode: 'required', units: ['cm'] }), '5')).toBe(false);
    expect(isSlotAnswerCorrect('number', num('5', { unitMode: 'required', units: ['cm'] }), '5cm')).toBe(true);
    expect(isSlotAnswerCorrect('number', num('5', { unitMode: 'required', units: ['cm'] }), '5 m')).toBe(false);
  });

  it('turkishLetters_DotlessAndDottedI', () => {
    expect(turkishLower('İZMİR')).toBe('izmir');
    expect(turkishLower('IŞIK')).toBe('ışık');
    expect(isSlotAnswerCorrect('text', { accept: ['İzmir'], optionalSuffixes: [] }, '  izmir. ')).toBe(true);
    expect(isSlotAnswerCorrect('text', { accept: ['ılık'], optionalSuffixes: [] }, 'ilik')).toBe(false);
    expect(normalizeSlotText('Ege’nin   kıyısı!')).toBe("ege'nin kıyısı");
  });

  it('text_OptionalSuffix', () => {
    const solution = { accept: ['İç Anadolu'], optionalSuffixes: ['Bölgesi'] };
    expect(isSlotAnswerCorrect('text', solution, 'iç anadolu bölgesi')).toBe(true);
    expect(isSlotAnswerCorrect('text', solution, 'İç Anadolu')).toBe(true);
    expect(isSlotAnswerCorrect('text', solution, 'bölgesi')).toBe(false);
  });

  it('paletteAndSelect_ExactAfterWhitespace', () => {
    expect(isSlotAnswerCorrect('palette', { accept: ['∠ABC'] }, '∠ A B C')).toBe(true);
    expect(isSlotAnswerCorrect('palette', { accept: ['∠ABC'] }, '∠abc')).toBe(false);
    expect(isSlotAnswerCorrect('select', { optionIds: ['o2'] }, 'o2')).toBe(true);
    expect(isSlotAnswerCorrect('select', { optionIds: ['o2'] }, 'o1')).toBe(false);
    expect(isSlotAnswerCorrect('text', { accept: ['x'], optionalSuffixes: [] }, '   ')).toBe(false);
  });
});
