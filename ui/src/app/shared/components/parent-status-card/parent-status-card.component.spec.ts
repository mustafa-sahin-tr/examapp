import type { Mock } from "vitest";
import { BreakpointObserver } from '@angular/cdk/layout';
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatBottomSheet } from '@angular/material/bottom-sheet';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { PARENT_GRACE_BANNER_DISMISS_KEY, ParentStatusCardComponent } from './parent-status-card.component';
import { ParentStatus } from '../../../models/parent-status.model';
import { AuthService } from '../../../services/auth.service';
import { LocaleService } from '../../../services/locale.service';
import { ParentStatusStore } from '../../../services/parent-status.store';
import { StudentParentService } from '../../../services/student-parent.service';
import { awaitingStatusOf, parentStatusOf } from '../../testing/parent-status-testing';
import { translocoTestingModule } from '../../testing/transloco-testing';

/** Issue #440 / #441: öğrenci panelindeki davet kartı ve hoşgörü banner'ı. */
describe('ParentStatusCardComponent', () => {
    const USER_ID = 77;
    const dismissKey = `${PARENT_GRACE_BANNER_DISMISS_KEY}:${USER_ID}`;
    let store: ParentStatusStore;
    let service: {
        resend: Mock;
        reload: Mock;
    };

    let sheet: { open: Mock };

    function create(status: ParentStatus | null, mobile = false): ComponentFixture<ParentStatusCardComponent> {
        sheet = { open: vi.fn().mockName('open') };
        service = {
            resend: vi.fn().mockName('resend'),
            reload: vi.fn().mockName('reload').mockReturnValue(of(null)),
        };
        TestBed.configureTestingModule({
            imports: [ParentStatusCardComponent, translocoTestingModule()],
            providers: [
                provideRouter([]),
                { provide: StudentParentService, useValue: service },
                { provide: AuthService, useValue: { user: signal({ id: USER_ID }) } },
                { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: 'tr-TR' }) } },
                { provide: BreakpointObserver, useValue: { observe: () => of({ matches: mobile, breakpoints: {} }) } },
                { provide: MatBottomSheet, useValue: sheet },
            ],
        });
        store = TestBed.inject(ParentStatusStore);
        store.set(status);
        const fixture = TestBed.createComponent(ParentStatusCardComponent);
        fixture.detectChanges();
        return fixture;
    }

    function q(fixture: ComponentFixture<ParentStatusCardComponent>, selector: string): HTMLElement | null {
        return fixture.nativeElement.querySelector(selector) as HTMLElement | null;
    }

    beforeEach(() => sessionStorage.removeItem(dismissKey));
    afterEach(() => sessionStorage.removeItem(dismissKey));

    it('noStatusOrOk_RendersNothing', () => {
        expect(create(null).nativeElement.textContent.trim()).toBe('');
        TestBed.resetTestingModule();
        expect(create(parentStatusOf({ mode: 'Ok' })).nativeElement.textContent.trim()).toBe('');
    });

    it('awaiting_ShowsMaskedEmailExpiryAndRestrictedInfo', () => {
        const fixture = create(awaitingStatusOf());

        expect(q(fixture, '[data-test="invitation-sent-to"]')?.textContent).toContain('a***@g***.com');
        expect(fixture.nativeElement.textContent).toContain('Velinize davet gönderildi');
        expect(fixture.nativeElement.textContent).toContain('özel ders talebi, mesajlaşma');
        expect(q(fixture, '[data-test="invitation-fix-email"]')?.getAttribute('href')).toBe('/parent-required');
        expect(q(fixture, '[data-test="invitation-dev-link"]')).toBeNull();
    });

    it('awaitingExpired_ShowsExpiredResendPrompt', () => {
        const fixture = create(awaitingStatusOf({ expired: true }));
        expect(q(fixture, '[data-test="invitation-expired"]')?.textContent).toContain('Davet süresi doldu, yeniden gönder');
    });

    it('resend_NotAllowed_ButtonDisabledWithNextDate', () => {
        const fixture = create(awaitingStatusOf({ canResend: false, nextResendAt: '2026-10-16T09:00:00Z', resendsLeft: 3 }));

        expect((q(fixture, '[data-test="invitation-resend"]') as HTMLButtonElement).disabled).toBe(true);
        expect(q(fixture, '[data-test="invitation-next-resend"]')?.textContent).toContain('2026');
        expect(q(fixture, '[data-test="invitation-resends-left"]')?.textContent).toContain('3');
    });

    it('resend_NoResendsLeft_ShowsExhaustedHint', () => {
        const fixture = create(awaitingStatusOf({ canResend: false, resendsLeft: 0 }));
        expect(q(fixture, '[data-test="invitation-no-resends"]')).not.toBeNull();
    });

    it('resend_Allowed_CallsServiceAndShowsConfirmation', () => {
        const fixture = create(awaitingStatusOf({ canResend: true }));
        service.resend.mockReturnValue(of({ message: 'ok', status: awaitingStatusOf({ canResend: false }) }));

        (q(fixture, '[data-test="invitation-resend"]') as HTMLButtonElement).click();
        fixture.detectChanges();

        expect(service.resend).toHaveBeenCalled();
        expect(q(fixture, '[data-test="invitation-resent"]')).not.toBeNull();
    });

    it('resend_RateLimited_ShowsMessageAndReloads', () => {
        const fixture = create(awaitingStatusOf({ canResend: true }));
        service.resend.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 429, error: { errorCode: 'ParentInvitationResendLimited', retryAt: null } })));

        fixture.componentInstance.resend();
        fixture.detectChanges();

        expect(q(fixture, '[data-test="invitation-resend-error"]')?.textContent).toContain('yeniden gönderim hakkı doldu');
        expect(service.reload).toHaveBeenCalled();
    });

    it('emailLocked_HidesFixEmailAndShowsLock', () => {
        const fixture = create(awaitingStatusOf({ emailChangeLockedUntil: new Date(Date.now() + 3600000).toISOString() }));
        expect(q(fixture, '[data-test="invitation-email-locked"]')).not.toBeNull();
        expect(q(fixture, '[data-test="invitation-fix-email"]')).toBeNull();
    });

    it('devActivationUrl_ShownOnlyWhenPresentAndSafe', () => {
        const fixture = create(awaitingStatusOf({ devActivationUrl: 'http://localhost:5678/app/parent-activate?token=t' }));
        const link = q(fixture, '[data-test="invitation-dev-link"] a');
        expect(link?.textContent).toContain('dev: aktivasyon bağlantısı');
        expect(link?.getAttribute('href')).toBe('http://localhost:5678/app/parent-activate?token=t');

        TestBed.resetTestingModule();
        const unsafe = create(awaitingStatusOf({ devActivationUrl: 'javascript:alert(1)' }));
        expect(q(unsafe, '[data-test="invitation-dev-link"]')).toBeNull();
    });

    it('grace_ShowsDismissibleBannerWithDaysAndLink', () => {
        const fixture = create(parentStatusOf({ mode: 'Grace', daysLeft: 14 }));

        const banner = q(fixture, '[data-test="parent-grace-banner"]');
        expect(banner?.textContent).toContain('Velini ekle, 14 gün kaldı');
        expect(q(fixture, '[data-test="grace-banner-link"]')?.getAttribute('href')).toBe('/parent-required');
    });

    it('grace_Dismiss_HidesForSession_AndStaysHiddenOnRecreate', () => {
        const fixture = create(parentStatusOf({ mode: 'Grace', daysLeft: 14 }));
        (q(fixture, '[data-test="grace-banner-dismiss"]') as HTMLButtonElement).click();
        fixture.detectChanges();

        expect(q(fixture, '[data-test="parent-grace-banner"]')).toBeNull();
        expect(sessionStorage.getItem(dismissKey)).toBe('1');

        TestBed.resetTestingModule();
        const again = create(parentStatusOf({ mode: 'Grace', daysLeft: 14 }));
        expect(q(again, '[data-test="parent-grace-banner"]')).toBeNull();
    });

    it('grace_NewSession_BannerBack', () => {
        sessionStorage.removeItem(dismissKey);
        const fixture = create(parentStatusOf({ mode: 'Grace', daysLeft: 14 }));
        expect(q(fixture, '[data-test="parent-grace-banner"]')).not.toBeNull();
    });

    it('graceFinalWarning_NoBanner (modal takes over)', () => {
        const fixture = create(parentStatusOf({ mode: 'Grace', daysLeft: 5, isFinalWarning: true }));
        expect(q(fixture, '[data-test="parent-grace-banner"]')).toBeNull();
    });

    // #440 review: AwaitingActivation'da da hoşgörü bilgisi.
    it('awaiting_WithDaysLeft_ShowsRestrictionDeadline', () => {
        const fixture = create({ ...awaitingStatusOf(), daysLeft: 12, isFinalWarning: false });
        const deadline = q(fixture, '[data-test="invitation-deadline"]');
        expect(deadline?.textContent).toContain('12 gün içinde veliniz aktive etmezse hesabınız kısıtlanır.');
        expect(deadline?.classList).not.toContain('psc__deadline--warning');
    });

    // Issue #721: ana sayfada ayrıntılar kapalı başlar, kısıtlar ve süre görünür kalır.
    describe('compact (issue #721)', () => {
        function compact(status: ParentStatus, mobile = false): ComponentFixture<ParentStatusCardComponent> {
            const fixture = create(status, mobile);
            fixture.componentRef.setInput('compact', true);
            fixture.detectChanges();
            return fixture;
        }

        it('oneLine_HidesDetailsAndRestrictions_KeepsDeadline_issue835', () => {
            const fixture = compact({ ...awaitingStatusOf(), daysLeft: 20 });

            expect(q(fixture, '.psc--compact')).not.toBeNull();
            expect(q(fixture, '[data-test="invitation-resend"]')).toBeNull();
            expect(q(fixture, '[data-test="invitation-sent-to"]')).toBeNull();
            expect(q(fixture, '[data-test="invitation-deadline"]')).not.toBeNull();
            expect(fixture.nativeElement.textContent).not.toContain('liderlik tablosu kapalı');
        });

        it('detailsButton_ShowsFullCard', () => {
            const fixture = compact(awaitingStatusOf());

            q(fixture, '[data-test="invitation-details"]')?.click();
            fixture.detectChanges();

            expect(q(fixture, '.psc--compact')).toBeNull();
            expect(q(fixture, '[data-test="invitation-resend"]')).not.toBeNull();
            expect(q(fixture, '[data-test="invitation-sent-to"]')).not.toBeNull();
        });

        it('mobile_OneLineWithoutActions_DetailsOpenBottomSheet_issue844', () => {
            const fixture = compact({ ...awaitingStatusOf(), daysLeft: 3, isFinalWarning: true }, true);

            expect(q(fixture, '[data-test="invitation-resend"]')).toBeNull();
            expect(q(fixture, '[data-test="invitation-deadline"]')?.textContent).toContain('3 gün kaldı');

            q(fixture, '[data-test="invitation-details"]')?.click();
            fixture.detectChanges();

            expect(sheet.open).toHaveBeenCalledTimes(1);
            expect(sheet.open.mock.calls[0][1]).toEqual(expect.objectContaining({ panelClass: 'ms-bottom-sheet' }));
            expect(q(fixture, '.psc--compact')).not.toBeNull();
        });

        it('finalWarning_ShowsActionsInTheSameLine_issue835', () => {
            const fixture = compact({ ...awaitingStatusOf(), daysLeft: 3, isFinalWarning: true });

            expect(q(fixture, '.psc--compact.psc--expired')).not.toBeNull();
            expect(q(fixture, '[data-test="invitation-resend"]')).not.toBeNull();
            expect(q(fixture, '[data-test="invitation-details"]')).not.toBeNull();
            expect(q(fixture, '[data-test="invitation-sent-to"]')).toBeNull();
        });
    });

    it('awaiting_FinalWarning_WarningColoredCardNoModal', () => {
        const fixture = create({ ...awaitingStatusOf(), daysLeft: 3, isFinalWarning: true });
        expect(q(fixture, '[data-test="invitation-deadline"]')?.classList).toContain('psc__deadline--warning');
        expect(q(fixture, '[data-test="parent-invitation-card"]')?.classList).toContain('psc--expired');
        expect(TestBed.inject(ParentStatusStore).finalWarning()).toBe(false);
    });

    it('awaiting_GraceOver_ShowsRestrictedText', () => {
        const fixture = create({ ...awaitingStatusOf(), daysLeft: 0, isFinalWarning: true });
        expect(q(fixture, '[data-test="invitation-deadline"]')?.textContent).toContain('Velini ekleme süren doldu');
    });

    it('awaiting_NoDaysLeft_NoDeadlineText', () => {
        const fixture = create(awaitingStatusOf());
        expect(q(fixture, '[data-test="invitation-deadline"]')).toBeNull();
    });
});
