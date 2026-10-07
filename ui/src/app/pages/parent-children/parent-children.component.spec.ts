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
import { LinkedChild, normalizeInviteCodeInput } from '../../models/parent-link.model';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import parentLinksTr from '../../../../public/i18n/parent-links/tr.json';
import parentLinksEn from '../../../../public/i18n/parent-links/en.json';

/** Issue #419: velinin "Çocuklarım" sayfası — kodla istek (onay bekler), liste, kaldırma/iptal. */
describe('ParentChildrenComponent (issue #419)', () => {
  let fixture: ComponentFixture<ParentChildrenComponent>;
  let service: jasmine.SpyObj<ParentLinkService>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;

  const ayse: LinkedChild = {
    linkId: 1,
    status: 'Active',
    studentName: 'Ayşe Kaya',
    gradeName: '7. Sınıf',
    schoolName: 'Atatürk Ortaokulu',
    linkedAt: '2026-10-01T09:00:00Z',
    requestedAt: '2026-10-01T08:00:00Z',
    pendingExpiresAt: null,
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
      'extractError',
      'errorCode',
    ]);
    service.getMyChildren.and.returnValue(of(children));
    service.extractError.and.callFake((err: HttpErrorResponse, fallback: string) => err.error?.message ?? fallback);
    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    TestBed.configureTestingModule({
      imports: [
        ParentChildrenComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { 'parent-links/tr': parentLinksTr, 'parent-links/en': parentLinksEn } }),
      ],
      providers: [
        { provide: ParentLinkService, useValue: service },
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
    const el = setup([ayse]);
    service.revoke.and.returnValue(of(undefined));
    el.querySelector<HTMLButtonElement>('[data-test="revoke"]')!.click();
    fixture.detectChanges();

    expect(service.revoke).toHaveBeenCalledWith(1);
    expect(el.querySelector('[data-test="child-row"]')).toBeNull();
  });
});
