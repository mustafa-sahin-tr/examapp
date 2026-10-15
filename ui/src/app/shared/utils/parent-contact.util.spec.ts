import { FormControl } from '@angular/forms';

import {
  normalizeParentEmail,
  normalizeParentName,
  normalizeParentPhone,
  parentEmailValidator,
  parentPhoneValidator,
} from './parent-contact.util';

/** Issue #440: sunucudaki `ParentContactRules` ile aynı sonuçlar. */
describe('parent-contact.util', () => {
  for (const [input, expected] of [
    ['05321234567', '+905321234567'],
    ['5321234567', '+905321234567'],
    ['905321234567', '+905321234567'],
    ['+905321234567', '+905321234567'],
    ['0 (532) 123-45.67', '+905321234567'],
  ] as const) {
    it(`normalizeParentPhone_${input}_ReturnsE164`, () => expect(normalizeParentPhone(input)).toBe(expected));
  }

  for (const input of ['', '02121234567', '053212345', '+9053212345678', '4321234567', 'abc']) {
    it(`normalizeParentPhone_Invalid_${input || 'empty'}_ReturnsNull`, () => expect(normalizeParentPhone(input)).toBeNull());
  }

  it('normalizeParentEmail_TrimsAndLowercases', () => {
    expect(normalizeParentEmail('  Ayse@Example.COM ')).toBe('ayse@example.com');
  });

  for (const input of ['ayse', 'ayse@', 'ayse@example', 'a b@example.com', 'a@@example.com', 'a@example.c']) {
    it(`normalizeParentEmail_Invalid_${input}_ReturnsNull`, () => expect(normalizeParentEmail(input)).toBeNull());
  }

  it('normalizeParentName_CollapsesSpacesAndRequiresLetter', () => {
    expect(normalizeParentName('  Ayşe   Yılmaz ')).toBe('Ayşe Yılmaz');
    expect(normalizeParentName('A')).toBeNull();
    expect(normalizeParentName('12345')).toBeNull();
    expect(normalizeParentName('x'.repeat(101))).toBeNull();
  });

  it('normalizeParentName_AllowsTurkishLettersAndPunctuation (#440 review)', () => {
    expect(normalizeParentName('Şükrü Öztürk-Çağlar')).toBe('Şükrü Öztürk-Çağlar');
    expect(normalizeParentName("A. O’Neil d'Arc")).toBe("A. O’Neil d'Arc");
  });

  for (const input of ['www.ornek', 'ornek.com', 'Ali x.co', 'Ayşe <b>', 'Ali 123', 'ali@site']) {
    it(`normalizeParentName_RejectsLinkLikeOrSymbols_${input}`, () => expect(normalizeParentName(input)).toBeNull());
  }

  it('validators_EmptyValueLeftToRequired_InvalidValueFlagged', () => {
    expect(parentEmailValidator(new FormControl(''))).toBeNull();
    expect(parentEmailValidator(new FormControl('nope'))).toEqual({ parentEmail: true });
    expect(parentPhoneValidator(new FormControl('0532 123 45 67'))).toBeNull();
    expect(parentPhoneValidator(new FormControl('123'))).toEqual({ parentPhone: true });
  });
});
