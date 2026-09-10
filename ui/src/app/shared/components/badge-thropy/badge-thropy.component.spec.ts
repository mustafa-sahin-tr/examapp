import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';

import { BadgeThropyComponent } from './badge-thropy.component';
import { BadgeProgressItem, BadgeProgressResponse, BadgeService } from '../../../services/badge.service';
import { AuthService } from '../../../services/auth.service';

describe('BadgeThropyComponent', () => {
  let component: BadgeThropyComponent;
  let fixture: ComponentFixture<BadgeThropyComponent>;
  let badgeServiceSpy: jasmine.SpyObj<BadgeService>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;

  function buildBadgeItem(overrides: Partial<BadgeProgressItem> = {}): BadgeProgressItem {
    return {
      badgeDefinitionId: 'badge-1',
      name: 'Hızlı Başlangıç',
      description: 'İlk 5 dakikalık çalışma tamamlandı.',
      iconUrl: 'achievements/badge-1.svg',
      pathKey: null,
      pathName: null,
      pathOrder: null,
      currentValue: 5,
      targetValue: 5,
      isCompleted: true,
      earnedDateUtc: new Date().toISOString(),
      ...overrides,
    };
  }

  function buildResponse(badgeProgress: BadgeProgressItem[]): BadgeProgressResponse {
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
      badgeProgress,
      subjectBreakdown: [],
    };
  }

  beforeEach(async () => {
    badgeServiceSpy = jasmine.createSpyObj('BadgeService', ['getUserBadgeProgress']);
    authServiceSpy = jasmine.createSpyObj('AuthService', ['getUserIdFromLocalStorage']);
    authServiceSpy.getUserIdFromLocalStorage.and.returnValue(null);

    await TestBed.configureTestingModule({
      imports: [BadgeThropyComponent],
      providers: [
        { provide: BadgeService, useValue: badgeServiceSpy },
        { provide: AuthService, useValue: authServiceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(BadgeThropyComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(buildResponse([])));
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('ngOnInit_WhileRequestPending_ShowsLoadingSpinner', () => {
    // A Subject that never emits lets us assert the loading state before resolution.
    const pending = new Subject<BadgeProgressResponse>();
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(pending.asObservable());

    fixture.detectChanges();

    expect(component.isLoading()).toBeTrue();
    const compiled: HTMLElement = fixture.nativeElement;
    expect(compiled.querySelector('mat-spinner')).toBeTruthy();
    expect(compiled.textContent).toContain('Rozetler yükleniyor');
  });

  it('loadBadges_ApiReturnsEmptyList_ShowsEmptyState', () => {
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(buildResponse([])));

    fixture.detectChanges();

    expect(component.isLoading()).toBeFalse();
    expect(component.loadError()).toBeFalse();
    expect(component.badgePaths().length).toBe(0);
    expect(component.standaloneBadges().length).toBe(0);

    const compiled: HTMLElement = fixture.nativeElement;
    expect(compiled.textContent).toContain('Henüz kazanılmış veya takip edilen bir rozet yok.');
  });

  it('loadBadges_ApiFails_ShowsErrorStateWithRetryButton', () => {
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(throwError(() => new Error('network error')));

    fixture.detectChanges();

    expect(component.isLoading()).toBeFalse();
    expect(component.loadError()).toBeTrue();
    expect(component.badges().length).toBe(0);

    const compiled: HTMLElement = fixture.nativeElement;
    expect(compiled.textContent).toContain('Rozet bilgisi alınırken bir hata oluştu.');
    const retryButton = compiled.querySelector('button');
    expect(retryButton).toBeTruthy();
    expect(retryButton?.textContent).toContain('Tekrar dene');
  });

  it('reload_AfterPreviousFailure_ClearsErrorAndRefetches', () => {
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(throwError(() => new Error('network error')));
    fixture.detectChanges();
    expect(component.loadError()).toBeTrue();

    badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(buildResponse([buildBadgeItem()])));
    component.reload();

    expect(component.loadError()).toBeFalse();
    expect(badgeServiceSpy.getUserBadgeProgress).toHaveBeenCalledTimes(2);
    expect(component.standaloneBadges().length).toBe(1);
  });

  it('loadBadges_ResponseHasStandaloneBadge_MapsPathAndProgressCorrectly', () => {
    const badge = buildBadgeItem({
      pathKey: null,
      currentValue: 3,
      targetValue: 5,
      isCompleted: false,
    });
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(buildResponse([badge])));

    fixture.detectChanges();

    expect(component.badgePaths().length).toBe(0);
    expect(component.standaloneBadges().length).toBe(1);
    const mapped = component.standaloneBadges()[0];
    expect(mapped.pathKey).toBeNull();
    expect(mapped.progressPercent).toBe(60);
    expect(mapped.isCompleted).toBeFalse();
  });

  it('loadBadges_ResponseHasPathBadges_GroupsIntoBadgePath', () => {
    const badges = [
      buildBadgeItem({
        badgeDefinitionId: 'badge-path-1',
        pathKey: 'correct-streak',
        pathName: 'Doğru Yolu Bul',
        pathOrder: 1,
        currentValue: 5,
        targetValue: 5,
        isCompleted: true,
      }),
      buildBadgeItem({
        badgeDefinitionId: 'badge-path-2',
        pathKey: 'correct-streak',
        pathName: 'Doğru Yolu Bul',
        pathOrder: 2,
        currentValue: 38,
        targetValue: 50,
        isCompleted: false,
      }),
    ];
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(buildResponse(badges)));

    fixture.detectChanges();

    expect(component.standaloneBadges().length).toBe(0);
    expect(component.badgePaths().length).toBe(1);
    const path = component.badgePaths()[0];
    expect(path.key).toBe('correct-streak');
    expect(path.badges.length).toBe(2);
    expect(path.completedCount).toBe(1);

    const compiled: HTMLElement = fixture.nativeElement;
    expect(compiled.querySelector('app-badge-path')).toBeTruthy();
  });

  it('loadBadges_UsesInputUserIdOverStoredWhenBothAvailable', () => {
    authServiceSpy.getUserIdFromLocalStorage.and.returnValue(42);
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(buildResponse([])));

    component.userId = 7;
    fixture.detectChanges();

    expect(badgeServiceSpy.getUserBadgeProgress).toHaveBeenCalledWith(7);
  });

  it('loadBadges_InputUserIdMissing_FallsBackToStoredUserId', () => {
    authServiceSpy.getUserIdFromLocalStorage.and.returnValue(42);
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(buildResponse([])));

    component.userId = 0;
    fixture.detectChanges();

    expect(badgeServiceSpy.getUserBadgeProgress).toHaveBeenCalledWith(42);
  });

  it('loadBadges_NeitherInputNorStoredUserId_FallsBackToZero', () => {
    authServiceSpy.getUserIdFromLocalStorage.and.returnValue(null);
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(buildResponse([])));

    component.userId = 0;
    fixture.detectChanges();

    expect(badgeServiceSpy.getUserBadgeProgress).toHaveBeenCalledWith(0);
  });
});
