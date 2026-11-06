import type { MockedObject } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoService } from '@jsverse/transloco';
import { of } from 'rxjs';

import { StudentProfileComponent } from './student-profile.component';
import { StudentService } from '../../services/student.service';
import { TestService } from '../../services/test.service';
import { BadgeService } from '../../services/badge.service';
import { StudentStatisticsResponse } from '../../models/statistics';
import { StudentProfile } from '../../models/student-profile';

import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import studentProfileTr from '../../../../public/i18n/student-profile/tr.json';
import studentProfileEn from '../../../../public/i18n/student-profile/en.json';
import accountSecurityTr from '../../../../public/i18n/account-security/tr.json';
import accountSecurityEn from '../../../../public/i18n/account-security/en.json';
import parentLinksTr from '../../../../public/i18n/parent-links/tr.json';
import parentLinksEn from '../../../../public/i18n/parent-links/en.json';
import { ParentLinkService } from '../../services/parent-link.service';

/**
 * Sayfa cevirileri kendi Transloco scope'undadir (issue #183); testte gercek sozluk verilir,
 * sahte ceviri kullanilmaz - boylece bir anahtar bozulursa test kirilir.
 */
const translocoTesting = translocoTestingModule({
    langs: {
        'student-profile/tr': studentProfileTr,
        'student-profile/en': studentProfileEn,
        'account-security/tr': accountSecurityTr,
        'account-security/en': accountSecurityEn,
        'parent-links/tr': parentLinksTr,
        'parent-links/en': parentLinksEn,
    },
});

describe('StudentProfileComponent', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });
    afterEach(() => {
        vi.useRealTimers();
    });
    let component: StudentProfileComponent;
    let fixture: ComponentFixture<StudentProfileComponent>;
    let studentService: MockedObject<StudentService>;
    let testService: MockedObject<TestService>;
    let badgeService: MockedObject<BadgeService>;

    const emptyStatistics: StudentStatisticsResponse = {
        total: {
            totalSolvedTests: 0,
            completedTests: 0,
            totalTimeSpentMinutes: 0,
            totalCorrectAnswers: 0,
            totalWrongAnswers: 0,
        },
        grouped: [],
    };

    beforeEach(async () => {
        studentService = {
            loadGrades: vi.fn().mockName("StudentService.loadGrades"),
            getProfile: vi.fn().mockName("StudentService.getProfile"),
            updateGrade: vi.fn().mockName("StudentService.updateGrade"),
            updateAvatar: vi.fn().mockName("StudentService.updateAvatar")
        } as unknown as MockedObject<StudentService>;
        studentService.loadGrades.mockReturnValue(of([]));
        studentService.getProfile.mockReturnValue(of({} as StudentProfile));

        testService = {
            studentStatistics: vi.fn().mockName("TestService.studentStatistics")
        } as unknown as MockedObject<TestService>;
        testService.studentStatistics.mockReturnValue(of(emptyStatistics));

        badgeService = {
            getUserActivity: vi.fn().mockName("BadgeService.getUserActivity"),
            getUserBadgeProgress: vi.fn().mockName("BadgeService.getUserBadgeProgress")
        } as unknown as MockedObject<BadgeService>;
        badgeService.getUserActivity.mockReturnValue(of({ userId: 1, startDateUtc: '', endDateUtc: '', days: [] }));
        badgeService.getUserBadgeProgress.mockReturnValue(of({
            summary: {
                userId: 1,
                totalQuestions: 0,
                correctQuestions: 0,
                accuracyPercentage: 0,
                totalPoints: 0,
                currentCorrectStreak: 0,
                bestCorrectStreak: 0,
                totalTimeSeconds: 0,
                totalActiveDays: 0,
                currentActivityStreak: 0,
                bestActivityStreak: 0,
                lastAnsweredAtUtc: null,
                lastUpdatedUtc: null,
            },
            badgeProgress: [],
            subjectBreakdown: [],
        }));

        await TestBed.configureTestingModule({
            imports: [StudentProfileComponent, NoopAnimationsModule, translocoTesting],
            providers: [
                provideHttpClient(withXhr()),
                provideHttpClientTesting(),
                { provide: StudentService, useValue: studentService },
                { provide: TestService, useValue: testService },
                { provide: BadgeService, useValue: badgeService },
                // Issue #419: veli davet kartı kendi servisini çağırır; profil testlerinde boş liste.
                {
                    provide: ParentLinkService,
                    useValue: {
                        getMyParents: () => of({ items: [], pendingRequests: [], maxActiveParents: 4, requiresParent: true }),
                        extractError: (_: unknown, fallback: string) => fallback,
                        errorCode: () => null,
                    },
                },
                provideRouter([]),
                { provide: MatSnackBar, useValue: {
                        open: vi.fn().mockName("MatSnackBar.open")
                    } },
                {
                    provide: ActivatedRoute,
                    useValue: { snapshot: { paramMap: convertToParamMap({}), data: {} }, params: of({}), queryParams: of({}) },
                },
            ],
        }).compileComponents();

        fixture = TestBed.createComponent(StudentProfileComponent);
        component = fixture.componentInstance;
    });

    function render(profile: Partial<StudentProfile>, summary?: Partial<{ totalQuestions: number; correctQuestions: number }>): HTMLElement {
        localStorage.setItem('user', JSON.stringify({ id: 4 }));
        studentService.getProfile.mockReturnValue(of(profile as StudentProfile));
        studentService.loadGrades.mockReturnValue(of([{ id: 22, name: '5. Sınıf' }]));
        if (summary) {
            badgeService.getUserBadgeProgress.mockReturnValue(of({
                summary: { ...summary } as never,
                badgeProgress: [],
                subjectBreakdown: [],
            }));
        }
        fixture.detectChanges();
        return fixture.nativeElement as HTMLElement;
    }

    afterEach(() => localStorage.removeItem('user'));

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    it('head_ShowsInitialsNameClassSchoolAndLevel (#847)', () => {
        const el = render({ fullName: 'Pilot Öğrenci', gradeId: 22, schoolName: 'Yıldızlar Okulu', level: 3, xp: 261, avatarUrl: '' });

        expect(el.querySelector('.sp__avatar-initials')?.textContent?.trim()).toBe('PÖ');
        expect(el.querySelector('h1')?.textContent).toContain('Pilot Öğrenci');
        expect(el.querySelector('[data-testid="profile-subtitle"]')?.textContent?.trim()).toBe('5. Sınıf · Yıldızlar Okulu');
        expect(el.querySelector('[data-testid="profile-level"]')?.textContent?.trim()).toBe('Seviye 3 · 261 puan');
        // Sınıf seçimi artık profilde değil, Ayarlar'da açık bir eylem.
        expect(el.querySelector('mat-select')).toBeNull();
        expect(el.querySelector('[data-testid="profile-settings-link"]')?.getAttribute('href')).toBe('/student-settings');
    });

    it('stats_SolvedAndCorrectComeFromDashboardSource_TestsFromProfile (#847)', () => {
        const el = render({ fullName: 'A', gradeId: 22, testsCompleted: 1, totalQuestionsSolved: 3, correctAnswers: 0 }, { totalQuestions: 25, correctQuestions: 2 });

        const value = (key: string) => el.querySelector(`[data-key="${key}"] .sp__stat-value`)?.textContent?.trim();
        expect(badgeService.getUserBadgeProgress).toHaveBeenCalledWith(4);
        expect(value('solved')).toBe('25');
        expect(value('correct')).toBe('2');
        expect(value('tests')).toBe('1');
    });

    it('noTabs_BadgesAndThemeLiveElsewhere_CardsAndLeaderboardStay (#847)', () => {
        const el = render({ fullName: 'A', gradeId: 22 });

        expect(el.querySelector('mat-tab-group')).toBeNull();
        expect(el.querySelector('app-badge-thropy')).toBeNull();
        expect(el.querySelector('app-user-theme-switcher')).toBeNull();
        for (const card of ['app-target-goal-card', 'app-weak-topics-card', 'app-topic-progress-card', 'app-outcome-mastery-card', 'app-leaderboard']) {
            expect(el.querySelector(card), card).not.toBeNull();
        }
    });

    it('allKeysExistInBothLanguages', () => {
        const transloco = TestBed.inject(TranslocoService);
        for (const key of ['profile.level', 'profile.settings', 'stats.solved', 'stats.correct', 'stats.tests', 'settings.grade.note']) {
            for (const lang of ['tr', 'en']) {
                const full = `student-profile.${key}`;
                expect(transloco.translate(full, {}, lang), `${lang}:${key}`).not.toBe(full);
            }
        }
    });
});
