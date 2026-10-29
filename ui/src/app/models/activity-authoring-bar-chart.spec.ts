// Issue #588 (dilim b): grafik kurma formu — doğrulama, plan + çözüm üretimi, düzenlemede geri kurma.
import {
  BarChartForm,
  axisErrors,
  buildBarChartDefinition,
  categoryValue,
  emptyBarChartForm,
  parseBarChartDefinition,
  validateBarChartForm,
} from './activity-authoring-bar-chart';
import { buildDefinition, emptyForm, parseDefinition, validateForm } from './activity-authoring';

function pilot(): BarChartForm {
  return {
    ...emptyBarChartForm(),
    title: 'Sevilen meyveler',
    categories: [
      { label: 'Elma', value: '6', fixed: true },
      { label: 'Armut', value: '4', fixed: false },
      { label: 'Muz', value: '7', fixed: false },
    ],
  };
}

const keys = (form: BarChartForm) => validateBarChartForm(form).map((e) => e.key);

describe('activity-authoring-bar-chart (#588)', () => {
  it('categoryValue', () => {
    expect(categoryValue('4', { max: 10, unit: 1 })).toBe(4);
    expect(categoryValue('', { max: 10, unit: 1 })).toBeUndefined();
    expect(categoryValue('11', { max: 10, unit: 1 })).toBeUndefined();
    expect(categoryValue('3', { max: 10, unit: 2 })).toBeUndefined();
  });

  it('axisErrors', () => {
    expect(axisErrors({ max: 10, step: 2, unit: 1 })).toEqual([]);
    expect(axisErrors({ max: 0, step: 1, unit: 1 })).toEqual(['chartMax']);
    expect(axisErrors({ max: 10, step: 3, unit: 1 })).toEqual(['chartStep']);
    expect(axisErrors({ max: 40, step: 1, unit: 1 })).toEqual(['chartStep']);
    expect(axisErrors({ max: 10, step: 2, unit: 3 })).toEqual(['chartUnit']);
  });

  it('pilotIsValidAndBuilds', () => {
    const form = pilot();
    expect(validateBarChartForm(form)).toEqual([]);
    const def = buildBarChartDefinition(form);
    expect(def.interactionType).toBe('barChart');
    expect(JSON.parse(def.plan)).toEqual({
      version: 1,
      type: 'barChart',
      title: 'Sevilen meyveler',
      max: 10,
      step: 2,
      unit: 1,
      categories: [
        { id: 'c1', label: 'Elma' },
        { id: 'c2', label: 'Armut' },
        { id: 'c3', label: 'Muz' },
      ],
      fixed: { c1: 6 },
    });
    expect(JSON.parse(def.solution)).toEqual({ values: { c2: 4, c3: 7 } });
  });

  it('omitsUnitWhenEqualToStepAndAddsLineStyle', () => {
    const def = buildBarChartDefinition({ ...pilot(), style: 'line', step: 2, unit: 2, categories: [{ label: 'A', value: '2', fixed: false }, { label: 'B', value: '4', fixed: false }] });
    const plan = JSON.parse(def.plan);
    expect(plan.unit).toBeUndefined();
    expect(plan.style).toBe('line');
    expect(plan.fixed).toBeUndefined();
  });

  it('validationErrors', () => {
    expect(keys({ ...pilot(), step: 3 })).toContain('chartStep');
    expect(keys({ ...pilot(), categories: [{ label: 'A', value: '1', fixed: false }] })).toContain('chartCategoryCount');
    expect(keys({ ...pilot(), categories: [{ label: '', value: '1', fixed: false }, { label: 'B', value: '1', fixed: false }] })).toContain('chartCategoryBlank');
    expect(keys({ ...pilot(), categories: [{ label: 'A', value: '12', fixed: false }, { label: 'B', value: '', fixed: false }] })).toContain('chartValueInvalid');
    expect(keys({ ...pilot(), categories: pilot().categories.map((c) => ({ ...c, fixed: true })) })).toContain('chartAllFixed');
  });

  it('roundTripsThroughDispatcher', () => {
    expect(emptyForm('barChart', ['D', 'Y']).kind).toBe('barChart');
    const form = pilot();
    expect(validateForm(form)).toEqual([]);
    const def = buildDefinition(form);
    expect(parseDefinition('barChart', def.plan, def.solution)).toEqual(form);
    expect(parseBarChartDefinition(def.plan, null)!.categories[1].value).toBe('');
  });
});
