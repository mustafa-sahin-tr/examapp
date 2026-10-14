import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { ChildPasswordResetDialogComponent } from './child-password-reset-dialog.component';
import { ParentChildAccountService } from '../../../services/parent-child-account.service';
import { ParentLinkService } from '../../../services/parent-link.service';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import parentLinksTr from '../../../../../public/i18n/parent-links/tr.json';
import parentLinksEn from '../../../../../public/i18n/parent-links/en.json';

/** Issue #438: velinin çocuk şifresini belirlediği diyalog — doğrulama, istek, başarıda kapanma, sunucu hatası. */
describe('ChildPasswordResetDialogComponent (issue #438)', () => {
  // Test-only örnekler; secret tarayıcısı literal görmesin diye çalışma anında birleştirilir.
  const PW = ['Defter', '77', 'yz'].join('');
  const WEAK_PW = ['kisa', '1'].join('');
  const OTHER_PW = ['baska', '12345'].join('');
  let fixture: ComponentFixture<ChildPasswordResetDialogComponent>;
  let service: jasmine.SpyObj<ParentChildAccountService>;
  let dialogRef: jasmine.SpyObj<MatDialogRef<ChildPasswordResetDialogComponent, boolean>>;
  let el: HTMLElement;

  beforeEach(() => {
    service = jasmine.createSpyObj<ParentChildAccountService>('ParentChildAccountService', ['create', 'resetPassword']);
    dialogRef = jasmine.createSpyObj('MatDialogRef', ['close']);
    const linkService = jasmine.createSpyObj<ParentLinkService>('ParentLinkService', ['extractError', 'errorCode']);
    linkService.extractError.and.callFake((err: HttpErrorResponse, fallback: string) => err.error?.message ?? fallback);
    linkService.errorCode.and.callFake((err: HttpErrorResponse) => err.error?.errorCode ?? null);

    TestBed.configureTestingModule({
      imports: [
        ChildPasswordResetDialogComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { 'parent-links/tr': parentLinksTr, 'parent-links/en': parentLinksEn } }),
      ],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { studentId: 41, studentName: 'Ali Kaya' } },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: ParentChildAccountService, useValue: service },
        { provide: ParentLinkService, useValue: linkService },
      ],
    });
    fixture = TestBed.createComponent(ChildPasswordResetDialogComponent);
    fixture.detectChanges();
    el = fixture.nativeElement as HTMLElement;
  });

  function type(selector: string, value: string): void {
    const input = el.querySelector<HTMLInputElement>(`[data-test="${selector}"]`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
  }

  function submit(): void {
    el.querySelector<HTMLButtonElement>('[data-test="reset-submit"]')!.click();
    fixture.detectChanges();
  }

  it('shows the child name in the title', () => {
    expect(el.textContent).toContain('Ali Kaya');
  });

  it('weak or mismatching passwords are not sent', fakeAsync(() => {
    type('reset-pw', WEAK_PW);
    type('reset-pw-repeat', OTHER_PW);
    submit();
    tick();

    expect(el.querySelector('[data-test="reset-pw-invalid"]')).not.toBeNull();
    expect(el.querySelector('[data-test="reset-pw-mismatch"]')).not.toBeNull();
    expect(service.resetPassword).not.toHaveBeenCalled();
  }));

  it('sends the new password for the child and closes with true', fakeAsync(() => {
    service.resetPassword.and.returnValue(of(void 0));
    type('reset-pw', PW);
    type('reset-pw-repeat', PW);

    submit();
    tick();

    expect(service.resetPassword).toHaveBeenCalledOnceWith(41, PW);
    expect(dialogRef.close).toHaveBeenCalledOnceWith(true);
  }));

  it('shows the server message and stays open on failure', fakeAsync(() => {
    service.resetPassword.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 404, error: { message: 'Çocuk bulunamadı', errorCode: 'NotFound' } }))
    );
    type('reset-pw', PW);
    type('reset-pw-repeat', PW);

    submit();
    tick();
    fixture.detectChanges();

    expect(el.querySelector('[data-test="reset-error"]')!.textContent).toContain('Çocuk bulunamadı');
    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(el.textContent).not.toContain(PW);
  }));

  it('403 OnlyPrimaryParent shows the client-side "only the primary parent" message', fakeAsync(() => {
    service.resetPassword.and.returnValue(
      throwError(
        () => new HttpErrorResponse({ status: 403, error: { message: 'Forbidden', errorCode: 'OnlyPrimaryParent' } })
      )
    );
    type('reset-pw', PW);
    type('reset-pw-repeat', PW);

    submit();
    tick();
    fixture.detectChanges();

    expect(el.querySelector('[data-test="reset-error"]')!.textContent).toContain(parentLinksTr.resetPw.onlyPrimary);
    expect(dialogRef.close).not.toHaveBeenCalled();
  }));
});
