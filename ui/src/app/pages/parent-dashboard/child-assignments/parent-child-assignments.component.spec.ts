import { HttpErrorResponse } from '@angular/common/http';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoService } from '@jsverse/transloco';
import { Observable, Subject, isObservable, of, throwError } from 'rxjs';

import { ParentChildAssignmentsComponent } from './parent-child-assignments.component';
import { ParentTestResultDialogComponent } from '../test-result-dialog/parent-test-result-dialog.component';
import { ParentDashboardService } from '../../../services/parent-dashboard.service';
import { LocaleService } from '../../../services/locale.service';
import {
  ParentChildAssignmentItem,
  ParentChildAssignmentList,
} from '../../../models/parent-dashboard.model';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import parentDashboardTr from '../../../../../public/i18n/parent-dashboard/tr.json';
import parentDashboardEn from '../../../../../public/i18n/parent-dashboard/en.json';

@Component({
  standalone: true,
  imports: [ParentChildAssignmentsComponent],
  template: `<app-parent-child-assignments [studentId]="studentId()" (notFound)="notFoundCount = notFoundCount + 1" />`,
})
class HostComponent {
  readonly studentId = signal(11);
  notFoundCount = 0;
}

/** Issue #421: veli "Ödevler ve testler" bölümü — çipler, liste, sayfalama, sonuç dialog'u, boş/yükleniyor/hata durumları. */
describe('ParentChildAssignmentsComponent (issue #421)', () => {
  let service: jasmine.SpyObj<ParentDashboardService>;
  let dialog: jasmine.SpyObj<MatDialog>;
  let fixture: ComponentFixture<HostComponent>;

  const completed: ParentChildAssignmentItem = {
    worksheetId: 1,
    title: 'Kesirler',
    subject: 'Matematik',
    teacherName: 'Zeynep Öğretmen',
    startAt: '2026-10-01T09:00:00Z',
    deadline: '2026-10-10T17:00:00Z',
    status: 'completed',
    testInstanceId: 501,
    result: { scorePercent: 75, correctCount: 6, wrongCount: 1, blankCount: 1, totalCount: 8, durationSeconds: 1500 },
  };

  const pending: ParentChildAssignmentItem = {
    worksheetId: 2,
    title: 'Ondalık',
    subject: null,
    teacherName: null,
    startAt: '2026-10-05T09:00:00Z',
    deadline: null,
    status: 'pending',
    testInstanceId: null,
    result: null,
  };

  function listOf(items: ParentChildAssignmentItem[], overrides: Partial<ParentChildAssignmentList> = {}): ParentChildAssignmentList {
    return {
      studentId: 11,
      status: null,
      page: 1,
      pageSize: 20,
      totalCount: items.length,
      counts: { completed: 1, overdue: 3, pending: 1, windowDays: 30 },
      items,
      ...overrides,
    };
  }

  async function setup(
    response: ParentChildAssignmentList | Observable<ParentChildAssignmentList> = listOf([pending, completed]),
    lang = 'tr'
  ): Promise<HTMLElement> {
    service = jasmine.createSpyObj<ParentDashboardService>('ParentDashboardService', ['getChildAssignments', 'getChildTestResult']);
    service.getChildAssignments.and.returnValue(isObservable(response) ? response : of(response));
    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);

    TestBed.configureTestingModule({
      imports: [
        HostComponent,
        NoopAnimationsModule,
        translocoTestingModule({
          langs: { 'parent-dashboard/tr': parentDashboardTr, 'parent-dashboard/en': parentDashboardEn },
        }),
      ],
      providers: [
        { provide: ParentDashboardService, useValue: service },
        { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: lang === 'tr' ? 'tr-TR' : 'en-US' }) } },
        { provide: MatDialog, useValue: dialog },
      ],
    });
    TestBed.inject(TranslocoService).setActiveLang(lang);
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const text = (el: Element | null) => el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const rows = (el: HTMLElement) => Array.from(el.querySelectorAll('[data-test="assignment-row"]'));

  /** MatChipOption tıklaması birincil aksiyon düğmesine bağlı. */
  function clickChip(el: HTMLElement, name: string): void {
    const chip = el.querySelector(`[data-test="chip-${name}"]`)!;
    (chip.querySelector('.mdc-evolution-chip__action--primary') as HTMLElement).click();
  }

  it('loadsTheChildsListOnInit_AndRendersRows', async () => {
    const el = await setup();
    expect(service.getChildAssignments).toHaveBeenCalledOnceWith(11, null, 1);
    expect(text(el.querySelector('h2'))).toBe(parentDashboardTr.assignments.title);

    const [first, second] = rows(el);
    expect(text(first.querySelector('[data-test="row-title"]'))).toBe('Ondalık');
    expect(text(first)).toContain(parentDashboardTr.assignments.noSubject);
    expect(text(first.querySelector('[data-test="row-deadline"]'))).toBe(parentDashboardTr.assignments.noDeadline);
    expect(text(first.querySelector('[data-test="row-status"]'))).toBe(parentDashboardTr.assignments.status.pending);
    expect(first.querySelector('[data-test="row-teacher"]')).toBeNull();
    expect(first.querySelector('[data-test="row-open-result"]')).toBeNull();
    expect(first.querySelector('[data-test="row-score"]')).toBeNull();

    expect(text(second)).toContain('Matematik');
    expect(text(second.querySelector('[data-test="row-teacher"]'))).toBe('Öğretmen: Zeynep Öğretmen');
    expect(text(second.querySelector('[data-test="row-status"]'))).toBe(parentDashboardTr.assignments.status.completed);
    expect(text(second.querySelector('[data-test="row-score"]'))).toBe('%75 başarı');
    expect(text(second.querySelector('[data-test="row-counts"]'))).toBe('6 doğru · 1 yanlış · 1 boş · 25 dk');
    expect(text(second.querySelector('[data-test="row-deadline"]'))).toContain('Son teslim: 10 Eki 2026');
  });

  it('chips_ShowBucketCounts_AndFilterResetsToPageOne', async () => {
    const el = await setup();
    expect(text(el.querySelector('[data-test="chip-all"]'))).toContain('5');
    expect(text(el.querySelector('[data-test="chip-overdue"]'))).toContain('3');
    expect(text(el.querySelector('[data-test="chip-overdue"]'))).toContain(parentDashboardTr.assignments.status.overdue);

    service.getChildAssignments.calls.reset();
    service.getChildAssignments.and.returnValue(of(listOf([], { status: 'overdue', totalCount: 0 })));
    clickChip(el, 'overdue');
    await settle();
    expect(service.getChildAssignments).toHaveBeenCalledOnceWith(11, 'overdue', 1);
    // Filtreli boş durum metni.
    expect(text(el.querySelector('[data-test="assignments-empty"]'))).toBe(parentDashboardTr.assignments.emptyFiltered);

    service.getChildAssignments.calls.reset();
    clickChip(el, 'all');
    await settle();
    expect(service.getChildAssignments).toHaveBeenCalledOnceWith(11, null, 1);
  });

  it('clickingTheSelectedChip_KeepsItSelected_WithoutRefetch', async () => {
    const el = await setup();
    service.getChildAssignments.calls.reset();

    clickChip(el, 'all');
    await settle();
    expect(service.getChildAssignments).not.toHaveBeenCalled();
    expect(el.querySelector('[data-test="chip-all"]')!.classList).toContain('mat-mdc-chip-selected');

    service.getChildAssignments.and.returnValue(of(listOf([], { status: 'overdue' })));
    clickChip(el, 'overdue');
    await settle();
    service.getChildAssignments.calls.reset();
    clickChip(el, 'overdue');
    await settle();
    expect(service.getChildAssignments).not.toHaveBeenCalled();
    expect(el.querySelector('[data-test="chip-overdue"]')!.classList).toContain('mat-mdc-chip-selected');
    expect(el.querySelector('[data-test="chip-all"]')!.classList).not.toContain('mat-mdc-chip-selected');
  });

  it('emptyList_ShowsEmptyState', async () => {
    const el = await setup(listOf([], { counts: { completed: 0, overdue: 0, pending: 0, windowDays: 30 } }));
    expect(text(el.querySelector('[data-test="assignments-empty"]'))).toBe(parentDashboardTr.assignments.empty);
    expect(el.querySelector('[data-test="assignments-paginator"]')).toBeNull();
  });

  it('whileLoading_ShowsSpinner', async () => {
    const pendingResponse = new Subject<ParentChildAssignmentList>();
    const el = await setup(pendingResponse);
    expect(el.querySelector('[data-test="assignments-loading"]')).not.toBeNull();

    pendingResponse.next(listOf([pending]));
    pendingResponse.complete();
    await settle();
    expect(el.querySelector('[data-test="assignments-loading"]')).toBeNull();
    expect(rows(el).length).toBe(1);
  });

  it('serverError_ShowsRetry_ThatRefetches', async () => {
    const el = await setup(listOf([pending]));
    service.getChildAssignments.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    (fixture.debugElement.children[0].componentInstance as { reload(): void }).reload();
    await settle();
    expect(text(el.querySelector('[data-test="assignments-error"]'))).toContain(parentDashboardTr.assignments.loadError);
    expect(fixture.componentInstance.notFoundCount).toBe(0);

    service.getChildAssignments.and.returnValue(of(listOf([pending])));
    el.querySelector<HTMLButtonElement>('[data-test="assignments-error"] button')!.click();
    await settle();
    expect(el.querySelector('[data-test="assignments-error"]')).toBeNull();
    expect(rows(el).length).toBe(1);
  });

  it('notFound_EmitsToParent', async () => {
    const el = await setup(listOf([pending]));
    service.getChildAssignments.and.returnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    (fixture.debugElement.children[0].componentInstance as { reload(): void }).reload();
    await settle();
    expect(fixture.componentInstance.notFoundCount).toBe(1);
    expect(text(el.querySelector('[data-test="assignments-error"]'))).toContain(parentDashboardTr.summaryNotFound);
  });

  it('childChange_ResetsFilterAndPage', async () => {
    const el = await setup();
    service.getChildAssignments.and.returnValue(of(listOf([], { status: 'completed' })));
    clickChip(el, 'completed');
    await settle();

    service.getChildAssignments.calls.reset();
    service.getChildAssignments.and.returnValue(of(listOf([pending], { studentId: 12 })));
    fixture.componentInstance.studentId.set(12);
    await settle();
    expect(service.getChildAssignments).toHaveBeenCalledOnceWith(12, null, 1);
  });

  it('paginator_ShownOnlyWhenMoreThanOnePage_AndRequestsNextPage', async () => {
    const el = await setup(listOf([pending, completed], { totalCount: 45 }));
    expect(el.querySelector('[data-test="assignments-paginator"]')).not.toBeNull();

    service.getChildAssignments.calls.reset();
    service.getChildAssignments.and.returnValue(of(listOf([pending], { totalCount: 45, page: 2 })));
    el.querySelector<HTMLButtonElement>('.mat-mdc-paginator-navigation-next')!.click();
    await settle();
    expect(service.getChildAssignments).toHaveBeenCalledOnceWith(11, null, 2);
  });

  it('openResult_OpensSummaryDialogForThatInstance', async () => {
    const el = await setup();
    el.querySelector<HTMLButtonElement>('[data-test="row-open-result"]')!.click();
    expect(dialog.open).toHaveBeenCalledTimes(1);
    const [component, config] = dialog.open.calls.mostRecent().args;
    expect(component).toBe(ParentTestResultDialogComponent);
    expect(config!.data).toEqual({ studentId: 11, testInstanceId: 501, title: 'Kesirler' });
  });

  it('english_RendersEnglishTexts', async () => {
    const el = await setup(listOf([completed]), 'en');
    expect(text(el.querySelector('h2'))).toBe(parentDashboardEn.assignments.title);
    expect(text(el.querySelector('[data-test="row-score"]'))).toBe('75% score');
    expect(text(el.querySelector('[data-test="row-counts"]'))).toBe('6 correct · 1 wrong · 1 blank · 25 min');
  });
});
