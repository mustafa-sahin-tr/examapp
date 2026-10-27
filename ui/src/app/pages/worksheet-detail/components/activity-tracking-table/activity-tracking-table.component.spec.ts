import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@jsverse/transloco';

import { ActivityTrackingTableComponent } from './activity-tracking-table.component';
import { TeacherAssignmentStudentSummary } from '../../../../models/assignment';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../../../models/locale';
import rootTr from '../../../../../../public/i18n/tr.json';
import worksheetDetailTr from '../../../../../../public/i18n/worksheet-detail/tr.json';

const translocoTesting = TranslocoTestingModule.forRoot({
  langs: { tr: { ...rootTr, 'worksheet-detail': worksheetDetailTr }, 'worksheet-detail/tr': worksheetDetailTr },
  translocoConfig: { availableLangs: [...SUPPORTED_LOCALE_CODES], defaultLang: DEFAULT_LOCALE, scopes: { keepCasing: true } },
  preloadLangs: true,
});

function student(overrides: Partial<TeacherAssignmentStudentSummary>): TeacherAssignmentStudentSummary {
  return { studentId: 1, userId: 1, studentNumber: '101', gradeName: '3-A', status: 'NotStarted', ...overrides };
}

describe('ActivityTrackingTableComponent (#573)', () => {
  let fixture: ComponentFixture<ActivityTrackingTableComponent>;
  const el = () => fixture.nativeElement as HTMLElement;
  const rows = () => Array.from(el().querySelectorAll('[data-testid="tracking-row"]')) as HTMLElement[];

  function setup(students: TeacherAssignmentStudentSummary[]): void {
    TestBed.configureTestingModule({ imports: [ActivityTrackingTableComponent, translocoTesting] });
    fixture = TestBed.createComponent(ActivityTrackingTableComponent);
    fixture.componentRef.setInput('students', students);
    fixture.detectChanges();
  }

  it('columns_StudentStatusActiveTimeFirstCheck_NoScoreOrRank', () => {
    setup([student({ status: 'Completed', firstCheckCorrectCount: 7, firstCheckTotalCount: 10 })]);

    const headers = Array.from(el().querySelectorAll('th')).map((th) => th.textContent?.trim());
    expect(headers).toEqual(['Öğrenci', 'Durum', 'Etkin süre', 'İlk kontrol']);
    expect(el().textContent).not.toMatch(/%|puan|sıralama/i);
  });

  it('firstCheck_FormatsCorrectAndWrong_OrDash', () => {
    setup([
      student({ studentId: 1, status: 'Completed', firstCheckCorrectCount: 7, firstCheckTotalCount: 10 }),
      student({ studentId: 2, studentNumber: '102', status: 'InProgress' }),
    ]);

    const cells = rows().map((r) => r.querySelector('[data-testid="first-check"]')?.textContent?.trim());
    expect(cells).toEqual(['7 doğru · 3 yanlış', '—']);
  });

  it('status_IconPlusText_ActiveTimeDashUntil572', () => {
    setup([student({ status: 'InProgress' })]);

    const status = rows()[0].querySelector('.att__status') as HTMLElement;
    expect(status.querySelector('mat-icon')?.textContent).toBe('timelapse');
    expect(status.textContent).toContain('Devam ediyor');
    expect(rows()[0].children[2].textContent?.trim()).toBe('—');
  });

  it('sameStudentInTwoAssignments_SingleRowWithMostAdvancedStatus', () => {
    setup([
      student({ studentId: 5, status: 'NotStarted' }),
      student({ studentId: 5, status: 'Completed', firstCheckCorrectCount: 2, firstCheckTotalCount: 2 }),
    ]);

    expect(rows().length).toBe(1);
    expect(rows()[0].textContent).toContain('Tamamladı');
  });

  it('noStudents_ShowsEmptyText', () => {
    setup([]);

    expect(el().textContent).toContain('Bu atamada öğrenci bilgisi yok.');
  });

  it('retaking_CompletedStatusKeptWithBadgeAndFirstAttemptResult (#609)', () => {
    setup([
      student({ studentId: 1, status: 'Completed', retaking: true, firstCheckCorrectCount: 7, firstCheckTotalCount: 10 }),
      student({ studentId: 2, studentNumber: '102', status: 'Completed', firstCheckCorrectCount: 5, firstCheckTotalCount: 5 }),
    ]);

    const [retaking, done] = rows();
    expect(retaking.querySelector('.att__status')?.textContent).toContain('Tamamladı');
    expect(retaking.querySelector('[data-testid="tracking-retaking"]')?.textContent?.trim()).toBe('Yeniden çözüyor');
    expect(retaking.querySelector('[data-testid="first-check"]')?.textContent?.trim()).toBe('7 doğru · 3 yanlış');
    expect(done.querySelector('[data-testid="tracking-retaking"]')).toBeNull();
  });

  it('retakeCount_ShownAsSecondaryInfoOnlyWhenPositive (#609)', () => {
    setup([
      student({ studentId: 1, status: 'Completed', retaking: true, retakeCount: 2, firstCheckCorrectCount: 7, firstCheckTotalCount: 10 }),
      student({ studentId: 2, studentNumber: '102', status: 'Completed', retakeCount: 1 }),
      student({ studentId: 3, studentNumber: '103', status: 'Completed', retakeCount: 0 }),
    ]);

    const [a, b, c] = rows();
    expect(a.querySelector('[data-testid="tracking-retaking"]')).not.toBeNull();
    expect(a.querySelector('[data-testid="tracking-retake-count"]')?.textContent?.trim()).toBe('2 kez yeniden çözdü');
    expect(b.querySelector('[data-testid="tracking-retaking"]')).toBeNull();
    expect(b.querySelector('[data-testid="tracking-retake-count"]')?.textContent?.trim()).toBe('1 kez yeniden çözdü');
    expect(c.querySelector('[data-testid="tracking-retake-count"]')).toBeNull();
    // İlk denemenin sonucu korunur.
    expect(a.querySelector('[data-testid="first-check"]')?.textContent?.trim()).toBe('7 doğru · 3 yanlış');
  });
});
