// Issue #632: terazinin görselsiz formu — doğrulama (değer, sınır, dengelenebilirlik), plan / çözüm üretimi ve geri okuma.
import {
  BalanceScaleForm,
  buildBalanceScaleDefinition,
  emptyBalanceScaleForm,
  parseBalanceScaleDefinition,
  validateBalanceScaleForm,
} from './activity-authoring-balance-scale';

const weights = (...values: string[]) => values.map((value) => ({ value, label: '' }));

describe('activity-authoring-balance-scale (#632)', () => {
  const pilot = (): BalanceScaleForm => ({ ...emptyBalanceScaleForm(), cards: weights('5', '9', '10', '14') });
  const keys = (form: BalanceScaleForm) => validateBalanceScaleForm(form).map((e) => e.key);

  it('validates_ValuesLimitsAndSolvability', () => {
    expect(keys(emptyBalanceScaleForm())).toEqual(['balanceValueInvalid']);
    expect(keys(pilot())).toEqual([]);
    expect(keys({ ...pilot(), cards: weights('5', '9', '10', '15') })).toEqual(['balanceUnsolvableAll']);
    expect(keys({ ...pilot(), cards: weights('5', '9', '10', '15'), useAll: false })).toEqual([]);
    expect(keys({ ...pilot(), cards: weights('5', '0') })).toEqual(['balanceValueInvalid']);
    expect(keys({ ...pilot(), cards: weights(...Array(13).fill('1')) })).toContain('balanceCardCount');
    expect(keys({ ...pilot(), left: weights(...Array(7).fill('1')) })).toContain('balanceFixedCount');
    expect(keys({ ...pilot(), unit: 'kilogramlar' })).toContain('balanceUnitTooLong');
  });

  it('buildsAndParsesDefinition', () => {
    const form: BalanceScaleForm = {
      ...pilot(),
      cards: [{ value: '3', label: '' }, { value: '4', label: ' (2+2) kg ' }, { value: '6', label: '' }],
      left: weights('7'),
      hideTilt: true,
    };
    const def = buildBalanceScaleDefinition(form);
    expect(JSON.parse(def.plan)).toEqual({
      version: 1,
      type: 'balanceScale',
      unit: 'kg',
      left: [{ id: 'l1', value: 7 }],
      cards: [
        { id: 'k1', value: 3 },
        { id: 'k2', value: 4, label: '(2+2) kg' },
        { id: 'k3', value: 6 },
      ],
      hideTilt: true,
    });
    expect(JSON.parse(def.solution)).toEqual({ useAll: true });
    const back = parseBalanceScaleDefinition(def.plan, def.solution)!;
    expect(back.cards.map((c) => c.value)).toEqual(['3', '4', '6']);
    expect(back.cards[1].label).toBe('(2+2) kg');
    expect(back.left).toEqual([{ value: '7', label: '' }]);
    expect(back.right).toEqual([]);
    expect(back.hideTilt).toBe(true);
    expect(back.useAll).toBe(true);
  });
});
