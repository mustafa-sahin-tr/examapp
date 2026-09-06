import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { CalendarDayDialogComponent, CalendarDayDialogData } from './calendar-day-dialog.component';
import { CalendarEvent } from '../../../models/calendar-event';

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

async function setup(data: CalendarDayDialogData) {
  const router = jasmine.createSpyObj<Router>('Router', ['navigate']);
  const dialogRef = jasmine.createSpyObj<MatDialogRef<CalendarDayDialogComponent>>('MatDialogRef', ['close']);

  await TestBed.configureTestingModule({
    imports: [CalendarDayDialogComponent],
    providers: [
      { provide: Router, useValue: router },
      { provide: MatDialogRef, useValue: dialogRef },
      { provide: MAT_DIALOG_DATA, useValue: data },
    ],
  }).compileComponents();

  const fixture = TestBed.createComponent(CalendarDayDialogComponent);
  fixture.detectChanges();
  return { fixture, router, dialogRef };
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
});
