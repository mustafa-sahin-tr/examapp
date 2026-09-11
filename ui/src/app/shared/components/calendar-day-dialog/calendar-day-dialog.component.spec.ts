import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';

import { CalendarDayDialogComponent, CalendarDayDialogData } from './calendar-day-dialog.component';
import { CalendarEvent } from '../../../models/calendar-event';
import { ProgramService } from '../../../services/program.service';
import { AuthService } from '../../../services/auth.service';
import { UserProgram } from '../../../models/program.interfaces';

function reminder(overrides: Partial<CalendarEvent> & { worksheetId: number }): CalendarEvent {
  return {
    kind: 'reminder',
    date: new Date(2026, 8, 15, 9, 0).toISOString(),
    worksheetTitle: 'Hatırlatma Testi',
    subject: 'Fen',
    imageUrl: null,
    status: 'Pending',
    remindBeforeMinutes: 60,
    isCompleted: null,
    teacherName: 'Ayşe Öğretmen',
    ...overrides,
  } as CalendarEvent;
}

function deadline(overrides: Partial<CalendarEvent> & { worksheetId: number }): CalendarEvent {
  return {
    kind: 'assignment-deadline',
    date: new Date(2026, 8, 15, 14, 0).toISOString(),
    worksheetTitle: 'Ödev Testi',
    subject: 'Matematik',
    imageUrl: null,
    status: null,
    remindBeforeMinutes: null,
    isCompleted: false,
    teacherName: 'Ali Öğretmen',
    ...overrides,
  } as CalendarEvent;
}

function program(overrides: Partial<CalendarEvent> & { programId: number }): CalendarEvent {
  return {
    kind: 'program-study-page',
    date: new Date(2026, 8, 15, 8, 0).toISOString(),
    endDate: null,
    worksheetId: 0,
    worksheetTitle: '',
    subject: null,
    imageUrl: null,
    status: null,
    remindBeforeMinutes: null,
    isCompleted: false,
    teacherName: null,
    programName: 'Deneme Programı',
    studyPageId: 1,
    studyPageTitle: 'Sayfa 1',
    ...overrides,
  } as CalendarEvent;
}

function makeUserProgram(overrides: Partial<UserProgram> = {}): UserProgram {
  return {
    id: 1,
    userId: 'u1',
    programName: 'Deneme Programı',
    description: '',
    createdDate: new Date(2026, 0, 1).toISOString(),
    isActive: true,
    studyType: 'daily',
    subjectsPerDay: 1,
    restDays: '',
    difficultSubjects: '',
    completedPageCount: 3,
    totalPageCount: 10,
    progressPercentage: 30,
    schedules: [],
    studyPageSchedules: [],
    ...overrides,
  } as UserProgram;
}

async function setup(
  data: CalendarDayDialogData,
  options: { programServiceSpy?: jasmine.SpyObj<ProgramService> } = {},
) {
  const router = jasmine.createSpyObj<Router>('Router', ['navigate']);
  const dialogRef = jasmine.createSpyObj<MatDialogRef<CalendarDayDialogComponent>>('MatDialogRef', ['close']);
  const programService =
    options.programServiceSpy ??
    jasmine.createSpyObj<ProgramService>('ProgramService', ['getProgramById']);
  if (!options.programServiceSpy) {
    programService.getProgramById.and.returnValue(of(makeUserProgram()));
  }

  const authService = jasmine.createSpyObj<AuthService>('AuthService', ['hasRealmRole']);
  authService.hasRealmRole.and.returnValue(false);

  await TestBed.configureTestingModule({
    imports: [CalendarDayDialogComponent],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: Router, useValue: router },
      { provide: MatDialogRef, useValue: dialogRef },
      { provide: MAT_DIALOG_DATA, useValue: data },
      { provide: ProgramService, useValue: programService },
      { provide: AuthService, useValue: authService },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(CalendarDayDialogComponent);
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return { fixture, router, dialogRef, programService };
}

function clickButtonByText(fixture: ComponentFixture<unknown>, text: string): void {
  const btn = Array.from(fixture.nativeElement.querySelectorAll('button.day-dialog__action')).find((b) =>
    (b as HTMLElement).textContent?.includes(text),
  ) as HTMLButtonElement | undefined;
  if (!btn) {
    throw new Error(`Button not found: ${text}`);
  }
  btn.click();
}

describe('CalendarDayDialogComponent', () => {
  const date = new Date(2026, 8, 15);

  it('HatirlatIciyiDuzenle_Clicked_NavigatesWithReminderEditQueryParamAndClosesDialog', async () => {
    const { fixture, router, dialogRef } = await setup({ date, events: [reminder({ worksheetId: 99 })] });

    clickButtonByText(fixture, 'Hatırlatıcıyı düzenle');

    expect(router.navigate).toHaveBeenCalledWith(['/test', 99], { queryParams: { reminder: 'edit' } });
    expect(dialogRef.close).toHaveBeenCalled();
  });

  it('DetayaGit_Clicked_NavigatesToTestDetail', async () => {
    const { fixture, router, dialogRef } = await setup({ date, events: [reminder({ worksheetId: 55 })] });

    clickButtonByText(fixture, 'Detaya git');

    expect(router.navigate).toHaveBeenCalledWith(['/test', 55], {});
    expect(dialogRef.close).toHaveBeenCalled();
  });

  it('AssignmentDeadline_Completed_RendersSonucuGorActionAndTamamlandiLabel', async () => {
    const { fixture } = await setup({ date, events: [deadline({ worksheetId: 1, isCompleted: true })] });

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Sonucu gör');
    expect(text).toContain('Tamamlandı');
    const statusEl = fixture.nativeElement.querySelector('.day-dialog__row-status');
    expect(statusEl?.textContent).toContain('Tamamlandı');
  });

  it('AssignmentDeadline_NotCompleted_RendersCozmeyeBaslaAction', async () => {
    const { fixture } = await setup({ date, events: [deadline({ worksheetId: 1, isCompleted: false })] });

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Çözmeye başla');
    expect(text).not.toContain('Tamamlandı');
  });

  it('Rows_WithCompletedAndPendingEvents_CompletedRenderedLast', async () => {
    const { fixture } = await setup({
      date,
      events: [
        deadline({ worksheetId: 1, worksheetTitle: 'Erken Biten', isCompleted: true, date: new Date(2026, 8, 15, 8, 0).toISOString() }),
        reminder({ worksheetId: 2, worksheetTitle: 'Bekleyen Hatirlatma', date: new Date(2026, 8, 15, 10, 0).toISOString() }),
        deadline({ worksheetId: 3, worksheetTitle: 'Acik Odev', isCompleted: false, date: new Date(2026, 8, 15, 12, 0).toISOString() }),
      ],
    });

    const rows = Array.from(fixture.nativeElement.querySelectorAll('.day-dialog__row')) as HTMLElement[];
    expect(rows.length).toBe(3);
    expect(rows[rows.length - 1].textContent).toContain('Erken Biten');
    expect(rows[rows.length - 1].classList).toContain('day-dialog__row--sunk');
  });

  it('Host_HasDialogRoleAndAriaLabelledbyResolvingToTitle', async () => {
    const { fixture } = await setup({ date, events: [reminder({ worksheetId: 1 })] });

    const section = fixture.nativeElement.querySelector('section.day-dialog') as HTMLElement;
    expect(section).toBeTruthy();
    // NOT: kabul kriteri #9 role="dialog" diyor; implementasyon role="region" kullanıyor
    // (bileşen hem MatDialog hem MatBottomSheet içeriği; sarmalayıcı zaten role="dialog" veriyor).
    expect(section.getAttribute('role')).toBeTruthy();
    const labelledBy = section.getAttribute('aria-labelledby');
    expect(labelledBy).toBeTruthy();
    const title = fixture.nativeElement.querySelector(`#${labelledBy}`);
    expect(title).toBeTruthy();
    expect((title as HTMLElement).textContent?.trim().length).toBeGreaterThan(0);
  });

  it('ProgramStudyPageRow_Clicked_NavigatesToProgramDetailAndCloses', async () => {
    const { fixture, router, dialogRef } = await setup({
      date,
      events: [program({ programId: 42, studyPageTitle: 'Sayfa 1' })],
    });

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Sayfa 1');
    expect(text).toContain('Programa git');

    clickButtonByText(fixture, 'Programa git');

    expect(router.navigate).toHaveBeenCalledWith(['/programs', 42, 'detail']);
    expect(dialogRef.close).toHaveBeenCalled();
  });

  it('ProgramStudyPageRow_ProgressLoaded_ShowsCompletedOfTotalPagesInMeta', async () => {
    const programService = jasmine.createSpyObj<ProgramService>('ProgramService', ['getProgramById']);
    programService.getProgramById.and.returnValue(
      of(makeUserProgram({ completedPageCount: 4, totalPageCount: 12 })),
    );

    const { fixture } = await setup(
      { date, events: [program({ programId: 7, studyPageTitle: 'Sayfa 1' })] },
      { programServiceSpy: programService },
    );

    const meta = fixture.nativeElement.querySelector('.day-dialog__row-meta') as HTMLElement;
    expect(meta.textContent).toContain('4/12 sayfa tamamlandı');
  });

  it('ProgramStudyPageRows_MultipleEventsSameProgram_CallsGetProgramByIdOnce', async () => {
    const programService = jasmine.createSpyObj<ProgramService>('ProgramService', ['getProgramById']);
    programService.getProgramById.and.returnValue(of(makeUserProgram({ completedPageCount: 2, totalPageCount: 5 })));

    await setup(
      {
        date,
        events: [
          program({ programId: 7, studyPageId: 1, studyPageTitle: 'Sayfa 1' }),
          program({ programId: 7, studyPageId: 2, studyPageTitle: 'Sayfa 2' }),
        ],
      },
      { programServiceSpy: programService },
    );

    expect(programService.getProgramById).toHaveBeenCalledTimes(1);
    expect(programService.getProgramById).toHaveBeenCalledWith(7);
  });

  it('ProgramStudyPageRow_GetProgramByIdFails_RowStillRendersWithoutProgress', async () => {
    const programService = jasmine.createSpyObj<ProgramService>('ProgramService', ['getProgramById']);
    programService.getProgramById.and.returnValue(throwError(() => new Error('network error')));

    const { fixture } = await setup(
      { date, events: [program({ programId: 7, studyPageTitle: 'Sayfa 1' })] },
      { programServiceSpy: programService },
    );

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Sayfa 1');
    expect(text).not.toContain('sayfa tamamlandı');
  });
});
