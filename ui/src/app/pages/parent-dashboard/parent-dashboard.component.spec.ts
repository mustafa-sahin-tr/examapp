import { HttpErrorResponse } from '@angular/common/http';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { TranslocoService } from '@jsverse/transloco';
import { Subject, of, throwError } from 'rxjs';

import { ParentDashboardComponent, parseChildParam, parseLocalDate } from './parent-dashboard.component';
import { ParentDashboardService } from '../../services/parent-dashboard.service';
import { ParentLinkService } from '../../services/parent-link.service';
import { LocaleService } from '../../services/locale.service';
import { LinkedChild } from '../../models/parent-link.model';
import { ParentChildAssignmentList, ParentChildSummary } from '../../models/parent-dashboard.model';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import parentDashboardTr from '../../../../public/i18n/parent-dashboard/tr.json';
import parentDashboardEn from '../../../../public/i18n/parent-dashboard/en.json';

@Component({ standalone: true, template: '' })
class MyChildrenStubComponent {}

/** Issue #420: veli paneli — çocuk seçici (?child=), özet kartları, boş durum CTA'sı. */
describe('ParentDashboardComponent (issue #420)', () => {
  let linkService: jasmine.SpyObj<ParentLinkService>;
  let dashboardService: jasmine.SpyObj<ParentDashboardService>;
  let harness: RouterTestingHarness;

  const ayse: LinkedChild = {
    linkId: 1,
    studentId: 11,
    status: 'Active',
    studentName: 'Ayşe Kaya',
    gradeName: '7. Sınıf',
    schoolName: 'Atatürk Ortaokulu',
    linkedAt: '2026-10-01T09:00:00Z',
    requestedAt: '2026-10-01T08:00:00Z',
    pendingExpiresAt: null,
  };

  const mert: LinkedChild = { ...ayse, linkId: 2, studentId: 12, studentName: 'Mert Kaya', gradeName: '5. Sınıf' };

  const pending: LinkedChild = {
    linkId: 3,
    studentId: null,
    status: 'Pending',
    studentName: null,
    gradeName: null,
    schoolName: null,
    linkedAt: null,
    requestedAt: '2026-10-07T09:00:00Z',
    pendingExpiresAt: '2026-10-14T09:00:00Z',
  };

  function summaryFor(studentId: number, overrides: Partial<ParentChildSummary> = {}): ParentChildSummary {
    return {
      studentId,
      weekStart: '2026-10-05',
      questionsSolvedThisWeek: 17,
      assignments: { completed: 4, overdue: 1, pending: 2, windowDays: 30 },
      totalPoints: 1250,
      lastActivityAt: '2026-10-07T08:30:00Z',
      ...overrides,
    };
  }

  function emptyList(studentId: number): ParentChildAssignmentList {
    return {
      studentId,
      status: null,
      page: 1,
      pageSize: 20,
      totalCount: 0,
      counts: { completed: 0, overdue: 0, pending: 0, windowDays: 30 },
      items: [],
    };
  }

  async function setup(children: LinkedChild[], url = '/parent', lang = 'tr'): Promise<HTMLElement> {
    linkService = jasmine.createSpyObj<ParentLinkService>('ParentLinkService', ['getMyChildren', 'extractError']);
    linkService.getMyChildren.and.returnValue(of(children));
    linkService.extractError.and.callFake((_err: HttpErrorResponse, fallback: string) => fallback);
    dashboardService = jasmine.createSpyObj<ParentDashboardService>('ParentDashboardService', [
      'getChildSummary',
      'getChildAssignments',
      'getChildTestResult',
    ]);
    dashboardService.getChildSummary.and.callFake((id: number) => of(summaryFor(id)));
    dashboardService.getChildAssignments.and.callFake((id: number) => of(emptyList(id)));

    TestBed.configureTestingModule({
      imports: [
        NoopAnimationsModule,
        translocoTestingModule({
          langs: { 'parent-dashboard/tr': parentDashboardTr, 'parent-dashboard/en': parentDashboardEn },
        }),
      ],
      providers: [
        provideRouter([
          { path: 'parent', component: ParentDashboardComponent },
          { path: 'my-children', component: MyChildrenStubComponent },
        ]),
        { provide: ParentLinkService, useValue: linkService },
        { provide: ParentDashboardService, useValue: dashboardService },
        { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: lang === 'tr' ? 'tr-TR' : 'en-US' }) } },
      ],
    });
    TestBed.inject(TranslocoService).setActiveLang(lang);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, ParentDashboardComponent);
    await settle();
    return harness.routeNativeElement as HTMLElement;
  }

  /** Query param düzeltmesi (replaceUrl navigasyonu) ve sinyal güncellemeleri. */
  async function settle(): Promise<void> {
    await harness.fixture.whenStable();
    harness.detectChanges();
  }

  const url = () => TestBed.inject(Router).url;
  const text = (el: HTMLElement, sel: string) => el.querySelector(sel)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  it('parseChildParam_AcceptsOnlyPositiveIntegers', () => {
    expect(parseChildParam('12')).toBe(12);
    expect(parseChildParam(null)).toBeNull();
    expect(parseChildParam('')).toBeNull();
    expect(parseChildParam('0')).toBeNull();
    expect(parseChildParam('-3')).toBeNull();
    expect(parseChildParam('1.5')).toBeNull();
    expect(parseChildParam('abc')).toBeNull();
    expect(parseChildParam('99999999999999999999')).toBeNull();
  });

  it('noChildren_ShowsEmptyStateWithAddChildCta_AndNoSummaryCall', async () => {
    const el = await setup([]);
    expect(el.querySelector('[data-test="empty"]')).not.toBeNull();
    const cta = el.querySelector<HTMLAnchorElement>('[data-test="add-child"]')!;
    expect(cta.textContent).toContain(parentDashboardTr.empty.cta);
    expect(cta.getAttribute('href')).toBe('/my-children');
    expect(el.querySelector('[data-test="pending-note"]')).toBeNull();
    expect(dashboardService.getChildSummary).not.toHaveBeenCalled();
    expect(url()).toBe('/parent');
  });

  it('onlyPendingRequest_ShowsEmptyStateWithPendingNote', async () => {
    const el = await setup([pending]);
    expect(el.querySelector('[data-test="empty"]')).not.toBeNull();
    expect(text(el, '[data-test="pending-note"]')).toBe(parentDashboardTr.empty.pendingNote);
    expect(dashboardService.getChildSummary).not.toHaveBeenCalled();
  });

  it('addChildCta_NavigatesToMyChildren', async () => {
    const el = await setup([]);
    el.querySelector<HTMLAnchorElement>('[data-test="add-child"]')!.click();
    await settle();
    expect(url()).toBe('/my-children');
  });

  it('noQueryParam_SelectsFirstActiveChild_AndRemembersItInUrl', async () => {
    const el = await setup([pending, ayse, mert]);
    expect(dashboardService.getChildSummary).toHaveBeenCalledOnceWith(11);
    expect(url()).toBe('/parent?child=11');
    expect(text(el, '[data-test="selected-child"]')).toContain('Ayşe Kaya');
    // İki aktif çocuk → seçici görünür; bekleyen istek listede yok.
    expect(el.querySelector('[data-test="child-select"]')).not.toBeNull();
  });

  it('singleChild_HidesSelector_ShowsChildName', async () => {
    const el = await setup([ayse]);
    expect(el.querySelector('[data-test="child-select"]')).toBeNull();
    expect(text(el, '[data-test="selected-child"]')).toContain('Ayşe Kaya');
  });

  it('queryParam_SelectsThatChild', async () => {
    const el = await setup([ayse, mert], '/parent?child=12');
    expect(dashboardService.getChildSummary).toHaveBeenCalledOnceWith(12);
    expect(url()).toBe('/parent?child=12');
    expect(text(el, '[data-test="selected-child"]')).toContain('Mert Kaya');
  });

  it('unknownOrInvalidQueryParam_FallsBackToFirstChild_AndFixesUrl', async () => {
    await setup([ayse, mert], '/parent?child=999');
    expect(dashboardService.getChildSummary).toHaveBeenCalledOnceWith(11);
    expect(url()).toBe('/parent?child=11');
  });

  it('selectChild_UpdatesUrl_AndLoadsThatChildsSummary', async () => {
    const el = await setup([ayse, mert]);
    dashboardService.getChildSummary.calls.reset();

    const component = harness.routeDebugElement!.componentInstance as ParentDashboardComponent;
    (component as unknown as { selectChild(id: number): void }).selectChild(12);
    await settle();

    expect(url()).toBe('/parent?child=12');
    expect(dashboardService.getChildSummary).toHaveBeenCalledOnceWith(12);
    expect(text(el, '[data-test="selected-child"]')).toContain('Mert Kaya');
  });

  it('summary_RendersCards', async () => {
    const el = await setup([ayse]);
    expect(text(el, '[data-test="solved-value"]')).toBe('17');
    expect(text(el, '[data-test="assignments-completed"]')).toContain('4');
    expect(text(el, '[data-test="assignments-completed"]')).toContain(parentDashboardTr.cards.assignments.completed);
    expect(text(el, '[data-test="assignments-pending"]')).toContain('2');
    expect(text(el, '[data-test="assignments-overdue"]')).toContain('1');
    expect(text(el, '[data-test="assignments-overdue"]')).toContain(parentDashboardTr.cards.assignments.overdue);
    expect(text(el, '[data-test="points-value"]')).toBe('1250');
    expect(text(el, '[data-test="card-solved"]')).toContain(parentDashboardTr.cards.solved.title);
    expect(text(el, '[data-test="card-assignments"]')).toContain('30');
    expect(text(el, '[data-test="last-activity-value"]')).not.toBe('');
    // Hafta başı ("yyyy-MM-dd") yerel gün olarak — saat dilimine göre bir önceki güne kaymaz.
    expect(text(el, '[data-test="card-solved"]')).toContain('5 Ekim');
  });

  it('summary_NoActivity_ShowsNoneText', async () => {
    const el = await setup([ayse]);
    dashboardService.getChildSummary.and.returnValue(of(summaryFor(11, { lastActivityAt: null })));
    const component = harness.routeDebugElement!.componentInstance as unknown as { retrySummary(): void };
    component.retrySummary();
    await settle();
    expect(text(el, '[data-test="last-activity-value"]')).toBe(parentDashboardTr.cards.lastActivity.none);
  });

  it('summary404_ShowsNoAccessMessage_RetryRefetches', async () => {
    const el = await setup([ayse]);
    dashboardService.getChildSummary.and.returnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    const component = harness.routeDebugElement!.componentInstance as unknown as { retrySummary(): void };
    component.retrySummary();
    await settle();
    expect(text(el, '[data-test="summary-error"]')).toContain(parentDashboardTr.summaryNotFound);
    // 404 → çocuk listesi yenilenir (bağlantı kaldırılmış olabilir); bilgi notu görünür.
    expect(linkService.getMyChildren).toHaveBeenCalledTimes(2);
    expect(text(el, '[data-test="notice"]')).toBe(parentDashboardTr.summaryNotFound);

    dashboardService.getChildSummary.and.returnValue(of(summaryFor(11)));
    el.querySelector<HTMLButtonElement>('[data-test="summary-error"] button')!.click();
    await settle();
    expect(el.querySelector('[data-test="summary-error"]')).toBeNull();
    expect(text(el, '[data-test="solved-value"]')).toBe('17');
  });

  it('summary404_ChildNoLongerLinked_ReloadsListAndFallsBackToRemainingChild', async () => {
    const el = await setup([ayse, mert], '/parent?child=12');
    dashboardService.getChildSummary.calls.reset();
    dashboardService.getChildSummary.and.callFake((id: number) =>
      id === 12 ? throwError(() => new HttpErrorResponse({ status: 404 })) : of(summaryFor(id))
    );
    linkService.getMyChildren.and.returnValue(of([ayse])); // Mert'in bağlantısı kaldırılmış

    (harness.routeDebugElement!.componentInstance as unknown as { retrySummary(): void }).retrySummary();
    await settle();

    expect(dashboardService.getChildSummary.calls.allArgs()).toEqual([[12], [11]]);
    expect(url()).toBe('/parent?child=11');
    expect(text(el, '[data-test="selected-child"]')).toContain('Ayşe Kaya');
    expect(text(el, '[data-test="notice"]')).toBe(parentDashboardTr.summaryNotFound);
    expect(el.querySelector('[data-test="summary-error"]')).toBeNull();
  });

  it('summary404_LastChildRemoved_ShowsEmptyState', async () => {
    const el = await setup([ayse]);
    dashboardService.getChildSummary.and.returnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    linkService.getMyChildren.and.returnValue(of([]));

    (harness.routeDebugElement!.componentInstance as unknown as { retrySummary(): void }).retrySummary();
    await settle();

    expect(el.querySelector('[data-test="empty"]')).not.toBeNull();
  });

  it('formatLastActivity_TodayYesterdayOrDate_WithRoundHour', async () => {
    await setup([ayse]);
    const component = harness.routeDebugElement!.componentInstance as unknown as {
      now: () => Date;
      formatLastActivity(iso: string | null): string;
    };
    component.now = () => new Date(2026, 9, 7, 18, 45);
    const at = (d: number, h: number) => new Date(2026, 9, d, h, 0).toISOString();

    expect(component.formatLastActivity(at(7, 14))).toBe('Bugün, yaklaşık 14:00');
    expect(component.formatLastActivity(at(6, 9))).toBe('Dün, yaklaşık 09:00');
    expect(component.formatLastActivity(at(3, 21))).toBe('3 Ekim 2026, yaklaşık 21:00');
    expect(component.formatLastActivity(null)).toBe(parentDashboardTr.cards.lastActivity.none);
  });

  it('parseLocalDate_BuildsLocalMidnight', () => {
    const d = parseLocalDate('2026-10-05')!;
    expect([d.getFullYear(), d.getMonth(), d.getDate(), d.getHours()]).toEqual([2026, 9, 5, 0]);
    expect(parseLocalDate('2026-10-5')).toBeNull();
    expect(parseLocalDate(null)).toBeNull();
  });

  it('overdueLabel_MentionsTimedOut', () => {
    expect(parentDashboardTr.cards.assignments.overdue).toBe('Gecikti / süresi doldu');
    expect(parentDashboardEn.cards.assignments.overdue).toBe('Overdue / timed out');
  });

  it('summaryServerError_ShowsGenericLoadError', async () => {
    const el = await setup([ayse]);
    dashboardService.getChildSummary.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    (harness.routeDebugElement!.componentInstance as unknown as { retrySummary(): void }).retrySummary();
    await settle();
    expect(text(el, '[data-test="summary-error"]')).toContain(parentDashboardTr.summaryLoadError);
  });

  it('childrenLoadError_ShowsRetry', async () => {
    const el = await setup([ayse]);
    linkService.getMyChildren.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    (harness.routeDebugElement!.componentInstance as unknown as { loadChildren(): void }).loadChildren();
    await settle();
    expect(text(el, '[data-test="children-error"]')).toContain(parentDashboardTr.childrenLoadError);
  });

  it('english_RendersEnglishDictionary', async () => {
    const el = await setup([ayse], '/parent', 'en');
    expect(text(el, 'h1')).toBe(parentDashboardEn.title);
    expect(text(el, '[data-test="card-points"]')).toContain(parentDashboardEn.cards.points.title);
    expect(text(el, '[data-test="card-solved"]')).toContain('October 5');
  });

  it('assignmentsSection_FollowsTheSelectedChild', async () => {
    const el = await setup([ayse, mert]);
    expect(el.querySelector('[data-test="assignments-section"]')).not.toBeNull();
    expect(dashboardService.getChildAssignments).toHaveBeenCalledOnceWith(11, null, 1);

    dashboardService.getChildAssignments.calls.reset();
    (harness.routeDebugElement!.componentInstance as unknown as { selectChild(id: number): void }).selectChild(12);
    await settle();
    expect(dashboardService.getChildAssignments).toHaveBeenCalledOnceWith(12, null, 1);
  });

  it('noChildren_HasNoAssignmentsSection', async () => {
    const el = await setup([]);
    expect(el.querySelector('[data-test="assignments-section"]')).toBeNull();
    expect(dashboardService.getChildAssignments).not.toHaveBeenCalled();
  });

  it('assignments404_ShowsNoticeAndReloadsChildren', async () => {
    const el = await setup([ayse]);
    linkService.getMyChildren.and.returnValue(of([]));
    dashboardService.getChildAssignments.and.returnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    const section = harness.routeDebugElement!.query(
      (d) => d.name === 'app-parent-child-assignments'
    )!.componentInstance as unknown as { reload(): void };
    section.reload();
    await settle();

    expect(linkService.getMyChildren).toHaveBeenCalledTimes(2);
    expect(el.querySelector('[data-test="empty"]')).not.toBeNull();
  });

  it('loadChildren_WhileARefreshIsPending_DoesNotStartAnother', async () => {
    await setup([ayse]);
    const pendingChildren = new Subject<LinkedChild[]>();
    linkService.getMyChildren.and.returnValue(pendingChildren);
    const component = harness.routeDebugElement!.componentInstance as unknown as {
      loadChildren(): void;
      onChildNotFound(): void;
    };

    component.loadChildren();
    component.onChildNotFound(); // özet + ödev 404'ü aynı anda: ikinci yenileme açılmaz
    expect(linkService.getMyChildren).toHaveBeenCalledTimes(2);

    pendingChildren.next([ayse]);
    pendingChildren.complete();
    await settle();
    component.loadChildren();
    expect(linkService.getMyChildren).toHaveBeenCalledTimes(3);
  });

  it('dictionaries_HaveSameKeys', () => {
    const keys = (o: object, prefix = ''): string[] =>
      Object.entries(o).flatMap(([k, v]) =>
        v && typeof v === 'object' ? keys(v as object, `${prefix}${k}.`) : [`${prefix}${k}`]
      );
    expect(keys(parentDashboardEn).sort()).toEqual(keys(parentDashboardTr).sort());
  });
});
