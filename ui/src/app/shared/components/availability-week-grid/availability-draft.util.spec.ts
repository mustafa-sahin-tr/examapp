import {
  DRAFT_MAX_ADVANCE_DAYS,
  DraftRange,
  applyCellClick,
  nextUtcDayBoundary,
  toSlotRequest,
} from './availability-draft.util';

/**
 * Testler Karma'nın koştuğu makinenin saat diliminden BAĞIMSIZDIR.
 *
 * Genişletme/daraltma kuralları iki sınıra bağlıdır: taslak tek bir YEREL günde kalır (grid sütunu) ve tek bir
 * UTC gününde kalır (backend `date` + `TimeOnly`). UTC gece yarısı dilime göre yerel günün farklı saatine düşer;
 * bu yüzden `BASE`, 25 Eylül 2026 yerel gününün UTC gece yarısıyla bölünen iki parçasından BÜYÜK olanının
 * (≥ 12 saat) başlangıcıdır. `at(dakika)` o parçanın içinde, yarım saate hizalı anlar üretir.
 */
const HALF_HOUR_MS = 30 * 60_000;
const DAY_MS = 24 * 60 * 60_000;

function safeBase(year: number, monthIndex: number, day: number): Date {
  const localStart = new Date(year, monthIndex, day).getTime();
  const localEnd = new Date(year, monthIndex, day + 1).getTime();
  // Yerel günün içine düşen UTC gece yarısı (yoksa ya da tam kenardaysa parça tüm gündür).
  const utcMidnight = Math.ceil(localStart / DAY_MS) * DAY_MS;
  const splitsDay = utcMidnight > localStart && utcMidnight < localEnd;
  const pieceStart = splitsDay && localEnd - utcMidnight >= utcMidnight - localStart ? utcMidnight : localStart;
  // Yerel :00/:30'a yukarı hizala (ör. +05:45 diliminde UTC gece yarısı hücre ortasına düşer).
  const misalign = (pieceStart - localStart) % HALF_HOUR_MS;
  return new Date(misalign === 0 ? pieceStart : pieceStart + HALF_HOUR_MS - misalign);
}

const BASE = safeBase(2026, 8, 25);
/** "Şimdi": BASE'den 4 gün önce — 25 Eylül hücreleri gelecekte ve 90 gün ufkunun içinde. */
const NOW = new Date(BASE.getTime() - 4 * DAY_MS);

function at(minutes: number): Date {
  return new Date(BASE.getTime() + minutes * 60_000);
}

function range(start: Date, end: Date): DraftRange {
  return { start, end };
}

function utc(day: number, hour: number, minute = 0): Date {
  return new Date(Date.UTC(2026, 8, day, hour, minute));
}

describe('availability-draft.util', () => {
  it('test penceresi (6 saat) tek yerel günde ve tek UTC gününde kalır', () => {
    expect(at(0).getDate()).toBe(25);
    expect(at(359).getDate()).toBe(25);
    expect(at(0).toISOString().slice(0, 10)).toBe(at(359).toISOString().slice(0, 10));
  });

  describe('applyCellClick', () => {
    it('ilk tıklama tıklanan hücrede 30 dakikalık taslak kurar', () => {
      const result = applyCellClick(null, at(60), [], NOW);

      expect(result.rejection).toBeNull();
      expect(result.draft).toEqual(range(at(60), at(90)));
    });

    it('aynı günde daha geç bir hücreye tıklama taslağı o hücrenin sonuna kadar genişletir', () => {
      const result = applyCellClick(range(at(60), at(90)), at(120), [], NOW);

      expect(result.rejection).toBeNull();
      expect(result.draft).toEqual(range(at(60), at(150)));
    });

    it('aynı günde daha erken bir hücreye tıklama taslağı geriye doğru genişletir', () => {
      const result = applyCellClick(range(at(60), at(120)), at(0), [], NOW);

      expect(result.draft).toEqual(range(at(0), at(120)));
    });

    it('ilk hücreye tıklama taslağı baştan daraltır', () => {
      const result = applyCellClick(range(at(60), at(150)), at(60), [], NOW);

      expect(result.rejection).toBeNull();
      expect(result.draft).toEqual(range(at(90), at(150)));
    });

    it('son hücreye tıklama taslağı sondan daraltır', () => {
      const result = applyCellClick(range(at(60), at(150)), at(120), [], NOW);

      expect(result.draft).toEqual(range(at(60), at(120)));
    });

    it('ortadaki hücreye tıklama taslağı o hücrede bitirir', () => {
      const result = applyCellClick(range(at(60), at(180)), at(90), [], NOW);

      expect(result.draft).toEqual(range(at(60), at(120)));
    });

    it('tek hücrelik taslağa tıklama taslağı kaldırır', () => {
      const result = applyCellClick(range(at(60), at(90)), at(60), [], NOW);

      expect(result).toEqual({ draft: null, rejection: null });
    });

    it('farklı yerel güne tıklama taslağı o güne taşır (yeni 30 dk)', () => {
      const draft = range(at(60), at(180));
      const nextDay = safeBase(2026, 8, 26);

      const result = applyCellClick(draft, nextDay, [], NOW);

      expect(result.rejection).toBeNull();
      expect(result.draft).toEqual(range(nextDay, new Date(nextDay.getTime() + HALF_HOUR_MS)));
    });

    it('geçmişteki hücre reddedilir ve mevcut taslak korunur', () => {
      const draft = range(at(60), at(120));

      const result = applyCellClick(draft, new Date(NOW.getTime() - 60 * 60_000), [], NOW);

      expect(result.rejection).toBe('past');
      expect(result.draft).toBe(draft);
    });

    it('başlangıcı geçmiş olan içinde bulunulan hücre de reddedilir (backend: start <= now)', () => {
      const now = new Date(at(60).getTime() + 10 * 60_000); // hücre başlayalı 10 dk olmuş

      expect(applyCellClick(null, at(60), [], now)).toEqual({ draft: null, rejection: 'past' });
      expect(applyCellClick(null, at(90), [], now).rejection).toBeNull();
    });

    it('sınır eşitlikte de geçmiştir: hücre tam "şimdi" başlıyorsa reddedilir, 1 ms sonrası kabul edilir', () => {
      expect(applyCellClick(null, at(60), [], at(60))).toEqual({ draft: null, rejection: 'past' });
      expect(applyCellClick(null, at(60), [], new Date(at(60).getTime() - 1)).rejection).toBeNull();
    });

    it('beklerken ilk hücresi geçmişe düşen taslak baştan daraltılabilir (doğru düzeltme reddedilmez)', () => {
      const draft = range(at(60), at(150));
      const now = new Date(at(60).getTime() + 5 * 60_000); // ilk hücre artık geçmişte

      const result = applyCellClick(draft, at(60), [], now);

      expect(result.rejection).toBeNull();
      expect(result.draft).toEqual(range(at(90), at(150)));
    });

    it('ilk hücresi geçmişe düşen taslağı genişletmek reddedilir (başlangıç hâlâ geçmişte)', () => {
      const draft = range(at(60), at(150));
      const now = new Date(at(60).getTime() + 5 * 60_000);

      const result = applyCellClick(draft, at(180), [], now);

      expect(result.rejection).toBe('past');
      expect(result.draft).toBe(draft);
    });

    it('geçmişe düşmüş tek hücrelik taslak tıklanarak kaldırılabilir', () => {
      const draft = range(at(60), at(90));
      const now = new Date(at(60).getTime() + 5 * 60_000);

      expect(applyCellClick(draft, at(60), [], now)).toEqual({ draft: null, rejection: null });
    });

    it('mevcut slotla kesişen hücre reddedilir', () => {
      const busy = [range(at(75), at(120))];

      expect(applyCellClick(null, at(60), busy, NOW)).toEqual({ draft: null, rejection: 'occupied' });
    });

    it('slota bitişik (kesişmeyen) hücre kabul edilir', () => {
      const busy = [range(at(90), at(120))];

      expect(applyCellClick(null, at(60), busy, NOW).draft).toEqual(range(at(60), at(90)));
      expect(applyCellClick(null, at(120), busy, NOW).draft).toEqual(range(at(120), at(150)));
    });

    it('genişletme aradaki bir slotun üzerinden geçiyorsa reddedilir ve taslak değişmez', () => {
      const draft = range(at(0), at(30));
      const busy = [range(at(60), at(120))];

      const result = applyCellClick(draft, at(180), busy, NOW);

      expect(result.rejection).toBe('occupied');
      expect(result.draft).toBe(draft);
    });

    it('tam 4 saatlik taslak kabul edilir, 4 saati aşan genişletme reddedilir', () => {
      const draft = range(at(0), at(30));

      const exact = applyCellClick(draft, at(210), [], NOW);
      expect(exact.rejection).toBeNull();
      expect(exact.draft).toEqual(range(at(0), at(240)));

      const over = applyCellClick(draft, at(240), [], NOW);
      expect(over.rejection).toBe('tooLong');
      expect(over.draft).toBe(draft);
    });

    it('girdi taslağını yerinde değiştirmez', () => {
      const draft = range(at(60), at(120));

      applyCellClick(draft, at(180), [], NOW);

      expect(draft).toEqual(range(at(60), at(120)));
    });

    describe('UTC gün sınırı (backend tek date + TimeOnly tutar)', () => {
      const now = utc(21, 9, 10);

      it('23:00Z–23:30Z hücresi kabul edilir', () => {
        const result = applyCellClick(null, utc(24, 23, 0), [], now);

        expect(result.rejection).toBeNull();
        expect(result.draft).toEqual(range(utc(24, 23, 0), utc(24, 23, 30)));
      });

      it('23:30Z–00:00Z hücresi reddedilir: bitiş TimeOnly 00:00 olur ve başlangıçtan küçük kalır', () => {
        expect(applyCellClick(null, utc(24, 23, 30), [], now)).toEqual({ draft: null, rejection: 'crossesDayBoundary' });
      });

      it('00:00Z hücresi (yeni UTC gününün ilk hücresi) kabul edilir', () => {
        expect(applyCellClick(null, utc(25, 0, 0), [], now).draft).toEqual(range(utc(25, 0, 0), utc(25, 0, 30)));
      });

      it('UTC gece yarısının iki yanındaki hücreler tek taslakta birleşmez', () => {
        const draft = range(utc(24, 23, 0), utc(24, 23, 30));

        const result = applyCellClick(draft, utc(25, 0, 0), [], now);

        // Aynı yerel gündeyseler red; farklı yerel gündeyseler (ör. UTC dilimi) taslak taşınır. İki durumda da
        // sonuç tek bir UTC gününde kalır ve 23:00Z–00:30Z aralığı ASLA oluşmaz.
        const sameLocalDay = draft.start.getDate() === utc(25, 0, 0).getDate();
        if (sameLocalDay) {
          expect(result).toEqual({ draft, rejection: 'crossesDayBoundary' });
        } else {
          expect(result).toEqual({ draft: range(utc(25, 0, 0), utc(25, 0, 30)), rejection: null });
        }
      });

      it('yerel 23:30 hücresi, bitişi UTC gece yarısı değilse kabul edilir (sınır yerel gece yarısı değildir)', () => {
        const cell = new Date(2026, 8, 25, 23, 30);
        const end = new Date(cell.getTime() + HALF_HOUR_MS);
        const endsAtUtcMidnight = end.getTime() % DAY_MS === 0;

        const result = applyCellClick(null, cell, [], NOW);

        if (endsAtUtcMidnight) {
          expect(result).toEqual({ draft: null, rejection: 'crossesDayBoundary' });
        } else {
          expect(result).toEqual({ draft: range(cell, end), rejection: null });
        }
      });
    });

    describe('90 gün ufku (backend: UTC bugün + 90)', () => {
      const now = utc(21, 9, 10);

      it('ufkun son UTC günü kabul edilir (günün son geçerli hücresi dahil)', () => {
        const lastDayMorning = new Date(Date.UTC(2026, 8, 21 + DRAFT_MAX_ADVANCE_DAYS, 10, 0));
        const lastDayLate = new Date(Date.UTC(2026, 8, 21 + DRAFT_MAX_ADVANCE_DAYS, 23, 0));

        expect(applyCellClick(null, lastDayMorning, [], now).rejection).toBeNull();
        expect(applyCellClick(null, lastDayLate, [], now).rejection).toBeNull();
      });

      it('ertesi UTC gününün ilk hücresi reddedilir', () => {
        const dayAfter = new Date(Date.UTC(2026, 8, 22 + DRAFT_MAX_ADVANCE_DAYS, 0, 0));

        expect(applyCellClick(null, dayAfter, [], now)).toEqual({ draft: null, rejection: 'tooFarAhead' });
      });

      it('ufuk "şimdi"nin UTC gününe göre hesaplanır (UTC günü sonunda bile +90 gün)', () => {
        const lateNow = utc(21, 23, 50);
        const lastDay = new Date(Date.UTC(2026, 8, 21 + DRAFT_MAX_ADVANCE_DAYS, 12, 0));

        expect(applyCellClick(null, lastDay, [], lateNow).rejection).toBeNull();
      });
    });
  });

  describe('nextUtcDayBoundary', () => {
    it('verilen andan sonraki UTC gece yarısını döner; tam sınırdaki an için bir SONRAKİ günü', () => {
      expect(nextUtcDayBoundary(utc(24, 11, 0))).toEqual(utc(25, 0, 0));
      expect(nextUtcDayBoundary(utc(24, 23, 59))).toEqual(utc(25, 0, 0));
      expect(nextUtcDayBoundary(utc(25, 0, 0))).toEqual(utc(26, 0, 0));
    });
  });

  describe('toSlotRequest', () => {
    it('taslak anlarının UTC günü ve UTC duvar saatini üretir', () => {
      expect(toSlotRequest(range(utc(24, 11, 0), utc(24, 12, 30)))).toEqual({
        date: '2026-09-24',
        startTime: '11:00:00',
        endTime: '12:30:00',
      });
    });

    it('tek haneli ay/gün/saatleri sıfırla doldurur', () => {
      const start = new Date(Date.UTC(2027, 0, 5, 8, 0));
      const end = new Date(Date.UTC(2027, 0, 5, 9, 30));

      expect(toSlotRequest(range(start, end))).toEqual({
        date: '2027-01-05',
        startTime: '08:00:00',
        endTime: '09:30:00',
      });
    });

    it('UTC günü yerel günden farklı olan anda date UTC gününü taşır', () => {
      // 23:00Z ve 00:30Z: UTC dışındaki her dilimde ikisinden en az biri yerelde başka takvim gününe düşer.
      const lateUtc = toSlotRequest(range(utc(24, 23, 0), utc(24, 23, 30)));
      const earlyUtc = toSlotRequest(range(utc(25, 0, 30), utc(25, 1, 0)));

      expect(lateUtc).toEqual({ date: '2026-09-24', startTime: '23:00:00', endTime: '23:30:00' });
      expect(earlyUtc).toEqual({ date: '2026-09-25', startTime: '00:30:00', endTime: '01:00:00' });
    });

    it('hizasız girdi (ön koşul ihlali) hata vermez: saniye/milisaniye atılır, dakika aynen gönderilir', () => {
      const start = new Date(Date.UTC(2026, 8, 24, 11, 7, 45, 500));
      const end = new Date(Date.UTC(2026, 8, 24, 12, 22, 10));

      expect(toSlotRequest(range(start, end))).toEqual({
        date: '2026-09-24',
        startTime: '11:07:00',
        endTime: '12:22:00',
      });
    });

    it('yerel saat bileşenlerini KULLANMAZ: istek toISOString ile birebir örtüşür', () => {
      const start = new Date(2026, 8, 25, 0, 30); // yerel an; dilime göre UTC günü değişebilir
      const end = new Date(start.getTime() + HALF_HOUR_MS);

      expect(toSlotRequest(range(start, end))).toEqual({
        date: start.toISOString().slice(0, 10),
        startTime: start.toISOString().slice(11, 19),
        endTime: end.toISOString().slice(11, 19),
      });
    });
  });
});
