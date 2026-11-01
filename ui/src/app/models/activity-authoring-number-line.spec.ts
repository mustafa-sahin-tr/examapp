// Issue #588: sayı doğrusu formu — doğrulama, plan + çözüm üretimi, düzenlemede geri kurma.
import {
  NumberLineForm,
  buildNumberLineDefinition,
  emptyNumberLineForm,
  parseNumberLineDefinition,
  targetTick,
  validateNumberLineForm,
} from './activity-authoring-number-line';
import { buildDefinition, emptyForm, parseDefinition, validateForm } from './activity-authoring';

function pilot(): NumberLineForm {
  return {
    ...emptyNumberLineForm(),
    cards: [
      { label: '3/8', target: '3' },
      { label: '1/2', target: '4' },
      { label: '5/4', target: '' },
    ],
  };
}

const keys = (form: NumberLineForm) => validateNumberLineForm(form).map((e) => e.key);

describe('activity-authoring-number-line (#588)', () => {
  it('targetTick', () => {
    expect(targetTick('', 8)).toBeNull();
    expect(targetTick(' 3 ', 8)).toBe(3);
    expect(targetTick('9', 8)).toBeUndefined();
    expect(targetTick('1/2', 8)).toBeUndefined();
  });

  it('pilotIsValidAndBuilds', () => {
    const form = pilot();
    expect(validateNumberLineForm(form)).toEqual([]);
    const def = buildNumberLineDefinition(form);
    expect(def.interactionType).toBe('numberLine');
    expect(JSON.parse(def.plan)).toEqual({
      version: 1,
      type: 'numberLine',
      divisions: 8,
      labels: [
        { at: 0, text: '0' },
        { at: 8, text: '1' },
      ],
      cards: [
        { id: 'k1', label: '3/8' },
        { id: 'k2', label: '1/2' },
        { id: 'k3', label: '5/4' },
      ],
    });
    expect(JSON.parse(def.solution)).toEqual({ positions: { k1: 3, k2: 4, k3: null } });
  });

  it('writesToleranceAndHideTicksOnlyWhenSet', () => {
    const plan = JSON.parse(buildNumberLineDefinition({ ...pilot(), tolerance: 1, hideTicks: true }).plan);
    expect(plan.tolerance).toBe(1);
    expect(plan.hideTicks).toBe(true);
  });

  it('validationErrors', () => {
    expect(keys({ ...pilot(), divisions: 0 })).toContain('lineDivisions');
    expect(keys({ ...pilot(), tolerance: 8 })).toContain('lineTolerance');
    expect(keys({ ...pilot(), labels: [{ at: 9, text: 'x' }] })).toContain('lineLabelTick');
    expect(keys({ ...pilot(), labels: [{ at: 1, text: 'a' }, { at: 1, text: 'b' }] })).toContain('lineLabelDuplicate');
    expect(keys({ ...pilot(), labels: [{ at: 1, text: ' ' }] })).toContain('lineLabelBlank');
    expect(keys({ ...pilot(), cards: [{ label: '', target: '1' }] })).toContain('blankCard');
    expect(keys({ ...pilot(), cards: [{ label: 'a', target: '12' }] })).toContain('lineTargetInvalid');
    expect(keys({ ...pilot(), cards: [{ label: 'a', target: '' }] })).toContain('lineTargetMissing');
  });

  it('roundTripsThroughParse', () => {
    const form = { ...pilot(), tolerance: 1, hideTicks: true };
    const def = buildNumberLineDefinition(form);
    expect(parseNumberLineDefinition(def.plan, def.solution)).toEqual(form);
  });

  it('isWiredIntoAuthoring', () => {
    const form = emptyForm('numberLine', ['D', 'Y']);
    expect(form.kind).toBe('numberLine');
    expect(validateForm(form).map((e) => e.key)).toContain('lineCardCount');
    const def = buildDefinition(pilot());
    expect(parseDefinition('numberLine', def.plan, def.solution)).toEqual(pilot());
  });
});
