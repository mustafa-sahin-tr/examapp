import type { Mock } from "vitest";
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, ParamMap, Router, convertToParamMap } from '@angular/router';
import { BehaviorSubject, Observable, of, throwError } from 'rxjs';
import { Paged } from '../../models/test-instance';
import { AdminSchoolPagedQuery } from '../../models/admin-paged-query.model';
import { SchoolPagedList, adminListErrorMessage } from './school-paged-list';

describe('adminListErrorMessage', () => {
    const text = (key: 'forbidden' | 'loadFailed' | 'rateLimited'): string => `[${key}]`;

    it('status403_ReturnsForbiddenText', () => {
        expect(adminListErrorMessage(new HttpErrorResponse({ status: 403 }), text)).toBe('[forbidden]');
    });

    it('status400WithStringBody_ReturnsBackendMessage', () => {
        const err = new HttpErrorResponse({ status: 400, error: 'schoolId ve unassigned birlikte kullanılamaz.' });
        expect(adminListErrorMessage(err, text)).toBe('schoolId ve unassigned birlikte kullanılamaz.');
    });

    it('status400WithBlankBody_ReturnsLoadFailed', () => {
        expect(adminListErrorMessage(new HttpErrorResponse({ status: 400, error: '   ' }), text)).toBe('[loadFailed]');
    });

    it('status429_ReturnsRateLimitedText', () => {
        const err = new HttpErrorResponse({ status: 429, error: 'Too many list requests' });
        expect(adminListErrorMessage(err, text)).toBe('[rateLimited]');
    });

    it('status500_ReturnsLoadFailed', () => {
        expect(adminListErrorMessage(new HttpErrorResponse({ status: 500, error: 'boom' }), text)).toBe('[loadFailed]');
    });
});

describe('SchoolPagedList', () => {
    let queryParams$: BehaviorSubject<ParamMap>;
    let navigate: Mock;
    let fetch: Mock;

    const paged = (items: string[], totalCount = items.length): Paged<string> => ({
        pageNumber: 1,
        pageSize: 20,
        totalCount,
        items,
    });

    function create(params: Record<string, string> = {}): SchoolPagedList<string> {
        queryParams$ = new BehaviorSubject<ParamMap>(convertToParamMap(params));
        navigate = vi.fn().mockName('navigate').mockResolvedValue(true);
        TestBed.configureTestingModule({
            providers: [
                { provide: ActivatedRoute, useValue: { queryParamMap: queryParams$.asObservable() } },
                { provide: Router, useValue: { navigate } },
            ],
        });
        return TestBed.runInInjectionContext(() => new SchoolPagedList<string>({
            fetch: (q) => fetch(q),
            errorMessage: () => 'hata',
        }));
    }

    beforeEach(() => {
        fetch = vi.fn().mockName('fetch');
        fetch.mockReturnValue(of(paged(['a', 'b'], 45)));
    });

    it('queryParams_DriveFetchAndState', () => {
        const list = create({ schoolId: '5', page: '2', pageSize: '50' });

        expect(fetch).toHaveBeenCalledTimes(1);

        expect(fetch).toHaveBeenCalledWith({ page: 2, pageSize: 50, schoolId: 5, unassigned: false });
        expect(list.filter()).toBe(5);
        expect(list.items()).toEqual(['a', 'b']);
        expect(list.totalCount()).toBe(45);
        expect(list.loading()).toBe(false);
    });

    it('onFilterChange_NavigatesToFirstPageWithoutFetching', () => {
        const list = create({ page: '3' });
        fetch.mockClear();

        list.onFilterChange('unassigned');

        expect(fetch).not.toHaveBeenCalled();
        expect(navigate).toHaveBeenCalledWith([], expect.objectContaining({
            queryParams: { schoolId: null, unassigned: 'true', page: null, pageSize: null },
        }));
    });

    it('fetchError_SetsErrorAndClearsItems', () => {
        fetch.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
        const list = create();

        expect(list.error()).toBe('hata');
        expect(list.items()).toEqual([]);
        expect(list.isEmpty()).toBe(false);
    });

    it('pageBeyondLast_NavigatesToLastPage', () => {
        fetch.mockReturnValue(of(paged([], 45)));
        create({ page: '9' });

        expect(navigate).toHaveBeenCalledWith([], expect.objectContaining({ queryParams: expect.objectContaining({ page: 3 }) }));
    });
});
