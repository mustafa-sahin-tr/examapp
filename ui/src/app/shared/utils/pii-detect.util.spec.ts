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
                expect(containsPhoneNumber(text), text).toBe(true);
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
                expect(containsPhoneNumber(text), text).toBe(false);
            });
        }
    });

    describe('containsEmail', () => {
        it('detects common addresses', () => {
            expect(containsEmail('ali.veli@example.com')).toBe(true);
            expect(containsEmail('mail at: ayse+okul@okul.k12.tr lütfen')).toBe(true);
            expect(containsEmail('çağla@örnek.com.tr')).toBe(true);
        });

        it('ignores non-addresses', () => {
            expect(containsEmail('')).toBe(false);
            expect(containsEmail('@ali bak buna')).toBe(false);
            expect(containsEmail('a@b')).toBe(false);
            expect(containsEmail('x @ y . z')).toBe(false);
        });
    });

    it('detectPersonalInfo_ReportsBothFlags; hasPersonalInfo_AnyFlag', () => {
        expect(detectPersonalInfo('0532 123 45 67 ve a@b.co')).toEqual({ phone: true, email: true });
        expect(detectPersonalInfo('a@b.co')).toEqual({ phone: false, email: true });
        expect(hasPersonalInfo('0532 123 45 67')).toBe(true);
        expect(hasPersonalInfo('Bu soruyu anlamadım')).toBe(false);
    });

    describe('bilinçli kapsam dışı (review kararı — uyarı üretmez)', () => {
        it('TC kimlik no telefon sayılmaz', () => {
            expect(hasPersonalInfo('TC: 12345678950')).toBe(false);
        });

        it('gizlenmiş e-posta ([at] / [dot]) aranmaz', () => {
            expect(hasPersonalInfo('ali [at] example [dot] com')).toBe(false);
            expect(hasPersonalInfo('ali(at)example.com')).toBe(false);
        });

        it('kullanıcı adları / sosyal medya hesapları aranmaz', () => {
            expect(hasPersonalInfo('instagram: @ali_kaya')).toBe(false);
            expect(hasPersonalInfo('discord ali#1234')).toBe(false);
        });
    });
});
