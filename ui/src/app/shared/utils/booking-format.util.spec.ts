import {
  endsOnNextLocalDay,
  formatSlotFull,
  formatSlotRange,
  slotEndsNextDay,
} from './booking-format.util';

/** Gün aşan aralığın ortak gösterimi (issue #300). Anlar yerel kurulur; sonuç makinenin diliminden bağımsızdır. */
describe('booking-format.util', () => {
  describe('endsOnNextLocalDay', () => {
    it('ertesi yerel günde biten aralık için true', () => {
      expect(endsOnNextLocalDay(new Date(2026, 8, 25, 23, 30), new Date(2026, 8, 26, 0, 30))).toBeTrue();
    });

    it('tam yerel gece yarısında biten aralık aynı güne sayılır', () => {
      expect(endsOnNextLocalDay(new Date(2026, 8, 25, 23, 0), new Date(2026, 8, 26, 0, 0))).toBeFalse();
    });

    it('yerelde aynı gündeki aralık false (UTC gün aşsa bile ipucu yerel saate göredir)', () => {
      expect(endsOnNextLocalDay(new Date(2026, 8, 25, 2, 30), new Date(2026, 8, 25, 3, 30))).toBeFalse();
    });

    it('bitiş başlangıçtan sonra değilse false', () => {
      const t = new Date(2026, 8, 25, 10, 0);
      expect(endsOnNextLocalDay(t, t)).toBeFalse();
    });
  });

  describe('slotEndsNextDay', () => {
    it('ISO dizelerle çalışır; geçersiz tarihte false', () => {
      const start = new Date(2026, 8, 25, 23, 30).toISOString();
      const end = new Date(2026, 8, 26, 0, 30).toISOString();

      expect(slotEndsNextDay(start, end)).toBeTrue();
      expect(slotEndsNextDay('bozuk', end)).toBeFalse();
    });
  });

  describe('formatSlotRange (+1 gün)', () => {
    const start = new Date(2026, 8, 25, 23, 30).toISOString();
    const end = new Date(2026, 8, 26, 0, 30).toISOString();

    it('etiket verilir ve bitiş ertesi yerel gündeyse sona eklenir', () => {
      expect(formatSlotRange(start, end, '(+1 gün)')).toMatch(/ \(\+1 gün\)$/);
    });

    it('etiket verilmezse aralık değişmez', () => {
      expect(formatSlotRange(start, end)).not.toContain('+1');
    });

    it('aynı gündeki aralığa etiket eklenmez', () => {
      const s = new Date(2026, 8, 25, 14, 0).toISOString();
      const e = new Date(2026, 8, 25, 15, 0).toISOString();

      expect(formatSlotRange(s, e, '(+1 gün)')).not.toContain('+1');
    });

    it('formatSlotFull etiketi aralığa iletir', () => {
      expect(formatSlotFull(start, end, '(+1 day)')).toMatch(/ \(\+1 day\)$/);
    });
  });

  /** Biçimlendirici önbelleği ilk çağrıdaki dile kilitlenmez (#394). */
  describe('aktif dil', () => {
    const s = new Date(2026, 8, 25, 14, 0).toISOString();
    const e = new Date(2026, 8, 25, 15, 0).toISOString();
    let originalLang: string | null;

    beforeEach(() => (originalLang = document.documentElement.getAttribute('lang')));
    afterEach(() =>
      originalLang === null
        ? document.documentElement.removeAttribute('lang')
        : document.documentElement.setAttribute('lang', originalLang)
    );

    it('formatSlotRange_HtmlLangChangesBetweenCalls_FollowsCurrentLocale', () => {
      document.documentElement.setAttribute('lang', 'en');
      const en = formatSlotRange(s, e);
      document.documentElement.setAttribute('lang', 'tr');
      const tr = formatSlotRange(s, e);

      // ICU saat ile AM/PM arasına dar boşluk (U+202F) koyabilir; \s ikisini de kapsar.
      expect(en).toMatch(/^02:00\sPM – 03:00\sPM$/);
      expect(tr).toBe('14:00 – 15:00');
    });
  });
});
