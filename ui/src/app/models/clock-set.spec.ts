// Issue #634: saat kurma modeli — plan / çözüm / cevap ayrıştırma, payload, kontrol kuralı (backend ile aynı), el yuvarlama.
import {
  buildClockSetPayload,
  clockMatches,
  formatClockTime,
  handsFor,
  isBlankClockSetPayload,
  parseClockSetAnswer,
  parseClockSetFeedback,
  parseClockSetPlan,
  parseClockSetSolution,
  parseClockTime,
  snapHour,
  snapMinute,
  turnHour,
  turnMinute,
} from './clock-set';

const PLAN = JSON.stringify({
  version: 1,
  type: 'clockSet',
  step: 5,
  clocks: [{ id: 'c0', label: 'Başlangıç', fixed: '09:20' }, { id: 'c1', label: 'Bitiş' }],
});

describe('clock-set model (#634)', () => {
  it('parses times on a 12-hour dial', () => {
    expect(parseClockTime('03:45')).toBe(225);
    expect(parseClockTime('15:45')).toBe(225);
    expect(parseClockTime('00:00')).toBe(0);
    expect(parseClockTime('3:45')).toBeNull();
    expect(parseClockTime('24:00')).toBeNull();
    expect(formatClockTime(225)).toBe('03:45');
    expect(formatClockTime(5)).toBe('12:05');
    expect(handsFor(225)).toEqual({ h: 225, m: 45 });
  });

  it('parses the plan; defaults and invalid plans', () => {
    const plan = parseClockSetPlan(PLAN)!;
    expect(plan.step).toBe(5);
    expect(plan.numbers).toBe(true);
    expect(plan.minuteTicks).toBe(true);
    expect(plan.clocks).toEqual([
      { id: 'c0', label: 'Başlangıç', fixed: 560 },
      { id: 'c1', label: 'Bitiş', fixed: null },
    ]);
    expect(parseClockSetPlan(JSON.stringify({ type: 'clockSet', clocks: [{ id: 'c1', fixed: '01:00' }] }))).toBeNull();
    expect(parseClockSetPlan(JSON.stringify({ type: 'balanceScale', clocks: [{ id: 'c1' }] }))).toBeNull();
    expect(parseClockSetPlan('{')).toBeNull();
  });

  it('parses solution, answer, feedback; blank payload', () => {
    const plan = parseClockSetPlan(PLAN);
    expect(parseClockSetSolution(JSON.stringify({ times: { c1: '10:00' } }))).toEqual({ c1: 600 });
    const payload = buildClockSetPayload({ c1: { h: 600, m: 0 } });
    expect(parseClockSetAnswer(payload, plan)).toEqual({ c1: { h: 600, m: 0 } });
    // Sabit saat, adım dışı yelkovan ve aralık dışı akrep atlanır.
    const bad = JSON.stringify({ type: 'clockSet', clocks: { c0: { h: 0, m: 0 }, c1: { h: 0, m: 3 } } });
    expect(parseClockSetAnswer(bad, plan)).toEqual({});
    expect(isBlankClockSetPayload(buildClockSetPayload({}))).toBe(true);
    expect(isBlankClockSetPayload(payload)).toBe(false);
    expect(parseClockSetFeedback(JSON.stringify({ rows: [{ id: 'c1', correct: true }] }))).toEqual({ c1: true });
  });

  it('matches like the backend: exact minute, hour hand within ±15 units, wraps at 12', () => {
    expect(clockMatches({ h: 225, m: 45 }, 225)).toBe(true);
    expect(clockMatches({ h: 240, m: 45 }, 225)).toBe(true);
    expect(clockMatches({ h: 241, m: 45 }, 225)).toBe(false);
    expect(clockMatches({ h: 180, m: 45 }, 225)).toBe(false);
    expect(clockMatches({ h: 225, m: 40 }, 225)).toBe(false);
    expect(clockMatches({ h: 710, m: 0 }, 0)).toBe(true);
  });

  it('snaps and turns hands', () => {
    expect(snapHour(112.5)).toBe(225); // 3.45 konumu
    expect(snapHour(359)).toBe(0);
    expect(snapMinute(270, 5)).toBe(45);
    expect(snapMinute(268, 15)).toBe(45);
    expect(snapMinute(359, 5)).toBe(0);
    expect(turnHour(0, -1)).toBe(715);
    expect(turnHour(715, 1)).toBe(0);
    expect(turnMinute(55, 1, 5)).toBe(0);
    expect(turnMinute(0, -3, 15)).toBe(15);
  });
});
