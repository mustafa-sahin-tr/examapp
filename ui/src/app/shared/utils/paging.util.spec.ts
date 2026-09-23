import { DEFAULT_PAGE_SIZE, MAX_PAGE, lastPageIndex, parsePageIndex, parsePageSize, parsePositiveInt } from './paging.util';

describe('paging.util', () => {
  it('parsePositiveInt_InvalidOrOutOfRange_ReturnsFallback', () => {
    expect(parsePositiveInt(null, 7)).toBe(7);
    expect(parsePositiveInt('', 7)).toBe(7);
    expect(parsePositiveInt('abc', 7)).toBe(7);
    expect(parsePositiveInt('0', 7)).toBe(7);
    expect(parsePositiveInt('-2', 7)).toBe(7);
    expect(parsePositiveInt('1.5', 7)).toBe(7);
    expect(parsePositiveInt(String(MAX_PAGE + 1), 7)).toBe(7);
    expect(parsePositiveInt(String(MAX_PAGE), 7)).toBe(MAX_PAGE);
    expect(parsePositiveInt('3', 7)).toBe(3);
  });

  it('parsePageIndex_ConvertsOneBasedToZeroBased', () => {
    expect(parsePageIndex('3')).toBe(2);
    expect(parsePageIndex(null)).toBe(0);
  });

  it('parsePageSize_OnlyAllowsOptions', () => {
    expect(parsePageSize('50')).toBe(50);
    expect(parsePageSize('37')).toBe(DEFAULT_PAGE_SIZE);
    expect(parsePageSize('500')).toBe(DEFAULT_PAGE_SIZE);
  });

  it('lastPageIndex_ComputesZeroBasedLastPage', () => {
    expect(lastPageIndex(45, 20)).toBe(2);
    expect(lastPageIndex(40, 20)).toBe(1);
    expect(lastPageIndex(0, 20)).toBe(0);
  });
});
