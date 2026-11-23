import type { MockedObject } from "vitest";
import { WritableSignal, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNativeDateAdapter } from '@angular/material/core';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@jsverse/transloco';

import { StudentLookupStatus, WorksheetAssignmentDialogComponent, commentsOverrideValue, WorksheetAssignmentDialogData, } from './worksheet-assignment-dialog.component';
import { StudentLookup } from '../../../../models/student';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../../../models/locale';
import rootTr from '../../../../../../public/i18n/tr.json';
import worksheetDetailTr from '../../../../../../public/i18n/worksheet-detail/tr.json';
import { of } from 'rxjs';
import { SchoolSection, SchoolSectionService } from '../../../../services/school-section.service';

const translocoTesting = TranslocoTestingModule.forRoot({
    langs: { tr: { ...rootTr, 'worksheet-detail': worksheetDetailTr }, 'worksheet-detail/tr': worksheetDetailTr },
    translocoConfig: {
        availableLangs: [...SUPPORTED_LOCALE_CODES],
        defaultLang: DEFAULT_LOCALE,
        scopes: { keepCasing: true },
    },
    preloadLangs: true,
});

/** Issue #222: bağımsız öğretmen sınıf bazlı atama yapamaz. */
describe('WorksheetAssignmentDialogComponent', () => {
    let dialogRef: MockedObject<MatDialogRef<WorksheetAssignmentDialogComponent>>;

    const ada: StudentLookup = { id: 11, userId: 21, studentNumber: '101', fullName: 'Ada', schoolName: 'Okul', gradeId: 1 };
    let studentsSig: WritableSignal<StudentLookup[]>;
    let statusSig: WritableSignal<StudentLookupStatus>;

    type CreateOptions = Partial<Omit<WorksheetAssignmentDialogData, 'students' | 'studentsStatus'>> & {
        students?: StudentLookup[];
        studentsStatus?: StudentLookupStatus;
        sections?: SchoolSection[];
    };
    let sectionList: ReturnType<typeof vi.fn>;

    function create(options: CreateOptions = {}) {
        const { students = [ada], studentsStatus = 'loaded', sections = [], ...overrides } = options;
        sectionList = vi.fn().mockReturnValue(of(sections));
        studentsSig = signal(students);
        statusSig = signal(studentsStatus);
        const data: WorksheetAssignmentDialogData = {
            worksheetId: 7,
            scope: 'grade',
            grades: [{ id: 1, name: '5-A' }],
            students: studentsSig.asReadonly(),
            studentsStatus: statusSig.asReadonly(),
            ...overrides,
        };

        dialogRef = {
            close: vi.fn().mockName("MatDialogRef.close")
        } as unknown as MockedObject<MatDialogRef<WorksheetAssignmentDialogComponent, any>>;
        TestBed.configureTestingModule({
            imports: [WorksheetAssignmentDialogComponent, translocoTesting, NoopAnimationsModule],
            providers: [
                provideNativeDateAdapter(),
                { provide: MAT_DIALOG_DATA, useValue: data },
                { provide: MatDialogRef, useValue: dialogRef },
                { provide: SchoolSectionService, useValue: { list: sectionList } },
            ],
        });

        const fixture = TestBed.createComponent(WorksheetAssignmentDialogComponent);
        fixture.detectChanges();
        return fixture;
    }

    it('okullu öğretmende sınıf ve öğrenci seçeneklerini gösterir, varsayılan scope grade', () => {
        const fixture = create();
        const el: HTMLElement = fixture.nativeElement;

        expect(el.querySelector('[data-testid="scope-grade"]')).not.toBeNull();
        expect(el.querySelector('[data-testid="scope-student"]')).not.toBeNull();
        expect(el.querySelector('[data-testid="independent-tutor-note"]')).toBeNull();
        expect(fixture.componentInstance['form'].controls.scope.value).toBe('grade');
    });

    // Issue #860: diyalog kimin alacağını söyler — test adı, öğrenci sayılı sınıf kartları, özet ve buton.
    describe('who gets it (#860)', () => {
        const grades = [{ id: 1, name: '5-A' }, { id: 2, name: '6-A' }];
        const students: StudentLookup[] = [
            ada,
            { ...ada, id: 12, fullName: 'Bora' },
            { ...ada, id: 13, fullName: 'Can', gradeId: 2 },
        ];
        const text = (el: HTMLElement, id: string) => el.querySelector(`[data-testid="${id}"]`)?.textContent?.trim();

        it('preselects the test grade and shows counts, summary and a counted button', () => {
            const fixture = create({ grades, students, defaultGradeId: 2, worksheetName: 'Kesirler' });
            const el: HTMLElement = fixture.nativeElement;

            expect(text(el, 'assign-worksheet-name')).toBe('Kesirler');
            expect(el.querySelector('[data-grade="1"]')?.textContent).toContain('2 öğrenci');
            expect(el.querySelector('[data-grade="2"]')?.getAttribute('aria-checked')).toBe('true');
            expect(text(el, 'assign-summary')).toBe('6-A · 1 öğrenci alacak');
            expect(text(el, 'assign-submit')).toBe('1 öğrenciye ata');

            (el.querySelector('[data-grade="1"]') as HTMLButtonElement).click();
            fixture.detectChanges();
            expect(text(el, 'assign-submit')).toBe('2 öğrenciye ata');
            fixture.componentInstance['submit']();
            expect(dialogRef.close).toHaveBeenCalledWith(expect.objectContaining({ request: expect.objectContaining({ gradeId: 1 }) }));
        });

        it('without loaded students shows no counts and a plain button', () => {
            const fixture = create({ grades, students: [], studentsStatus: 'loading' });
            const el: HTMLElement = fixture.nativeElement;

            expect(el.querySelector('[data-grade="1"]')?.textContent).not.toContain('öğrenci');
            expect(text(el, 'assign-summary')).toBe('');
            expect(text(el, 'assign-submit')).toBe('Ata');
        });

        it('student scope names the student', () => {
            const fixture = create({ grades, students, scope: 'student' });
            const el: HTMLElement = fixture.nativeElement;
            fixture.componentInstance['form'].controls.studentId.setValue(12);
            fixture.detectChanges();

            expect(text(el, 'assign-summary')).toBe('Bora alacak');
            expect(text(el, 'assign-submit')).toBe('Öğrenciye ata');
        });
    });

    it('bağımsız öğretmende "Sınıfa ata" seçeneğini render etmez ve scope student olur', () => {
        const fixture = create({ isIndependentTutor: true, scope: 'grade' });
        const el: HTMLElement = fixture.nativeElement;
        const form = fixture.componentInstance['form'];

        expect(el.querySelector('[data-testid="scope-grade"]')).toBeNull();
        expect(el.querySelector('mat-radio-group')).toBeNull();
        expect(el.querySelector('[data-testid="independent-tutor-note"]')).not.toBeNull();
        expect(form.controls.scope.value).toBe('student');
        expect(form.controls.gradeId.value).toBeNull();
    });

    it('bağımsız öğretmen yalnızca öğrenci bazlı istek üretir', () => {
        const fixture = create({ isIndependentTutor: true });
        const component = fixture.componentInstance;
        component['form'].controls.studentId.setValue(11);

        component['submit']();

        const result = vi.mocked(dialogRef.close).mock.lastCall![0] as {
            request: {
                gradeId?: number;
                studentId?: number;
            };
        };
        expect(result.request.studentId).toBe(11);
        expect(result.request.gradeId).toBeUndefined();
    });

    it('saat yerel saattir: 09:00 girilince istek yerel 09:00 olur; varsayılan başlangıç şimdi', () => {
        const before = new Date();
        const fixture = create({ isIndependentTutor: true });
        const form = fixture.componentInstance['form'];
        const [h, m] = form.controls.startTime.value.split(':').map(Number);
        const shown = new Date(before);
        shown.setHours(h, m, 0, 0);
        expect(Math.abs(shown.getTime() - before.getTime())).toBeLessThan(2 * 60_000);

        form.controls.studentId.setValue(11);
        form.controls.startDate.setValue(new Date(2026, 9, 30));
        form.controls.startTime.setValue('09:00');
        fixture.componentInstance['submit']();

        const request = (vi.mocked(dialogRef.close).mock.lastCall![0] as { request: { startAt: string } }).request;
        expect(new Date(request.startAt).getTime()).toBe(new Date(2026, 9, 30, 9, 0).getTime());
    });

    // Issue #1283: ödev olarak ver bitiş zamanını açar; bitiş kapatılınca ödev de kalkar.
    it('homework turns on the end time and is sent with the request', () => {
        const fixture = create({ isIndependentTutor: true });
        const form = fixture.componentInstance['form'];
        form.controls.studentId.setValue(11);
        form.controls.isHomework.setValue(true);
        expect(form.controls.hasEndDate.value).toBe(true);
        form.controls.endDate.setValue(new Date(2030, 0, 10));
        fixture.componentInstance['submit']();
        const request = (vi.mocked(dialogRef.close).mock.lastCall![0] as { request: { isHomework?: boolean; endAt?: string } }).request;
        expect(request.isHomework).toBe(true);
        expect(request.endAt).toBeTruthy();

        form.controls.hasEndDate.setValue(false);
        expect(form.controls.isHomework.value).toBe(false);
        fixture.componentInstance['submit']();
        const plain = (vi.mocked(dialogRef.close).mock.lastCall![0] as { request: { isHomework?: boolean } }).request;
        expect(plain.isHomework).toBeUndefined();
    });

    describe('boş öğrenci listesi (issue #223)', () => {
        const q = (fixture: {
            nativeElement: HTMLElement;
        }, id: string) => fixture.nativeElement.querySelector(`[data-testid="${id}"]`) as HTMLElement | null;

        it('bağımsız öğretmende onaylı ders metnini gösterir, genel metni göstermez', () => {
            const fixture = create({ isIndependentTutor: true, students: [] });

            const independent = q(fixture, 'no-students-independent');
            expect(independent).not.toBeNull();
            expect(independent!.textContent).toContain('Henüz onaylı dersi olan öğrencin yok; randevu onaylanınca burada görünür.');
            expect(q(fixture, 'no-students')).toBeNull();
            expect(fixture.nativeElement.textContent).not.toContain('önce sınıf seçerek');
        });

        it('okullu öğretmende genel metni gösterir, bağımsız metnini göstermez', () => {
            const fixture = create({ scope: 'student', students: [] });

            expect(q(fixture, 'no-students')).not.toBeNull();
            expect(q(fixture, 'no-students-independent')).toBeNull();
        });

        it('bağımsız öğretmende liste doluysa boş durum göstermez', () => {
            const fixture = create({ isIndependentTutor: true });

            expect(q(fixture, 'no-students-independent')).toBeNull();
            expect(q(fixture, 'no-students')).toBeNull();
        });

        it('liste yüklenirken boş durum yerine yükleniyor metnini gösterir', () => {
            const fixture = create({ isIndependentTutor: true, students: [], studentsStatus: 'loading' });

            expect(q(fixture, 'students-loading')).not.toBeNull();
            expect(q(fixture, 'no-students-independent')).toBeNull();
            expect(q(fixture, 'no-students')).toBeNull();
        });

        it('lookup hata verdiyse boş durum yerine hata metnini gösterir', () => {
            const fixture = create({ isIndependentTutor: true, students: [], studentsStatus: 'error' });

            expect(q(fixture, 'students-error')).not.toBeNull();
            expect(q(fixture, 'no-students-independent')).toBeNull();
        });

        it('okullu öğretmende lookup hata verdiyse genel boş metni göstermez', () => {
            const fixture = create({ scope: 'student', students: [], studentsStatus: 'error' });

            expect(q(fixture, 'students-error')).not.toBeNull();
            expect(q(fixture, 'no-students')).toBeNull();
        });

        it('yüklenmeden önce öğrenci seçici disabled, yüklenince enabled olur', () => {
            const fixture = create({ isIndependentTutor: true, students: [], studentsStatus: 'loading' });
            const control = fixture.componentInstance['form'].controls.studentId;
            expect(control.disabled).toBe(true);

            statusSig.set('error');
            fixture.detectChanges();
            expect(control.disabled).toBe(true);

            statusSig.set('loaded');
            fixture.detectChanges();
            expect(control.enabled).toBe(true);
        });

        it('yükleniyor durumunda açılan dialog, parent sinyali yüklenince canlı güncellenir', () => {
            const fixture = create({ isIndependentTutor: true, students: [], studentsStatus: 'loading' });
            expect(q(fixture, 'students-loading')).not.toBeNull();

            studentsSig.set([ada]);
            statusSig.set('loaded');
            fixture.detectChanges();

            expect(q(fixture, 'students-loading')).toBeNull();
            expect(q(fixture, 'no-students-independent')).toBeNull();
            expect(fixture.componentInstance['filteredStudents']()).toEqual([ada]);
            expect(fixture.componentInstance['form'].controls.studentId.enabled).toBe(true);
        });
    });

    /** Issue #105: atamaya özel yorum-soru override'ı (Varsayılan/Açık/Kapalı). */
    describe('commentsEnabledOverride', () => {
        const q = (fixture: {
            nativeElement: HTMLElement;
        }, id: string) => fixture.nativeElement.querySelector(`[data-testid="${id}"]`) as HTMLElement | null;

        function submitGrade(fixture: ReturnType<typeof create>) {
            fixture.componentInstance['submit']();
            return (vi.mocked(dialogRef.close).mock.lastCall![0] as {
                request: {
                    commentsEnabledOverride?: boolean | null;
                };
            })
                .request;
        }

        it('commentsOverrideValue_MapsChoices', () => {
            expect(commentsOverrideValue('default')).toBeNull();
            expect(commentsOverrideValue('on')).toBe(true);
            expect(commentsOverrideValue('off')).toBe(false);
        });

        it('varsayılan seçimde istek null gönderir ve üç seçenek görünür', () => {
            const fixture = create();

            expect(q(fixture, 'comments-override-default')).not.toBeNull();
            expect(q(fixture, 'comments-override-on')).not.toBeNull();
            expect(q(fixture, 'comments-override-off')).not.toBeNull();
            expect(q(fixture, 'comments-override')!.getAttribute('aria-label')).toBe(worksheetDetailTr.assignDialog.commentsGroupAria);
            expect(submitGrade(fixture).commentsEnabledOverride).toBeNull();
        });

        it('Kapalı seçilince istek false, Açık seçilince true gönderir', () => {
            const fixture = create();

            (q(fixture, 'comments-override-off')!.querySelector('button') as HTMLButtonElement).click();
            fixture.detectChanges();
            expect(fixture.componentInstance['form'].controls.commentsOverride.value).toBe('off');
            expect(submitGrade(fixture).commentsEnabledOverride).toBe(false);

            (q(fixture, 'comments-override-on')!.querySelector('button') as HTMLButtonElement).click();
            fixture.detectChanges();
            expect(submitGrade(fixture).commentsEnabledOverride).toBe(true);
        });

        it('yardımcı metin worksheet in güncel değerini gösterir', () => {
            const on = create({ worksheetCommentsEnabled: true });
            expect(q(on, 'comments-override-helper')!.textContent).toContain(worksheetDetailTr.assignDialog.commentsStateOn);

            TestBed.resetTestingModule();
            const off = create({ worksheetCommentsEnabled: false });
            expect(q(off, 'comments-override-helper')!.textContent).toContain(worksheetDetailTr.assignDialog.commentsStateOff);
        });
    });

    // Issue #1296: şubeye ata — okulun şubeleri kart olarak, istek yalnız sectionId taşır.
    describe('section target (#1296)', () => {
        const sections: SchoolSection[] = [
            { id: 4, gradeId: 1, gradeName: '5. Sınıf', name: 'A', studentCount: 3 },
            { id: 5, gradeId: 1, gradeName: '5. Sınıf', name: 'B', studentCount: 0 },
        ];
        const pick = (el: HTMLElement, id: string) => el.querySelector(`[data-testid="${id}"] input`) as HTMLInputElement;

        it('lists sections with counts and sends only the section id', () => {
            const fixture = create({ sections });
            const el: HTMLElement = fixture.nativeElement;
            pick(el, 'scope-section').click();
            fixture.detectChanges();

            expect(el.querySelector('[data-section="4"]')?.textContent).toContain('5. Sınıf / A');
            expect(el.querySelector('[data-section="4"]')?.textContent).toContain('3 öğrenci');
            (el.querySelector('[data-section="4"]') as HTMLButtonElement).click();
            fixture.detectChanges();
            expect(el.querySelector('[data-testid="assign-summary"]')?.textContent?.trim()).toBe('5. Sınıf / A · 3 öğrenci alacak');

            (el.querySelector('[data-testid="assign-submit"]') as HTMLButtonElement).click();
            const request = dialogRef.close.mock.calls[0][0].request;
            expect(request.sectionId).toBe(4);
            expect(request.gradeId).toBeUndefined();
            expect(request.studentId).toBeUndefined();
        });

        it('requires a section and says when the school has none', () => {
            const fixture = create({ sections: [] });
            const el: HTMLElement = fixture.nativeElement;
            pick(el, 'scope-section').click();
            fixture.detectChanges();

            expect(el.querySelector('[data-testid="no-sections"]')).not.toBeNull();
            (el.querySelector('[data-testid="assign-submit"]') as HTMLButtonElement).click();
            expect(dialogRef.close).not.toHaveBeenCalled();
        });

        it('does not read sections for an independent tutor', () => {
            create({ isIndependentTutor: true, sections });
            expect(sectionList).not.toHaveBeenCalled();
        });
    });
});
