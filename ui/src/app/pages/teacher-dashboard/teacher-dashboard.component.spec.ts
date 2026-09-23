import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { NEVER, of, throwError } from 'rxjs';

import { TeacherDashboardComponent } from './teacher-dashboard.component';
import { TeacherService } from '../../services/teacher.service';
import { LocaleService } from '../../services/locale.service';
import { localeDefinitionOf } from '../../models/locale';
import {
  TeacherDashboardSummary,
  TeacherLaggingStudent,
  TeacherOwnActivitySummary,
  TeacherStudentsActivitySummary,
  TeacherWorksheetOverview,
} from '../../models/teacher-dashboard.model';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import teacherDashboardTr from '../../../../public/i18n/teacher-dashboard/tr.json';

/** Dönem satırı tarayıcı diline bağlı kalmasın: aktif dil sabit 'tr'. */
const localeServiceStub = {
  locale: signal('tr' as const).asReadonly(),
  localeDefinition: signal(localeDefinitionOf('tr')).asReadonly(),
};

describe('TeacherDashboardComponent', () => {
  let fixture: ComponentFixture<TeacherDashboardComponent>;
  let component: TeacherDashboardComponent;
  let teacherService: jasmine.SpyObj<TeacherService>;
  let router: Router;

  const summary: TeacherDashboardSummary = { totalWorksheets: 3, totalUniqueStudents: 10 };

  const worksheetRows: TeacherWorksheetOverview[] = [
    { worksheetId: 1, name: 'WS 1', assignedStudentCount: 4, completionPercentage: 75 },
    { worksheetId: 2, name: 'WS 2', assignedStudentCount: 2, completionPercentage: 25 },
  ];

  const laggingRows: TeacherLaggingStudent[] = [
    {
      studentId: 1,
      studentName: 'Ayşe Yılmaz',
      worksheetId: 1,
      worksheetName: 'WS 1',
      completionPercentage: 0,
      isLowCompletion: true,
      isExpired: false,
    },
    {
      studentId: 2,
      studentName: 'Mehmet Kaya',
      worksheetId: 2,
      worksheetName: 'WS 2',
      completionPercentage: 0,
      isLowCompletion: true,
      isExpired: true,
    },
  ];

  const ownActivity: TeacherOwnActivitySummary = { worksheetsCreated: 3, assignmentsCreated: 2, activeStudents: 4 };

  // Backend sırası (çözülen soru azalan); isimler bilinçli olarak alfabetik değil — UI sırayı korumalı.
  const studentsActivity: TeacherStudentsActivitySummary = {
    totalQuestionsSolved: 16,
    totalCorrectCount: 8,
    totalTimeSeconds: 150,
    topStudents: [
      { studentId: 12, studentName: 'Zeynep Z.', questionsSolved: 10, correctCount: 5, timeSeconds: 2 * 3600 + 5 * 60 },
      { studentId: 7, studentName: 'Ali A.', questionsSolved: 4, correctCount: 1, timeSeconds: 150 },
      { studentId: 3, studentName: 'Mert M.', questionsSolved: 2, correctCount: 2, timeSeconds: 40 },
      { studentId: 9, studentName: 'Can C.', questionsSolved: 1, correctCount: 0, timeSeconds: 0 },
    ],
  };

  function configure(): ComponentFixture<TeacherDashboardComponent> {
    teacherService = jasmine.createSpyObj<TeacherService>('TeacherService', [
      'getDashboardSummary',
      'getWorksheetsOverview',
      'getLaggingStudents',
      'getOwnActivitySummary',
      'getStudentsActivitySummary',
    ]);
    teacherService.getDashboardSummary.and.returnValue(of(summary));
    teacherService.getWorksheetsOverview.and.returnValue(of(worksheetRows));
    teacherService.getLaggingStudents.and.returnValue(of(laggingRows));
    teacherService.getOwnActivitySummary.and.returnValue(of(ownActivity));
    teacherService.getStudentsActivitySummary.and.returnValue(of(studentsActivity));

    TestBed.configureTestingModule({
      imports: [TeacherDashboardComponent, translocoTestingModule({
        langs: { 'teacher-dashboard/tr': teacherDashboardTr },
        // app.config.ts ile aynı: tireli scope önekleri camelCase'e çevrilmez.
        translocoConfig: { scopes: { keepCasing: true } },
      })],
      providers: [
        { provide: TeacherService, useValue: teacherService },
        { provide: LocaleService, useValue: localeServiceStub },
        provideRouter([]),
      ],
    });

    const created = TestBed.createComponent(TeacherDashboardComponent);
    router = TestBed.inject(Router);
    return created;
  }

  it('loadWorksheetsOverview_SuccessfulResponse_FillsWorksheetsSignal', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges(); // triggers ngOnInit -> loadWorksheetsOverview

    expect(component.worksheets()).toEqual(worksheetRows);
    expect(component.worksheetsError()).toBeNull();
  });

  it('loadWorksheetsOverview_RequestFails_SetsWorksheetsError', () => {
    fixture = configure();
    component = fixture.componentInstance;
    teacherService.getWorksheetsOverview.and.returnValue(throwError(() => new Error('network error')));

    fixture.detectChanges();

    expect(component.worksheets()).toEqual([]);
    expect(component.worksheetsError()).toBe('Sınav listesi alınırken bir sorun oluştu.');
  });

  it('completionClass_PercentageAtOrAboveFifty_ReturnsSuccessClass', () => {
    fixture = configure();
    component = fixture.componentInstance;

    expect(component.completionClass(50)).toBe('is-success');
    expect(component.completionClass(75)).toBe('is-success');
  });

  it('completionClass_PercentageBelowFifty_ReturnsWarningClass', () => {
    fixture = configure();
    component = fixture.componentInstance;

    expect(component.completionClass(49.99)).toBe('is-warning');
    expect(component.completionClass(0)).toBe('is-warning');
  });

  it('openWorksheet_Called_NavigatesToWorksheetDetailRoute', () => {
    fixture = configure();
    component = fixture.componentInstance;
    const navigateSpy = spyOn(router, 'navigate').and.resolveTo(true);

    component.openWorksheet(worksheetRows[0]);

    expect(navigateSpy).toHaveBeenCalledWith(['/test', worksheetRows[0].worksheetId]);
  });

  // ── Issue #55: Geride Kalan Öğrenciler ────────────────────────────────────

  it('loadLaggingStudents_SuccessfulResponse_FillsLaggingStudentsSignal', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges(); // triggers ngOnInit -> loadLaggingStudents

    expect(component.laggingStudents()).toEqual(laggingRows);
    expect(component.laggingStudentsError()).toBeNull();
  });

  it('loadLaggingStudents_RequestFails_SetsLaggingStudentsError', () => {
    fixture = configure();
    component = fixture.componentInstance;
    teacherService.getLaggingStudents.and.returnValue(throwError(() => new Error('network error')));

    fixture.detectChanges();

    expect(component.laggingStudents()).toEqual([]);
    expect(component.laggingStudentsError()).toBe('Geride kalan öğrenci listesi alınırken bir sorun oluştu.');
  });

  it('laggingStudentsEmpty_NoRows_IsTrue', () => {
    fixture = configure();
    component = fixture.componentInstance;
    teacherService.getLaggingStudents.and.returnValue(of([]));

    fixture.detectChanges();

    expect(component.laggingStudentsEmpty()).toBeTrue();
  });

  it('laggingStudentsEmpty_HasRows_IsFalse', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges();

    expect(component.laggingStudentsEmpty()).toBeFalse();
  });

  it('laggingStudentsTable_RowWithBothFlagsTrue_RendersWarningAndDangerChips', () => {
    fixture = configure();
    component = fixture.componentInstance;

    fixture.detectChanges();

    const chips = fixture.nativeElement.querySelectorAll('.lagging-table .flag-chips .completion-chip');
    const classesByRow: string[][] = [];
    chips.forEach((chip: HTMLElement) => classesByRow.push(Array.from(chip.classList)));

    // Row 0 (isLowCompletion=true, isExpired=false): only the warning chip.
    // Row 1 (isLowCompletion=true, isExpired=true): both warning and danger chips.
    const warningChips = Array.from(chips as NodeListOf<HTMLElement>).filter((c) =>
      c.classList.contains('is-warning'),
    );
    const dangerChips = Array.from(chips as NodeListOf<HTMLElement>).filter((c) =>
      c.classList.contains('is-danger'),
    );

    expect(warningChips.length).toBe(2); // both rows are low completion
    expect(dangerChips.length).toBe(1); // only the second row is expired
  });

  it('laggingStudentsTable_RowWithOnlyLowCompletion_RendersOnlyWarningChip', () => {
    fixture = configure();
    component = fixture.componentInstance;
    teacherService.getLaggingStudents.and.returnValue(of([laggingRows[0]]));

    fixture.detectChanges();

    const chips: HTMLElement[] = Array.from(
      fixture.nativeElement.querySelectorAll('.lagging-table .flag-chips .completion-chip'),
    );

    expect(chips.length).toBe(1);
    expect(chips[0].classList.contains('is-warning')).toBeTrue();
    expect(chips[0].classList.contains('is-danger')).toBeFalse();
  });

  // ── Issue #56: aktivite kartları + En Aktif Öğrenciler ────────────────────

  function text(selector: string): string {
    const el: HTMLElement | null = fixture.nativeElement.querySelector(selector);
    return (el?.textContent ?? '').replace(/\s+/g, ' ').trim();
  }

  function tileValue(cardKey: 'own' | 'students', metricKey: string): string {
    return text(`.activity-card[data-key="${cardKey}"] .activity-tile[data-key="${metricKey}"] .activity-tile__value`);
  }

  function topStudentRows(): HTMLElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('.top-students-table tr.top-students-table__row'));
  }

  it('ngOnInit_Always_RequestsBothActivitySummariesForSevenDays', () => {
    fixture = configure();
    fixture.detectChanges();

    expect(teacherService.getOwnActivitySummary).toHaveBeenCalledOnceWith(7);
    expect(teacherService.getStudentsActivitySummary).toHaveBeenCalledOnceWith(7);
  });

  it('ownActivityCard_SuccessfulResponse_ShowsCreatedCountsAndActiveStudents', () => {
    fixture = configure();
    fixture.detectChanges();

    expect(text('.activity-card[data-key="own"] .activity-card__title')).toBe('👤 Benim Aktivitem');
    expect(tileValue('own', 'worksheetsCreated')).toBe('3');
    expect(tileValue('own', 'assignmentsCreated')).toBe('2');
    expect(tileValue('own', 'activeStudents')).toBe('4');
  });

  it('studentsActivityCard_SuccessfulResponse_ShowsTotalsAccuracyAndReadableTime', () => {
    fixture = configure();
    fixture.detectChanges();

    expect(text('.activity-card[data-key="students"] .activity-card__title')).toBe('🎓 Öğrenci Aktivitesi');
    expect(tileValue('students', 'totalQuestionsSolved')).toBe('16');
    expect(tileValue('students', 'totalCorrectCount')).toBe('8');
    expect(text('.activity-tile[data-key="totalCorrectCount"] .activity-tile__hint')).toBe('%50 doğruluk');
    expect(tileValue('students', 'totalTimeSeconds')).toBe('2 dk 30 sn');
  });

  it('studentsActivityCard_NoQuestionsSolved_ShowsNoAccuracyInsteadOfDividingByZero', () => {
    fixture = configure();
    teacherService.getStudentsActivitySummary.and.returnValue(
      of({ totalQuestionsSolved: 0, totalCorrectCount: 0, totalTimeSeconds: 0, topStudents: [] }),
    );
    fixture.detectChanges();

    expect(tileValue('students', 'totalQuestionsSolved')).toBe('0');
    expect(text('.activity-tile[data-key="totalCorrectCount"] .activity-tile__hint')).toBe('Henüz çözülen soru yok');
    expect(tileValue('students', 'totalTimeSeconds')).toBe('0 sn');
  });

  it('topStudentsTable_SuccessfulResponse_KeepsBackendOrderAndRanks', () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();

    const rows = topStudentRows();
    const names = rows.map((r) => r.querySelector('.top-students-table__student')?.textContent?.trim());
    const ranks = rows.map((r) => r.querySelector('.top-students-table__rank [aria-hidden="true"]')?.textContent?.trim());
    const spokenRanks = rows.map((r) => r.querySelector('.top-students-table__rank .cdk-visually-hidden')?.textContent?.trim());
    const solved = rows.map((r) => r.querySelector('.top-students-table__solved')?.textContent?.trim());

    expect(names).toEqual(['Zeynep Z.', 'Ali A.', 'Mert M.', 'Can C.']);
    // İlk üç sıra madalya, sonrası sıra numarası.
    expect(ranks).toEqual(['🥇', '🥈', '🥉', '4']);
    // Ekran okuyucu madalya yerine sıra numarasını okur.
    expect(spokenRanks).toEqual(['1', '2', '3', '4']);
    expect(solved).toEqual(['10', '4', '2', '1']);
    expect(component.topStudents()).toEqual(studentsActivity.topStudents);
  });

  it('topStudentsTable_Rows_ShowAccuracyAndReadableTime', () => {
    fixture = configure();
    fixture.detectChanges();

    const rows = topStudentRows();
    const times = rows.map((r) => r.querySelector('.top-students-table__time')?.textContent?.trim());
    const accuracies = rows.map((r) => r.querySelector('.top-students-table__accuracy')?.textContent?.trim());

    expect(times).toEqual(['2 sa 5 dk', '2 dk 30 sn', '40 sn', '0 sn']);
    expect(accuracies).toEqual(['(%50)', '(%25)', '(%100)', '(%0)']);
  });

  it('topStudentsTable_EmptyList_ShowsEmptyMessageAndNoTable', () => {
    fixture = configure();
    component = fixture.componentInstance;
    teacherService.getStudentsActivitySummary.and.returnValue(
      of({ totalQuestionsSolved: 0, totalCorrectCount: 0, totalTimeSeconds: 0, topStudents: [] }),
    );
    fixture.detectChanges();

    expect(component.topStudentsEmpty()).toBeTrue();
    expect(fixture.nativeElement.querySelector('.top-students-table')).toBeNull();
    expect(text('.top-students-empty .state-text')).toBe('Son 7 günde öğrencileriniz henüz soru çözmedi.');
  });

  it('ownActivity_RequestFails_ShowsErrorAndRetryReloads', () => {
    fixture = configure();
    component = fixture.componentInstance;
    teacherService.getOwnActivitySummary.and.returnValue(throwError(() => new Error('network error')));
    fixture.detectChanges();

    const message = 'Aktivite bilgileriniz alınırken bir sorun oluştu.';
    expect(component.ownActivity()).toBeNull();
    expect(component.ownActivityError()).toBe(message);
    expect(text('.activity-card[data-key="own"] .state-box--error .state-text')).toBe(message);
    // Diğer kart etkilenmez.
    expect(tileValue('students', 'totalQuestionsSolved')).toBe('16');

    teacherService.getOwnActivitySummary.and.returnValue(of(ownActivity));
    const retry: HTMLButtonElement = fixture.nativeElement.querySelector(
      '.activity-card[data-key="own"] .state-box--error button',
    );
    retry.click();
    fixture.detectChanges();

    expect(teacherService.getOwnActivitySummary).toHaveBeenCalledTimes(2);
    expect(component.ownActivityError()).toBeNull();
    expect(tileValue('own', 'worksheetsCreated')).toBe('3');
  });

  it('studentsActivity_RequestFails_ShowsErrorInCardAndTableSection', () => {
    fixture = configure();
    component = fixture.componentInstance;
    teacherService.getStudentsActivitySummary.and.returnValue(throwError(() => new Error('network error')));
    fixture.detectChanges();

    const message = 'Öğrenci aktivitesi alınırken bir sorun oluştu.';
    expect(component.studentsActivityError()).toBe(message);
    expect(component.topStudents()).toEqual([]);
    expect(text('.activity-card[data-key="students"] .state-box--error .state-text')).toBe(message);
    expect(fixture.nativeElement.querySelector('.top-students-table')).toBeNull();
    // Hata durumunda "boş" mesajı gösterilmez.
    expect(fixture.nativeElement.querySelector('.top-students-empty')).toBeNull();
  });

  it('activitySections_Titles_IncludeWindowAndSortHint', () => {
    fixture = configure();
    fixture.detectChanges();

    const titles: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.section-title'));
    const activityTitle = titles.find((el) => el.textContent?.includes('Aktivite Özeti (son 7 gün)'));
    const topTitle = titles.find((el) => el.textContent?.includes('En Aktif Öğrenciler (son 7 gün)'));

    // Emoji dekoratif: i18n metninde değil, aria-hidden span'de.
    expect(activityTitle?.querySelector('[aria-hidden="true"]')?.textContent?.trim()).toBe('📊');
    expect(topTitle?.querySelector('[aria-hidden="true"]')?.textContent?.trim()).toBe('🏆');
    expect(text('.activity-card[data-key="own"] .activity-card__title [aria-hidden="true"]')).toBe('👤');
    expect(text('.activity-card[data-key="students"] .activity-card__title [aria-hidden="true"]')).toBe('🎓');
    expect(text('.section-header-row__hint')).toBe('Çözülen soruya göre sıralı');
    expect(fixture.nativeElement.querySelector('.top-students-table').getAttribute('aria-label')).toBe(
      'En Aktif Öğrenciler (son 7 gün)',
    );
  });

  it('activityCards_PeriodLine_ShowsDateRangeAndShortMobileText', () => {
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(Date.UTC(2026, 8, 23, 12, 0, 0)));
    try {
      fixture = configure();
      fixture.detectChanges();

      const ranges: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.activity-card__period-range'));
      expect(ranges.length).toBe(2);
      const range = (ranges[0].textContent ?? '').trim();
      expect(range).toContain('17');
      expect(range).toContain('23 Eylül 2026');
      expect(text('.activity-card__period-short')).toBe('Son 7 gün');
    } finally {
      jasmine.clock().uninstall();
    }
  });

  it('topStudentCards_Mobile_RenderSameOrderWithMedalCountsAndTime', () => {
    fixture = configure();
    fixture.detectChanges();

    const cards: HTMLElement[] = Array.from(fixture.nativeElement.querySelectorAll('.top-student-cards .top-student-card'));
    const clean = (el: Element | null) => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

    expect(cards.map((c) => clean(c.querySelector('.top-student-card__rank [aria-hidden="true"]')))).toEqual([
      '🥇',
      '🥈',
      '🥉',
      '4',
    ]);
    expect(cards.map((c) => clean(c.querySelector('.top-student-card__rank .cdk-visually-hidden')))).toEqual([
      '1',
      '2',
      '3',
      '4',
    ]);
    expect(cards.map((c) => clean(c.querySelector('.top-student-card__name')))).toEqual([
      'Zeynep Z.',
      'Ali A.',
      'Mert M.',
      'Can C.',
    ]);
    expect(clean(cards[0].querySelector('.top-student-card__counts'))).toBe('10 soru · 5 doğru');
    expect(clean(cards[0].querySelector('.top-student-card__time'))).toBe('2 sa 5 dk');
  });

  it('activityCards_Loading_ExposeScreenReaderText', () => {
    fixture = configure();
    teacherService.getOwnActivitySummary.and.returnValue(NEVER);
    teacherService.getStudentsActivitySummary.and.returnValue(NEVER);
    fixture.detectChanges();

    expect(text('.activity-card[data-key="own"] .activity-tiles .cdk-visually-hidden')).toBe('Aktivite bilgileri yükleniyor...');
    expect(text('.activity-card[data-key="students"] .activity-tiles .cdk-visually-hidden')).toBe(
      'Aktivite bilgileri yükleniyor...',
    );
  });
});
