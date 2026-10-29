// Issue #634: saat kurmanın görselsiz formu — doğrulama, plan + çözüm üretme, düzenlemede geri kurma.
import {
  buildClockSetDefinition,
  emptyClockSetForm,
  normalizeClockText,
  parseClockSetDefinition,
  validateClockSetForm,
} from './activity-authoring-clock-set';

describe('activity-authoring-clock-set (#634)', () => {
  const form = () => ({
    ...emptyClockSetForm(),
    clocks: [
      { label: 'Başlangıç', time: '9.20', fixed: true },
      { label: 'Bitiş', time: '15:45', fixed: false },
    ],
  });

  it('normalizes time text', () => {
    expect(normalizeClockText('3.45')).toBe('03:45');
    expect(normalizeClockText(' 15:45 ')).toBe('15:45');
    expect(normalizeClockText('3:4')).toBeNull();
    expect(normalizeClockText('25:00')).toBeNull();
  });

  it('validates', () => {
    expect(validateClockSetForm(form())).toEqual([]);
    expect(validateClockSetForm(emptyClockSetForm()).map((e) => e.key)).toEqual(['clockTimeInvalid']);
    const f = form();
    f.clocks[1].time = '15:43';
    expect(validateClockSetForm(f).map((e) => e.key)).toEqual(['clockTimeStep']);
    f.clocks[0].time = '9.21'; // kurulu saat adımdan bağımsız
    f.clocks[1].fixed = true;
    expect(validateClockSetForm(f).map((e) => e.key)).toEqual(['clockNoFree']);
  });

  it('builds plan + solution and parses them back', () => {
    const def = buildClockSetDefinition({ ...form(), numbers: false });
    expect(JSON.parse(def.plan)).toEqual({
      version: 1,
      type: 'clockSet',
      step: 5,
      numbers: false,
      clocks: [{ id: 'c1', label: 'Başlangıç', fixed: '09:20' }, { id: 'c2', label: 'Bitiş' }],
    });
    expect(JSON.parse(def.solution)).toEqual({ times: { c2: '15:45' } });
    expect(parseClockSetDefinition(def.plan, def.solution)).toEqual({
      kind: 'clockSet',
      step: 5,
      numbers: false,
      minuteTicks: true,
      clocks: [
        { label: 'Başlangıç', time: '09:20', fixed: true },
        { label: 'Bitiş', time: '15:45', fixed: false },
      ],
    });
  });
});
