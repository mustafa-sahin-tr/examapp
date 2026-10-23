import type { MockedObject } from "vitest";
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarRef, TextOnlySnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';

import { TutorPublicProfileComponent } from './tutor-public-profile.component';
import { TUTOR_SEARCH_SCOPE } from './tutor-search-scope';
import { BookingResult } from '../../models/booking.model';
import { LinkedChild } from '../../models/parent-link.model';
import { TutorPublicProfile } from '../../models/tutor.model';
import { LocaleService } from '../../services/locale.service';
import { ParentLinkService } from '../../services/parent-link.service';
import { TeacherService } from '../../services/teacher.service';
import { BookingSlotDialogData } from '../../shared/components/booking-slot-dialog/booking-slot-dialog.component';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import tutorSearchTr from '../../../../public/i18n/tutor-search/tr.json';
import tutorSearchEn from '../../../../public/i18n/tutor-search/en.json';

/** Issue #439: öğrenci "veli onayına gönderildi" bildirimi ve veli modunda (çocuk adına) talep. */
describe('TutorPublicProfileComponent (issue #439)', () => {
    let fixture: ComponentFixture<TutorPublicProfileComponent>;
    let dialog: MockedObject<MatDialog>;
    let snackBar: MockedObject<MatSnackBar>;
    let linkService: MockedObject<ParentLinkService>;
    let snackAction: Subject<void>;

    const profile = { teacherId: 4, fullName: 'Elif Hoca', subjects: [], teachesOnline: true, teachesInPerson: false,
        hourlyRate: null, bio: null, avatar: null } as unknown as TutorPublicProfile;

    const child: LinkedChild = {
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

    async function setup(params: Record<string, string>, dialogResult: BookingResult | false, children: LinkedChild[] = [child]) {
        const teacherService = {
            getTutorPublicProfile: vi.fn().mockName("TeacherService.getTutorPublicProfile")
        };
        teacherService.getTutorPublicProfile.mockReturnValue(of(profile));
        linkService = {
            getMyChildren: vi.fn().mockName("ParentLinkService.getMyChildren")
        } as unknown as MockedObject<ParentLinkService>;
        linkService.getMyChildren.mockReturnValue(of(children));
        dialog = {
            open: vi.fn().mockName("MatDialog.open")
        } as unknown as MockedObject<MatDialog>;
        dialog.open.mockReturnValue({ afterClosed: () => of(dialogResult) } as ReturnType<MatDialog['open']>);
        snackAction = new Subject<void>();
        snackBar = {
            open: vi.fn().mockName("MatSnackBar.open")
        } as unknown as MockedObject<MatSnackBar>;
        snackBar.open.mockReturnValue({ onAction: () => snackAction } as unknown as MatSnackBarRef<TextOnlySnackBar>);

        TestBed.configureTestingModule({
            imports: [
                TutorPublicProfileComponent,
                NoopAnimationsModule,
                translocoTestingModule({
                    langs: { [`${TUTOR_SEARCH_SCOPE}/tr`]: tutorSearchTr, [`${TUTOR_SEARCH_SCOPE}/en`]: tutorSearchEn },
                }),
            ],
            providers: [
                provideRouter([]),
                { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap(params) } } },
                { provide: TeacherService, useValue: teacherService },
                { provide: ParentLinkService, useValue: linkService },
                { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: 'tr-TR' }) } },
            ],
        });
        TestBed.overrideProvider(MatDialog, { useValue: dialog });
        TestBed.overrideProvider(MatSnackBar, { useValue: snackBar });
        fixture = TestBed.createComponent(TutorPublicProfileComponent);
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
        return fixture.nativeElement as HTMLElement;
    }

    const bookButton = (el: HTMLElement) => el.querySelector('[data-test="book"]') as HTMLButtonElement;

    it('Student_AwaitingParentResult_ToastsSentToParent', async () => {
        const el = await setup({ id: '4' }, {
            success: true,
            message: '',
            booking: { status: 'AwaitingParent' } as BookingResult['booking'],
        });
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

        expect(linkService.getMyChildren).not.toHaveBeenCalled();
        expect(el.querySelector('[data-test="child-banner"]')).toBeNull();
        bookButton(el).click();

        const data = vi.mocked(dialog.open).mock.lastCall![1]?.data as BookingSlotDialogData;
        expect(data.child).toBeNull();
        expect(snackBar.open).toHaveBeenCalledWith('Talebin velinin onayına gönderildi.', 'Randevularım', expect.any(Object));
        snackAction.next();
        expect(navigate).toHaveBeenCalledWith(['/my-bookings'], undefined);
    });

    it('Student_PendingResult_KeepsOriginalToast', async () => {
        const el = await setup({ id: '4' }, { success: true, message: '', booking: { status: 'Pending' } as BookingResult['booking'] });

        bookButton(el).click();

        expect(snackBar.open).toHaveBeenCalledWith('Randevu talebin gönderildi, öğretmenin onayı bekleniyor.', 'Randevularım', expect.any(Object));
    });

    it('ChildMode_PassesChildToDialog_ToastsSentToTeacher_ActionOpensParentPanel', async () => {
        const el = await setup({ id: '4', studentId: '21' }, {
            success: true,
            message: '',
            booking: { status: 'Pending' } as BookingResult['booking'],
        });
        const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

        expect(el.querySelector('[data-test="child-banner"]')?.textContent).toContain('Ayşe Kaya adına ders talebi');
        expect(bookButton(el).textContent).toContain('Ders Talebi Oluştur');
        bookButton(el).click();

        const data = vi.mocked(dialog.open).mock.lastCall![1]?.data as BookingSlotDialogData;
        expect(data.child).toEqual({ studentId: 21, name: 'Ayşe Kaya' });
        expect(snackBar.open).toHaveBeenCalledWith('Talep öğretmene gönderildi.', 'Veli paneli', expect.any(Object));
        snackAction.next();
        expect(navigate).toHaveBeenCalledWith(['/parent'], { queryParams: { child: 21 } });
    });

    it('ChildMode_ChildNotLinked_DisablesBooking', async () => {
        const el = await setup({ id: '4', studentId: '99' }, false);

        expect(el.querySelector('[data-test="child-missing"]')).not.toBeNull();
        expect(bookButton(el).disabled).toBe(true);
    });

    it('ChildMode_ChildrenLoadFails_ShowsRetryAndDisablesBooking', async () => {
        const el = await setup({ id: '4', studentId: '21' }, false);
        linkService.getMyChildren.mockReturnValue(throwError(() => ({ status: 500 })));
        fixture.componentInstance.loadChild();
        fixture.detectChanges();

        expect(el.querySelector('[data-test="child-banner"] [role="alert"]')?.textContent).toContain('yüklenemedi');
        expect(bookButton(el).disabled).toBe(true);
    });

    it('Cancelled_NoToast', async () => {
        const el = await setup({ id: '4' }, false);

        bookButton(el).click();

        expect(snackBar.open).not.toHaveBeenCalled();
    });
});
