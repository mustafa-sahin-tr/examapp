import { ProgramStep } from '../../models/programstep';
import {
  isEndAfterStart,
  toLocalDateString,
  isExclusiveOption,
  reachableFrom,
  remainingRange,
  resolveNext,
  toggleValue,
} from './program-wizard.logic';

function step(id: number, multiple: boolean, options: ProgramStep['options']): ProgramStep {
  return { id, title: `S${id}`, description: `S${id}`, multiple, actions: [], options };
}

describe('program-wizard.logic (issue #135)', () => {
  const graph = new Map<number, ProgramStep>([
    [1, step(1, false, [{ label: 'a', value: 'a', nextStep: 2 }, { label: 'b', value: 'b', nextStep: 3 }])],
    [2, step(2, false, [{ label: 'x', value: 'x', nextStep: 4 }])],
    [3, step(3, false, [{ label: 'y', value: 'y' }])],
    [4, step(4, false, [{ label: 'z', value: 'z' }])],
  ]);

  it('resolveNext_FirstSelectedInOptionOrder_NullToForm_DanglingToInvalid', () => {
    expect(resolveNext(graph, graph.get(1)!, ['b', 'a'])).toEqual({ kind: 'step', stepId: 2 });
    expect(resolveNext(graph, graph.get(3)!, ['y'])).toEqual({ kind: 'form' });
    const dangling = step(9, false, [{ label: 'q', value: 'q', nextStep: 99 }]);
    expect(resolveNext(graph, dangling, ['q'])).toEqual({ kind: 'invalid', nextStep: 99 });
  });

  it('reachableFrom_FollowsEveryOption', () => {
    expect([...reachableFrom(graph, 1)].sort()).toEqual([1, 2, 3, 4]);
    expect([...reachableFrom(graph, 2)].sort()).toEqual([2, 4]);
  });

  it('remainingRange_UnequalBranches_MinMaxThenExactAfterAnswer', () => {
    expect(remainingRange(graph, 1, new Map())).toEqual({ min: 1, max: 2 });
    expect(remainingRange(graph, 1, new Map([[1, ['b']]]))).toEqual({ min: 1, max: 1 });
    expect(remainingRange(graph, 1, new Map([[1, ['a']]]))).toEqual({ min: 2, max: 2 });
  });

  it('remainingRange_Cycle_Terminates', () => {
    const cyclic = new Map<number, ProgramStep>([
      [1, step(1, false, [{ label: 'a', value: 'a', nextStep: 2 }])],
      [2, step(2, false, [{ label: 'b', value: 'b', nextStep: 1 }])],
    ]);
    expect(remainingRange(cyclic, 1, new Map())).toEqual({ min: 1, max: 1 });
  });

  describe('Yok dışlaması (backend ProgramService.cs eşlemesi)', () => {
    const days = step(6, true, [
      { label: 'Pazartesi', value: '1' },
      { label: 'Salı', value: '2' },
      { label: 'Yok', value: '8' },
    ]);

    it('mappedStep_UsesValueNotLabel', () => {
      expect(isExclusiveOption(days, days.options[2])).toBeTrue();
      // Eşlemedeki adımda etiket belirleyici değil.
      expect(isExclusiveOption(days, { label: 'Yok', value: '3' })).toBeFalse();
      const subjects = step(7, true, [{ label: 'Hiçbiri', value: '5' }]);
      expect(isExclusiveOption(subjects, subjects.options[0])).toBeTrue();
    });

    it('unmappedStep_FallsBackToLabel', () => {
      const other = step(42, true, [{ label: 'Yok', value: 'n' }, { label: 'None', value: 'm' }, { label: 'X', value: 'x' }]);
      expect(isExclusiveOption(other, other.options[0])).toBeTrue();
      expect(isExclusiveOption(other, other.options[1])).toBeTrue();
      expect(isExclusiveOption(other, other.options[2])).toBeFalse();
    });

    it('toggleValue_ExclusiveClearsOthersAndViceVersa_KeepsOptionOrder', () => {
      expect(toggleValue(days, ['2'], days.options[0])).toEqual(['1', '2']);
      expect(toggleValue(days, ['1', '2'], days.options[2])).toEqual(['8']);
      expect(toggleValue(days, ['8'], days.options[1])).toEqual(['2']);
      expect(toggleValue(days, ['1', '2'], days.options[0])).toEqual(['2']);
    });

    it('toggleValue_Single_Replaces', () => {
      expect(toggleValue(graph.get(1)!, ['a'], graph.get(1)!.options[1])).toEqual(['b']);
    });
  });

  describe('isEndAfterStart', () => {
    it('comparesCalendarDays_IgnoringTimeOfDay', () => {
      const start = new Date(2026, 9, 6, 15, 30);
      expect(isEndAfterStart(start, new Date(2026, 9, 7))).toBeTrue();
      expect(isEndAfterStart(start, new Date(2026, 9, 6, 23, 59))).toBeFalse();
      expect(isEndAfterStart(start, new Date(2026, 9, 6))).toBeFalse();
      expect(isEndAfterStart(start, new Date(2026, 9, 5))).toBeFalse();
    });

    it('toLocalDateString_UsesLocalCalendarDay', () => {
      expect(toLocalDateString(new Date(2026, 9, 6))).toBe('2026-10-06');
      expect(toLocalDateString(new Date(2026, 0, 1, 23, 59))).toBe('2026-01-01');
    });
  });
});
