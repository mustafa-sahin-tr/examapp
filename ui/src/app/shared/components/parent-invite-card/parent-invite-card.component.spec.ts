import { Clipboard } from '@angular/cdk/clipboard';
import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { ParentInviteCardComponent } from './parent-invite-card.component';
import { ParentLinkService } from '../../../services/parent-link.service';
import { LocaleService } from '../../../services/locale.service';
import { StudentParentLinks } from '../../../models/parent-link.model';
import { translocoTestingModule } from '../../testing/transloco-testing';
import parentLinksTr from '../../../../../public/i18n/parent-links/tr.json';
import parentLinksEn from '../../../../../public/i18n/parent-links/en.json';

/** Issue #419: öğrenci "Veli davet kodu" kartı — kod üret/göster/kopyala, bekleyen istek onay/ret, bağlı veliler, kaldır, tavan. */
describe('ParentInviteCardComponent (issue #419)', () => {
  let fixture: ComponentFixture<ParentInviteCardComponent>;
  let service: jasmine.SpyObj<ParentLinkService>;
  let clipboard: jasmine.SpyObj<Clipboard>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;
  let dialogResult: boolean;

  function setup(parents: StudentParentLinks): HTMLElement {
    service = jasmine.createSpyObj<ParentLinkService>('ParentLinkService', [
      'getMyParents',
      'createInviteCode',
      'revoke',
      'approve',
      'reject',
      'extractError',
      'errorCode',
    ]);
    service.getMyParents.and.returnValue(of(parents));
    service.extractError.and.callFake((err: HttpErrorResponse, fallback: string) => err.error?.message ?? fallback);
    service.errorCode.and.callFake((err: HttpErrorResponse) => err.error?.errorCode ?? null);
    clipboard = jasmine.createSpyObj<Clipboard>('Clipboard', ['copy']);
    clipboard.copy.and.returnValue(true);
    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);
    dialogResult = true;

    TestBed.configureTestingModule({
      imports: [
        ParentInviteCardComponent,
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
    TestBed.overrideProvider(MatDialog, {
      useValue: { open: () => ({ afterClosed: () => of(dialogResult) }) },
    });
    fixture = TestBed.createComponent(ParentInviteCardComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const empty: StudentParentLinks = {
    items: [],
    pendingRequests: [],
    activeInviteExpiresAt: null,
    maxActiveParents: 4,
  };

  it('generate_ShowsCodeOnceFormatted_WithExpiryAndCopy', () => {
    const el = setup(empty);
    service.createInviteCode.and.returnValue(of({ code: 'K7M2P9QZR4ST', expiresAt: '2026-10-09T09:00:00Z' }));

    el.querySelector<HTMLButtonElement>('[data-test="generate"]')!.click();
    fixture.detectChanges();

    const codeBox = el.querySelector('[data-test="invite-code"]');
    expect(codeBox?.textContent).toContain('K7M2-P9QZ-R4ST');
    expect(codeBox?.textContent).toContain(parentLinksTr.invite.shownOnce);
    expect(el.querySelector('[data-test="generate"]')?.textContent).toContain(parentLinksTr.invite.regenerate);

    el.querySelector<HTMLButtonElement>('[data-test="copy"]')!.click();
    expect(clipboard.copy).toHaveBeenCalledWith('K7M2P9QZR4ST');
    expect(snackBar.open).toHaveBeenCalledWith(parentLinksTr.invite.copied, jasmine.any(String), jasmine.any(Object));
  });

  it('existingActiveCode_ShowsExpiryHintButNotTheCode', () => {
    const el = setup({ ...empty, activeInviteExpiresAt: '2026-10-14T09:00:00Z' });
    expect(el.querySelector('[data-test="active-code"]')).not.toBeNull();
    expect(el.querySelector('[data-test="invite-code"]')).toBeNull();
  });

  it('listsLinkedParents_AndRevokeRemovesRowAfterConfirm', () => {
    const el = setup({
      items: [
        { linkId: 1, parentName: 'Veli Bir', linkedAt: '2026-10-01T00:00:00Z' },
        { linkId: 2, parentName: 'Veli İki', linkedAt: '2026-10-02T00:00:00Z' },
      ],
      pendingRequests: [],
      activeInviteExpiresAt: null,
      maxActiveParents: 4,
    });
    service.revoke.and.returnValue(of(undefined));

    const rows = el.querySelectorAll('[data-test="parent-row"]');
    expect(rows.length).toBe(2);
    rows[0].querySelector<HTMLButtonElement>('[data-test="revoke"]')!.click();
    fixture.detectChanges();

    expect(service.revoke).toHaveBeenCalledWith(1);
    expect(el.querySelectorAll('[data-test="parent-row"]').length).toBe(1);
    expect(el.textContent).not.toContain('Veli Bir');
  });

  it('revokeCancelled_DoesNotCallService', () => {
    const el = setup({
      items: [{ linkId: 1, parentName: 'Veli', linkedAt: '' }],
      pendingRequests: [],
      activeInviteExpiresAt: null,
      maxActiveParents: 4,
    });
    dialogResult = false;
    el.querySelector<HTMLButtonElement>('[data-test="revoke"]')!.click();
    expect(service.revoke).not.toHaveBeenCalled();
  });

  it('fourParents_HidesGenerate_ShowsLimit', () => {
    const items = [1, 2, 3, 4].map((i) => ({ linkId: i, parentName: `V${i}`, linkedAt: '' }));
    const el = setup({ items, pendingRequests: [], activeInviteExpiresAt: null, maxActiveParents: 4 });
    expect(el.querySelector('[data-test="generate"]')).toBeNull();
    expect(el.querySelector('[data-test="limit-reached"]')).not.toBeNull();
  });

  it('pendingRequestsCountTowardLimit', () => {
    const items = [1, 2, 3].map((i) => ({ linkId: i, parentName: `V${i}`, linkedAt: '' }));
    const pendingRequests = [
      { linkId: 9, parentName: 'Bekleyen', parentEmailMasked: '', requestedAt: '', expiresAt: '' },
    ];
    const el = setup({ items, pendingRequests, activeInviteExpiresAt: null, maxActiveParents: 4 });
    expect(el.querySelector('[data-test="generate"]')).toBeNull();
    expect(el.querySelector('[data-test="limit-reached"]')).not.toBeNull();
  });

  it('pendingRequest_ShowsParentName_ApproveCallsServiceAndReloads', () => {
    const el = setup({
      ...empty,
      pendingRequests: [
        {
          linkId: 7,
          parentName: 'Veli Yeni',
          parentEmailMasked: 'v***@g***.com',
          requestedAt: '2026-10-07T09:00:00Z',
          expiresAt: '2026-10-14T09:00:00Z',
        },
      ],
    });
    const row = el.querySelector('[data-test="pending-row"]');
    expect(row?.textContent).toContain('Veli Yeni');
    expect(row?.querySelector('[data-test="pending-email"]')?.textContent).toContain('v***@g***.com');
    expect(row?.querySelector('[data-test="pending-expires"]')?.textContent).toContain('2026');
    expect(el.textContent).toContain(parentLinksTr.invite.pendingWarning);
    expect(el.querySelector('[data-test="pending-section"]')?.textContent).toContain(parentLinksTr.invite.pendingTitle);

    service.approve.and.returnValue(of(undefined));
    service.getMyParents.and.returnValue(
      of({ ...empty, items: [{ linkId: 7, parentName: 'Veli Yeni', linkedAt: '2026-10-07T10:00:00Z' }] })
    );
    row!.querySelector<HTMLButtonElement>('[data-test="approve"]')!.click();
    fixture.detectChanges();

    expect(service.approve).toHaveBeenCalledWith(7);
    expect(snackBar.open).toHaveBeenCalledWith(parentLinksTr.invite.approved, jasmine.any(String), jasmine.any(Object));
    expect(el.querySelector('[data-test="pending-row"]')).toBeNull();
    expect(el.querySelectorAll('[data-test="parent-row"]').length).toBe(1);
  });

  it('pendingRequest_RejectCallsServiceAndReloads', () => {
    const el = setup({
      ...empty,
      pendingRequests: [
        { linkId: 8, parentName: 'Tanımadığım', parentEmailMasked: '', requestedAt: '', expiresAt: '' },
      ],
    });
    service.reject.and.returnValue(of(undefined));
    service.getMyParents.and.returnValue(of(empty));
    el.querySelector<HTMLButtonElement>('[data-test="reject"]')!.click();
    fixture.detectChanges();

    expect(service.reject).toHaveBeenCalledWith(8);
    expect(service.approve).not.toHaveBeenCalled();
    expect(el.querySelector('[data-test="pending-row"]')).toBeNull();
  });

  it('loadError_HidesPendingSectionAndShowsRetry', () => {
    const el = setup(empty);
    service.getMyParents.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500, error: { message: 'Yüklenemedi' } }))
    );
    (fixture.componentInstance as unknown as { load(): void }).load();
    fixture.detectChanges();

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Yüklenemedi');
    expect(el.querySelector('[data-test="pending-section"]')).toBeNull();
    expect(el.querySelector('[data-test="empty"]')).toBeNull();
  });

  it('generateError_ShowsServerMessage', () => {
    const el = setup(empty);
    service.createInviteCode.and.returnValue(
      throwError(
        () => new HttpErrorResponse({ status: 429, error: { message: 'Çok fazla', errorCode: 'RateLimited' } })
      )
    );
    el.querySelector<HTMLButtonElement>('[data-test="generate"]')!.click();
    expect(snackBar.open).toHaveBeenCalledWith('Çok fazla', jasmine.any(String), jasmine.any(Object));
    expect(el.querySelector('[data-test="invite-code"]')).toBeNull();
  });
});
