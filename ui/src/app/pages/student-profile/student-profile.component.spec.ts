import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';

import { StudentProfileComponent } from './student-profile.component';
import { StudentService } from '../../services/student.service';
import { TestService } from '../../services/test.service';
import { BadgeService, BadgeProgressResponse, UserActivityResponse } from '../../services/badge.service';
import { AuthService } from '../../services/auth.service';
import { StudentProfile } from '../../models/student-profile';
import { Grade } from '../../models/student';
import { StudentStatisticsResponse } from '../../models/statistics';

function emptyActivityResponse(): UserActivityResponse {
  return {
    userId: 16,
    startDateUtc: '2026-01-01T00:00:00Z',
    endDateUtc: '2026-01-31T00:00:00Z',
    days: [],
  };
}

function emptyBadgeProgressResponse(): BadgeProgressResponse {
  return {
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
  };
}

function emptyStatistics(): StudentStatisticsResponse {
  return {
    total: {
      totalSolvedTests: 0,
      completedTests: 0,
      totalTimeSpentMinutes: 0,
      totalCorrectAnswers: 0,
      totalWrongAnswers: 0,
    },
    grouped: [],
  };
}

describe('StudentProfileComponent', () => {
  let component: StudentProfileComponent;
  let fixture: ComponentFixture<StudentProfileComponent>;
  let studentServiceSpy: jasmine.SpyObj<StudentService>;
  let testServiceSpy: jasmine.SpyObj<TestService>;
  let badgeServiceSpy: jasmine.SpyObj<BadgeService>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;

  beforeEach(async () => {
    studentServiceSpy = jasmine.createSpyObj<StudentService>('StudentService', [
      'loadGrades',
      'getProfile',
      'updateGrade',
      'updateAvatar',
    ]);
    testServiceSpy = jasmine.createSpyObj<TestService>('TestService', ['studentStatistics']);
    badgeServiceSpy = jasmine.createSpyObj<BadgeService>('BadgeService', ['getUserActivity', 'getUserBadgeProgress']);
    authServiceSpy = jasmine.createSpyObj<AuthService>('AuthService', ['getUserIdFromLocalStorage']);
    authServiceSpy.getUserIdFromLocalStorage.and.returnValue(null);

    studentServiceSpy.loadGrades.and.returnValue(of([] as Grade[]));
    studentServiceSpy.getProfile.and.returnValue(of({} as StudentProfile));
    testServiceSpy.studentStatistics.and.returnValue(of(emptyStatistics()));
    badgeServiceSpy.getUserActivity.and.returnValue(of(emptyActivityResponse()));
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(emptyBadgeProgressResponse()));

    await TestBed.configureTestingModule({
      imports: [StudentProfileComponent, HttpClientTestingModule],
      providers: [
        { provide: StudentService, useValue: studentServiceSpy },
        { provide: TestService, useValue: testServiceSpy },
        { provide: BadgeService, useValue: badgeServiceSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: new Map() } } },
        { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']) },
        provideNoopAnimations(),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(StudentProfileComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('ngOnInit_GetUserActivitySucceeds_ActivityApiErrorStaysFalse', () => {
    fixture.detectChanges();

    expect(component.activityApiError).toBeFalse();
  });

  it('ngOnInit_GetUserActivityFails_ActivityApiErrorBecomesTrueAndActivityDataIsEmpty', () => {
    badgeServiceSpy.getUserActivity.and.returnValue(throwError(() => new Error('boom')));

    expect(() => fixture.detectChanges()).not.toThrow();

    expect(component.activityApiError).toBeTrue();
    expect(component.activityDataFromApi).toEqual([]);
  });
});
