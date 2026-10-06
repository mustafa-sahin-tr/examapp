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

import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import studentProfileTr from '../../../../public/i18n/student-profile/tr.json';

/**
 * Sayfa cevirileri kendi Transloco scope'undadir (issue #183); testte gercek sozluk verilir,
 * sahte ceviri kullanilmaz - boylece bir anahtar bozulursa test kirilir.
 */
const translocoTesting = translocoTestingModule({ langs: { 'student-profile/tr': studentProfileTr } });

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
      imports: [StudentProfileComponent, NoopAnimationsModule, translocoTesting],
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

  // #149 CR Ö1: sayfanın "Rozetlerim" bölüm başlığı varken bileşen kendi başlığını tekrar etmez.
  it('activeTab_SetToBadgesTab_BadgeThropyRendersWithoutOwnHeading', fakeAsync(() => {
    activateBadgesTab();

    const thropy = fixture.debugElement.query((debugEl) => debugEl.componentInstance instanceof BadgeThropyComponent);
    expect((thropy.componentInstance as BadgeThropyComponent).showHeading).toBeFalse();
    expect((thropy.nativeElement as HTMLElement).querySelector('.ms-badge-title')).toBeNull();
  }));

  // #322: "Bilgi" sekmesindeki eski `student.badges[].imageUrl` blogu kaldirildi; rozetler yalnizca
  // "Rozetler" sekmesindeki app-badge-thropy ile (tek bolum) gosterilir.
  it('activeTab_SetToInfoTab_DoesNotRenderLegacyBadgeImageBlock', fakeAsync(() => {
    studentService.getProfile.and.returnValue(of({ fullName: 'Ada Lovelace', gradeId: 5 } as StudentProfile));
    fixture.detectChanges();
    component.activeTab = 1;
    fixture.detectChanges();
    tick(500);
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain('Ada Lovelace');
    expect(host.querySelector('.badges-container')).toBeNull();
    expect(host.querySelector('img.badge')).toBeNull();
    expect(host.textContent).not.toContain('Kazanılan Rozetler');
    expect(host.querySelectorAll('app-badge-thropy').length).toBe(0);
  }));

  describe('report user id (auth user id, not StudentProfileDto.id)', () => {
    let previousUser: string | null;

    beforeEach(() => {
      previousUser = localStorage.getItem('user');
      localStorage.setItem('user', JSON.stringify({ id: 7 }));
    });

    afterEach(() => {
      if (previousUser === null) {
        localStorage.removeItem('user');
      } else {
        localStorage.setItem('user', previousUser);
      }
    });

    // API (StudentProfileDto.Id) ogrenci KAYIT id'sini de gonderir; frontend modelinde alan yok, gercek yuku taklit ediyoruz.
    const profileWithStudentRecordId3 = { id: 3, fullName: 'Ada' } as unknown as StudentProfile;

    function findBadgeThropy(): BadgeThropyComponent {
      const debugEl = fixture.debugElement.query((el) => el.componentInstance instanceof BadgeThropyComponent);
      expect(debugEl).toBeTruthy();
      return debugEl.componentInstance as BadgeThropyComponent;
    }

    it('getProfile_ReturnsStudentRecordId3_DoesNotOverrideStoredUserId7', fakeAsync(() => {
      studentService.getProfile.and.returnValue(of(profileWithStudentRecordId3));

      fixture.detectChanges();

      expect(component.reportUserId).toBe(7);
      expect(badgeService.getUserActivity).toHaveBeenCalledOnceWith(7);
      expect(badgeService.getUserActivity).not.toHaveBeenCalledWith(3);
    }));

    it('activeTab_SetToBadgesTab_PassesStoredUserIdToBadgeThropy', fakeAsync(() => {
      studentService.getProfile.and.returnValue(of(profileWithStudentRecordId3));

      activateBadgesTab();

      expect(findBadgeThropy().userId).toBe(7);
      expect(badgeService.getUserBadgeProgress).toHaveBeenCalledWith(7);
      expect(badgeService.getUserBadgeProgress).not.toHaveBeenCalledWith(3);
    }));

    it('noStoredUser_DoesNotRequestActivityReport', () => {
      localStorage.removeItem('user');

      fixture.detectChanges();

      expect(component.reportUserId).toBeNull();
      expect(badgeService.getUserActivity).not.toHaveBeenCalled();
    });
  });
});
