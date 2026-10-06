import { formatDateOnly, parseDateOnly } from './date-only.util';

/** Issue #386: program tarihleri gün olarak gösterilir; UTC gece yarısı yerel saatte önceki güne kaymaz. */
describe('date-only.util (issue #386)', () => {
  it('parseDateOnly_UtcMidnightIso_KeepsCalendarDayInLocalTime', () => {
    const date = parseDateOnly('2026-09-09T00:00:00Z');

    expect(date).not.toBeNull();
    expect(date!.getFullYear()).toBe(2026);
    expect(date!.getMonth()).toBe(8);
    expect(date!.getDate()).toBe(9);
    expect(date!.getHours()).toBe(0);
  });

  it('parseDateOnly_LateUtcTimeThatIsNextDayInTurkey_StillUsesServerDay', () => {
    // 21:30Z, TRT'de (UTC+3) ertesi gün 00:30'dur; tarih-yalnız alan olduğu için sunucudaki gün korunur.
    expect(parseDateOnly('2026-09-30T21:30:00Z')!.getDate()).toBe(30);
  });

  it('parseDateOnly_DateOnlyAndInvalidValues', () => {
    expect(parseDateOnly('2026-09-30')!.getDate()).toBe(30);
    expect(parseDateOnly('2026-02-31')).toBeNull();
    expect(parseDateOnly('not-a-date')).toBeNull();
    expect(parseDateOnly('')).toBeNull();
    expect(parseDateOnly(null)).toBeNull();
    expect(parseDateOnly(undefined)).toBeNull();
  });

  it('parseDateOnly_DiffersFromNaiveDateParsingWestOfUtc', () => {
    // Naif `new Date(iso)` UTC'nin batısında önceki günü verir; bizim sonucumuz saat diliminden bağımsız.
    const naive = new Date('2026-09-09T00:00:00Z');
    const expectedNaiveDay = naive.getTimezoneOffset() > 0 ? 8 : 9;
    expect(naive.getDate()).toBe(expectedNaiveDay);
    expect(parseDateOnly('2026-09-09T00:00:00Z')!.getDate()).toBe(9);
  });

  it('formatDateOnly_Turkish_NoTimeOrZ', () => {
    const text = formatDateOnly('2026-09-09T00:00:00Z', 'tr');

    expect(text).toBe('9 Eyl 2026');
    expect(text).not.toContain('T00');
    expect(text).not.toContain('Z');
  });

  it('formatDateOnly_English', () => {
    expect(formatDateOnly('2026-09-30T00:00:00Z', 'en-US')).toBe('Sep 30, 2026');
  });

  it('formatDateOnly_Invalid_ReturnsEmpty', () => {
    expect(formatDateOnly(undefined, 'tr')).toBe('');
    expect(formatDateOnly('garbage', 'tr')).toBe('');
  });
});
