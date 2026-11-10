import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Observable, Subject, of, throwError } from 'rxjs';

import { SCHOOL_SELECT_DEBOUNCE_MS, SCHOOL_SELECT_MAX_OPTIONS, SchoolSelectComponent } from './school-select.component';
import { SchoolService } from '../../../services/school.service';
import { SchoolOption } from '../../../models/taxonomy';
import { translocoTestingModule } from '../../testing/transloco-testing';
import trTranslations from '../../../../../public/i18n/tr.json';
import enTranslations from '../../../../../public/i18n/en.json';

const school = (id: number, name: string, districtName: string | null = null, provinceName: string | null = null): SchoolOption => ({
    id,
    name,
    provinceId: null,
    provinceName,
    districtId: null,
    districtName,
});

const NO_SCOPE = { provinceId: null, districtId: null };

const SCHOOLS = [
    school(1, 'Ankara Fen Lisesi', 'Çankaya', 'Ankara'),
    school(2, 'İstanbul Erkek Lisesi', 'Fatih', 'İstanbul'),
    school(3, 'Kadıköy Anadolu Lisesi', 'Kadıköy', 'İstanbul'),
];

describe('SchoolSelectComponent (issue #277)', () => {
    let fixture: ComponentFixture<SchoolSelectComponent>;
    let component: SchoolSelectComponent;
    let schoolService: MockedObject<SchoolService>;

    /** Sunucu araması taklidi: varsayılan olarak `q` ad, ilçe ve ilde aranır. */
    const serve = (query: string): Observable<SchoolOption[]> =>
        of(SCHOOLS.filter((s) => `${s.name} ${s.districtName} ${s.provinceName}`.toLocaleLowerCase('tr').includes(query.toLocaleLowerCase('tr'))));

    function create(search: (query: string) => Observable<SchoolOption[]> = serve, inputs: Record<string, unknown> = {}): void {
        schoolService = {
            search: vi.fn().mockImplementation(search),
            getById: vi.fn().mockImplementation((id: number) => of(SCHOOLS.find((s) => s.id === id))),
            provinces: vi.fn().mockReturnValue(of([{ id: 34, name: 'İstanbul' }])),
            districts: vi.fn().mockReturnValue(of([{ id: 7, name: 'Kadıköy', provinceId: 34 }])),
        } as unknown as MockedObject<SchoolService>;
        TestBed.configureTestingModule({
            imports: [SchoolSelectComponent, translocoTestingModule()],
            providers: [{ provide: SchoolService, useValue: schoolService }, provideNoopAnimations()],
        });
        fixture = TestBed.createComponent(SchoolSelectComponent);
        component = fixture.componentInstance;
        for (const [key, value] of Object.entries(inputs))
            fixture.componentRef.setInput(key, value);
        fixture.detectChanges();
    }

    const el = (): HTMLElement => fixture.nativeElement;
    const input = (): HTMLInputElement => el().querySelector('[data-testid="school-select-input"]') as HTMLInputElement;

    function type(text: string): void {
        input().value = text;
        input().dispatchEvent(new Event('input'));
        fixture.detectChanges();
    }

    it('loadsFirstSchools_FromSchoolService', () => {
        create();
        expect(schoolService.search).toHaveBeenCalledTimes(1);
        expect(schoolService.search).toHaveBeenCalledWith('', SCHOOL_SELECT_MAX_OPTIONS, NO_SCOPE);
        expect(component.schools().map((s) => s.id)).toEqual([1, 2, 3]);
        expect(input()).not.toBeNull();
    });

    it('loading_KeepsInputUsable', () => {
        create(() => new Subject<SchoolOption[]>());
        expect(component.loading()).toBe(true);
        expect(input().disabled).toBe(false);
    });

    it('error_ShowsMessage_RetryReloads', () => {
        let calls = 0;
        create((query) => (++calls === 1 ? throwError(() => new Error('boom')) : serve(query)));
        expect(el().querySelector('[data-testid="school-select-error"]')?.textContent).toContain(trTranslations.shared.schoolSelect.loadFailed);

        (el().querySelector('[data-testid="school-select-retry"]') as HTMLButtonElement).click();
        fixture.detectChanges();

        expect(schoolService.search).toHaveBeenCalledTimes(2);
        expect(el().querySelector('[data-testid="school-select-error"]')).toBeNull();
        expect(input()).not.toBeNull();
    });

    it('emptyList_ShowsEmptyState', () => {
        create(() => of([]));
        expect(el().querySelector('[data-testid="school-select-empty"]')?.textContent).toContain(trTranslations.shared.schoolSelect.empty);
    });

    it('typing_SearchesOnTheServerOnceAfterDebounce', async () => {
        vi.useFakeTimers();
        try {
            create();
            type('kad');
            await vi.advanceTimersByTimeAsync(SCHOOL_SELECT_DEBOUNCE_MS - 1);
            type('kadı');
            await vi.advanceTimersByTimeAsync(SCHOOL_SELECT_DEBOUNCE_MS);
            fixture.detectChanges();

            expect(schoolService.search).toHaveBeenCalledTimes(2);
            expect(schoolService.search).toHaveBeenLastCalledWith('kadı', SCHOOL_SELECT_MAX_OPTIONS, NO_SCOPE);
            expect(component.filtered().map((s) => s.id)).toEqual([3]);
        } finally {
            vi.useRealTimers();
        }
    });

    it('noResultForASearch_KeepsTheField', async () => {
        vi.useFakeTimers();
        try {
            create();
            type('yok böyle');
            await vi.advanceTimersByTimeAsync(SCHOOL_SELECT_DEBOUNCE_MS);
            fixture.detectChanges();

            expect(input()).not.toBeNull();
            expect(el().querySelector('[data-testid="school-select-empty"]')).toBeNull();
        } finally {
            vi.useRealTimers();
        }
    });

    it('externalValueClearedByParent_ClearsTheText', () => {
        create(undefined, { value: 3 });
        fixture.detectChanges();
        expect(input().value).toBe('Kadıköy Anadolu Lisesi');

        fixture.componentRef.setInput('value', null);
        fixture.detectChanges();

        expect(input().value).toBe('');
    });

    it('selectingOption_SetsValueAndEmitsSchool', () => {
        create();
        const emitted: (SchoolOption | null)[] = [];
        const values: (number | null)[] = [];
        component.selectionChange.subscribe((s) => emitted.push(s));
        component.value.subscribe((v) => values.push(v));

        input().dispatchEvent(new Event('focusin'));
        fixture.detectChanges();
        // CDK 21: autocomplete paneli tetikleyicinin yanına native popover olarak eklenir (#545).
        const options = document.querySelectorAll('mat-option');
        expect(options.length).toBe(3);
        (options[0] as HTMLElement).click();
        fixture.detectChanges();

        expect(component.value()).toBe(1);
        expect(values).toEqual([1]);
        expect(emitted.map((s) => s?.id)).toEqual([1]);
        expect(input().value).toBe('Ankara Fen Lisesi');
    });

    it('editingTextAfterSelection_DropsSelection_ShowsPickFromListHint', () => {
        create();
        component.select(2);
        fixture.detectChanges();
        const emitted: (SchoolOption | null)[] = [];
        component.selectionChange.subscribe((s) => emitted.push(s));

        type('İstanbul Erkek');

        expect(component.value()).toBeNull();
        expect(emitted).toEqual([null]);
        expect(input().value).toBe('İstanbul Erkek');
        expect(el().querySelector('[data-testid="school-select-unmatched"]')?.textContent).toContain(trTranslations.shared.schoolSelect.pickFromList);
    });

    it('externalValue_ShowsSchoolName', () => {
        create(undefined, { value: 3 });
        fixture.detectChanges();
        expect(schoolService.getById).toHaveBeenCalledWith(3);
        expect(component.query()).toBe('Kadıköy Anadolu Lisesi');
        expect(input().value).toBe('Kadıköy Anadolu Lisesi');
    });

    it('clear_ResetsValueAndText', () => {
        create();
        component.select(1);
        fixture.detectChanges();

        (el().querySelector('[data-testid="school-select-clear"]') as HTMLButtonElement).click();
        fixture.detectChanges();

        expect(component.value()).toBeNull();
        expect(input().value).toBe('');
    });

    it('excludeSchoolId_RemovesSchoolFromOptions', () => {
        create(undefined, { excludeSchoolId: 2 });
        expect(component.filtered().map((s) => s.id)).toEqual([1, 3]);
    });

    it('i18n_TrAndEnHaveSameKeys', () => {
        expect(Object.keys(enTranslations.shared.schoolSelect).sort()).toEqual(Object.keys(trTranslations.shared.schoolSelect).sort());
    });
    it('unmatchedChange_EmitsOnlyOnStateChanges', () => {
        create();
        const emitted: boolean[] = [];
        component.unmatchedChange.subscribe((v) => emitted.push(v));

        type('Ank');
        type('Anka');
        component.select(1);
        type('Ankara Fen');
        component.clear();

        expect(emitted).toEqual([true, false, true, false]);
    });

    it('province_ScopesTheSearch_DistrictNeedsProvince_issue1106', () => {
        create();
        component.loadProvinces();
        component.loadProvinces();
        expect(schoolService.provinces).toHaveBeenCalledTimes(1);
        expect(component.provinces().map((p) => p.name)).toEqual(['İstanbul']);

        component.onProvinceChange(34);
        fixture.detectChanges();
        expect(schoolService.districts).toHaveBeenCalledWith(34);
        expect(schoolService.search).toHaveBeenLastCalledWith('', SCHOOL_SELECT_MAX_OPTIONS, { provinceId: 34, districtId: null });

        component.districtId.set(7);
        fixture.detectChanges();
        expect(schoolService.search).toHaveBeenLastCalledWith('', SCHOOL_SELECT_MAX_OPTIONS, { provinceId: 34, districtId: 7 });

        component.onProvinceChange(null);
        fixture.detectChanges();
        expect(component.districtId()).toBeNull();
        expect(component.districts()).toEqual([]);
        expect(schoolService.search).toHaveBeenLastCalledWith('', SCHOOL_SELECT_MAX_OPTIONS, NO_SCOPE);
    });

    it('emptyProvince_KeepsTheFieldInsteadOfTheEmptyState_issue1106', () => {
        create((_query) => of([]));
        component.onProvinceChange(34);
        fixture.detectChanges();
        expect(el().querySelector('[data-testid="school-select-empty"]')).toBeNull();
        expect(input()).not.toBeNull();
    });

    it('cappedResult_ShowsMoreNote_issue1106', () => {
        const many = Array.from({ length: SCHOOL_SELECT_MAX_OPTIONS }, (_, i) => school(100 + i, `Okul ${i}`));
        create(() => of(many));
        expect(component.hasMore()).toBe(true);
        TestBed.resetTestingModule();
        create();
        expect(component.hasMore()).toBe(false);
    });
});
