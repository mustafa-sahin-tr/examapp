// Issue #587: boyama formu — hedef metni (kesir / ondalık / yüzde / sayı) → kare sayısı, doğrulama, plan + çözüm, düzenlemede geri kurma.
import {
  RegionPaintForm,
  buildRegionPaintDefinition,
  emptyRegionPaintForm,
  nextPaintColor,
  parseRegionPaintDefinition,
  targetCells,
  validateRegionPaintForm,
} from './activity-authoring-region-paint';

const PILOT: RegionPaintForm = {
  kind: 'regionPaint',
  rows: 10,
  cols: 10,
  entries: [
    { color: 'black', label: '2/5', target: '2/5' },
    { color: 'blue', label: '', target: '0,35' },
    { color: 'green', label: '', target: '%25' },
    { color: 'red', label: '', target: '' },
  ],
};

describe('activity-authoring-region-paint (#587)', () => {
  it('targetCells_Forms', () => {
    expect(targetCells('2/5', 100)).toBe(40);
    expect(targetCells('0,35', 100)).toBe(35);
    expect(targetCells('%25', 100)).toBe(25);
    expect(targetCells('25%', 100)).toBe(25);
    expect(targetCells('40', 100)).toBe(40);
    expect(targetCells('', 100)).toBe(0);
    expect(targetCells('1/3', 100)).toBeNull();
    expect(targetCells('120', 100)).toBeNull();
    expect(targetCells('-%5', 100)).toBeNull();
    expect(targetCells('5 cm', 100)).toBeNull();
    expect(targetCells('abc', 100)).toBeNull();
  });

  it('validate_PilotOk_AndErrors', () => {
    expect(validateRegionPaintForm(PILOT)).toEqual([]);
    const keys = (form: RegionPaintForm) => validateRegionPaintForm(form).map((e) => e.key);
    expect(keys({ ...PILOT, rows: 1 })).toContain('paintGridSize');
    expect(keys({ ...PILOT, entries: [...PILOT.entries, { color: 'red', label: '', target: '' }] })).toContain('paintColorDuplicate');
    expect(keys({ ...PILOT, entries: [{ color: 'blue', label: '', target: '1/3' }] })).toContain('paintTargetInvalid');
    expect(keys({ ...PILOT, entries: [{ color: 'blue', label: '', target: '' }] })).toContain('paintTargetMissing');
    expect(keys({ ...PILOT, entries: [{ color: 'blue', label: '', target: '%60' }, { color: 'red', label: '', target: '0,5' }] })).toContain(
      'paintTargetTooMany'
    );
  });

  it('build_AndParse_RoundTrip', () => {
    const def = buildRegionPaintDefinition(PILOT);
    expect(JSON.parse(def.plan)).toEqual({
      version: 1,
      type: 'regionPaint',
      mode: 'grid',
      rows: 10,
      cols: 10,
      palette: [
        { id: 'p1', color: 'black', label: '2/5' },
        { id: 'p2', color: 'blue' },
        { id: 'p3', color: 'green' },
        { id: 'p4', color: 'red' },
      ],
    });
    expect(JSON.parse(def.solution)).toEqual({ counts: { p1: 40, p2: 35, p3: 25, p4: 0 } });

    const form = parseRegionPaintDefinition(def.plan, def.solution)!;
    expect(form.entries.map((e) => e.target)).toEqual(['40', '35', '25', '']);
    expect(form.entries[0].label).toBe('2/5');
  });

  it('nextPaintColor_SkipsUsed', () => {
    expect(nextPaintColor(emptyRegionPaintForm())).toBe('red');
  });
});
