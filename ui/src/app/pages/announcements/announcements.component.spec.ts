import type { MockedObject } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { By } from '@angular/platform-browser';
import { HttpErrorResponse } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';

import { AnnouncementsComponent } from './announcements.component';
import { AnnouncementService, SentAnnouncement } from '../../services/announcement.service';
import { SchoolSection, SchoolSectionService } from '../../services/school-section.service';
import { GradesService } from '../../services/grades.service';
import { LocaleService } from '../../services/locale.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import scopeTr from '../../../../public/i18n/announcements/tr.json';
import scopeEn from '../../../../public/i18n/announcements/en.json';

const sections: SchoolSection[] = [{ id: 4, gradeId: 7, gradeName: '7. Sınıf', name: 'A', studentCount: 2 }];

const sent: SentAnnouncement = {
  id: 9,
  target: 'Section',
  targetLabel: '7. Sınıf / A',
  title: 'Gezi',
  body: 'Cuma gezisi.',
  studentCount: 2,
  teacherCount: 0,
  parentCount: 1,
  createdAt: '2026-11-22T10:00:00Z',
};

describe('AnnouncementsComponent (issue #1281)', () => {
  let service: MockedObject<AnnouncementService>;
  let sectionService: MockedObject<SchoolSectionService>;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let snackBar: { open: ReturnType<typeof vi.fn> };

  function create(admin: boolean, optionsError = false) {
    service = {
      options: vi.fn().mockReturnValue(
        optionsError
          ? throwError(() => new HttpErrorResponse({ status: 403, error: { message: 'Okul öğretmeni değilsiniz.' } }))
          : of({ schoolName: 'A Okulu', canTargetSchoolAndGrade: admin }),
      ),
      send: vi.fn().mockReturnValue(of(sent)),
      sent: vi.fn().mockReturnValue(of({ pageNumber: 1, pageSize: 10, totalCount: 1, items: [sent] })),
    } as unknown as MockedObject<AnnouncementService>;
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
      imports: [
        AnnouncementsComponent,
        translocoTestingModule({ langs: { 'announcements/tr': scopeTr, 'announcements/en': scopeEn } }),
      ],
      providers: [
        { provide: AnnouncementService, useValue: service },
        { provide: SchoolSectionService, useValue: sectionService },
        { provide: GradesService, useValue: { getGrades: () => of([{ id: 7, name: '7. Sınıf' }]) } },
        { provide: LocaleService, useValue: { localeDefinition: () => ({ angularLocale: 'tr' }) } },
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
        provideNoopAnimations(),
      ],
    });
    const fixture = TestBed.createComponent(AnnouncementsComponent);
    fixture.detectChanges();
    return fixture;
  }

  const testIds = (fixture: ReturnType<typeof create>, id: string) =>
    fixture.debugElement.queryAll(By.css(`[data-testid="${id}"]`));

  type Internals = {
    title: { set(v: string): void };
    body: { set(v: string): void };
    setTarget(t: string): void;
    setSection(id: number | null): void;
    toggleStudent(id: number, on: boolean): void;
    gradeId: { set(v: number | null): void };
    canSend(): boolean;
    confirmSend(): void;
  };
  const internals = (fixture: ReturnType<typeof create>) => fixture.componentInstance as unknown as Internals;

  it('offers only section and student targets to a teacher', () => {
    const fixture = create(false);

    expect(testIds(fixture, 'target-School').length).toBe(0);
    expect(testIds(fixture, 'target-Grade').length).toBe(0);
    expect(testIds(fixture, 'target-Section').length).toBe(1);
    expect(testIds(fixture, 'target-Students').length).toBe(1);
  });

  it('defaults a school admin to the whole school and sends after confirmation', () => {
    const fixture = create(true);
    const c = internals(fixture);
    expect(testIds(fixture, 'target-School').length).toBe(1);
    expect(testIds(fixture, 'school-hint')[0].nativeElement.textContent).toContain('A Okulu');

    c.title.set('  Veli toplantısı ');
    c.body.set('Cuma 15.00');
    expect(c.canSend()).toBe(true);
    c.confirmSend();

    expect(dialog.open).toHaveBeenCalled();
    expect(service.send).toHaveBeenCalledWith({ target: 'School', title: 'Veli toplantısı', body: 'Cuma 15.00' });
    expect(snackBar.open.mock.calls[0][0]).toContain('2 öğrenci · 1 veli');
    expect(service.sent).toHaveBeenCalledTimes(2);
  });

  it('needs a grade or section before sending', () => {
    const fixture = create(true);
    const c = internals(fixture);
    c.title.set('t');
    c.body.set('b');
    c.setTarget('Grade');
    expect(c.canSend()).toBe(false);
    c.gradeId.set(7);
    expect(c.canSend()).toBe(true);
    c.setTarget('Section');
    expect(c.canSend()).toBe(false);
  });

  it('sends to chosen students of a section', () => {
    const fixture = create(false);
    const c = internals(fixture);
    c.setTarget('Students');
    c.setSection(4);
    fixture.detectChanges();
    expect(sectionService.students).toHaveBeenCalledWith(4, 1, 100);
    expect(testIds(fixture, 'student-option').length).toBe(2);

    c.title.set('t');
    c.body.set('b');
    expect(c.canSend()).toBe(false);
    c.toggleStudent(12, true);
    c.confirmSend();

    expect(service.send).toHaveBeenCalledWith({ target: 'Students', studentIds: [12], title: 't', body: 'b' });
  });

  it('does not send when the confirmation is cancelled', () => {
    const fixture = create(false);
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });
    const c = internals(fixture);
    c.setSection(4);
    c.title.set('t');
    c.body.set('b');
    c.confirmSend();

    expect(service.send).not.toHaveBeenCalled();
  });

  it('shows the backend error when sending fails', () => {
    const fixture = create(false);
    service.send.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 429, error: { message: 'Son 24 saatte en çok 20 duyuru.' } })),
    );
    const c = internals(fixture);
    c.setSection(4);
    c.title.set('t');
    c.body.set('b');
    c.confirmSend();
    fixture.detectChanges();

    expect(testIds(fixture, 'send-error')[0].nativeElement.textContent).toContain('en çok 20 duyuru');
  });

  it('lists sent announcements with target and recipient counts', () => {
    const fixture = create(false);
    const item = testIds(fixture, 'sent-item')[0].nativeElement.textContent as string;

    expect(item).toContain('Gezi');
    expect(item).toContain('7. Sınıf / A');
    expect(item).toContain('2 öğrenci · 1 veli');
  });

  it('shows the reason when the user is not a school teacher', () => {
    const fixture = create(false, true);

    expect(testIds(fixture, 'announcements-error')[0].nativeElement.textContent).toContain('Okul öğretmeni değilsiniz.');
    expect(testIds(fixture, 'announcement-form').length).toBe(0);
  });
});
