import type { Mock, MockedObject } from "vitest";
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { ParentRequiredComponent } from './parent-required.component';
import { ParentStatus } from '../../models/parent-status.model';
import { LocaleService } from '../../services/locale.service';
import { ParentStatusStore } from '../../services/parent-status.store';
import { StudentParentService } from '../../services/student-parent.service';
import { awaitingStatusOf, invitationOf, parentStatusOf } from '../../shared/testing/parent-status-testing';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';

/** Issue #440 / #441: "veli bilgisi ekle" ekranı — form sonrası akış. */
describe('ParentRequiredComponent', () => {
    let store: ParentStatusStore;
    let service: {
        reload: Mock;
        startInvitation: Mock;
        resend: Mock;
    };
    let router: Router;
    let snackBar: MockedObject<MatSnackBar>;

    function create(status: ParentStatus | null, reload$?: Observable<ParentStatus | null>): ComponentFixture<ParentRequiredComponent> {
        service = {
            reload: vi.fn().mockName('reload').mockImplementation(() => {
                if (reload$)
                    return reload$;
                if (status)
                    store.set(status);
                return of(status);
            }),
            startInvitation: vi.fn().mockName('startInvitation'),
            resend: vi.fn().mockName('resend'),
        };
        snackBar = {
            open: vi.fn().mockName("MatSnackBar.open")
        } as unknown as MockedObject<MatSnackBar>;
        TestBed.configureTestingModule({
            imports: [ParentRequiredComponent, translocoTestingModule(), NoopAnimationsModule],
            providers: [
                provideRouter([]),
                { provide: StudentParentService, useValue: service },
                { provide: MatSnackBar, useValue: snackBar },
                { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: 'tr-TR' }) } },
            ],
        });
        store = TestBed.inject(ParentStatusStore);
        router = TestBed.inject(Router);
        vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
        const fixture = TestBed.createComponent(ParentRequiredComponent);
        fixture.detectChanges();
        return fixture;
    }

    function q(fixture: ComponentFixture<ParentRequiredComponent>, selector: string): HTMLElement | null {
        return fixture.nativeElement.querySelector(selector) as HTMLElement | null;
    }

    function fill(fixture: ComponentFixture<ParentRequiredComponent>, phone = '0532 123 45 67'): void {
        fixture.componentInstance.form.setValue({
            parentFullName: ' Ayşe Yılmaz ',
            parentEmail: 'ayse@example.com ',
            parentPhone: phone,
        });
    }

    it('expired_ShowsExpiredNoticeAndForm', () => {
        const fixture = create(parentStatusOf({ mode: 'Expired', daysLeft: 0, isFinalWarning: true }));

        expect(service.reload).toHaveBeenCalled();
        expect(q(fixture, '[data-test="prq-expired"]')?.textContent).toContain('verilerin silinmedi');
        expect(q(fixture, '[data-test="prq-form"]')).not.toBeNull();
    });

    it('grace_ShowsDaysLeft', () => {
        const fixture = create(parentStatusOf({ mode: 'Grace', daysLeft: 9 }));
        expect(q(fixture, '[data-test="prq-grace"]')?.textContent).toContain('9 gün kaldı');
    });

    it('awaiting_StatusCardWithSentDaysLeftAndClosedFeatures_FormHidden_issue892', () => {
        const sent = new Date();
        sent.setDate(sent.getDate() - 3);
        const fixture = create({ ...awaitingStatusOf({ lastSentAt: sent.toISOString() }), daysLeft: 11 });

        expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Velinin onayı bekleniyor');
        expect(q(fixture, '[data-test="prq-sent"]')?.textContent?.trim()).toBe('a***@g***.com adresine 3 gün önce gönderildi');
        expect(q(fixture, '[data-test="prq-days-left"]')?.textContent?.trim()).toBe('11 gün; sonra hesabın kilitlenir');
        expect(q(fixture, '[data-test="prq-link"]')?.textContent).toContain('tarihine kadar geçerli');
        expect(q(fixture, '[data-test="prq-closed"]')?.textContent).toContain('Mesajlar');
        expect(q(fixture, '[data-test="prq-resend"]')).not.toBeNull();
        expect(q(fixture, '[data-test="prq-preview"]')).not.toBeNull();
        // Form yalnız "Başka bir adres gir" ile açılır; ad yine doldurulmuş gelir.
        expect(q(fixture, '[data-test="prq-form"]')).toBeNull();
        (q(fixture, '[data-test="prq-other-address"]') as HTMLButtonElement).click();
        fixture.detectChanges();
        expect(q(fixture, '[data-test="prq-form"]')).not.toBeNull();
        expect(q(fixture, '[data-test="prq-other-warning"]')).not.toBeNull();
        expect(fixture.componentInstance.form.controls.parentFullName.value).toBe('Ayşe Yılmaz');
    });

    it('awaiting_ResendToSameAddress_StaysOnPage_issue892', () => {
        const fixture = create(awaitingStatusOf());
        service.resend.mockImplementation(() => of({ message: 'ok', status: awaitingStatusOf() }));

        (q(fixture, '[data-test="prq-resend"]') as HTMLButtonElement).click();
        fixture.detectChanges();

        expect(service.resend).toHaveBeenCalled();
        expect(snackBar.open).toHaveBeenCalled();
        expect(router.navigateByUrl).not.toHaveBeenCalled();
    });

    it('awaiting_ResendNotYetAllowed_ShowsNextTime_issue892', () => {
        const fixture = create(awaitingStatusOf({ canResend: false, nextResendAt: '2026-11-08T10:00:00Z', resendsLeft: 4 }));
        expect(q(fixture, '[data-test="prq-resend"]')).toBeNull();
        expect(q(fixture, '[data-test="prq-next-resend"]')?.textContent).toContain('Bir sonraki gönderim');
    });

    it('awaiting_LinkExpired_SaysSoInTheStatusCard_issue892', () => {
        const fixture = create(awaitingStatusOf({ expired: true }));
        const link = q(fixture, '[data-test="prq-link"]')!;
        expect(link.textContent).toContain('Süresi doldu');
        expect(link.classList).toContain('prq__fact--danger');
    });

    it('awaiting_EmailLocked_ShowsLockNotice', () => {
        const fixture = create(awaitingStatusOf({ emailChangeLockedUntil: new Date(Date.now() + 3600000).toISOString() }));
        expect(q(fixture, '[data-test="prq-locked"]')).not.toBeNull();
    });

    it('ok_NoFormAndDashboardLink', () => {
        const fixture = create(parentStatusOf({ mode: 'Ok' }));
        expect(q(fixture, '[data-test="prq-ok"]')).not.toBeNull();
        expect(q(fixture, '[data-test="prq-form"]')).toBeNull();
    });

    it('loadError_ShowsRetry', () => {
        const fixture = create(null, throwError(() => new HttpErrorResponse({ status: 500 })));
        expect(q(fixture, '[data-test="prq-error"]')).not.toBeNull();
    });

    it('submit_InvalidPhone_DoesNotCallServer', () => {
        const fixture = create(parentStatusOf({ mode: 'Expired' }));
        fill(fixture, '0212 123 45 67');

        fixture.componentInstance.submit();
        fixture.detectChanges();

        expect(service.startInvitation).not.toHaveBeenCalled();
        expect(q(fixture, '[data-test="prq-phone-invalid"]')).not.toBeNull();
    });

    it('submit_Valid_SendsTrimmedContact_ThenGoesToDashboardInRestrictedMode', () => {
        const fixture = create(parentStatusOf({ mode: 'Expired' }));
        service.startInvitation.mockImplementation(() => {
            store.set(awaitingStatusOf());
            return of({ message: 'ok', status: awaitingStatusOf() });
        });
        fill(fixture);

        fixture.componentInstance.submit();

        expect(service.startInvitation).toHaveBeenCalledWith({
            parentFullName: 'Ayşe Yılmaz',
            parentEmail: 'ayse@example.com',
            parentPhone: '0532 123 45 67',
        });
        expect(snackBar.open).toHaveBeenCalledWith('Velinize davet gönderildi.', expect.any(String), expect.any(Object));
        expect(router.navigateByUrl).toHaveBeenCalledWith('/dashboard');
        expect(store.activationRestricted()).toBe(true);
    });

    it('submit_EmailNotUsable409_ShowsMappedMessageAndStays', () => {
        const fixture = create(parentStatusOf({ mode: 'Grace', daysLeft: 3 }));
        service.startInvitation.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { errorCode: 'ParentEmailNotUsable', message: 'x' } })));
        fill(fixture);

        fixture.componentInstance.submit();
        fixture.detectChanges();

        expect(q(fixture, '[data-test="prq-submit-error"]')?.textContent).toContain('Bu e-posta veli hesabı olarak kullanılamaz.');
        expect(router.navigateByUrl).not.toHaveBeenCalled();
    });

    it('submit_EmailChangeLocked429_ShowsRetryDate', () => {
        const fixture = create(awaitingStatusOf());
        service.startInvitation.mockReturnValue(throwError(() => new HttpErrorResponse({
            status: 429,
            error: { errorCode: 'ParentEmailChangeLocked', retryAt: '2026-10-15T12:00:00Z', message: 'x' },
        })));
        fixture.componentInstance.otherAddress.set(true); // #892: davet beklerken form "Başka bir adres gir"le açılır
        fill(fixture);

        fixture.componentInstance.submit();
        fixture.detectChanges();

        const text = q(fixture, '[data-test="prq-submit-error"]')?.textContent ?? '';
        expect(text).toContain('Çok sayıda farklı e-posta denendi.');
        expect(text).toContain('2026');
    });

    // #440 review: süresi dolmuş davetle Expired — davet + yeniden gönder + e-postayı düzelt formu.
    it('expiredWithInvitation_ShowsInvitationResendAndEditForm', () => {
        const fixture = create(parentStatusOf({ mode: 'Expired', daysLeft: 0, isFinalWarning: true, invitation: invitationOf({ expired: true }) }));

        expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Velinin e-postasını düzelt');
        expect(q(fixture, '[data-test="prq-expired-invitation"]')?.textContent).toContain('a***@g***.com');
        expect(q(fixture, '[data-test="prq-resend"]')).not.toBeNull();
        expect(q(fixture, '[data-test="prq-form"]')).not.toBeNull();
        expect(fixture.componentInstance.form.controls.parentFullName.value).toBe('Ayşe Yılmaz');
    });

    it('expiredWithInvitation_ResendReopensInvitation_GoesToDashboard', () => {
        const fixture = create(parentStatusOf({ mode: 'Expired', invitation: invitationOf({ expired: true }) }));
        service.resend.mockImplementation(() => {
            store.set(awaitingStatusOf());
            return of({ message: 'ok', status: awaitingStatusOf() });
        });

        (q(fixture, '[data-test="prq-resend"]') as HTMLButtonElement).click();
        fixture.detectChanges();

        expect(router.navigateByUrl).toHaveBeenCalledWith('/dashboard');
    });

    it('expiredWithInvitation_NoResendsLeft_HidesResend', () => {
        const fixture = create(parentStatusOf({ mode: 'Expired', invitation: invitationOf({ expired: true, canResend: false, resendsLeft: 0 }) }));
        expect(q(fixture, '[data-test="prq-resend"]')).toBeNull();
        expect(q(fixture, '[data-test="prq-no-resends"]')).not.toBeNull();
    });

    it('submit_Busy409_ShowsTryAgainMessage', () => {
        const fixture = create(awaitingStatusOf());
        service.startInvitation.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { errorCode: 'Busy', message: 'x' } })));
        fixture.componentInstance.otherAddress.set(true); // #892: davet beklerken form "Başka bir adres gir"le açılır
        fill(fixture);

        fixture.componentInstance.submit();
        fixture.detectChanges();

        expect(q(fixture, '[data-test="prq-submit-error"]')?.textContent).toContain('Veliniz şu anda hesabını etkinleştiriyor, biraz sonra tekrar deneyin.');
    });

    it('nameValidation_LinkLikeName_Rejected', () => {
        const fixture = create(parentStatusOf({ mode: 'Expired' }));
        fixture.componentInstance.form.setValue({ parentFullName: 'www.ornek.com', parentEmail: 'a@b.co', parentPhone: '05321234567' });
        fixture.componentInstance.form.markAllAsTouched();
        fixture.detectChanges();

        expect(fixture.componentInstance.form.controls.parentFullName.hasError('parentName')).toBe(true);
        expect(fixture.nativeElement.textContent).toContain('Veli adı yalnız harf içerebilir');
    });
});
