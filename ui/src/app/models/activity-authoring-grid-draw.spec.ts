// Issue #588 (dilim c): kareli zeminde çizim formu — doğrulama (zemin, kural, sığan boyut, "hepsi" sınırı), plan + çözüm, geri kurma.
import {
  buildGridDrawDefinition,
  emptyGridDrawForm,
  gridDrawFormTargets,
  parseGridDrawDefinition,
  validateGridDrawForm,
} from './activity-authoring-grid-draw';

describe('activity-authoring-grid-draw (#588)', () => {
  const keys = (form: ReturnType<typeof emptyGridDrawForm>) => validateGridDrawForm(form).map((e) => e.key);

  it('pilot: çevresi 14 olan bütün dikdörtgenler', () => {
    const form = { ...emptyGridDrawForm(), perimeter: '14' };
    expect(keys(form)).toEqual([]);
    expect(gridDrawFormTargets(form).map((t) => `${t.short}x${t.long}`)).toEqual(['1x6', '2x5', '3x4']);
    const def = buildGridDrawDefinition(form);
    expect(def.interactionType).toBe('gridDraw');
    expect(JSON.parse(def.plan)).toEqual({ version: 1, type: 'gridDraw', rows: 10, cols: 16 });
    expect(JSON.parse(def.solution)).toEqual({ perimeter: 14, all: true });
    expect(parseGridDrawDefinition(def.plan, def.solution)).toEqual(form);
  });

  it('noktalı zemin, kare, tek çizim', () => {
    const form = { ...emptyGridDrawForm(), grid: 'dot' as const, shape: 'square' as const, perimeter: '20', all: false };
    const def = buildGridDrawDefinition(form);
    expect(JSON.parse(def.plan)).toEqual({ version: 1, type: 'gridDraw', rows: 10, cols: 16, grid: 'dot', shape: 'square' });
    expect(JSON.parse(def.solution)).toEqual({ perimeter: 20, all: false });
    expect(parseGridDrawDefinition(def.plan, def.solution)).toEqual(form);
  });

  it('hataları bulur', () => {
    expect(keys({ ...emptyGridDrawForm(), rows: 1 })).toEqual(['drawSize']);
    expect(keys(emptyGridDrawForm())).toEqual(['drawRuleMissing']);
    expect(keys({ ...emptyGridDrawForm(), area: '2,5' })).toEqual(['drawRuleInvalid']);
    expect(keys({ ...emptyGridDrawForm(), area: '12', perimeter: '12' })).toEqual(['drawNoTarget']);
    // 1×17 sığmaz (16 kolon).
    expect(keys({ ...emptyGridDrawForm(), area: '17' })).toEqual(['drawNoTarget']);
    // 30×30 zeminde çevre 60: 1×29 … 15×15 → 15 boyut.
    expect(keys({ ...emptyGridDrawForm(), rows: 30, cols: 30, perimeter: '60' })).toEqual(['drawTooManyTargets']);
    expect(keys({ ...emptyGridDrawForm(), rows: 30, cols: 30, perimeter: '60', all: false })).toEqual([]);
  });
});
