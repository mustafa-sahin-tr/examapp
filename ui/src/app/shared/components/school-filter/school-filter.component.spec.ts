import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { Observable, Subject, of, throwError } from 'rxjs';

import { SCHOOL_FILTER_DEBOUNCE_MS, SCHOOL_FILTER_MAX_OPTIONS, SchoolFilterComponent } from './school-filter.component';
import { SchoolFilterValue, schoolFilterFromParams, schoolFilterToQuery } from './school-filter.model';
import { SchoolService } from '../../../services/school.service';
import { SchoolOption } from '../../../models/taxonomy';
import { translocoTestingModule } from '../../testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';

describe('SchoolFilterComponent', () => {
    let fixture: ComponentFixture<SchoolFilterComponent>;
    let schoolService: MockedObject<SchoolService>;
    let emitted: SchoolFilterValue[];

    const school = (id: number, name: string): SchoolOption => ({
        id,
        name,
        provinceId: null,
        provinceName: null,
        districtId: null,
        districtName: null,
    });

    function create(value: SchoolFilterValue, getById: () => Observable<SchoolOption> = () => of(school(5, 'Ankara Lisesi')), unassignedLabel?: 'unassigned' | 'unassignedStudent'): void {
        schoolService = {
            search: vi.fn().mockReturnValue(of([school(5, 'Ankara Lisesi')])),
            getById: vi.fn().mockImplementation(getById),
        } as unknown as MockedObject<SchoolService>;
        TestBed.configureTestingModule({
            imports: [
                SchoolFilterComponent,
                translocoTestingModule({ langs: { 'admin/tr': adminTr }, translocoConfig: { scopes: { keepCasing: true } } }),
            ],
            providers: [{ provide: SchoolService, useValue: schoolService }, provideNoopAnimations()],
        });
        fixture = TestBed.createComponent(SchoolFilterComponent);
        fixture.componentRef.setInput('value', value);
        if (unassignedLabel)
            fixture.componentRef.setInput('unassignedLabel', unassignedLabel);
        emitted = [];
        fixture.componentInstance.value.subscribe((v) => emitted.push(v));
        fixture.detectChanges();
    }

    const input = (): HTMLInputElement => fixture.nativeElement.querySelector('[data-testid="school-filter-input"]');

    it('unknownSchoolId_Lookup404_ResetsToAllAndEmits', () => {
        create(999, () => throwError(() => new HttpErrorResponse({ status: 404 })));

        expect(fixture.componentInstance.value()).toBe('all');
        expect(emitted).toEqual(['all']);
    });

    it('knownSchoolId_ShowsItsNameWithoutEmitting', () => {
        create(5);
        fixture.detectChanges();

        expect(fixture.componentInstance.value()).toBe(5);
        expect(input().value).toBe('Ankara Lisesi');
        expect(emitted).toEqual([]);
    });

    it('schoolLookupFailsForAnotherReason_KeepsValue', () => {
        create(5, () => throwError(() => new HttpErrorResponse({ status: 500 })));

        expect(fixture.componentInstance.value()).toBe(5);
        expect(emitted).toEqual([]);
    });

    it('schoolIdWhileLookupPending_KeepsValue', () => {
        create(999, () => new Subject<SchoolOption>());

        expect(fixture.componentInstance.value()).toBe(999);
        expect(emitted).toEqual([]);
    });

    it('typing_SearchesOnTheServer_AfterDebounce', async () => {
        vi.useFakeTimers();
        try {
            create('all');
            input().value = 'kadikoy';
            input().dispatchEvent(new Event('input'));
            fixture.detectChanges();
            expect(schoolService.search).not.toHaveBeenCalledWith('kadikoy', SCHOOL_FILTER_MAX_OPTIONS);

            await vi.advanceTimersByTimeAsync(SCHOOL_FILTER_DEBOUNCE_MS);

            expect(schoolService.search).toHaveBeenLastCalledWith('kadikoy', SCHOOL_FILTER_MAX_OPTIONS);
        } finally {
            vi.useRealTimers();
        }
    });

    it('closingPanelWithoutPicking_RestoresTheSelectedLabel', () => {
        create('all');
        input().value = 'yarım';
        input().dispatchEvent(new Event('input'));
        fixture.detectChanges();

        fixture.componentInstance.onClosed();
        fixture.detectChanges();

        expect(input().value).toBe(adminTr.schoolFilter.all);
    });

    it('pickingASchool_SetsItsId', () => {
        create('all');
        fixture.componentInstance.onSelected({ option: { value: 5 } } as MatAutocompleteSelectedEvent);
        fixture.detectChanges();

        expect(emitted).toEqual([5]);
        expect(input().value).toBe('Ankara Lisesi');
    });

    it('schoolFilterToQuery_MapsEachValue', () => {
        expect(schoolFilterToQuery('all')).toEqual({ schoolId: null, unassigned: false });
        expect(schoolFilterToQuery('unassigned')).toEqual({ schoolId: null, unassigned: true });
        expect(schoolFilterToQuery(5)).toEqual({ schoolId: 5, unassigned: false });
    });

    it('schoolFilterFromParams_InvalidValues_FallBackToAll', () => {
        expect(schoolFilterFromParams(null, null)).toBe('all');
        expect(schoolFilterFromParams('abc', null)).toBe('all');
        expect(schoolFilterFromParams('-3', null)).toBe('all');
        expect(schoolFilterFromParams('5', null)).toBe(5);
        expect(schoolFilterFromParams('5', 'true')).toBe('unassigned');
    });

    function optionTexts(): string[] {
        input().dispatchEvent(new Event('focusin'));
        fixture.detectChanges();
        // CDK 21: bağlı paneller OverlayContainer'a değil, tetikleyicinin yanına native popover olarak eklenir (#545).
        return Array.from(document.querySelectorAll('mat-option')).map((o) => (o.textContent ?? '').trim());
    }

    it('unassignedLabel_Default_ShowsIndependentOrNoSchool', () => {
        create('all');
        expect(optionTexts()).toEqual([adminTr.schoolFilter.all, adminTr.schoolFilter.unassigned, 'Ankara Lisesi']);
    });

    it('unassignedLabel_Student_ShowsNoSchool', () => {
        create('all', undefined, 'unassignedStudent');
        expect(optionTexts()).toEqual([adminTr.schoolFilter.all, adminTr.schoolFilter.unassignedStudent, 'Ankara Lisesi']);
    });
});
