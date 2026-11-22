import type { MockedObject } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute } from '@angular/router';
import { By } from '@angular/platform-browser';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';

import { SchoolSectionsComponent } from './school-sections.component';
import { SchoolSection, SchoolSectionService } from '../../services/school-section.service';
import { GradesService } from '../../services/grades.service';
import { LocaleService } from '../../services/locale.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import scopeTr from '../../../../public/i18n/school-sections/tr.json';
import scopeEn from '../../../../public/i18n/school-sections/en.json';

const sections: SchoolSection[] = [
    { id: 1, gradeId: 7, gradeName: '7. Sınıf', name: 'A', studentCount: 2 },
    { id: 2, gradeId: 7, gradeName: '7. Sınıf', name: 'B', studentCount: 0 },
    { id: 3, gradeId: 8, gradeName: '8. Sınıf', name: 'A', studentCount: 1 },
];

describe('SchoolSectionsComponent (issue #1279)', () => {
    let service: MockedObject<SchoolSectionService>;
    let dialog: { open: ReturnType<typeof vi.fn> };

    function create(manage: boolean, list = sections) {
        service = {
            list: vi.fn().mockReturnValue(of(list)),
            students: vi.fn().mockReturnValue(
                of({
                    pageNumber: 1,
                    pageSize: 50,
                    totalCount: 1,
                    items: [{ studentId: 11, fullName: 'Ayşe Öğrenci', studentNumber: '****1234', since: '2026-11-01T00:00:00Z' }],
                }),
            ),
            assign: vi.fn().mockReturnValue(of({ assigned: 1, unchanged: 0 })),
            remove: vi.fn().mockReturnValue(of(undefined)),
            delete: vi.fn().mockReturnValue(of(undefined)),
        } as unknown as MockedObject<SchoolSectionService>;
        dialog = { open: vi.fn() };

        TestBed.configureTestingModule({
            imports: [
                SchoolSectionsComponent,
                translocoTestingModule({ langs: { 'school-sections/tr': scopeTr, 'school-sections/en': scopeEn } }),
            ],
            providers: [
                { provide: SchoolSectionService, useValue: service },
                { provide: GradesService, useValue: { getGrades: () => of([{ id: 7, name: '7. Sınıf' }]) } },
                { provide: LocaleService, useValue: { localeDefinition: () => ({ angularLocale: 'tr' }) } },
                { provide: ActivatedRoute, useValue: { snapshot: { data: { schoolSectionsManage: manage } } } },
                { provide: MatDialog, useValue: dialog },
                { provide: MatSnackBar, useValue: { open: vi.fn() } },
                provideNoopAnimations(),
            ],
        });
        const fixture = TestBed.createComponent(SchoolSectionsComponent);
        fixture.detectChanges();
        return fixture;
    }

    it('groups sections by grade and opens the first one', () => {
        const fixture = create(false);
        const text = fixture.nativeElement.textContent as string;

        expect(text).toContain('7. Sınıf');
        expect(text).toContain('8. Sınıf');
        expect(fixture.debugElement.queryAll(By.css('[data-testid="section-item"]')).length).toBe(3);
        expect(service.students).toHaveBeenCalledWith(1, 1, 50);
        expect(text).toContain('Ayşe Öğrenci');
    });

    it('is read-only for teachers', () => {
        const fixture = create(false);
        for (const id of ['create-section', 'section-menu', 'add-students', 'student-menu']) {
            expect(fixture.debugElement.query(By.css(`[data-testid="${id}"]`)), id).toBeNull();
        }
    });

    it('shows management controls for the school admin', () => {
        const fixture = create(true);
        for (const id of ['create-section', 'section-menu', 'add-students', 'student-menu']) {
            expect(fixture.debugElement.query(By.css(`[data-testid="${id}"]`)), id).not.toBeNull();
        }
    });

    it('moves a student only to sections of the same grade', () => {
        const fixture = create(true);
        const cmp = fixture.componentInstance as unknown as {
            moveTargets(): SchoolSection[];
            move(st: { studentId: number; fullName: string }, target: SchoolSection): void;
        };
        expect(cmp.moveTargets().map((s) => s.id)).toEqual([2]);

        cmp.move({ studentId: 11, fullName: 'Ayşe Öğrenci' }, sections[1]);
        expect(service.assign).toHaveBeenCalledWith(2, [11]);
    });

    it('selecting another section loads its students', () => {
        const fixture = create(false);
        fixture.debugElement.queryAll(By.css('[data-testid="section-item"]'))[2].nativeElement.click();
        expect(service.students).toHaveBeenLastCalledWith(3, 1, 50);
    });

    it('shows the empty state when there are no sections', () => {
        const fixture = create(true, []);
        expect(fixture.debugElement.query(By.css('[data-testid="sections-empty"]'))).not.toBeNull();
    });
});
