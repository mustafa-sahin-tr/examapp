import type { MockedObject } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { By } from '@angular/platform-browser';
import { of, throwError } from 'rxjs';

import { SchoolOverviewComponent } from './school-overview.component';
import { SchoolAdminService, SchoolAdminStudent, SchoolAdminTeacher } from '../../../services/school-admin.service';
import { CacheService } from '../../../services/cache.service';
import { LocaleService } from '../../../services/locale.service';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import scopeTr from '../../../../../public/i18n/school-overview/tr.json';
import scopeEn from '../../../../../public/i18n/school-overview/en.json';

const teacher: SchoolAdminTeacher = {
    teacherId: 1,
    fullName: 'Ali Yönetici',
    isSchoolAdmin: true,
    isSuspended: false,
    approvedAt: '2026-10-01T08:00:00Z',
    subjects: ['Matematik'],
};
const student: SchoolAdminStudent = {
    studentId: 9,
    fullName: 'Ayşe Öğrenci',
    studentNumber: '****1234',
    gradeId: 5,
    gradeName: '5. Sınıf',
    verifiedAt: '2026-10-02T08:00:00Z',
};

describe('SchoolOverviewComponent (issue #1278)', () => {
    let service: MockedObject<SchoolAdminService>;

    function create(opts: { summaryFails?: boolean } = {}) {
        service = {
            refreshSummary: vi.fn(),
            getTeachers: vi.fn(),
            getStudents: vi.fn(),
        } as unknown as MockedObject<SchoolAdminService>;
        service.refreshSummary.mockReturnValue(
            opts.summaryFails
                ? throwError(() => new Error('x'))
                : of({
                      schoolId: 3,
                      schoolName: 'Atatürk Ortaokulu',
                      teacherCount: 4,
                      studentCount: 120,
                      pendingTeacherApplications: 2,
                      pendingStudentRequests: 5,
                  }),
        );
        service.getTeachers.mockReturnValue(of({ pageNumber: 1, pageSize: 20, totalCount: 1, items: [teacher] }));
        service.getStudents.mockReturnValue(of({ pageNumber: 1, pageSize: 20, totalCount: 1, items: [student] }));

        TestBed.configureTestingModule({
            imports: [
                SchoolOverviewComponent,
                translocoTestingModule({ langs: { 'school-overview/tr': scopeTr, 'school-overview/en': scopeEn } }),
            ],
            providers: [
                provideRouter([]),
                { provide: SchoolAdminService, useValue: service },
                { provide: CacheService, useValue: { getGrades: () => of([{ id: 5, name: '5. Sınıf' }]) } },
                { provide: LocaleService, useValue: { localeDefinition: () => ({ angularLocale: 'tr' }) } },
                provideNoopAnimations(),
            ],
        });
        const fixture = TestBed.createComponent(SchoolOverviewComponent);
        fixture.detectChanges();
        return fixture;
    }

    it('renders school name, counts, teachers and students', () => {
        const fixture = create();
        const text = fixture.nativeElement.textContent as string;

        expect(text).toContain('Atatürk Ortaokulu');
        expect(text).toContain('120');
        expect(text).toContain('Ali Yönetici');
        expect(text).toContain('Okul yöneticisi');
        expect(text).toContain('Ayşe Öğrenci');
        expect(text).toContain('****1234');
        expect(service.getTeachers).toHaveBeenCalledWith(1, 20);
        expect(service.getStudents).toHaveBeenCalledWith(1, 20, null);
    });

    it('links pending cards to the existing approval queues', () => {
        const fixture = create();
        const href = (id: string) =>
            fixture.debugElement.query(By.css(`[data-testid="${id}"]`)).nativeElement.getAttribute('href');

        expect(href('card-teacher-applications')).toBe('/school-admin/teacher-approvals');
        expect(href('card-student-requests')).toBe('/student-school-requests');
    });

    it('reloads students from the first page when the grade filter changes', () => {
        const fixture = create();
        (fixture.componentInstance as unknown as { onGradeChange(id: number | null): void }).onGradeChange(5);

        expect(service.getStudents).toHaveBeenLastCalledWith(1, 20, 5);
    });

    it('shows an error with retry when the summary fails, lists still load', () => {
        const fixture = create({ summaryFails: true });
        const text = fixture.nativeElement.textContent as string;

        expect(text).toContain('Okul özeti yüklenemedi.');
        expect(text).toContain('Ali Yönetici');
    });
});
