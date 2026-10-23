import { firstValueFrom, of, throwError } from 'rxjs';

import { childTutorProfileCommands, childTutorSearchCommands, findBookingChild, parseBookingChildParam, resolveBookingChild, } from './booking-child-context';
import { LinkedChild } from '../../models/parent-link.model';

/** Issue #439: veli "çocuk adına ders talebi" bağlamı. */
describe('booking-child-context (issue #439)', () => {
    const active: LinkedChild = {
        linkId: 1,
        studentId: 21,
        status: 'Active',
        studentName: 'Ayşe Kaya',
        gradeName: null,
        schoolName: null,
        linkedAt: '2026-10-01T09:00:00Z',
        requestedAt: '2026-10-01T08:00:00Z',
        pendingExpiresAt: null,
    };
    const pending: LinkedChild = { ...active, linkId: 2, studentId: 22, status: 'Pending', studentName: null };

    it('parseBookingChildParam_AcceptsOnlyPositiveIntegers', () => {
        expect(parseBookingChildParam('21')).toBe(21);
        for (const value of [null, undefined, '', '0', '-1', '1.5', 'abc', '99999999999999999999']) {
            expect(parseBookingChildParam(value), String(value)).toBeNull();
        }
    });

    it('Commands_BuildParentRoutes', () => {
        expect(childTutorSearchCommands(21)).toEqual(['/my-children', 21, 'tutors']);
        expect(childTutorProfileCommands(21, 4)).toEqual(['/my-children', 21, 'tutors', 4]);
    });

    it('findBookingChild_OnlyActiveMatchingChild', () => {
        expect(findBookingChild([pending, active], 21, 'X')).toEqual({ studentId: 21, name: 'Ayşe Kaya' });
        expect(findBookingChild([pending], 22, 'X')).toBeNull();
        expect(findBookingChild([active], 99, 'X')).toBeNull();
        expect(findBookingChild(null, 21, 'X')).toBeNull();
        expect(findBookingChild([{ ...active, studentName: ' ' }], 21, 'Çocuğunuz')?.name).toBe('Çocuğunuz');
    });

    it('resolveBookingChild_404MeansNoChild_OtherErrorsPropagate', async () => {
        expect(await firstValueFrom(resolveBookingChild(of([active]), 21, ''))).toEqual({ studentId: 21, name: 'Ayşe Kaya' });
        expect(await firstValueFrom(resolveBookingChild(throwError(() => ({ status: 404 })), 21, ''))).toBeNull();
        await expect(firstValueFrom(resolveBookingChild(throwError(() => ({ status: 500 })), 21, ''))).rejects.toThrow();
    });
});
