import { convertToParamMap } from '@angular/router';
import { EMPTY_SCHOOL_LIST_FILTER, MAX_SEARCH_LENGTH, SchoolListFilter, hasSchoolListFilterParams, isSchoolListFilterActive, normalizeSearchText, sanitizeSchoolListFilter, schoolListFilterFromParams, schoolListFilterToParams, } from './school-list-filter';

// Issue #281: okul listesi filtre durumu (URL ↔ filtre).
describe('school-list-filter', () => {
    const filter = (patch: Partial<SchoolListFilter>): SchoolListFilter => ({ ...EMPTY_SCHOOL_LIST_FILTER, ...patch });

    // ── normalizeSearchText ────────────────────────────────────────────────────

    describe('normalizeSearchText', () => {
        it('dottedCapitalI_FoldsToPlainI', () => {
            expect(normalizeSearchText('İstanbul')).toBe('istanbul');
            expect(normalizeSearchText('istanbul')).toBe('istanbul');
        });

        it('ilkokulVariants_AllNormalizeToSameKey', () => {
            const keys = ['ilkokul', 'İlkokul', 'ILKOKUL', 'İLKOKUL', 'Ilkokul'].map(normalizeSearchText);
            expect(new Set(keys).size, keys.join(',')).toBe(1);
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
                expect(schoolListFilterFromParams(convertToParamMap({ provinceId: raw })).provinceId, raw).toBeNull();
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
        expect(hasSchoolListFilterParams(convertToParamMap({ q: 'x' }))).toBe(true);
        expect(hasSchoolListFilterParams(convertToParamMap({ provinceId: '6' }))).toBe(true);
        expect(hasSchoolListFilterParams(convertToParamMap({ q: '  ', provinceId: 'abc', districtId: '5' }))).toBe(false);
        expect(hasSchoolListFilterParams(convertToParamMap({ page: '2' }))).toBe(false);
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
            expect(sanitizeSchoolListFilter(filter({ provinceId: 99, districtId: 601 }), provinces, districts)).toEqual(EMPTY_SCHOOL_LIST_FILTER);
        });

        it('districtOfAnotherProvince_IsDropped', () => {
            expect(sanitizeSchoolListFilter(filter({ provinceId: 6, districtId: 3401 }), provinces, districts)).toEqual(filter({ provinceId: 6 }));
        });

        it('listsNotLoadedYet_KeepIdsAsIs', () => {
            const f = filter({ provinceId: 34, districtId: 3401 });
            expect(sanitizeSchoolListFilter(f, [], {})).toEqual(f);
            expect(sanitizeSchoolListFilter(f, provinces, {})).toEqual(f);
        });
    });
});
