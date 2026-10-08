import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { PARENT_HIDDEN_KEYS, PARENT_SEES_KEYS, StudentParentsCardComponent } from './student-parents-card.component';
import { ParentLinkService } from '../../../services/parent-link.service';
import { LocaleService } from '../../../services/locale.service';
import { StudentParentLinks } from '../../../models/parent-link.model';
import { translocoTestingModule } from '../../testing/transloco-testing';
import parentLinksTr from '../../../../../public/i18n/parent-links/tr.json';
import parentLinksEn from '../../../../../public/i18n/parent-links/en.json';

/**
 * Issue #436: öğrencinin salt okunur "Velilerim" kartı — bağlı veliler + birincil işareti, "Velim ne görüyor?", velisiz
 * öğrenciye bilgi (#437), geçiş dönemindeki eski isteklerin onay/reddi. Kod üretme / koparma YOK.
 */
describe('StudentParentsCardComponent (issue #436)', () => {
  let fixture: ComponentFixture<StudentParentsCardComponent>;
  let service: jasmine.SpyObj<ParentLinkService>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;

  function setup(parents: StudentParentLinks): HTMLElement {
    service = jasmine.createSpyObj<ParentLinkService>('ParentLinkService', [
      'getMyParents',
      'approve',
      'reject',
      'revoke',
      'createSecondParentCode',
      'extractError',
      'errorCode',
    ]);
    service.getMyParents.and.returnValue(of(parents));
    service.extractError.and.callFake((err: HttpErrorResponse, fallback: string) => err.error?.message ?? fallback);
    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    TestBed.configureTestingModule({
      imports: [
        StudentParentsCardComponent,
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
    fixture = TestBed.createComponent(StudentParentsCardComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const empty: StudentParentLinks = { items: [], pendingRequests: [], maxActiveParents: 4, requiresParent: true };

  const linked: StudentParentLinks = {
    ...empty,
    items: [
      { linkId: 1, parentName: 'Fatma Kaya', linkedAt: '2026-10-01T09:00:00Z', isPrimary: true },
      { linkId: 2, parentName: 'Ali Kaya', linkedAt: '2026-10-03T09:00:00Z', isPrimary: false },
    ],
  };

  it('listsLinkedParents_ReadOnly_WithPrimaryBadge_AndNoManagementActions', () => {
    const el = setup(linked);

    const rows = el.querySelectorAll('[data-test="parent-row"]');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Fatma Kaya');
    expect(rows[0].querySelector('[data-test="primary-badge"]')?.textContent).toContain(parentLinksTr.parents.primary);
    expect(rows[1].querySelector('[data-test="primary-badge"]')).toBeNull();
    // Öğrenci kod üretmez, veli kaldıramaz.
    expect(el.querySelector('button[data-test="generate"]')).toBeNull();
    expect(el.querySelector('button[data-test="revoke"]')).toBeNull();
    expect(el.querySelectorAll('button').length).toBe(0);
    expect(service.revoke).not.toHaveBeenCalled();
    expect(service.createSecondParentCode).not.toHaveBeenCalled();
  });

  it('showsWhatParentCanAndCannotSee', () => {
    const el = setup(linked);
    const section = el.querySelector('[data-test="what-parent-sees"]')!;

    expect(section.textContent).toContain(parentLinksTr.parents.seesTitle);
    const sees = section.querySelectorAll('[data-test="sees-list"] li');
    const hidden = section.querySelectorAll('[data-test="hidden-list"] li');
    expect(sees.length).toBe(PARENT_SEES_KEYS.length);
    expect(hidden.length).toBe(PARENT_HIDDEN_KEYS.length);
    expect(section.textContent).toContain(parentLinksTr.parents.sees.progress);
    expect(section.textContent).toContain(parentLinksTr.parents.hidden.messages);
    expect(section.textContent).toContain(parentLinksTr.parents.hidden.contact);
  });

  it('studentWithoutParent_SeesMissingParentNotice (issue #437)', () => {
    const el = setup(empty);
    expect(el.querySelector('[data-test="missing-parent"]')?.textContent).toContain(parentLinksTr.parents.missing);
    expect(el.querySelector('[data-test="what-parent-sees"]')).not.toBeNull();
  });

  it('legacyRequest_CanBeApprovedDuringTransition_ThenReloads', () => {
    const el = setup({
      ...empty,
      pendingRequests: [
        {
          linkId: 9,
          parentName: 'Eski Veli',
          parentEmailMasked: 'e***@g***.com',
          requestedAt: '2026-09-20T09:00:00Z',
          expiresAt: '2026-10-20T09:00:00Z',
        },
      ],
    });
    const row = el.querySelector('[data-test="legacy-row"]')!;
    expect(row.textContent).toContain('Eski Veli');
    expect(row.textContent).toContain('e***@g***.com');

    service.approve.and.returnValue(of(undefined));
    row.querySelector<HTMLButtonElement>('[data-test="approve"]')!.click();
    fixture.detectChanges();

    expect(service.approve).toHaveBeenCalledWith(9);
    expect(service.getMyParents).toHaveBeenCalledTimes(2);
    expect(snackBar.open).toHaveBeenCalledWith(parentLinksTr.parents.approved, jasmine.any(String), jasmine.any(Object));
  });

  it('legacyReject_ShowsServerErrorAndReloads', () => {
    const el = setup({
      ...empty,
      pendingRequests: [
        { linkId: 9, parentName: 'Eski Veli', parentEmailMasked: '', requestedAt: '', expiresAt: '2026-10-20T09:00:00Z' },
      ],
    });
    service.reject.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 404, error: { message: 'Bağlantı bulunamadı.' } }))
    );
    el.querySelector<HTMLButtonElement>('[data-test="reject"]')!.click();
    fixture.detectChanges();

    expect(service.reject).toHaveBeenCalledWith(9);
    expect(snackBar.open).toHaveBeenCalledWith('Bağlantı bulunamadı.', jasmine.any(String), jasmine.any(Object));
    expect(service.getMyParents).toHaveBeenCalledTimes(2);
  });

  it('noLegacySection_WhenNoOldRequests', () => {
    const el = setup(linked);
    expect(el.querySelector('[data-test="legacy-section"]')).toBeNull();
  });
});
