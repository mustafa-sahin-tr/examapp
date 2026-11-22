import type { MockedObject } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { By } from '@angular/platform-browser';
import { HttpErrorResponse } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';

import { MyHomeworkComponent } from './my-homework.component';
import { provideRouter } from '@angular/router';
import { HomeworkService, StudentAssignmentHomework, StudentHomework } from '../../services/homework.service';
import { LocaleService } from '../../services/locale.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import scopeTr from '../../../../public/i18n/homework/tr.json';
import scopeEn from '../../../../public/i18n/homework/en.json';

const open: StudentHomework = {
  id: 1,
  subjectName: 'Türkçe',
  bookName: 'Okuma Kitabı',
  pages: '8-10',
  note: null,
  dueDate: '2026-11-24',
  createdAt: '2026-11-22T10:00:00Z',
  teacherName: 'Zeynep Ö.',
  studentDoneAt: null,
  teacherStatus: null,
  teacherNote: null,
  isOverdue: false,
};
const overdue: StudentHomework = { ...open, id: 2, subjectName: 'Matematik', dueDate: '2026-11-20', isOverdue: true };
const checked: StudentHomework = {
  ...open,
  id: 3,
  subjectName: 'Fen Bilimleri',
  studentDoneAt: '2026-11-21T10:00:00Z',
  teacherStatus: 'Partial',
  teacherNote: 's. 10 eksik',
};

const pendingTest: StudentAssignmentHomework = {
  assignmentId: 41,
  worksheetId: 7,
  worksheetName: 'Kesirler Testi',
  worksheetKind: 'Test',
  subjectName: 'Matematik',
  teacherName: 'Zeynep Ö.',
  dueAt: '2026-11-24T20:59:00Z',
  status: 'Pending',
  inProgress: true,
  completedAt: null,
};
const doneActivity: StudentAssignmentHomework = {
  ...pendingTest,
  assignmentId: 42,
  worksheetId: 8,
  worksheetName: 'Bulmaca',
  worksheetKind: 'Activity',
  status: 'Completed',
  inProgress: false,
  completedAt: '2026-11-22T10:00:00Z',
};
const overdueTest: StudentAssignmentHomework = { ...pendingTest, assignmentId: 43, worksheetId: 9, status: 'Overdue', inProgress: false };

describe('MyHomeworkComponent (issue #1282)', () => {
  let service: MockedObject<HomeworkService>;
  let snackBar: { open: ReturnType<typeof vi.fn> };

  function create(items: StudentHomework[] = [overdue, open, checked], assignments: StudentAssignmentHomework[] = []) {
    service = {
      mine: vi.fn().mockReturnValue(of(items)),
      myAssignments: vi.fn().mockReturnValue(of(assignments)),
      markDone: vi.fn().mockImplementation((id: number, done: boolean) =>
        of({ ...items.find((h) => h.id === id)!, studentDoneAt: done ? '2026-11-22T12:00:00Z' : null, isOverdue: false }),
      ),
    } as unknown as MockedObject<HomeworkService>;
    snackBar = { open: vi.fn() };
    TestBed.configureTestingModule({
      imports: [MyHomeworkComponent, translocoTestingModule({ langs: { 'homework/tr': scopeTr, 'homework/en': scopeEn } })],
      providers: [
        { provide: HomeworkService, useValue: service },
        { provide: LocaleService, useValue: { localeDefinition: () => ({ angularLocale: 'tr' }) } },
        { provide: MatSnackBar, useValue: snackBar },
        provideNoopAnimations(),
        provideRouter([]),
      ],
    });
    const fixture = TestBed.createComponent(MyHomeworkComponent);
    fixture.detectChanges();
    return fixture;
  }

  const testIds = (fixture: ReturnType<typeof create>, id: string) =>
    fixture.debugElement.queryAll(By.css(`[data-testid="${id}"]`));

  it('splits open and checked homework and flags overdue', () => {
    const fixture = create();

    const openItems = testIds(fixture, 'open-item');
    expect(openItems.length).toBe(2);
    expect(openItems[0].nativeElement.textContent).toContain('Matematik');
    expect(testIds(fixture, 'overdue').length).toBe(1);
    expect(openItems[1].nativeElement.textContent).toContain('Okuma Kitabı · s. 8-10');

    const checkedItem = testIds(fixture, 'checked-item')[0].nativeElement.textContent as string;
    expect(checkedItem).toContain('Kısmen');
    expect(checkedItem).toContain('s. 10 eksik');
  });

  it('marks homework done and can undo it', () => {
    const fixture = create();
    (testIds(fixture, 'toggle-done')[1].nativeElement as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(service.markDone).toHaveBeenCalledWith(1, true);
    expect(testIds(fixture, 'marked').length).toBe(1);
    expect(testIds(fixture, 'toggle-done')[1].nativeElement.textContent).toContain('Geri al');

    (testIds(fixture, 'toggle-done')[1].nativeElement as HTMLButtonElement).click();
    expect(service.markDone).toHaveBeenLastCalledWith(1, false);
  });

  it('reloads when the teacher already checked the homework', () => {
    const fixture = create();
    service.markDone.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { message: 'Öğretmenin kontrol ettiği ödev.' } })),
    );
    (testIds(fixture, 'toggle-done')[0].nativeElement as HTMLButtonElement).click();

    expect(snackBar.open.mock.calls[0][0]).toContain('kontrol ettiği');
    expect(service.mine).toHaveBeenCalledTimes(2);
  });

  it('shows an empty state', () => {
    const fixture = create([]);
    expect(testIds(fixture, 'my-homework-empty').length).toBe(1);
  });

  // Issue #1283: test / etkinlik ödevleri — durum oturumdan, yalnız bekleyende "çöz" bağlantısı.
  it('lists test and activity homework with automatic status', () => {
    const fixture = create([], [pendingTest, doneActivity, overdueTest]);

    expect(testIds(fixture, 'my-homework-empty').length).toBe(0);
    expect(testIds(fixture, 'open-item').length).toBe(0);
    const statuses = testIds(fixture, 'assignment-status').map((e) => (e.nativeElement.textContent as string).trim());
    expect(statuses).toEqual(['Devam ediyor', 'Tamamlandı', 'Gecikti']);
    const links = testIds(fixture, 'assignment-open');
    expect(links.length).toBe(1);
    expect(links[0].nativeElement.getAttribute('href')).toBe('/test/7');
    expect(links[0].nativeElement.textContent).toContain('Devam et');
  });

  it('keeps plain homework when the test homework list fails', () => {
    const fixture = create();
    service.myAssignments.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    (fixture.componentInstance as unknown as { load(): void }).load();
    fixture.detectChanges();

    expect(testIds(fixture, 'open-item').length).toBe(2);
    expect(testIds(fixture, 'assignment-homework').length).toBe(0);
  });
});
