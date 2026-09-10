import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of } from 'rxjs';

import { StudentProfileComponent } from './student-profile.component';
import { StudentService } from '../../services/student.service';
import { TestService } from '../../services/test.service';
import { BadgeService } from '../../services/badge.service';
import { StudentStatisticsResponse } from '../../models/statistics';
import { StudentProfile } from '../../models/student-profile';
import { BadgeThropyComponent } from '../../shared/components/badge-thropy/badge-thropy.component';

describe('StudentProfileComponent', () => {
  let component: StudentProfileComponent;
  let fixture: ComponentFixture<StudentProfileComponent>;
  let studentService: jasmine.SpyObj<StudentService>;
  let testService: jasmine.SpyObj<TestService>;
  let badgeService: jasmine.SpyObj<BadgeService>;

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
    studentService = jasmine.createSpyObj<StudentService>('StudentService', [
      'loadGrades',
      'getProfile',
      'updateGrade',
      'updateAvatar',
    ]);
    studentService.loadGrades.and.returnValue(of([]));
    studentService.getProfile.and.returnValue(of({} as StudentProfile));

    testService = jasmine.createSpyObj<TestService>('TestService', ['studentStatistics']);
    testService.studentStatistics.and.returnValue(of(emptyStatistics));

    badgeService = jasmine.createSpyObj<BadgeService>('BadgeService', ['getUserActivity', 'getUserBadgeProgress']);
    badgeService.getUserActivity.and.returnValue(
      of({ userId: 1, startDateUtc: '', endDateUtc: '', days: [] })
    );
    badgeService.getUserBadgeProgress.and.returnValue(
      of({
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
      })
    );

    await TestBed.configureTestingModule({
      imports: [StudentProfileComponent, NoopAnimationsModule],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: StudentService, useValue: studentService },
        { provide: TestService, useValue: testService },
        { provide: BadgeService, useValue: badgeService },
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']) },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({}), data: {} }, params: of({}), queryParams: of({}) },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(StudentProfileComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  // "Badges" is the third mat-tab (index 2); Angular Material lazily renders
  // tab content, so it must be selected before app-badge-thropy appears in the DOM.
  function activateBadgesTab(): void {
    component.activeTab = 2;
    fixture.detectChanges();
    tick(500); // mat-tab-group lazy-renders body content after the tab switch animation
    fixture.detectChanges();
  }

  it('activeTab_SetToBadgesTab_RendersBadgeThropyComponentInsteadOfMockBadgeBox', fakeAsync(() => {
    activateBadgesTab();

    const badgeThropyDebugElements = fixture.debugElement.nativeElement.querySelectorAll('app-badge-thropy');
    expect(badgeThropyDebugElements.length).toBeGreaterThan(0);
    expect(fixture.debugElement.nativeElement.querySelector('app-badge-box')).toBeNull();
  }));

  it('activeTab_SetToBadgesTab_RendersOnlyOneBadgeThropyInstance_NoDuplicateInInfoTab', fakeAsync(() => {
    activateBadgesTab();

    const badgeThropyInstances = fixture.debugElement.nativeElement.querySelectorAll('app-badge-thropy');
    expect(badgeThropyInstances.length).toBe(1);
  }));

  it('activeTab_SetToBadgesTab_PassesResolvedStudentIdToBadgeThropy', fakeAsync(() => {
    activateBadgesTab();

    const badgeThropyDebugElement = fixture.debugElement.query((debugEl) => {
      return debugEl.componentInstance instanceof BadgeThropyComponent;
    });

    expect(badgeThropyDebugElement).toBeTruthy();
    const badgeThropyComponent = badgeThropyDebugElement.componentInstance as BadgeThropyComponent;
    expect(badgeThropyComponent.userId).toBe(component.studentId ?? 0);
  }));
});
