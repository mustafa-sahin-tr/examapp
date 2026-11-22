import type { MockedObject } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { By } from '@angular/platform-browser';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';

import { HomeworkComponent, localIsoDate } from './homework.component';
import { HomeworkDetail, HomeworkService, HomeworkStudentStatus, TeacherHomework } from '../../services/homework.service';
import { SchoolSection, SchoolSectionService } from '../../services/school-section.service';
import { GradesService } from '../../services/grades.service';
import { SubjectService } from '../../services/subject.service';
import { LocaleService } from '../../services/locale.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import scopeTr from '../../../../public/i18n/homework/tr.json';
import scopeEn from '../../../../public/i18n/homework/en.json';

const sections: SchoolSection[] = [{ id: 4, gradeId: 7, gradeName: '7. Sınıf', name: 'A', studentCount: 2 }];

const homework: TeacherHomework = {
  id: 9,
  target: 'Section',
  targetLabel: '7. Sınıf / A',
  subjectId: 3,
  subjectName: 'Türkçe',
  bookName: 'Okuma Kitabı',
  pages: '8-10',
  note: null,
  dueDate: '2026-11-24',
  createdAt: '2026-11-22T10:00:00Z',
  isPastDue: false,
  studentCount: 2,
  studentDoneCount: 1,
  doneCount: 0,
  partialCount: 0,
  notDoneCount: 0,
  overdueCount: 0,
};

const ayse: HomeworkStudentStatus = {
  studentId: 11,
  fullName: 'Ayşe Öğrenci',
  studentNumber: '****1',
  studentDoneAt: '2026-11-23T10:00:00Z',
  teacherStatus: null,
  teacherNote: null,
  checkedAt: null,
  isOverdue: false,
};
const ali: HomeworkStudentStatus = { ...ayse, studentId: 12, fullName: 'Ali Öğrenci', studentNumber: '****2', studentDoneAt: null };
const detail: HomeworkDetail = { homework, students: [ayse, ali] };

describe('HomeworkComponent (issue #1282)', () => {
  let service: MockedObject<HomeworkService>;
  let sectionService: MockedObject<SchoolSectionService>;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let snackBar: { open: ReturnType<typeof vi.fn> };

  function create() {
    service = {
      create: vi.fn().mockReturnValue(of(homework)),
      list: vi.fn().mockReturnValue(of({ pageNumber: 1, pageSize: 10, totalCount: 1, items: [homework] })),
      detail: vi.fn().mockReturnValue(of(detail)),
      check: vi.fn().mockImplementation((_id: number, studentId: number, status, note) =>
        of({ ...(studentId === 11 ? ayse : ali), teacherStatus: status, teacherNote: note }),
      ),
      remove: vi.fn().mockReturnValue(of(undefined)),
    } as unknown as MockedObject<HomeworkService>;
    sectionService = {
      list: vi.fn().mockReturnValue(of(sections)),
      students: vi.fn().mockReturnValue(
        of({
          pageNumber: 1,
          pageSize: 100,
          totalCount: 2,
          items: [
            { studentId: 11, fullName: 'Ayşe Öğrenci', studentNumber: '****1', since: '2026-11-01T00:00:00Z' },
            { studentId: 12, fullName: 'Ali Öğrenci', studentNumber: '****2', since: '2026-11-01T00:00:00Z' },
          ],
        }),
      ),
    } as unknown as MockedObject<SchoolSectionService>;
    dialog = { open: vi.fn().mockReturnValue({ afterClosed: () => of(true) }) };
    snackBar = { open: vi.fn() };

    TestBed.configureTestingModule({
      imports: [HomeworkComponent, translocoTestingModule({ langs: { 'homework/tr': scopeTr, 'homework/en': scopeEn } })],
      providers: [
        { provide: HomeworkService, useValue: service },
        { provide: SchoolSectionService, useValue: sectionService },
        { provide: GradesService, useValue: { getGrades: () => of([{ id: 7, name: '7. Sınıf' }]) } },
        { provide: SubjectService, useValue: { loadCoreSubjects: () => of([{ id: 3, name: 'Türkçe' }]) } },
        { provide: LocaleService, useValue: { localeDefinition: () => ({ angularLocale: 'tr' }) } },
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
        provideNoopAnimations(),
      ],
    });
    const fixture = TestBed.createComponent(HomeworkComponent);
    fixture.detectChanges();
    return fixture;
  }

  const testIds = (fixture: ReturnType<typeof create>, id: string) =>
    fixture.debugElement.queryAll(By.css(`[data-testid="${id}"]`));

  type Internals = {
    subjectId: { set(v: number | null): void };
    pages: { set(v: string): void };
    note: { set(v: string): void };
    bookName: { set(v: string): void };
    dueDate: { set(v: string): void; (): string };
    gradeId: { set(v: number | null): void };
    onTargetChange(t: string): void;
    setSection(id: number | null): void;
    toggleStudent(id: number, on: boolean): void;
    canSave(): boolean;
    save(): void;
    toggleDetail(id: number): void;
    setStatus(s: HomeworkStudentStatus, status: string | null): void;
    setNoteDraft(id: number, v: string): void;
    confirmAllMarked(): void;
    confirmRemove(h: TeacherHomework): void;
  };
  const internals = (fixture: ReturnType<typeof create>) => fixture.componentInstance as unknown as Internals;

  it('gives section homework with subject, book, pages and due date', () => {
    const fixture = create();
    const c = internals(fixture);
    c.setSection(4);
    c.subjectId.set(3);
    expect(c.canSave()).toBe(false); // sayfa ya da not yok
    c.bookName.set(' Okuma Kitabı ');
    c.pages.set(' 8-10 ');
    const due = c.dueDate();
    expect(c.canSave()).toBe(true);
    c.save();

    expect(service.create).toHaveBeenCalledWith({
      target: 'Section',
      sectionId: 4,
      subjectId: 3,
      bookName: 'Okuma Kitabı',
      pages: '8-10',
      note: null,
      dueDate: due,
    });
    expect(snackBar.open.mock.calls[0][0]).toContain('2 öğrenci');
    expect(service.list).toHaveBeenCalledTimes(2);
  });

  it('needs a grade, a section or chosen students and a valid due date', () => {
    const fixture = create();
    const c = internals(fixture);
    c.subjectId.set(3);
    c.note.set('Kompozisyon');
    c.onTargetChange('Grade');
    expect(c.canSave()).toBe(false);
    c.gradeId.set(7);
    expect(c.canSave()).toBe(true);
    c.dueDate.set('2000-01-01');
    expect(c.canSave()).toBe(false);
    c.dueDate.set(localIsoDate(new Date()));
    expect(c.canSave()).toBe(true);

    c.setSection(4);
    c.onTargetChange('Students');
    fixture.detectChanges();
    expect(sectionService.students).toHaveBeenCalledWith(4, 1, 100);
    expect(c.canSave()).toBe(false);
    c.toggleStudent(12, true);
    c.save();
    expect(service.create.mock.calls[0][0]).toMatchObject({ target: 'Students', studentIds: [12], note: 'Kompozisyon', pages: null });
  });

  it('shows the backend error when saving fails', () => {
    const fixture = create();
    service.create.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 429, error: { message: 'Son 24 saatte en çok 50 ödev.' } })),
    );
    const c = internals(fixture);
    c.setSection(4);
    c.subjectId.set(3);
    c.pages.set('1');
    c.save();
    fixture.detectChanges();

    expect(testIds(fixture, 'save-error')[0].nativeElement.textContent).toContain('en çok 50 ödev');
  });

  it('lists given homework with work text and status counts', () => {
    const fixture = create();
    const item = testIds(fixture, 'homework-item')[0].nativeElement.textContent as string;

    expect(item).toContain('Türkçe');
    expect(item).toContain('7. Sınıf / A');
    expect(item).toContain('Okuma Kitabı · s. 8-10');
    expect(testIds(fixture, 'homework-counts')[0].nativeElement.textContent).toContain('2 öğrenci · 1 yaptım dedi');
  });

  it('opens the student list and saves a check with the drafted note', () => {
    const fixture = create();
    const c = internals(fixture);
    c.toggleDetail(9);
    fixture.detectChanges();
    expect(service.detail).toHaveBeenCalledWith(9);
    expect(testIds(fixture, 'student-row').length).toBe(2);
    expect(testIds(fixture, 'student-marked').length).toBe(1);

    c.setNoteDraft(12, ' s. 10 eksik ');
    c.setStatus(ali, 'Partial');
    fixture.detectChanges();

    expect(service.check).toHaveBeenCalledWith(9, 12, 'Partial', 's. 10 eksik');
    expect(testIds(fixture, 'homework-counts')[0].nativeElement.textContent).toContain('1 kısmen');
  });

  it('confirms every student who marked it done in one click', () => {
    const fixture = create();
    const c = internals(fixture);
    c.toggleDetail(9);
    fixture.detectChanges();
    expect(testIds(fixture, 'confirm-marked')[0].nativeElement.textContent).toContain('1 öğrenciyi');

    c.confirmAllMarked();
    fixture.detectChanges();

    expect(service.check).toHaveBeenCalledTimes(1);
    expect(service.check).toHaveBeenCalledWith(9, 11, 'Done', null);
    expect(testIds(fixture, 'confirm-marked').length).toBe(0);
  });

  it('removes homework after confirmation', () => {
    const fixture = create();
    internals(fixture).confirmRemove(homework);

    expect(service.remove).toHaveBeenCalledWith(9);
    expect(service.list).toHaveBeenCalledTimes(2);
  });
});
