import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { RejectTeacherApplicationDialogComponent } from './reject-teacher-application-dialog.component';
import { translocoTestingModule } from '../../../../shared/testing/transloco-testing';
import adminTr from '../../../../../../public/i18n/admin/tr.json';

describe('RejectTeacherApplicationDialogComponent — privacy hint (issue #187)', () => {
  it('reasonField_ShowsPrivacyHint_LinkedToTextareaDescription', () => {
    TestBed.configureTestingModule({
      imports: [RejectTeacherApplicationDialogComponent, translocoTestingModule({ langs: { 'admin/tr': adminTr } })],
      providers: [
        provideNoopAnimations(),
        { provide: MAT_DIALOG_DATA, useValue: { displayName: 'Ali Öğretmen' } },
        { provide: MatDialogRef, useValue: jasmine.createSpyObj('MatDialogRef', ['close']) },
      ],
    });
    const fixture = TestBed.createComponent(RejectTeacherApplicationDialogComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    const hint = root.querySelector<HTMLElement>('[data-testid="privacy-hint"]');
    expect(hint?.textContent?.trim()).toBe(adminTr.approvals.rejectDialog.privacyHint);
    const describedBy = root.querySelector('textarea')?.getAttribute('aria-describedby') ?? '';
    expect(hint?.id).toBeTruthy();
    expect(describedBy.split(' ')).toContain(hint!.id);
  });
});
