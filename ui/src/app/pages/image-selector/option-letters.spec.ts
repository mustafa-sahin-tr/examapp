import { moveOption, normalizeOptionLetter, orderByLetters } from './option-letters';

describe('option-letters (#1183)', () => {
  it('normalizeOptionLetter_KeepsOnlyALetterFromAToE', () => {
    expect(['A)', ' b. ', '(C)', 'E', 'F)', 'Şık 1', '', null].map(normalizeOptionLetter)).toEqual([
      'A',
      'B',
      'C',
      'E',
      null,
      null,
      null,
      null,
    ]);
  });

  it('orderByLetters_CompleteSet_OrdersByLetter', () => {
    expect(orderByLetters(['d', 'a', 'b', 'c'], (x) => x)).toEqual(['a', 'b', 'c', 'd']);
  });

  it('orderByLetters_RepeatedMissingOrGappedLetters_ReturnsNull', () => {
    expect(orderByLetters(['a', 'a', 'c'], (x) => x)).toBeNull();
    expect(orderByLetters(['a', null, 'c'], (x) => x)).toBeNull();
    expect(orderByLetters(['a', 'c'], (x) => x)).toBeNull();
    expect(orderByLetters([], (x) => x)).toBeNull();
  });

  it('moveOption_MovesTheItemAndShiftsTheOthers', () => {
    expect(moveOption(['A', 'B', 'C', 'D'], 3, 1)).toEqual(['A', 'D', 'B', 'C']);
    expect(moveOption(['A', 'B', 'C', 'D'], 0, 2)).toEqual(['B', 'C', 'A', 'D']);
  });
});
