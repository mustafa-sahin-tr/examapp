import type { Mock, MockedObject } from "vitest";
import { Clipboard } from '@angular/cdk/clipboard';
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { ParentChildrenComponent } from './parent-children.component';
import { ParentLinkService } from '../../services/parent-link.service';
import { LocaleService } from '../../services/locale.service';
import { GradesService } from '../../services/grades.service';
import { SchoolService } from '../../services/school.service';
import { ParentChildAccountService } from '../../services/parent-child-account.service';
import { CoParent, LinkedChild, normalizeInviteCodeInput } from '../../models/parent-link.model';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import parentLinksTr from '../../../../public/i18n/parent-links/tr.json';
import parentLinksEn from '../../../../public/i18n/parent-links/en.json';

/**
 * Issue #419 → #436: velinin "Çocuklarım" sayfası — ikinci veli koduyla istek (birincil veli onaylar), liste, iptal; birincil
 * veli için diğer veliler, istek onay/ret, veli kaldırma, ikinci veli kodu; birincil olmayan veliye yönetim yok.
 */
describe('ParentChildrenComponent (issue #419, #436)', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });
    afterEach(() => {
        vi.useRealTimers();
    });
    let fixture: ComponentFixture<ParentChildrenComponent>;
    let service: MockedObject<ParentLinkService>;
    let snackBar: MockedObject<MatSnackBar>;
    let clipboard: MockedObject<Clipboard>;
    let dialogOpen: Mock;

    const ayse: LinkedChild = {
        linkId: 1,
        status: 'Active',
        studentName: 'Ayşe Kaya',
        gradeName: '7. Sınıf',
        schoolName: 'Atatürk Ortaokulu',
        linkedAt: '2026-10-01T09:00:00Z',
        requestedAt: '2026-10-01T08:00:00Z',
        pendingExpiresAt: null,
        isPrimary: true,
        coParents: [],
        secondParentCodeExpiresAt: null,
        maxParents: 4,
    };

    const coParent: CoParent = {
        linkId: 11,
        status: 'Active',
        parentName: 'Ali Kaya',
        parentEmailMasked: '',
        linkedAt: '2026-10-02T09:00:00Z',
        requestedAt: '2026-10-02T08:00:00Z',
        pendingExpiresAt: null,
    };

    const request: CoParent = {
        linkId: 12,
        status: 'Pending',
        parentName: 'Zeynep Kaya',
        parentEmailMasked: 'z***@g***.com',
        linkedAt: null,
        requestedAt: '2026-10-06T09:00:00Z',
        pendingExpiresAt: '2026-10-13T09:00:00Z',
    };

    const pending: LinkedChild = {
        linkId: 2,
        status: 'Pending',
        studentName: null,
        gradeName: null,
        schoolName: null,
        linkedAt: null,
        requestedAt: '2026-10-07T09:00:00Z',
        pendingExpiresAt: '2026-10-14T09:00:00Z',
    };

    function setup(children: LinkedChild[]): HTMLElement {
        service = {
            getMyChildren: vi.fn().mockName("ParentLinkService.getMyChildren"),
            redeem: vi.fn().mockName("ParentLinkService.redeem"),
            revoke: vi.fn().mockName("ParentLinkService.revoke"),
            approve: vi.fn().mockName("ParentLinkService.approve"),
            reject: vi.fn().mockName("ParentLinkService.reject"),
            createSecondParentCode: vi.fn().mockName("ParentLinkService.createSecondParentCode"),
            extractError: vi.fn().mockName("ParentLinkService.extractError"),
            errorCode: vi.fn().mockName("ParentLinkService.errorCode")
        } as unknown as MockedObject<ParentLinkService>;
        service.getMyChildren.mockReturnValue(of(children));
        service.extractError.mockImplementation((err: HttpErrorResponse, fallback: string) => err.error?.message ?? fallback);
        service.errorCode.mockImplementation((err: HttpErrorResponse) => err.error?.errorCode ?? null);
        snackBar = {
            open: vi.fn().mockName("MatSnackBar.open")
        } as unknown as MockedObject<MatSnackBar>;
        clipboard = {
            copy: vi.fn().mockName("Clipboard.copy")
        } as unknown as MockedObject<Clipboard>;
        clipboard.copy.mockReturnValue(true);

        TestBed.configureTestingModule({
            imports: [
                ParentChildrenComponent,
                NoopAnimationsModule,
                translocoTestingModule({ langs: { 'parent-links/tr': parentLinksTr, 'parent-links/en': parentLinksEn } }),
            ],
            providers: [
                // Issue #438: gömülü formun rıza metnindeki /privacy-policy bağlantısı (RouterLink).
                provideRouter([]),
                { provide: ParentLinkService, useValue: service },
                { provide: Clipboard, useValue: clipboard },
                { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: 'tr-TR' }) } },
                // Issue #438: gömülü "Çocuk hesabı oluştur" formunun bağımlılıkları (HTTP yok).
                { provide: GradesService, useValue: { getGrades: () => of([]) } },
                { provide: SchoolService, useValue: { getSchools: () => of([]) } },
                { provide: ParentChildAccountService, useValue: {
                        create: vi.fn().mockName("ParentChildAccountService.create"),
                        resetPassword: vi.fn().mockName("ParentChildAccountService.resetPassword")
                    } },
            ],
        });
        TestBed.overrideProvider(MatSnackBar, { useValue: snackBar });
        dialogOpen = vi.fn().mockName('open').mockReturnValue({ afterClosed: () => of(true) });
        TestBed.overrideProvider(MatDialog, { useValue: { open: dialogOpen } });
        fixture = TestBed.createComponent(ParentChildrenComponent);
        fixture.detectChanges();
        return fixture.nativeElement as HTMLElement;
    }

    /** Sahte zamanlayıcıyla çağrılır: ngModel kontrolü bir microtask sonra bağlanır, önce o boşaltılır. */
    async function typeCode(el: HTMLElement, value: string): Promise<void> {
        await vi.advanceTimersByTimeAsync(0);
        const input = el.querySelector<HTMLInputElement>('[data-test="code-input"]')!;
        input.value = value;
        input.dispatchEvent(new Event('input'));
        fixture.detectChanges();
    }

    it('normalizeInviteCodeInput_StripsDashSpaceAndUppercases', () => {
        expect(normalizeInviteCodeInput(' abcd-efgh-jkmn ')).toBe('ABCDEFGHJKMN');
    });

    it('inviteCodeForm_IsASingleRow_FieldNotStretchedVertically (#421 review)', () => {
        const el = setup([]);
        document.body.appendChild(el); // hesaplanan stil için belgeye bağlı olmalı
        const form = el.querySelector<HTMLElement>('.pch__form')!;
        // Global `form { flex-direction: column }` alanın flex-basis'ini yüksekliğe çeviriyordu (~200px kutu).
        expect(getComputedStyle(form).flexDirection).toBe('row');
        expect(el.querySelector<HTMLElement>('.pch__field')!.getBoundingClientRect().height).toBeLessThan(120);
        el.remove();
    });

    it('emptyList_ShowsEmptyState', () => {
        const el = setup([]);
        expect(el.querySelector('[data-test="empty"]')?.textContent).toContain(parentLinksTr.children.empty);
    });

    it('listsChildren_WithGradeAndSchool', () => {
        const el = setup([ayse]);
        const row = el.querySelector('[data-test="child-row"]');
        expect(row?.textContent).toContain('Ayşe Kaya');
        expect(row?.textContent).toContain('7. Sınıf');
        expect(row?.textContent).toContain('Atatürk Ortaokulu');
    });

    it('pendingRequest_ShowsAwaitingApprovalWithoutStudentData_AndCanBeCancelled', () => {
        const el = setup([pending]);
        const row = el.querySelector('[data-test="pending-row"]');
        expect(row?.textContent).toContain(parentLinksTr.children.pendingStatus);
        expect(row?.textContent).toContain(parentLinksTr.children.cancel);
        expect(el.querySelector('[data-test="child-row"]')).toBeNull();

        service.revoke.mockReturnValue(of(undefined));
        row!.querySelector<HTMLButtonElement>('[data-test="revoke"]')!.click();
        fixture.detectChanges();
        expect(service.revoke).toHaveBeenCalledWith(2);
        expect(el.querySelector('[data-test="pending-row"]')).toBeNull();
        expect(snackBar.open).toHaveBeenCalledWith(parentLinksTr.children.cancelled, expect.any(String), expect.any(Object));
    });

    it('submit_DisabledUntilTwelveChars_ThenRedeemsNormalizedCodeAndShowsPendingRequest', async () => {
        const el = setup([]);
        service.redeem.mockReturnValue(of(pending));
        const submit = () => el.querySelector<HTMLButtonElement>('[data-test="submit"]')!;

        await typeCode(el, 'abcd-efgh-jkm');
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();
        expect(submit().disabled).toBe(true);

        await typeCode(el, 'abcd-efgh-jkmn');
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();
        expect(submit().disabled).toBe(false);

        submit().click();
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();

        expect(service.redeem).toHaveBeenCalledWith('ABCDEFGHJKMN');
        expect(el.querySelectorAll('[data-test="pending-row"]').length).toBe(1);
        expect(snackBar.open).toHaveBeenCalledWith(parentLinksTr.children.requested, expect.any(String), expect.any(Object));
    });

    it('invalidCode_ShowsGenericServerError', async () => {
        const el = setup([]);
        service.redeem.mockReturnValue(throwError(() => new HttpErrorResponse({
            status: 400,
            error: { message: 'Kod geçersiz veya süresi dolmuş.', errorCode: 'InvalidCode' },
        })));
        await typeCode(el, 'ZZZZZZZZZZZZ');
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();
        el.querySelector<HTMLButtonElement>('[data-test="submit"]')!.click();
        await vi.advanceTimersByTimeAsync(0);
        fixture.detectChanges();

        expect(service.redeem).toHaveBeenCalledWith('ZZZZZZZZZZZZ');
        expect(el.querySelector('[data-test="redeem-error"]')?.textContent).toContain('Kod geçersiz veya süresi dolmuş.');
        expect(el.querySelector('[data-test="child-row"]')).toBeNull();
    });

    it('revoke_RemovesChildAfterConfirm', () => {
        const el = setup([{ ...ayse, coParents: [coParent] }]);
        service.revoke.mockReturnValue(of(undefined));
        el.querySelector<HTMLButtonElement>('[data-test="revoke"]')!.click();
        fixture.detectChanges();

        expect(service.revoke).toHaveBeenCalledWith(1);
        expect(el.querySelector('[data-test="child-row"]')).toBeNull();
    });

    // ---- issue #436: birincil veli -------------------------------------------------------------------------------------

    it('nonPrimaryParent_SeesChildWithoutManagementActions_ButCanLeaveAfterConfirm', () => {
        const el = setup([{ ...ayse, isPrimary: false, coParents: [] }]);
        const row = el.querySelector('[data-test="child-row"]')!;

        expect(row.querySelector('[data-test="not-primary-note"]')?.textContent).toContain(parentLinksTr.family.notPrimary);
        expect(row.querySelector('[data-test="family"]')).toBeNull();
        expect(row.querySelector('[data-test="second-parent-code"]')).toBeNull();
        expect(row.querySelector('[data-test="primary-badge"]')).toBeNull();

        const leave = row.querySelector<HTMLButtonElement>('[data-test="revoke"]')!;
        expect(leave.textContent).toContain(parentLinksTr.children.leave);
        service.revoke.mockReturnValue(of(undefined));
        leave.click();
        fixture.detectChanges();
        expect(service.revoke).toHaveBeenCalledWith(1);
        expect(el.querySelector('[data-test="child-row"]')).toBeNull();
        expect(snackBar.open).toHaveBeenCalledWith(parentLinksTr.children.left, expect.any(String), expect.any(Object));
    });

    it('onlyParent_CannotLeave_ButtonHiddenWithExplanation', () => {
        const el = setup([ayse]); // birincil, başka Active veli yok
        expect(el.querySelector('[data-test="revoke"]')).toBeNull();
        expect(el.querySelector('[data-test="only-parent-note"]')?.textContent).toContain(parentLinksTr.family.onlyParent);
    });

    it('primaryWithAnotherActiveParent_CanLeave', () => {
        const el = setup([{ ...ayse, coParents: [coParent] }]);
        expect(el.querySelector('[data-test="only-parent-note"]')).toBeNull();
        expect(el.querySelector('[data-test="revoke"]')?.textContent).toContain(parentLinksTr.children.revoke);
    });

    it('limit_UsesServerOpenParentCount_IncludingLegacyRequests (m6)', () => {
        // Listede yalnız 1 diğer veli var ama sunucu eski bekleyen istekler dahil 4 açık veli sayıyor.
        const el = setup([{ ...ayse, coParents: [coParent], openParents: 4 }]);
        expect(el.querySelector('[data-test="second-parent-code"]')).toBeNull();
        expect(el.querySelector('[data-test="family-limit"]')).not.toBeNull();
    });

    it('primaryParent_SeesCoParentsAndPendingRequestWithMaskedEmail', () => {
        const el = setup([{ ...ayse, coParents: [coParent, request] }]);
        const family = el.querySelector('[data-test="family"]')!;

        expect(el.querySelector('[data-test="primary-badge"]')?.textContent).toContain(parentLinksTr.family.primaryBadge);
        expect(family.querySelector('[data-test="coparent-row"]')?.textContent).toContain('Ali Kaya');
        const pendingRow = family.querySelector('[data-test="coparent-request"]')!;
        expect(pendingRow.textContent).toContain('Zeynep Kaya');
        expect(pendingRow.querySelector('[data-test="coparent-email"]')?.textContent).toContain('z***@g***.com');
    });

    it('primaryParent_ApprovesAndRejectsRequests_ThenReloads', () => {
        const el = setup([{ ...ayse, coParents: [request] }]);
        service.approve.mockReturnValue(of(undefined));
        service.reject.mockReturnValue(of(undefined));

        el.querySelector<HTMLButtonElement>('[data-test="coparent-approve"]')!.click();
        fixture.detectChanges();
        expect(service.approve).toHaveBeenCalledWith(12);
        expect(service.getMyChildren).toHaveBeenCalledTimes(2);

        el.querySelector<HTMLButtonElement>('[data-test="coparent-reject"]')!.click();
        fixture.detectChanges();
        expect(service.reject).toHaveBeenCalledWith(12);
        expect(service.getMyChildren).toHaveBeenCalledTimes(3);
    });

    it('primaryParent_RemovesCoParentAfterConfirm', () => {
        const el = setup([{ ...ayse, coParents: [coParent] }]);
        service.revoke.mockReturnValue(of(undefined));

        el.querySelector<HTMLButtonElement>('[data-test="coparent-remove"]')!.click();
        fixture.detectChanges();

        expect(service.revoke).toHaveBeenCalledWith(11);
        expect(snackBar.open).toHaveBeenCalledWith('Ali Kaya kaldırıldı.', expect.any(String), expect.any(Object));
    });

    it('primaryParent_GeneratesSecondParentCode_ShowsItOnceFormattedAndCopies', () => {
        const el = setup([ayse]);
        service.createSecondParentCode.mockReturnValue(of({ code: 'K7M2P9QZR4ST', expiresAt: '2026-10-14T09:00:00Z' }));

        el.querySelector<HTMLButtonElement>('[data-test="second-parent-code"]')!.click();
        fixture.detectChanges();

        expect(service.createSecondParentCode).toHaveBeenCalledWith(1);
        const box = el.querySelector('[data-test="second-parent-code-box"]')!;
        expect(box.textContent).toContain('K7M2-P9QZ-R4ST');
        expect(box.textContent).toContain(parentLinksTr.family.shownOnce);
        expect(el.querySelector('[data-test="second-parent-code"]')?.textContent).toContain(parentLinksTr.family.regenerate);

        box.querySelector<HTMLButtonElement>('[data-test="copy"]')!.click();
        expect(clipboard.copy).toHaveBeenCalledWith('K7M2P9QZR4ST');
    });

    it('existingCode_ShowsExpiryOnlyNeverTheCode', () => {
        const el = setup([{ ...ayse, secondParentCodeExpiresAt: '2026-10-14T09:00:00Z' }]);
        expect(el.querySelector('[data-test="active-code"]')).not.toBeNull();
        expect(el.querySelector('[data-test="second-parent-code-box"]')).toBeNull();
    });

    it('fullFamily_HidesCodeButtonAndShowsLimit', () => {
        const others: CoParent[] = [coParent, request, { ...coParent, linkId: 13, parentName: 'Can Kaya' }];
        const el = setup([{ ...ayse, coParents: others }]);
        expect(el.querySelector('[data-test="second-parent-code"]')).toBeNull();
        expect(el.querySelector('[data-test="family-limit"]')).not.toBeNull();
    });

    it('notPrimaryError_OnCodeGeneration_ReloadsAndToastsServerMessage', () => {
        const el = setup([ayse]);
        service.createSecondParentCode.mockReturnValue(throwError(() => new HttpErrorResponse({
            status: 403,
            error: { message: 'Bu işlemi yalnızca çocuğun birincil velisi yapabilir.', errorCode: 'NotPrimaryParent' },
        })));
        el.querySelector<HTMLButtonElement>('[data-test="second-parent-code"]')!.click();
        fixture.detectChanges();

        expect(service.getMyChildren).toHaveBeenCalledTimes(2);
        expect(snackBar.open).toHaveBeenCalledWith('Bu işlemi yalnızca çocuğun birincil velisi yapabilir.', expect.any(String), expect.any(Object));
    });

    // ---- Issue #438: çocuk hesabı oluşturma + şifre sıfırlama ----------------------------------------------------------

    it('rendersCreateChildAccountForm_AboveSecondParentCode (issue #438)', () => {
        const el = setup([]);
        const form = el.querySelector('app-create-child-account');
        expect(form).not.toBeNull();
        expect(form!.querySelector('#create-child')).not.toBeNull();
        expect(form!.textContent).toContain(parentLinksTr.createChild.title);
    });

    it('resetPasswordButton_FollowsServerCanResetPassword (issue #438)', () => {
        const managed: LinkedChild = {
            ...ayse,
            linkId: 5,
            studentId: 41,
            studentName: 'Ali Kaya',
            parentManaged: true,
            canResetPassword: true,
        };
        // Veli yönetimli ama birincil olmayan veli: sunucu canResetPassword=false döner → düğme yok.
        const managedNotPrimary: LinkedChild = { ...managed, linkId: 6, studentId: 42, studentName: 'Can Kaya', canResetPassword: false };
        const el = setup([ayse, managed, managedNotPrimary]);
        const rows = el.querySelectorAll('[data-test="child-row"]');
        expect(rows.length).toBe(3);
        expect(rows[0].querySelector('[data-test="reset-password"]')).toBeNull();
        const button = rows[1].querySelector<HTMLButtonElement>('[data-test="reset-password"]');
        expect(button).not.toBeNull();
        expect(button!.textContent).toContain(parentLinksTr.resetPw.action);
        expect(rows[2].querySelector('[data-test="reset-password"]')).toBeNull();
    });

    it('resetPasswordButton_HiddenWhenServerOmitsCanResetPassword (issue #438)', () => {
        const el = setup([{ ...ayse, linkId: 5, studentId: 41, parentManaged: true }]);
        expect(el.querySelector('[data-test="reset-password"]')).toBeNull();
    });

    it('createChildForm_HiddenWithInfo_WhenTenChildrenAreLinked (issue #438)', () => {
        const ten: LinkedChild[] = Array.from({ length: 10 }, (_, i) => ({ ...ayse, linkId: 100 + i, studentId: 200 + i }));
        const el = setup(ten);
        const host = el.querySelector('app-create-child-account')!;
        expect(host.querySelector('[data-test="create-form"]')).toBeNull();
        expect(host.querySelector('[data-test="create-limit"]')).not.toBeNull();
    });

    /** Sunucu kuralı ParentLinkPrimary.IsOpen: Active ∨ süresi dolmamış Pending. */
    function pendingRow(linkId: number, pendingExpiresAt: string | null): LinkedChild {
        return {
            ...ayse,
            linkId,
            status: 'Pending',
            studentName: null,
            gradeName: null,
            schoolName: null,
            linkedAt: null,
            pendingExpiresAt,
            isPrimary: false,
        } as LinkedChild;
    }

    it('createChildForm_CountsLivePendingTowardsTheLimit (issue #438)', () => {
        const future = new Date(Date.now() + 86400000).toISOString();
        const nine: LinkedChild[] = Array.from({ length: 9 }, (_, i) => ({ ...ayse, linkId: 100 + i, studentId: 200 + i }));
        const el = setup([...nine, pendingRow(300, future)]);
        const host = el.querySelector('app-create-child-account')!;
        expect(host.querySelector('[data-test="create-form"]')).toBeNull();
        expect(host.querySelector('[data-test="create-limit"]')).not.toBeNull();
    });

    it('createChildForm_IgnoresExpiredPending_LikeTheServer (issue #438)', () => {
        const past = new Date(Date.now() - 60000).toISOString();
        const nine: LinkedChild[] = Array.from({ length: 9 }, (_, i) => ({ ...ayse, linkId: 100 + i, studentId: 200 + i }));
        const el = setup([...nine, pendingRow(300, past)]);
        const host = el.querySelector('app-create-child-account')!;
        // 9 Active + 1 süresi dolmuş Pending = 9 açık bağlantı: sunucu yeni hesaba izin verir, form açık kalır.
        expect(host.querySelector('[data-test="create-form"]')).not.toBeNull();
        expect(host.querySelector('[data-test="create-limit"]')).toBeNull();
    });

    it('resetPassword_OpensDialogForTheChild_AndToastsOnSuccess (issue #438)', () => {
        const managed: LinkedChild = {
            ...ayse,
            linkId: 5,
            studentId: 41,
            studentName: 'Ali Kaya',
            parentManaged: true,
            canResetPassword: true,
        };
        const el = setup([managed]);
        el.querySelector<HTMLButtonElement>('[data-test="reset-password"]')!.click();

        expect(dialogOpen).toHaveBeenCalledTimes(1);
        expect(vi.mocked(dialogOpen).mock.lastCall![1].data).toEqual({ studentId: 41, studentName: 'Ali Kaya' });
        expect(snackBar.open).toHaveBeenCalledWith(parentLinksTr.resetPw.done, expect.anything(), expect.anything());
    });

    it('childCreated_ReloadsTheList (issue #438)', () => {
        setup([]);
        service.getMyChildren.mockClear();
        service.getMyChildren.mockReturnValue(of([{ ...ayse, studentId: 41, parentManaged: true, canResetPassword: true }]));
        (fixture.componentInstance as unknown as {
            onChildCreated(): void;
        }).onChildCreated();
        fixture.detectChanges();
        expect(service.getMyChildren).toHaveBeenCalledTimes(1);
        expect((fixture.nativeElement as HTMLElement).querySelector('[data-test="reset-password"]')).not.toBeNull();
    });

    // Issue #439: Active çocuk için "Ders talebi oluştur" → veli modunda öğretmen arama.
    it('activeChild_ShowsBookLessonLinkToChildTutorSearch; pendingRow_NoLink', () => {
        const pendingRow: LinkedChild = {
            ...ayse,
            linkId: 9,
            status: 'Pending',
            studentId: null,
            studentName: null,
            linkedAt: null,
            pendingExpiresAt: '2026-10-20T09:00:00Z',
        };
        const el = setup([{ ...ayse, studentId: 21 }, pendingRow]);

        const links = el.querySelectorAll<HTMLAnchorElement>('[data-test="book-lesson"]');
        expect(links.length).toBe(1);
        expect(links[0].getAttribute('href')).toBe('/my-children/21/tutors');
        expect(links[0].textContent).toContain('Ders talebi oluştur');
    });
    // ── Issue #440: öğrencinin veli daveti (StudentRegistered) ───────────────────────────────────────────────────────────

    const invite: LinkedChild = {
        linkId: 30,
        status: 'Pending',
        origin: 'StudentRegistered',
        requiresConsent: true,
        consentVersion: '2026-10',
        studentName: 'Ali K.',
        gradeName: null,
        schoolName: null,
        linkedAt: null,
        requestedAt: '2026-10-15T09:00:00Z',
        pendingExpiresAt: '2026-10-22T09:00:00Z',
        maxParents: 4,
    };

    it('studentInvite_RendersNameConsentTextAndActions_NotCountedInChildLimit', () => {
        const el = setup([invite]);

        expect(el.querySelector('[data-test="student-invite-title"]')?.textContent).toContain('Ali K. sizi veli olarak ekledi');
        expect(el.querySelector('[data-test="student-invite-consent-text"]')?.textContent).toContain('KVKK');
        expect(el.querySelector('.pch__consent-link')?.getAttribute('href')).toBe('/privacy-policy');
        expect(el.querySelector('[data-test="pending-row"]')).toBeNull();
        expect(el.querySelector('[data-test="student-invite-approve"]')).not.toBeNull();
        expect(el.querySelector('[data-test="student-invite-reject"]')).not.toBeNull();
    });

    it('studentInvite_ApproveWithoutConsent_DoesNotCallServer', () => {
        const el = setup([invite]);

        el.querySelector<HTMLButtonElement>('[data-test="student-invite-approve"]')!.click();
        fixture.detectChanges();

        expect(service.approve).not.toHaveBeenCalled();
        expect(el.querySelector('[data-test="student-invite-consent-missing"]')).not.toBeNull();
    });

    it('studentInvite_ApproveWithConsent_SendsConsentAndVersion_ThenReloads', () => {
        const el = setup([invite]);
        service.approve.mockReturnValue(of(undefined));
        service.getMyChildren.mockClear();

        el.querySelector<HTMLInputElement>('[data-test="student-invite-consent"] input')!.click();
        fixture.detectChanges();
        el.querySelector<HTMLButtonElement>('[data-test="student-invite-approve"]')!.click();
        fixture.detectChanges();

        expect(service.approve).toHaveBeenCalledWith(30, { consentAccepted: true, consentVersion: '2026-10' });
        expect(snackBar.open).toHaveBeenCalledWith('Ali K. artık bağlı çocuklarınız arasında.', expect.any(String), expect.any(Object));
        expect(service.getMyChildren).toHaveBeenCalled();
    });

    it('studentInvite_ApproveExpired409_ShowsInvitationExpired', () => {
        const el = setup([invite]);
        service.approve.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409, error: { errorCode: 'InvitationExpired', message: 'x' } })));

        el.querySelector<HTMLInputElement>('[data-test="student-invite-consent"] input')!.click();
        fixture.detectChanges();
        el.querySelector<HTMLButtonElement>('[data-test="student-invite-approve"]')!.click();
        fixture.detectChanges();

        expect(snackBar.open).toHaveBeenCalledWith('Davetin süresi dolmuş; çocuğunuzdan yeni bir davet göndermesini isteyin.', expect.any(String), expect.any(Object));
    });

    it('studentInvite_Reject_ConfirmsThenRejectsAndRemovesRow', () => {
        const el = setup([invite]);
        service.reject.mockReturnValue(of(undefined));

        el.querySelector<HTMLButtonElement>('[data-test="student-invite-reject"]')!.click();
        fixture.detectChanges();

        expect(dialogOpen).toHaveBeenCalled();
        expect(service.reject).toHaveBeenCalledWith(30);
        expect(el.querySelector('[data-test="student-invite-row"]')).toBeNull();
        expect(snackBar.open).toHaveBeenCalledWith('Davet reddedildi.', expect.any(String), expect.any(Object));
    });
});
