import { convertToParamMap } from '@angular/router';
import { School } from '../../../models/taxonomy';
import {
  EMPTY_SCHOOL_LIST_FILTER,
  MAX_SEARCH_LENGTH,
  SchoolListFilter,
  filterSchools,
  hasSchoolListFilterParams,
  isSchoolListFilterActive,
  normalizeSearchText,
  sanitizeSchoolListFilter,
  schoolListFilterFromParams,
  schoolListFilterToParams,
} from './school-list-filter';

// Issue #281: okul listesi istemci tarafı filtre mantığı.
describe('school-list-filter', () => {
  function school(id: number, name: string, provinceId: number | null, districtId: number | null): School {
    return { id, name, provinceId, provinceName: null, districtId, districtName: null, addressLine: null };
  }

  const schools: School[] = [
    school(1, 'Atatürk İlkokulu', 6, 601),
    school(2, 'Keçiören Anadolu Lisesi', 6, 602),
    school(3, 'İstanbul Erkek Lisesi', 34, 3401),
    school(4, 'CUMHURIYET ILKOKULU', 34, 3401),
    school(5, 'ışık ortaokulu', null, null),
  ];

  const filter = (patch: Partial<SchoolListFilter>): SchoolListFilter => ({ ...EMPTY_SCHOOL_LIST_FILTER, ...patch });
  const ids = (f: SchoolListFilter): number[] => filterSchools(schools, f).map((s) => s.id);

  // ── normalizeSearchText ────────────────────────────────────────────────────

  describe('normalizeSearchText', () => {
    it('dottedCapitalI_FoldsToPlainI', () => {
      expect(normalizeSearchText('İstanbul')).toBe('istanbul');
      expect(normalizeSearchText('istanbul')).toBe('istanbul');
    });

    it('ilkokulVariants_AllNormalizeToSameKey', () => {
      const keys = ['ilkokul', 'İlkokul', 'ILKOKUL', 'İLKOKUL', 'Ilkokul'].map(normalizeSearchText);
      expect(new Set(keys).size).withContext(keys.join(',')).toBe(1);
      expect(keys[0]).toBe('ilkokul');
    });

    it('dotlessI_FoldsToPlainI', () => {
      expect(normalizeSearchText('IŞIK')).toBe(normalizeSearchText('ışık'));
      expect(normalizeSearchText('ışık')).toBe('isik');
    });

    it('turkishDiacritics_AreStripped', () => {
      expect(normalizeSearchText('Çankaya Şehit Öğretmen Ünal')).toBe('cankaya sehit ogretmen unal');
    });

    it('whitespace_IsCollapsedAndTrimmed', () => {
      expect(normalizeSearchText('  Fen   Lisesi ')).toBe('fen lisesi');
    });

    it('nullOrEmpty_ReturnsEmptyString', () => {
      expect(normalizeSearchText(null)).toBe('');
      expect(normalizeSearchText(undefined)).toBe('');
      expect(normalizeSearchText('')).toBe('');
    });
  });

  // ── filterSchools ─────────────────────────────────────────────────────────

  describe('filterSchools', () => {
    it('emptyFilter_ReturnsAllInOriginalOrder', () => {
      expect(ids(EMPTY_SCHOOL_LIST_FILTER)).toEqual([1, 2, 3, 4, 5]);
    });

    it('search_IsSubstringAndCaseAndTurkishInsensitive', () => {
      expect(ids(filter({ q: 'istanbul' }))).toEqual([3]);
      expect(ids(filter({ q: 'İSTANBUL' }))).toEqual([3]);
      expect(ids(filter({ q: 'ilkokul' }))).toEqual([1, 4]);
      expect(ids(filter({ q: 'ILKOKUL' }))).toEqual([1, 4]);
      expect(ids(filter({ q: 'İlkokul' }))).toEqual([1, 4]);
      expect(ids(filter({ q: 'IŞIK' }))).toEqual([5]);
      expect(ids(filter({ q: 'kecioren' }))).toEqual([2]);
    });

    it('whitespaceOnlySearch_DoesNotFilter', () => {
      expect(ids(filter({ q: '   ' }))).toEqual([1, 2, 3, 4, 5]);
    });

    it('province_KeepsOnlyThatProvince_ExcludesSchoolsWithoutProvince', () => {
      expect(ids(filter({ provinceId: 6 }))).toEqual([1, 2]);
    });

    it('district_KeepsOnlyThatDistrict', () => {
      expect(ids(filter({ provinceId: 6, districtId: 602 }))).toEqual([2]);
    });

    it('criteria_AreCombinedWithAnd', () => {
      expect(ids(filter({ q: 'lise', provinceId: 34 }))).toEqual([3]);
      expect(ids(filter({ q: 'ilkokul', provinceId: 34, districtId: 3401 }))).toEqual([4]);
      expect(ids(filter({ q: 'lise', provinceId: 6, districtId: 601 }))).toEqual([]);
    });

    it('clearedFilter_RestoresFullList', () => {
      const narrowed = filter({ q: 'lise', provinceId: 6, districtId: 602 });
      expect(ids(narrowed)).toEqual([2]);
      expect(ids({ ...EMPTY_SCHOOL_LIST_FILTER })).toEqual([1, 2, 3, 4, 5]);
    });
  });

  // ── isSchoolListFilterActive ──────────────────────────────────────────────

  it('isSchoolListFilterActive_ReflectsAnyCriterion', () => {
    expect(isSchoolListFilterActive(EMPTY_SCHOOL_LIST_FILTER)).toBeFalse();
    expect(isSchoolListFilterActive(filter({ q: '  ' }))).toBeFalse();
    expect(isSchoolListFilterActive(filter({ q: 'a' }))).toBeTrue();
    expect(isSchoolListFilterActive(filter({ provinceId: 6 }))).toBeTrue();
  });

  // ── URL ───────────────────────────────────────────────────────────────────

  describe('schoolListFilterFromParams', () => {
    it('validParams_AreParsed', () => {
      expect(schoolListFilterFromParams(convertToParamMap({ q: 'Lise', provinceId: '6', districtId: '601' }))).toEqual({
        q: 'Lise',
        provinceId: 6,
        districtId: 601,
      });
    });

    it('noParams_ReturnsEmptyFilter', () => {
      expect(schoolListFilterFromParams(convertToParamMap({}))).toEqual(EMPTY_SCHOOL_LIST_FILTER);
    });

    it('invalidIds_AreIgnored', () => {
      for (const raw of ['abc', '0', '-3', '1.5', '']) {
        expect(schoolListFilterFromParams(convertToParamMap({ provinceId: raw })).provinceId)
          .withContext(raw)
          .toBeNull();
      }
      expect(schoolListFilterFromParams(convertToParamMap({ provinceId: '6', districtId: 'x' })).districtId).toBeNull();
    });

    it('districtWithoutProvince_IsIgnored', () => {
      expect(schoolListFilterFromParams(convertToParamMap({ districtId: '601' }))).toEqual(EMPTY_SCHOOL_LIST_FILTER);
    });

    it('overlongSearch_IsTruncated', () => {
      const q = 'a'.repeat(MAX_SEARCH_LENGTH + 50);
      expect(schoolListFilterFromParams(convertToParamMap({ q })).q.length).toBe(MAX_SEARCH_LENGTH);
    });
  });

  it('hasSchoolListFilterParams_OnlyValidFilterParamsCount', () => {
    expect(hasSchoolListFilterParams(convertToParamMap({ q: 'x' }))).toBeTrue();
    expect(hasSchoolListFilterParams(convertToParamMap({ provinceId: '6' }))).toBeTrue();
    expect(hasSchoolListFilterParams(convertToParamMap({ q: '  ', provinceId: 'abc', districtId: '5' }))).toBeFalse();
    expect(hasSchoolListFilterParams(convertToParamMap({ page: '2' }))).toBeFalse();
  });

  describe('schoolListFilterToParams', () => {
    it('emptyValues_AreNullSoMergeRemovesThem', () => {
      expect(schoolListFilterToParams(filter({ q: '  ' }))).toEqual({ q: null, provinceId: null, districtId: null });
    });

    it('filledValues_AreWritten_SearchIsTrimmed', () => {
      expect(schoolListFilterToParams(filter({ q: ' lise ', provinceId: 6, districtId: 601 }))).toEqual({
        q: 'lise',
        provinceId: 6,
        districtId: 601,
      });
    });

    it('districtWithoutProvince_IsNotWritten', () => {
      expect(schoolListFilterToParams(filter({ districtId: 601 }))['districtId']).toBeNull();
    });

    it('roundTrip_PreservesFilter', () => {
      const original = filter({ q: 'İlkokul', provinceId: 34, districtId: 3401 });
      const params = schoolListFilterToParams(original);
      const asStrings = Object.fromEntries(Object.entries(params).map(([k, v]) => [k, String(v)]));
      expect(schoolListFilterFromParams(convertToParamMap(asStrings))).toEqual(original);
    });
  });

  // ── sanitizeSchoolListFilter ──────────────────────────────────────────────

  describe('sanitizeSchoolListFilter', () => {
    const provinces = [
      { id: 6, name: 'Ankara' },
      { id: 34, name: 'İstanbul' },
    ];
    const districts = { 6: [{ id: 601, name: 'Çankaya', provinceId: 6 }] };

    it('knownIds_AreKept', () => {
      const f = filter({ q: 'x', provinceId: 6, districtId: 601 });
      expect(sanitizeSchoolListFilter(f, provinces, districts)).toEqual(f);
    });

    it('unknownProvince_IsDroppedWithItsDistrict', () => {
      expect(sanitizeSchoolListFilter(filter({ provinceId: 99, districtId: 601 }), provinces, districts)).toEqual(
        EMPTY_SCHOOL_LIST_FILTER,
      );
    });

    it('districtOfAnotherProvince_IsDropped', () => {
      expect(sanitizeSchoolListFilter(filter({ provinceId: 6, districtId: 3401 }), provinces, districts)).toEqual(
        filter({ provinceId: 6 }),
      );
    });

    it('listsNotLoadedYet_KeepIdsAsIs', () => {
      const f = filter({ provinceId: 34, districtId: 3401 });
      expect(sanitizeSchoolListFilter(f, [], {})).toEqual(f);
      expect(sanitizeSchoolListFilter(f, provinces, {})).toEqual(f);
    });
  });
});
