import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { StudentSettingsComponent } from './student-settings.component';
import { AuthService } from '../../services/auth.service';
import { SchoolService } from '../../services/school.service';
import { StudentService } from '../../services/student.service';
import { ParentLinkService } from '../../services/parent-link.service';
import { StudentProfile } from '../../models/student-profile';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import studentProfileTr from '../../../../public/i18n/student-profile/tr.json';
import accountSecurityTr from '../../../../public/i18n/account-security/tr.json';
import parentLinksTr from '../../../../public/i18n/parent-links/tr.json';

/** #847: öğrenci ayarları — sınıf değişikliği açık bir eylem; tema, hesap ve veliler burada. */
describe('StudentSettingsComponent (#847)', () => {
    let fixture: ComponentFixture<StudentSettingsComponent>;
    let studentService: MockedObject<StudentService>;

    const el = () => fixture.nativeElement as HTMLElement;
    const q = (id: string) => el().querySelector<HTMLElement>(`[data-testid="${id}"]`);

    function setup(profile: Partial<StudentProfile> = { gradeId: 22 }): void {
        studentService = {
            getProfile: vi.fn().mockReturnValue(of(profile as StudentProfile)),
            loadGrades: vi.fn().mockReturnValue(of([{ id: 22, name: '5. Sınıf' }, { id: 23, name: '6. Sınıf' }])),
            updateGrade: vi.fn().mockReturnValue(of({} as StudentProfile)),
            requestSchool: vi.fn().mockReturnValue(
                of({ gradeId: 22, pendingSchoolId: 5, pendingSchoolName: 'Kars Ortaokulu' } as StudentProfile),
            ),
        } as unknown as MockedObject<StudentService>;
        TestBed.configureTestingModule({
            imports: [
                StudentSettingsComponent,
                NoopAnimationsModule,
                translocoTestingModule({
                    langs: {
                        'student-profile/tr': studentProfileTr,
                        'account-security/tr': accountSecurityTr,
                        'parent-links/tr': parentLinksTr,
                    },
                }),
            ],
            providers: [
                provideHttpClient(withXhr()),
                provideHttpClientTesting(),
                provideRouter([]),
                { provide: StudentService, useValue: studentService },
                { provide: AuthService, useValue: { refreshProfile: () => of(null) } },
                { provide: SchoolService, useValue: { getSchools: () => of([{ id: 5, name: 'Kars Ortaokulu' }]) } },
                {
                    provide: ParentLinkService,
                    useValue: {
                        getMyParents: () => of({ items: [], pendingRequests: [], maxActiveParents: 4, requiresParent: true }),
                        extractError: (_: unknown, fallback: string) => fallback,
                        errorCode: () => null,
                    },
                },
            ],
        });
        fixture = TestBed.createComponent(StudentSettingsComponent);
        fixture.detectChanges();
    }

    it('studentWithoutSchool_ChoosesOnce_ThenWaitsForApproval', () => {
        setup({ gradeId: 22, schoolId: null, pendingSchoolId: null });
        q('settings-school-choose')!.click();
        fixture.detectChanges();
        expect((q('settings-school-save') as HTMLButtonElement).disabled).toBe(true);

        fixture.componentInstance.pendingSchoolId.set(5);
        fixture.detectChanges();
        q('settings-school-save')!.click();
        fixture.detectChanges();

        expect(studentService.requestSchool).toHaveBeenCalledWith(5);
        expect(q('settings-school-pending')?.textContent?.trim()).toBe('Kars Ortaokulu');
        expect(q('settings-school-choose')).toBeNull();
    });

    it('verifiedSchool_IsReadOnly', () => {
        setup({ gradeId: 22, schoolId: 5, schoolName: 'Kars Ortaokulu' });
        expect(q('settings-school')?.textContent?.trim()).toBe('Kars Ortaokulu');
        expect(q('settings-school-choose')).toBeNull();
    });

    it('schoolError_ShowsServerMessage', () => {
        setup({ gradeId: 22, schoolId: null, pendingSchoolId: null });
        studentService.requestSchool.mockReturnValue(throwError(() => ({ error: { message: 'Reddedildi' } })));
        q('settings-school-choose')!.click();
        fixture.detectChanges();
        fixture.componentInstance.pendingSchoolId.set(5);
        fixture.detectChanges();
        q('settings-school-save')!.click();
        fixture.detectChanges();
        expect(q('settings-school-error')?.textContent).toContain('Reddedildi');
        expect(q('settings-school-save')).not.toBeNull();
    });

    it('showsCurrentClass_ChangeNeedsExplicitSave', () => {
        setup();
        expect(q('settings-grade')?.textContent?.trim()).toBe('5. Sınıf');
        expect(q('settings-grade-select')).toBeNull();

        q('settings-grade-change')!.click();
        fixture.detectChanges();
        expect(el().textContent).toContain('yeni sınıfa göre gelir');
        // Aynı sınıf seçiliyken kaydedilemez.
        expect((q('settings-grade-save') as HTMLButtonElement).disabled).toBe(true);

        fixture.componentInstance.pendingGradeId.set(23);
        fixture.detectChanges();
        q('settings-grade-save')!.click();
        fixture.detectChanges();

        expect(studentService.updateGrade).toHaveBeenCalledWith(23);
        expect(q('settings-grade')?.textContent?.trim()).toBe('6. Sınıf');
        expect(q('settings-grade-saved')).not.toBeNull();
    });

    it('cancel_KeepsClass_AndDoesNotSave', () => {
        setup();
        q('settings-grade-change')!.click();
        fixture.detectChanges();
        fixture.componentInstance.pendingGradeId.set(23);
        q('settings-grade-cancel')!.click();
        fixture.detectChanges();

        expect(studentService.updateGrade).not.toHaveBeenCalled();
        expect(q('settings-grade')?.textContent?.trim()).toBe('5. Sınıf');
    });

    it('saveError_StaysInEditAndSaysSo', () => {
        setup();
        studentService.updateGrade.mockReturnValue(throwError(() => new Error('x')));
        q('settings-grade-change')!.click();
        fixture.detectChanges();
        fixture.componentInstance.pendingGradeId.set(23);
        fixture.detectChanges();
        q('settings-grade-save')!.click();
        fixture.detectChanges();

        expect(el().querySelector('[role="alert"]')?.textContent).toContain('kaydedilemedi');
        expect(q('settings-grade-select')).not.toBeNull();
    });

    it('hasThemeAccountAndParents_AccountHiddenForParentManaged', () => {
        setup({ gradeId: 22 });
        expect(el().querySelector('app-user-theme-switcher')).not.toBeNull();
        expect(el().querySelector('app-account-security-card')).not.toBeNull();
        expect(el().querySelector('app-student-parents-card')).not.toBeNull();

        TestBed.resetTestingModule();
        setup({ gradeId: 22, parentManaged: true });
        expect(el().querySelector('app-account-security-card')).toBeNull();
    });
});
