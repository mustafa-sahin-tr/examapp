import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { WorksheetListViewCardComponent } from './worksheet-list-view-card.component';
import { ActivityLauncherService } from '../../services/activity-launcher.service';
import { Test, WorksheetTeacherSharing } from '../../models/test-instance';
import { AssignedWorksheet } from '../../models/assignment';

import { TranslocoTestingModule } from '@jsverse/transloco';
import { DEFAULT_LOCALE, SUPPORTED_LOCALE_CODES } from '../../models/locale';
import rootTr from '../../../../public/i18n/tr.json';
import worksheetListTr from '../../../../public/i18n/worksheet-list/tr.json';

/** Gercek scope sozlugu yuklenir; anahtar bozulursa test kirilir (issue #183). */
const translocoTesting = TranslocoTestingModule.forRoot({
    // Scope sozlugu hem scope yolu (provideTranslocoScope yukleyicisi) hem de kok 'tr' icine
    // gomulu olarak verilir; sablondaki 'prefix' bicimi ikincisinden cozulur.
    langs: { tr: { ...rootTr, 'worksheet-list': worksheetListTr }, 'worksheet-list/tr': worksheetListTr },
    translocoConfig: {
        availableLangs: [...SUPPORTED_LOCALE_CODES],
        defaultLang: DEFAULT_LOCALE,
        // TranslocoTestingModule uygulamanin config'ini almaz; scope oneki kebab kalsin (issue #183).
        scopes: { keepCasing: true },
    },
    preloadLangs: true,
});

describe('WorksheetListViewCardComponent', () => {
    let component: WorksheetListViewCardComponent;
    const launcher = { open: vi.fn() };

    beforeEach(() => {
        TestBed.configureTestingModule({
            imports: [WorksheetListViewCardComponent, translocoTesting],
            providers: [provideRouter([]), { provide: ActivityLauncherService, useValue: launcher }],
        });

        component = TestBed.createComponent(WorksheetListViewCardComponent).componentInstance;
    });

    function setCourse(partial: Partial<Test>): void {
        component.course = { id: 1, name: 'W', ...partial } as Test;
    }

    describe('canCopy', () => {
        it('canCopy_TeacherOnForeignPublicViewWorksheet_ReturnsTrue', () => {
            component.isTeacher = true;
            setCourse({ canEdit: false, isOwner: false, teacherSharing: WorksheetTeacherSharing.PublicView });

            expect(component.canCopy).toBe(true);
        });

        it('canCopy_TeacherOnForeignPublicAssignableWorksheet_ReturnsTrue', () => {
            component.isTeacher = true;
            setCourse({ canEdit: false, isOwner: false, teacherSharing: WorksheetTeacherSharing.PublicAssignable });

            expect(component.canCopy).toBe(true);
        });

        it('canCopy_TeacherOnForeignSchoolOnlyWorksheet_ReturnsTrue', () => {
            component.isTeacher = true;
            setCourse({ canEdit: false, isOwner: false, teacherSharing: WorksheetTeacherSharing.SchoolOnly });

            expect(component.canCopy).toBe(true);
        });

        it('canCopy_NotTeacher_ReturnsFalse', () => {
            component.isTeacher = false;
            setCourse({ canEdit: false, isOwner: false, teacherSharing: WorksheetTeacherSharing.PublicView });

            expect(component.canCopy).toBe(false);
        });

        it('canCopy_OwnWorksheet_ReturnsFalse', () => {
            component.isTeacher = true;
            setCourse({ canEdit: false, isOwner: true, teacherSharing: WorksheetTeacherSharing.PublicView });

            expect(component.canCopy).toBe(false);
        });

        it('canCopy_EditableWorksheet_ReturnsFalse', () => {
            component.isTeacher = true;
            setCourse({ canEdit: true, isOwner: false, teacherSharing: WorksheetTeacherSharing.PublicView });

            expect(component.canCopy).toBe(false);
        });

        it('canCopy_PrivateForeignWorksheet_ReturnsFalse', () => {
            component.isTeacher = true;
            setCourse({ canEdit: false, isOwner: false, teacherSharing: WorksheetTeacherSharing.Private });

            expect(component.canCopy).toBe(false);
        });
    });

    describe('emitCopy', () => {
        it('emitCopy_Called_StopsPropagationAndEmitsCourseId', () => {
            setCourse({ id: 55 });
            const emitted: number[] = [];
            component.copy.subscribe((id) => emitted.push(id));
            const event = {
                stopPropagation: vi.fn().mockName("Event.stopPropagation")
            };

            component.emitCopy(event as unknown as Event);

            expect(event.stopPropagation).toHaveBeenCalled();
            expect(emitted).toEqual([55]);
        });

        it('emitCopy_NoCourseId_DoesNotEmit', () => {
            component.course = { name: 'W' } as Test;
            const emitSpy = vi.fn().mockName('emit');
            component.copy.subscribe(emitSpy);
            const event = {
                stopPropagation: vi.fn().mockName("Event.stopPropagation")
            };

            component.emitCopy(event as unknown as Event);

            expect(event.stopPropagation).toHaveBeenCalled();
            expect(emitSpy).not.toHaveBeenCalled();
        });
    });

    // Issue #573: etkinlik kartı — puan/yüzde/süre yok, madde dili, başlat/devam doğrudan oynatıcıya.
    describe('activity (#573)', () => {
        const event = { stopPropagation: vi.fn() } as unknown as Event;

        beforeEach(() => launcher.open.mockClear());

        it('primary_NotCompletedActivity_OpensPlayerViaLauncher', () => {
            setCourse({ id: 31, kind: 1, instance: { status: 0 } as Test['instance'] });

            expect(component.primaryLabel).toBe('Devam et');
            component.openPrimary(event);

            expect(launcher.open).toHaveBeenCalledWith(31);
        });

        it('primary_NotStartedActivity_LabelStart', () => {
            setCourse({ id: 32, kind: 1 });

            expect(component.primaryLabel).toBe('Başla');
        });

        it('primary_CompletedActivity_GoesToDetailNotLauncher', () => {
            setCourse({ id: 33, kind: 1, instance: { status: 1 } as Test['instance'] });

            component.openPrimary(event);

            expect(component.primaryLabel).toBe('Detayı gör');
            expect(launcher.open).not.toHaveBeenCalled();
        });

        it('progress_UsesAnsweredQuestionsNotScoreFields', () => {
            setCourse({
                kind: 1,
                questionCount: 4,
                instance: { status: 0, answeredQuestions: 3, totalQuestions: 4, correctAnswers: 0, wrongAnswers: 0 } as Test['instance'],
            });

            expect(component.progressPercent).toBe(75);
        });

        it('template_CompletedActivity_NoScoreRingNoDuration', () => {
            const fixture = TestBed.createComponent(WorksheetListViewCardComponent);
            fixture.componentInstance.course = {
                id: 3,
                name: 'Biten',
                kind: 1,
                questionCount: 10,
                maxDurationSeconds: 0,
                instance: { status: 1, score: 0, correctAnswers: 0, wrongAnswers: 0 },
            } as unknown as Test;
            fixture.detectChanges();
            const el = fixture.nativeElement as HTMLElement;

            expect(el.querySelector('.wcard__ring')).toBeNull();
            expect(el.textContent).toContain('10 madde');
            expect(el.textContent).not.toContain('dk');
            expect(el.textContent).not.toMatch(/%|puan/i);
            expect(el.querySelector('[data-testid="activity-first-check"]')).toBeNull();
        });

        it('template_CompletedActivityWithFirstCheck_ShowsInfoLineWithoutPercent', () => {
            const fixture = TestBed.createComponent(WorksheetListViewCardComponent);
            fixture.componentInstance.course = {
                id: 3,
                name: 'Biten',
                kind: 1,
                questionCount: 10,
                instance: { status: 1, score: 0, firstCheckCorrectCount: 8, firstCheckTotalCount: 10 },
            } as unknown as Test;
            fixture.detectChanges();
            const el = fixture.nativeElement as HTMLElement;

            expect(el.querySelector('[data-testid="activity-first-check"]')?.textContent?.trim()).toBe('İlk kontrol: 8 / 10 doğru');
            expect(el.querySelector('.wcard__ring')).toBeNull();
            expect(el.textContent).not.toMatch(/%/);
        });

        it('template_OpenTextOnlyActivity_ZeroTotal_HidesInfoLine (#583)', () => {
            const fixture = TestBed.createComponent(WorksheetListViewCardComponent);
            fixture.componentInstance.course = {
                id: 5,
                name: 'Açık uçlu',
                kind: 1,
                questionCount: 2,
                instance: { status: 1, score: 0, firstCheckCorrectCount: 0, firstCheckTotalCount: 0 },
            } as unknown as Test;
            fixture.detectChanges();

            expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="activity-first-check"]')).toBeNull();
        });

        it('template_TestCard_NeverShowsActivityInfoLine', () => {
            const fixture = TestBed.createComponent(WorksheetListViewCardComponent);
            fixture.componentInstance.course = {
                id: 4,
                name: 'Test',
                kind: 0,
                instance: { status: 1, score: 50, correctAnswers: 1, wrongAnswers: 1, firstCheckCorrectCount: 1, firstCheckTotalCount: 2 },
            } as unknown as Test;
            fixture.detectChanges();

            expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="activity-first-check"]')).toBeNull();
        });
    
        it('template_RetakingActivity_ContinueOpensPlayerWithRetakingTag (#609)', () => {
            const fixture = TestBed.createComponent(WorksheetListViewCardComponent);
            fixture.componentInstance.course = {
                id: 35,
                name: 'Yeniden',
                kind: 1,
                questionCount: 4,
                instance: { status: 0, answeredQuestions: 0, totalQuestions: 4 },
            } as unknown as Test;
            fixture.componentInstance.assignment = {
                worksheetId: 35,
                instanceStatus: 0,
                assignmentStatus: 'Completed',
                isCompleted: true,
                hasStarted: true,
                retaking: true,
            } as unknown as AssignedWorksheet;
            fixture.detectChanges();
            const el = fixture.nativeElement as HTMLElement;

            expect(el.querySelector('[data-testid="card-retaking"]')?.textContent?.trim()).toBe('Yeniden çözüyor');
            expect(el.textContent).not.toContain('Devam ediyor');
            expect(el.textContent).not.toContain('Öğretmenin atadı');
            const primary = el.querySelector('.wcard__actions button') as HTMLButtonElement;
            expect(primary.textContent?.trim()).toBe('Devam et');

            primary.click();
            expect(launcher.open).toHaveBeenCalledWith(35);
        });

        it('template_UnassignedRetakingActivity_TagFromInstanceFirstCompletedAt (#609)', () => {
            const fixture = TestBed.createComponent(WorksheetListViewCardComponent);
            fixture.componentInstance.course = {
                id: 37,
                name: 'Keşfet',
                kind: 1,
                instance: { status: 0, firstCompletedAt: '2026-10-27T10:00:00Z' },
            } as unknown as Test;
            fixture.detectChanges();
            const el = fixture.nativeElement as HTMLElement;

            expect(el.querySelector('[data-testid="card-retaking"]')?.textContent?.trim()).toBe('Yeniden çözüyor');
            expect(el.textContent).not.toContain('Devam ediyor');
            (el.querySelector('.wcard__actions button') as HTMLButtonElement).click();
            expect(launcher.open).toHaveBeenCalledWith(37);
        });

        it('template_TestCardWithFirstCompletedAt_NoRetakingTag (#609)', () => {
            const fixture = TestBed.createComponent(WorksheetListViewCardComponent);
            fixture.componentInstance.course = {
                id: 38,
                name: 'Test',
                kind: 0,
                instance: { status: 0, firstCompletedAt: '2026-10-27T10:00:00Z' },
            } as unknown as Test;
            fixture.detectChanges();

            expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="card-retaking"]')).toBeNull();
        });

        it('template_InProgressActivityWithoutRetaking_ShowsInProgressTag (#609)', () => {
            const fixture = TestBed.createComponent(WorksheetListViewCardComponent);
            fixture.componentInstance.course = { id: 36, name: 'Devam', kind: 1, instance: { status: 0 } } as unknown as Test;
            fixture.componentInstance.assignment = { worksheetId: 36, isCompleted: false, retaking: false } as unknown as AssignedWorksheet;
            fixture.detectChanges();
            const el = fixture.nativeElement as HTMLElement;

            expect(el.querySelector('[data-testid="card-retaking"]')).toBeNull();
            expect(el.textContent).toContain('Devam ediyor');
        });
    });
});
