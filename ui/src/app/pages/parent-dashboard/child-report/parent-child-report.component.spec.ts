import type { MockedObject } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoService } from '@jsverse/transloco';
import { of, throwError } from 'rxjs';

import { MAX_REPORT_WEEKS, ParentChildReportComponent, shiftDay } from './parent-child-report.component';
import { ParentDashboardService } from '../../../services/parent-dashboard.service';
import { LocaleService } from '../../../services/locale.service';
import { ParentWeeklyReport } from '../../../models/parent-dashboard.model';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import parentDashboardTr from '../../../../../public/i18n/parent-dashboard/tr.json';
import parentDashboardEn from '../../../../../public/i18n/parent-dashboard/en.json';

@Component({
    standalone: true,
    imports: [ParentChildReportComponent],
    changeDetection: ChangeDetectionStrategy.Eager,
    template: `<app-parent-child-report [studentId]="studentId()" (notFound)="notFound = notFound + 1" (openPlan)="openPlan = openPlan + 1" />`,
})
class HostComponent {
    readonly studentId = signal(11);
    notFound = 0;
    openPlan = 0;
}

/** Issue #1112: veli "Rapor" sekmesi — son biten hafta, 12 haftalık gezinme, öneri, kilit (403), 404, boş hafta. */
describe('ParentChildReportComponent (issue #1112)', () => {
    let service: MockedObject<ParentDashboardService>;
    let fixture: ComponentFixture<HostComponent>;

    function report(overrides: Partial<ParentWeeklyReport> = {}): ParentWeeklyReport {
        return {
            studentId: 11,
            weekStart: '2026-11-02',
            questionsSolved: 40,
            correct: 30,
            activeDays: 4,
            studyMinutes: 95,
            points: 120,
            subjects: [{ subjectId: 1, subjectName: 'Matematik', answered: 40, correct: 30, accuracyPercent: 75 }],
            droppedTopics: [{ topicId: 2, topicName: 'Kesirler', subjectName: 'Matematik', accuracyPercent: 40, previousAccuracyPercent: 70 }],
            assignmentsCompleted: 2,
            assignmentsOverdue: 1,
            mockExams: [{ name: 'Deneme 1', net: 31.25, rank: 3, takers: 20, finishedAtUtc: '2026-11-04T10:00:00Z' }],
            badges: [{ name: 'İlk adım', icon: 'star' }],
            suggestion: 'reviewTopic',
            suggestionTopic: 'Kesirler',
            ...overrides,
        };
    }

    async function setup(impl: (id: number, week?: string) => unknown = (_, week) => of(report(week ? { weekStart: week } : {}))) {
        service = {
            getChildWeeklyReport: vi.fn().mockName('getChildWeeklyReport').mockImplementation(impl),
        } as unknown as MockedObject<ParentDashboardService>;
        TestBed.configureTestingModule({
            imports: [
                HostComponent,
                NoopAnimationsModule,
                translocoTestingModule({ langs: { 'parent-dashboard/tr': parentDashboardTr, 'parent-dashboard/en': parentDashboardEn } }),
            ],
            providers: [
                { provide: ParentDashboardService, useValue: service },
                { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: 'tr-TR' }) } },
            ],
        });
        TestBed.inject(TranslocoService).setActiveLang('tr');
        fixture = TestBed.createComponent(HostComponent);
        await settle();
        return fixture.nativeElement as HTMLElement;
    }

    async function settle(): Promise<void> {
        fixture.detectChanges();
        await fixture.whenStable();
        fixture.detectChanges();
    }

    const text = (el: Element | null) => el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
    const q = (el: HTMLElement, test: string) => el.querySelector(`[data-test="${test}"]`);
    const button = (el: HTMLElement, test: string) => q(el, test) as HTMLButtonElement;

    it('firstLoad_AsksForTheLastCompletedWeek_AndShowsNumbersAndSuggestion', async () => {
        const el = await setup();
        expect(service.getChildWeeklyReport).toHaveBeenCalledWith(11, undefined);
        expect(text(q(el, 'report-range'))).toBe('2 Kasım – 8 Kasım 2026');
        expect(text(q(el, 'report-suggestion'))).toContain('Kesirler konusunu tekrar etmesi iyi olur.');
        expect(text(q(el, 'stat-solved'))).toContain('40');
        expect(text(q(el, 'stat-accuracy'))).toContain('%75');
        expect(text(q(el, 'stat-minutes'))).toContain('95 dk');
        expect(text(q(el, 'stat-assignments'))).toContain('2 bitti · 1 gecikmiş');
        expect(text(q(el, 'report-dropped'))).toContain('%70 → %40');
        expect(text(q(el, 'report-mock'))).toContain('31,25 net · 3/20. sıra');
        expect(text(q(el, 'report-badge'))).toContain('İlk adım');
        // En yeni hafta: ileri kapalı, geri açık.
        expect(button(el, 'report-next').disabled).toBe(true);
        expect(button(el, 'report-prev').disabled).toBe(false);
    });

    it('previousWeek_RequestsTheMondayBefore_UpToTwelveWeeks', async () => {
        const el = await setup();
        button(el, 'report-prev').click();
        await settle();
        expect(service.getChildWeeklyReport).toHaveBeenLastCalledWith(11, '2026-10-26');
        expect(button(el, 'report-next').disabled).toBe(false);

        for (let i = 2; i < MAX_REPORT_WEEKS; i++) {
            button(el, 'report-prev').click();
            await settle();
        }
        expect(service.getChildWeeklyReport).toHaveBeenLastCalledWith(11, shiftDay('2026-11-02', -7 * (MAX_REPORT_WEEKS - 1)));
        expect(button(el, 'report-prev').disabled).toBe(true);
    });

    it('notInPlan_ShowsLockWithPlanButton', async () => {
        const el = await setup(() => throwError(() => new HttpErrorResponse({ status: 403 })));
        expect(text(q(el, 'report-locked'))).toContain('Essential ve Pro');
        expect(q(el, 'report-prev')).toBeNull();
        button(el, 'report-open-plan').click();
        expect(fixture.componentInstance.openPlan).toBe(1);
    });

    it('notFound_ShowsErrorAndNotifiesParent', async () => {
        const el = await setup(() => throwError(() => new HttpErrorResponse({ status: 404 })));
        expect(q(el, 'report-error')).not.toBeNull();
        expect(fixture.componentInstance.notFound).toBe(1);
    });

    it('quietWeek_ShowsNoActivitySuggestionAndNoCards', async () => {
        const el = await setup(() => of(report({
            questionsSolved: 0, correct: 0, activeDays: 0, subjects: [], droppedTopics: [], mockExams: [], badges: [],
            suggestion: 'noActivity', suggestionTopic: null,
        })));
        expect(text(q(el, 'report-suggestion'))).toContain('hiç soru çözmedi');
        expect(text(q(el, 'stat-accuracy'))).toContain('–');
        expect(q(el, 'report-subject')).toBeNull();
    });

    it('childChange_ReturnsToTheLastCompletedWeek', async () => {
        const el = await setup();
        button(el, 'report-prev').click();
        await settle();
        fixture.componentInstance.studentId.set(12);
        await settle();
        expect(service.getChildWeeklyReport).toHaveBeenLastCalledWith(12, undefined);
    });
});
