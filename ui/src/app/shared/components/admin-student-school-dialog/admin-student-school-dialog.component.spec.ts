import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Subject, of, throwError } from 'rxjs';

import {
  AdminStudentSchoolDialogComponent,
  AdminStudentSchoolDialogData,
  AdminStudentSchoolDialogResult,
} from './admin-student-school-dialog.component';
import { AdminService } from '../../../services/admin.service';
import { SchoolService } from '../../../services/school.service';
import { School } from '../../../models/taxonomy';
import { AdminStudentSchoolResponse } from '../../../models/admin-student-school.model';
import { translocoTestingModule } from '../../testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';
import adminEn from '../../../../../public/i18n/admin/en.json';

const school = (id: number, name: string): School => ({
  id,
  name,
  provinceId: null,
  provinceName: null,
  districtId: null,
  districtName: null,
  addressLine: null,
});

describe('AdminStudentSchoolDialogComponent (issue #277)', () => {
  const texts = adminTr.studentSchool;
  let fixture: ComponentFixture<AdminStudentSchoolDialogComponent>;
  let component: AdminStudentSchoolDialogComponent;
  let adminService: jasmine.SpyObj<AdminService>;
  let dialogRef: jasmine.SpyObj<MatDialogRef<AdminStudentSchoolDialogComponent, AdminStudentSchoolDialogResult | undefined>>;

  function configure(data: Partial<AdminStudentSchoolDialogData> = {}): void {
    adminService = jasmine.createSpyObj<AdminService>('AdminService', ['changeStudentSchool']);
    dialogRef = jasmine.createSpyObj('MatDialogRef', ['close']);
    TestBed.configureTestingModule({
      imports: [AdminStudentSchoolDialogComponent, translocoTestingModule({ langs: { 'admin/tr': adminTr } })],
      providers: [
        provideNoopAnimations(),
        { provide: AdminService, useValue: adminService },
        { provide: SchoolService, useValue: { getSchools: () => of([school(5, 'Ankara Lisesi'), school(8, 'İzmir Lisesi')]) } },
        { provide: MatDialogRef, useValue: dialogRef },
        {
          provide: MAT_DIALOG_DATA,
          useValue: { id: 7, displayName: 'Ali Veli', currentSchoolId: 5, currentSchoolName: 'Ankara Lisesi', ...data },
        },
      ],
    });
    fixture = TestBed.createComponent(AdminStudentSchoolDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  const el = (): HTMLElement => fixture.nativeElement;
  const button = (id: string): HTMLButtonElement | null => el().querySelector(`button[data-testid="${id}"]`);

  function pick(id = 8): void {
    component.selectedSchoolId.set(id);
    component.onSchoolSelected(school(id, id === 8 ? 'İzmir Lisesi' : 'Başka'));
    fixture.detectChanges();
  }

  function confirmWithError(status: number, error: unknown = null): void {
    adminService.changeStudentSchool.and.returnValue(throwError(() => new HttpErrorResponse({ status, error })));
    pick();
    button('confirm')!.click();
    fixture.detectChanges();
  }

  it('render_ShowsCurrentSchoolEffectsAndExcludesCurrentSchool', () => {
    configure();
    expect(el().querySelector('[data-testid="current-school"]')?.textContent).toContain('Ankara Lisesi');
    expect(el().querySelector('[data-testid="school-effects"]')?.textContent).toContain(texts.effectOldTeachers);
    expect(button('confirm')!.disabled).toBeTrue();
  });

  it('render_NoCurrentSchool_ShowsNoSchoolLabel', () => {
    configure({ currentSchoolId: null, currentSchoolName: null });
    expect(el().querySelector('[data-testid="current-school"]')?.textContent).toContain(texts.noSchool);
  });

  it('confirm_WhileSubmitting_SendsSingleRequest', () => {
    configure();
    adminService.changeStudentSchool.and.returnValue(new Subject<AdminStudentSchoolResponse>());
    pick();

    component.confirm();
    component.confirm();

    expect(adminService.changeStudentSchool).toHaveBeenCalledOnceWith(7, 8);
    expect(component.submitting()).toBeTrue();
  });

  it('confirm_Success_ClosesWithServerSchoolAndSelectedName', () => {
    configure();
    adminService.changeStudentSchool.and.returnValue(of({ studentId: 7, schoolId: 8, previousSchoolId: 5, changed: true }));
    pick();

    button('confirm')!.click();

    expect(dialogRef.close).toHaveBeenCalledOnceWith({ schoolId: 8, schoolName: 'İzmir Lisesi' });
  });

  it('error400WithoutBody_ShowsInvalidSchoolText', () => {
    configure();
    confirmWithError(400);
    expect(el().querySelector('[data-testid="school-error"]')?.textContent).toContain(texts.errors.invalidSchool);
  });

  it('error403_ShowsServerMessage', () => {
    configure();
    confirmWithError(403, { message: 'Korumalı hesap.' });
    expect(el().querySelector('[data-testid="school-error"]')?.textContent).toContain('Korumalı hesap.');
  });

  it('error502_ThenCancel_ClosesWithRefresh', () => {
    configure();
    confirmWithError(502);
    expect(el().querySelector('[data-testid="school-error"]')?.textContent).toContain(texts.errors.upstream);

    button('cancel')!.click();

    expect(dialogRef.close).toHaveBeenCalledOnceWith({ refresh: true });
  });

  it('error409WithoutBody_ShowsConflictTextAndReloadClosesWithRefresh', () => {
    configure();
    confirmWithError(409);
    expect(el().querySelector('[data-testid="school-error"]')?.textContent).toContain(texts.errors.conflict);
    expect(button('confirm')).toBeNull();

    button('reload')!.click();

    expect(dialogRef.close).toHaveBeenCalledOnceWith({ refresh: true });
  });

  it('cancelWithoutError_ClosesWithUndefined', () => {
    configure();
    button('cancel')!.click();
    expect(dialogRef.close).toHaveBeenCalledOnceWith(undefined);
  });

  it('i18n_TrAndEnHaveSameKeys', () => {
    const keys = (o: object): string[] =>
      Object.entries(o).flatMap(([k, v]) => (v && typeof v === 'object' ? keys(v).map((c) => `${k}.${c}`) : [k]));
    expect(keys(adminEn.studentSchool).sort()).toEqual(keys(adminTr.studentSchool).sort());
  });
});
