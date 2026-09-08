import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import {
  ManageSubjectGradesDialogComponent,
  ManageSubjectGradesDialogData,
} from './manage-subject-grades-dialog.component';
import { AdminService } from '../../../../services/admin.service';
import { ApiResult, TaxonomyGrade } from '../../../../models/taxonomy';

describe('ManageSubjectGradesDialogComponent', () => {
  let fixture: ComponentFixture<ManageSubjectGradesDialogComponent>;
  let component: ManageSubjectGradesDialogComponent;
  let adminService: jasmine.SpyObj<AdminService>;
  let dialogRef: jasmine.SpyObj<MatDialogRef<ManageSubjectGradesDialogComponent, boolean>>;

  const okResult: ApiResult = { success: true, message: 'İşlem başarılı' };
  const failResult: ApiResult = { success: false, message: 'Sunucu reddetti' };

  const grades: TaxonomyGrade[] = [
    { id: 5, name: '5. Sınıf' },
    { id: 6, name: '6. Sınıf' },
    { id: 7, name: '7. Sınıf' },
  ];

  function configure(selectedGradeIds: number[] = [5], gradeList: TaxonomyGrade[] = grades): void {
    adminService = jasmine.createSpyObj<AdminService>('AdminService', [
      'addSubjectGrade',
      'removeSubjectGrade',
    ]);
    adminService.addSubjectGrade.and.returnValue(of(okResult));
    adminService.removeSubjectGrade.and.returnValue(of(okResult));

    dialogRef = jasmine.createSpyObj<MatDialogRef<ManageSubjectGradesDialogComponent, boolean>>(
      'MatDialogRef',
      ['close']
    );

    const data: ManageSubjectGradesDialogData = {
      subjectId: 1,
      subjectName: 'Matematik',
      grades: gradeList,
      selectedGradeIds,
    };

    TestBed.configureTestingModule({
      imports: [ManageSubjectGradesDialogComponent],
      providers: [
        { provide: AdminService, useValue: adminService },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: MAT_DIALOG_DATA, useValue: data },
        provideNoopAnimations(),
      ],
    });

    fixture = TestBed.createComponent(ManageSubjectGradesDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function checkboxInputs(): HTMLInputElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('mat-checkbox input[type="checkbox"]'));
  }

  // ── Açılış durumu ─────────────────────────────────────────────────────────

  it('init_SelectedGradeIdsGiven_MarksOnlyThoseCheckboxesChecked', () => {
    configure([5, 7]);

    const inputs = checkboxInputs();
    expect(inputs.length).toBe(3);
    expect(inputs.map((i) => i.checked)).toEqual([true, false, true]);
    expect(component.isChecked(5)).toBeTrue();
    expect(component.isChecked(6)).toBeFalse();
    expect(component.isChecked(7)).toBeTrue();
  });

  it('init_NoChanges_HasChangesFalseAndSaveDisabled', () => {
    configure([5]);

    expect(component.hasChanges()).toBeFalse();
    const saveBtn: HTMLButtonElement = fixture.nativeElement.querySelector('mat-dialog-actions button[color="primary"]');
    expect(saveBtn.disabled).toBeTrue();
  });

  it('init_EmptyGradeList_ShowsEmptyState', () => {
    configure([], []);

    const empty: HTMLElement = fixture.nativeElement.querySelector('.empty-state');
    expect(empty).toBeTruthy();
    expect(empty.textContent).toContain('Tanımlı sınıf yok');
  });

  // ── Diff hesaplama ────────────────────────────────────────────────────────

  it('toggle_AddAndRemove_ComputesToAddAndToRemove', () => {
    configure([5]);

    component.toggle(6, true);
    component.toggle(5, false);

    expect(component.toAdd()).toEqual([6]);
    expect(component.toRemove()).toEqual([5]);
    expect(component.hasChanges()).toBeTrue();
  });

  it('toggle_RevertToInitial_HasChangesFalse', () => {
    configure([5]);

    component.toggle(5, false);
    component.toggle(5, true);

    expect(component.toAdd()).toEqual([]);
    expect(component.toRemove()).toEqual([]);
    expect(component.hasChanges()).toBeFalse();
  });

  // ── save(): sadece değişenler için çağrı ──────────────────────────────────

  it('save_NoChanges_ClosesWithFalseWithoutCallingService', async () => {
    configure([5]);

    await component.save();

    expect(adminService.addSubjectGrade).not.toHaveBeenCalled();
    expect(adminService.removeSubjectGrade).not.toHaveBeenCalled();
    expect(dialogRef.close).toHaveBeenCalledOnceWith(false);
  });

  it('save_OnlyChangedGrades_CallsAddAndRemoveForDiffOnly', async () => {
    configure([5, 6]);
    component.toggle(7, true); // ekle
    component.toggle(5, false); // kaldır
    // 6 değişmedi → çağrı yapılmamalı

    await component.save();

    expect(adminService.addSubjectGrade).toHaveBeenCalledOnceWith(1, 7);
    expect(adminService.removeSubjectGrade).toHaveBeenCalledOnceWith(1, 5);
    expect(adminService.addSubjectGrade).not.toHaveBeenCalledWith(1, 6);
    expect(adminService.removeSubjectGrade).not.toHaveBeenCalledWith(1, 6);
  });

  it('save_AllRequestsSucceed_ClosesWithTrue', async () => {
    configure([5]);
    component.toggle(6, true);

    await component.save();

    expect(dialogRef.close).toHaveBeenCalledOnceWith(true);
    expect(component.error()).toBeNull();
    expect(component.saving()).toBeFalse();
  });

  // ── Kısmi başarı / hata birikimi ──────────────────────────────────────────

  it('save_SomeRequestsFail_KeepsDialogOpenAndShowsJoinedErrors', async () => {
    configure([5]);
    component.toggle(6, true);
    component.toggle(7, true);
    adminService.addSubjectGrade.withArgs(1, 6).and.returnValue(of(okResult));
    adminService.addSubjectGrade.withArgs(1, 7).and.returnValue(of(failResult));

    await component.save();
    fixture.detectChanges();

    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(component.saving()).toBeFalse();
    expect(component.error()).toBe('Sunucu reddetti');
    const errorBox: HTMLElement = fixture.nativeElement.querySelector('.state-box--error');
    expect(errorBox).toBeTruthy();
    expect(errorBox.textContent).toContain('Sunucu reddetti');
  });

  it('save_HttpErrorAndApiFailure_AccumulatesBothMessages', async () => {
    configure([5]);
    component.toggle(6, true);
    component.toggle(5, false);
    adminService.addSubjectGrade.and.returnValue(
      throwError(() => ({ error: { message: 'Ağ hatası' } }))
    );
    adminService.removeSubjectGrade.and.returnValue(of(failResult));

    await component.save();

    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(component.error()).toBe('Ağ hatası · Sunucu reddetti');
  });

  it('save_HttpErrorWithoutMessage_UsesFallbackText', async () => {
    configure([5]);
    component.toggle(6, true);
    adminService.addSubjectGrade.and.returnValue(throwError(() => new Error('boom')));

    await component.save();

    expect(component.error()).toBe('İşlem başarısız');
  });

  it('save_PartialSuccess_SucceededItemsLeaveDiffAndFailedOnesRemain', async () => {
    configure([5]);
    component.toggle(6, true);
    component.toggle(7, true);
    adminService.addSubjectGrade.withArgs(1, 6).and.returnValue(of(okResult));
    adminService.addSubjectGrade.withArgs(1, 7).and.returnValue(of(failResult));

    await component.save();

    // 6 initial'a işlendi; sadece 7 tekrar denenecek.
    expect(component.toAdd()).toEqual([7]);
    expect(component.hasChanges()).toBeTrue();
  });

  it('save_RetryAfterPartialFailure_OnlyRetriesFailedGrade', async () => {
    configure([5]);
    component.toggle(6, true);
    component.toggle(7, true);
    adminService.addSubjectGrade.withArgs(1, 6).and.returnValue(of(okResult));
    adminService.addSubjectGrade.withArgs(1, 7).and.returnValue(of(failResult));
    await component.save();

    adminService.addSubjectGrade.withArgs(1, 7).and.returnValue(of(okResult));
    await component.save();

    expect(adminService.addSubjectGrade).toHaveBeenCalledTimes(3);
    expect(adminService.addSubjectGrade.calls.allArgs().filter(([, g]) => g === 6).length).toBe(1);
    expect(dialogRef.close).toHaveBeenCalledOnceWith(true);
  });

  // ── cancel(): applied sayacına göre kapanış ───────────────────────────────

  it('cancel_NothingApplied_ClosesWithFalse', () => {
    configure([5]);
    component.toggle(6, true);

    component.cancel();

    expect(dialogRef.close).toHaveBeenCalledOnceWith(false);
  });

  it('cancel_AfterPartialSuccess_ClosesWithTrueSoParentReloads', async () => {
    configure([5]);
    component.toggle(6, true);
    component.toggle(7, true);
    adminService.addSubjectGrade.withArgs(1, 6).and.returnValue(of(okResult));
    adminService.addSubjectGrade.withArgs(1, 7).and.returnValue(of(failResult));
    await component.save();

    component.cancel();

    expect(dialogRef.close).toHaveBeenCalledOnceWith(true);
  });

  it('cancel_AllRequestsFailed_ClosesWithFalse', async () => {
    configure([5]);
    component.toggle(6, true);
    adminService.addSubjectGrade.and.returnValue(of(failResult));
    await component.save();

    component.cancel();

    expect(dialogRef.close).toHaveBeenCalledOnceWith(false);
  });

  it('cancelButtonClick_AfterPartialSuccess_ClosesWithTrue', async () => {
    configure([5]);
    component.toggle(6, true);
    component.toggle(7, true);
    adminService.addSubjectGrade.withArgs(1, 6).and.returnValue(of(okResult));
    adminService.addSubjectGrade.withArgs(1, 7).and.returnValue(of(failResult));
    await component.save();
    fixture.detectChanges();

    const cancelBtn: HTMLButtonElement = fixture.nativeElement.querySelector('mat-dialog-actions button');
    expect(cancelBtn.textContent).toContain('İptal');
    cancelBtn.click();

    expect(dialogRef.close).toHaveBeenCalledOnceWith(true);
  });

  it('actions_WhileSaving_BothButtonsDisabled', () => {
    configure([5]);
    component.toggle(6, true);
    component.saving.set(true);
    fixture.detectChanges();

    const buttons: HTMLButtonElement[] = Array.from(
      fixture.nativeElement.querySelectorAll('mat-dialog-actions button')
    );
    expect(buttons.length).toBe(2);
    expect(buttons.every((b) => b.disabled)).toBeTrue();
  });
});
