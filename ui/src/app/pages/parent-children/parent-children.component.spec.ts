import { Clipboard } from '@angular/cdk/clipboard';
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { ParentChildrenComponent } from './parent-children.component';
import { ParentLinkService } from '../../services/parent-link.service';
import { LocaleService } from '../../services/locale.service';
import { CoParent, LinkedChild, normalizeInviteCodeInput } from '../../models/parent-link.model';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import parentLinksTr from '../../../../public/i18n/parent-links/tr.json';
import parentLinksEn from '../../../../public/i18n/parent-links/en.json';

/**
 * Issue #419 → #436: velinin "Çocuklarım" sayfası — ikinci veli koduyla istek (birincil veli onaylar), liste, iptal; birincil
 * veli için diğer veliler, istek onay/ret, veli kaldırma, ikinci veli kodu; birincil olmayan veliye yönetim yok.
 */
describe('ParentChildrenComponent (issue #419, #436)', () => {
  let fixture: ComponentFixture<ParentChildrenComponent>;
  let service: jasmine.SpyObj<ParentLinkService>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;
  let clipboard: jasmine.SpyObj<Clipboard>;

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
    service = jasmine.createSpyObj<ParentLinkService>('ParentLinkService', [
      'getMyChildren',
      'redeem',
      'revoke',
      'approve',
      'reject',
      'createSecondParentCode',
      'extractError',
      'errorCode',
    ]);
    service.getMyChildren.and.returnValue(of(children));
    service.extractError.and.callFake((err: HttpErrorResponse, fallback: string) => err.error?.message ?? fallback);
    service.errorCode.and.callFake((err: HttpErrorResponse) => err.error?.errorCode ?? null);
    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);
    clipboard = jasmine.createSpyObj<Clipboard>('Clipboard', ['copy']);
    clipboard.copy.and.returnValue(true);

    TestBed.configureTestingModule({
      imports: [
        ParentChildrenComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { 'parent-links/tr': parentLinksTr, 'parent-links/en': parentLinksEn } }),
      ],
      providers: [
        { provide: ParentLinkService, useValue: service },
        { provide: Clipboard, useValue: clipboard },
        { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: 'tr-TR' }) } },
      ],
    });
    TestBed.overrideProvider(MatSnackBar, { useValue: snackBar });
    TestBed.overrideProvider(MatDialog, { useValue: { open: () => ({ afterClosed: () => of(true) }) } });
    fixture = TestBed.createComponent(ParentChildrenComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  /** fakeAsync içinde çağrılır: ngModel kontrolü bir microtask sonra bağlanır, önce o boşaltılır. */
  function typeCode(el: HTMLElement, value: string): void {
    tick();
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

    service.revoke.and.returnValue(of(undefined));
    row!.querySelector<HTMLButtonElement>('[data-test="revoke"]')!.click();
    fixture.detectChanges();
    expect(service.revoke).toHaveBeenCalledWith(2);
    expect(el.querySelector('[data-test="pending-row"]')).toBeNull();
    expect(snackBar.open).toHaveBeenCalledWith(
      parentLinksTr.children.cancelled,
      jasmine.any(String),
      jasmine.any(Object)
    );
  });

  it('submit_DisabledUntilTwelveChars_ThenRedeemsNormalizedCodeAndShowsPendingRequest', fakeAsync(() => {
    const el = setup([]);
    service.redeem.and.returnValue(of(pending));
    const submit = () => el.querySelector<HTMLButtonElement>('[data-test="submit"]')!;

    typeCode(el, 'abcd-efgh-jkm');
    tick();
    fixture.detectChanges();
    expect(submit().disabled).toBeTrue();

    typeCode(el, 'abcd-efgh-jkmn');
    tick();
    fixture.detectChanges();
    expect(submit().disabled).toBeFalse();

    submit().click();
    tick();
    fixture.detectChanges();

    expect(service.redeem).toHaveBeenCalledWith('ABCDEFGHJKMN');
    expect(el.querySelectorAll('[data-test="pending-row"]').length).toBe(1);
    expect(snackBar.open).toHaveBeenCalledWith(
      parentLinksTr.children.requested,
      jasmine.any(String),
      jasmine.any(Object)
    );
  }));

  it('invalidCode_ShowsGenericServerError', fakeAsync(() => {
    const el = setup([]);
    service.redeem.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { message: 'Kod geçersiz veya süresi dolmuş.', errorCode: 'InvalidCode' },
          })
      )
    );
    typeCode(el, 'ZZZZZZZZZZZZ');
    tick();
    fixture.detectChanges();
    el.querySelector<HTMLButtonElement>('[data-test="submit"]')!.click();
    tick();
    fixture.detectChanges();

    expect(service.redeem).toHaveBeenCalledWith('ZZZZZZZZZZZZ');
    expect(el.querySelector('[data-test="redeem-error"]')?.textContent).toContain('Kod geçersiz veya süresi dolmuş.');
    expect(el.querySelector('[data-test="child-row"]')).toBeNull();
  }));

  it('revoke_RemovesChildAfterConfirm', () => {
    const el = setup([{ ...ayse, coParents: [coParent] }]);
    service.revoke.and.returnValue(of(undefined));
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
    service.revoke.and.returnValue(of(undefined));
    leave.click();
    fixture.detectChanges();
    expect(service.revoke).toHaveBeenCalledWith(1);
    expect(el.querySelector('[data-test="child-row"]')).toBeNull();
    expect(snackBar.open).toHaveBeenCalledWith(parentLinksTr.children.left, jasmine.any(String), jasmine.any(Object));
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
    service.approve.and.returnValue(of(undefined));
    service.reject.and.returnValue(of(undefined));

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
    service.revoke.and.returnValue(of(undefined));

    el.querySelector<HTMLButtonElement>('[data-test="coparent-remove"]')!.click();
    fixture.detectChanges();

    expect(service.revoke).toHaveBeenCalledWith(11);
    expect(snackBar.open).toHaveBeenCalledWith('Ali Kaya kaldırıldı.', jasmine.any(String), jasmine.any(Object));
  });

  it('primaryParent_GeneratesSecondParentCode_ShowsItOnceFormattedAndCopies', () => {
    const el = setup([ayse]);
    service.createSecondParentCode.and.returnValue(of({ code: 'K7M2P9QZR4ST', expiresAt: '2026-10-14T09:00:00Z' }));

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
    service.createSecondParentCode.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 403,
            error: { message: 'Bu işlemi yalnızca çocuğun birincil velisi yapabilir.', errorCode: 'NotPrimaryParent' },
          })
      )
    );
    el.querySelector<HTMLButtonElement>('[data-test="second-parent-code"]')!.click();
    fixture.detectChanges();

    expect(service.getMyChildren).toHaveBeenCalledTimes(2);
    expect(snackBar.open).toHaveBeenCalledWith(
      'Bu işlemi yalnızca çocuğun birincil velisi yapabilir.',
      jasmine.any(String),
      jasmine.any(Object)
    );
  });
});
