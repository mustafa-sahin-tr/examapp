import { containsEmail, containsPhoneNumber, detectPersonalInfo, hasPersonalInfo } from './pii-detect.util';

describe('pii-detect.util (issue #305)', () => {
  describe('containsPhoneNumber', () => {
    const positives = [
      '0532 123 45 67',
      '05321234567',
      '0532-123-45-67',
      '0532.123.45.67',
      '(0532) 123 45 67',
      '532 123 45 67',
      '5321234567',
      '+90 532 123 45 67',
      '+905321234567',
      '+90 (532) 123-45-67',
      '0090 532 123 45 67',
      '905321234567',
      '+90 0532 123 45 67',
      '0212 555 12 34',
      '+90 212 555 12 34',
      '0532 123 4567',
      '(532) 123-45-67',
      '532.123.45.67',
      'Beni ara: 0532 123 45 67, akşam müsaitim.',
      'numaram 0 532 123 45 67',
      'iş: 0212 555 12 34 - cep: 0532 123 45 67',
    ];
    for (const text of positives) {
      it(`detects "${text}"`, () => {
        expect(containsPhoneNumber(text)).withContext(text).toBeTrue();
      });
    }

    const negatives = [
      '',
      'Cevap 12 değil 15 olmalı.',
      '3x + 5 = 20 ise x = 5',
      '12345678901',
      '2 3 4 5 6 7 8 9 1 2',
      '1234567890',
      '2123456789',
      '+1 555 123 4567',
      'Sayfa 532, soru 12',
      '0000 000 00 00',
      'Soru 5321234567890 numaralı kayıtta',
      // Review: ondalık sayılar
      '0,5321234567',
      '0.5321234567',
      'x = 3,5321234567 yaklaşık',
      'pi 3.1415926535',
      // Review: boşlukla ayrılmış tek haneli diziler
      '5 3 2 1 2 3 4 5 6 7',
      '0 5 3 2 1 2 3 4 5 6 7',
      '1 2 3 4 5 6 7 8 9 0',
      // Review: öneksiz cep yalnız bitişik ya da 3-3-2-2
      '532 1234 567',
      '53 21 23 45 67',
      '5321 234 567',
    ];
    for (const text of negatives) {
      it(`ignores "${text}"`, () => {
        expect(containsPhoneNumber(text)).withContext(text).toBeFalse();
      });
    }
  });

  describe('containsEmail', () => {
    it('detects common addresses', () => {
      expect(containsEmail('ali.veli@example.com')).toBeTrue();
      expect(containsEmail('mail at: ayse+okul@okul.k12.tr lütfen')).toBeTrue();
      expect(containsEmail('çağla@örnek.com.tr')).toBeTrue();
    });

    it('ignores non-addresses', () => {
      expect(containsEmail('')).toBeFalse();
      expect(containsEmail('@ali bak buna')).toBeFalse();
      expect(containsEmail('a@b')).toBeFalse();
      expect(containsEmail('x @ y . z')).toBeFalse();
    });
  });

  it('detectPersonalInfo_ReportsBothFlags; hasPersonalInfo_AnyFlag', () => {
    expect(detectPersonalInfo('0532 123 45 67 ve a@b.co')).toEqual({ phone: true, email: true });
    expect(detectPersonalInfo('a@b.co')).toEqual({ phone: false, email: true });
    expect(hasPersonalInfo('0532 123 45 67')).toBeTrue();
    expect(hasPersonalInfo('Bu soruyu anlamadım')).toBeFalse();
  });

  describe('bilinçli kapsam dışı (review kararı — uyarı üretmez)', () => {
    it('TC kimlik no telefon sayılmaz', () => {
      expect(hasPersonalInfo('TC: 12345678950')).toBeFalse();
    });

    it('gizlenmiş e-posta ([at] / [dot]) aranmaz', () => {
      expect(hasPersonalInfo('ali [at] example [dot] com')).toBeFalse();
      expect(hasPersonalInfo('ali(at)example.com')).toBeFalse();
    });

    it('kullanıcı adları / sosyal medya hesapları aranmaz', () => {
      expect(hasPersonalInfo('instagram: @ali_kaya')).toBeFalse();
      expect(hasPersonalInfo('discord ali#1234')).toBeFalse();
    });
  });
});
