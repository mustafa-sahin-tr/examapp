import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';

import { BadgeThropyComponent } from './badge-thropy.component';
import { BadgeProgressItem, BadgeProgressResponse, BadgeService } from '../../../services/badge.service';
import { AuthService } from '../../../services/auth.service';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { LocaleService } from '../../../services/locale.service';
import { localeDefinitionOf } from '../../../models/locale';

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
      imports: [translocoTestingModule(), BadgeThropyComponent],
      providers: [
        { provide: BadgeService, useValue: badgeServiceSpy },
        { provide: AuthService, useValue: authServiceSpy },
        {
          provide: LocaleService,
          useValue: { locale: signal('tr').asReadonly(), localeDefinition: signal(localeDefinitionOf('tr')).asReadonly() },
        },
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

  // ===== Issue #149 =====
  const DAY = 24 * 60 * 60 * 1000;

  it('mapBadge_DerivesMedallionState_NewLockedInProgressEarned', () => {
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(
      of(
        buildResponse([
          buildBadgeItem({ badgeDefinitionId: 'a', name: 'A', earnedDateUtc: new Date(Date.now() - DAY).toISOString() }),
          buildBadgeItem({ badgeDefinitionId: 'b', name: 'B', earnedDateUtc: new Date(Date.now() - 30 * DAY).toISOString() }),
          buildBadgeItem({ badgeDefinitionId: 'c', name: 'C', isCompleted: false, currentValue: 0, earnedDateUtc: null }),
          buildBadgeItem({ badgeDefinitionId: 'd', name: 'D', isCompleted: false, currentValue: 2, earnedDateUtc: null }),
        ])
      )
    );

    fixture.detectChanges();

    expect(component.standaloneBadges().map((b) => [b.id, b.state])).toEqual([
      ['a', 'new'],
      ['b', 'earned'],
      ['c', 'locked'],
      ['d', 'in-progress'],
    ]);
    expect(component.standaloneBadges()[3].remainingLabel).toBe('3');
    expect(component.earnedCount()).toBe(2);
  });

  it('mapBadge_CarriesIconField_AndOldServerWithoutIconIsNull', () => {
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(
      of(buildResponse([buildBadgeItem({ badgeDefinitionId: 'x', icon: 'gps_fixed' }), buildBadgeItem({ badgeDefinitionId: 'y' })]))
    );

    fixture.detectChanges();

    const byId = new Map(component.standaloneBadges().map((b) => [b.id, b.icon]));
    expect(byId.get('x')).toBe('gps_fixed');
    expect(byId.get('y')).toBeNull();
  });

  it('render_HeaderSummaryChipAndFourStateLegend', () => {
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(
      of(buildResponse([buildBadgeItem(), buildBadgeItem({ badgeDefinitionId: 'z', isCompleted: false, currentValue: 0 })]))
    );

    fixture.detectChanges();
    const compiled: HTMLElement = fixture.nativeElement;

    expect(compiled.querySelector('[data-testid="badge-earned-summary"]')?.textContent?.trim()).toBe('1 / 2 kazanıldı');
    const legend = Array.from(compiled.querySelectorAll('.ms-badge-legend-item'));
    expect(legend.map((li) => li.querySelector('app-badge-medallion')?.getAttribute('data-state'))).toEqual([
      'earned',
      'new',
      'in-progress',
      'locked',
    ]);
    for (const li of legend) {
      expect(li.querySelector('app-badge-medallion')?.getAttribute('style')).toContain('--bm-size: 40px');
      expect(li.querySelector('[data-testid="badge-medallion-ring"]')).toBeNull();
    }
    expect(legend.map((li) => li.querySelector(':scope > span')?.textContent?.trim())).toEqual(['Kazanıldı', 'Yeni', 'İlerlemede', 'Kilitli']);
  });

  // CR U1: seçimin tek kaynağı thropy — açılışta seçilen yol düğümü vurgulu ve detayı açık, tek panel.
  it('pathSelection_InitialPendingNodeHighlightedWithSingleDetailPanel_ToggleViaThropy', () => {
    const badges = [
      buildBadgeItem({ badgeDefinitionId: 'p1', pathKey: 'hunter', pathName: 'Soru Avcısı', pathOrder: 1 }),
      buildBadgeItem({ badgeDefinitionId: 'p2', pathKey: 'hunter', pathName: 'Soru Avcısı', pathOrder: 2, isCompleted: false, currentValue: 2 }),
      buildBadgeItem({ badgeDefinitionId: 's1', pathKey: 'streak', pathName: 'İstikrar', pathOrder: 1, isCompleted: false, currentValue: 1 }),
      buildBadgeItem({ badgeDefinitionId: 'solo', pathKey: null }),
    ];
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(buildResponse(badges)));
    fixture.detectChanges();
    const compiled: HTMLElement = fixture.nativeElement;
    const expanded = () =>
      Array.from(compiled.querySelectorAll<HTMLButtonElement>('button[aria-expanded="true"]')).map((b) => b.getAttribute('aria-label'));
    const panels = () => compiled.querySelectorAll('app-badge-detail:not(.ms-path-inline-detail)');

    // Yollar ada göre sıralı (İstikrar, Soru Avcısı): ilk yolun bekleyen düğümü seçilir.
    expect(component.selectedBadge()?.id).toBe('s1');
    expect(expanded().length).toBe(1);
    expect(expanded()[0]).toContain('Hızlı Başlangıç');
    expect(panels().length).toBe(1);

    // Başka yoldaki düğüm (DOM sırası: s1 | p1, p2): seçim oraya geçer, yine tek panel.
    const hunterNode = compiled.querySelectorAll<HTMLButtonElement>('button.ms-path-node')[2];
    hunterNode.click();
    fixture.detectChanges();
    expect(component.selectedBadge()?.id).toBe('p2');
    expect(hunterNode.getAttribute('aria-expanded')).toBe('true');
    expect(expanded().length).toBe(1);
    expect(panels().length).toBe(1);

    // Aynı düğüm tekrar: kapanır.
    hunterNode.click();
    fixture.detectChanges();
    expect(component.selectedBadge()).toBeNull();
    expect(panels().length).toBe(0);

    // Tekil rozet seçilince yol panelleri kapalı, tekil panel açık ve aria-controls ona işaret eder.
    const solo = compiled.querySelector<HTMLButtonElement>('button.ms-badge-item')!;
    solo.click();
    fixture.detectChanges();
    expect(panels().length).toBe(1);
    expect(solo.getAttribute('aria-controls')).toBe('badge-standalone-detail');
    expect(compiled.querySelector('#badge-standalone-detail')).not.toBeNull();
  });

  it('showHeadingFalse_HidesTitleButKeepsSummary', () => {
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(buildResponse([buildBadgeItem()])));
    component.showHeading = false;
    fixture.detectChanges();
    const compiled: HTMLElement = fixture.nativeElement;

    expect(compiled.querySelector('.ms-badge-title')).toBeNull();
    expect(compiled.querySelector('.ms-badge-subtitle')).toBeNull();
    expect(compiled.querySelector('[data-testid="badge-earned-summary"]')).not.toBeNull();
  });

  it('render_PathBadges_SingleBadgePathPerPath_NoLegacyCanvasOrDuplicates', () => {
    const badges = [
      buildBadgeItem({ badgeDefinitionId: 'p1', pathKey: 'hunter', pathName: 'Soru Avcısı', pathOrder: 1 }),
      buildBadgeItem({ badgeDefinitionId: 'p2', pathKey: 'hunter', pathName: 'Soru Avcısı', pathOrder: 2, isCompleted: false, currentValue: 0 }),
      buildBadgeItem({ badgeDefinitionId: 's1', pathKey: 'streak', pathName: 'İstikrar', pathOrder: 1 }),
    ];
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(of(buildResponse(badges)));

    fixture.detectChanges();
    const compiled: HTMLElement = fixture.nativeElement;

    expect(compiled.querySelectorAll('app-badge-path').length).toBe(2);
    expect(compiled.querySelector('.ms-badge-path-canvas')).toBeNull();
    expect(compiled.querySelector('linearGradient')).toBeNull();
    const headers = Array.from(compiled.querySelectorAll('.ms-badge-path-progress')).map((h) => h.textContent?.trim());
    expect(headers).toContain('1 / 2 kazanıldı');
    expect(compiled.querySelectorAll('button.ms-path-node').length).toBe(3);
  });

  it('render_StandaloneBadges_AreButtonsWithAriaAndDetailOnActivate', () => {
    badgeServiceSpy.getUserBadgeProgress.and.returnValue(
      of(
        buildResponse([
          buildBadgeItem({ badgeDefinitionId: 'a', name: 'Alfa', isCompleted: false, currentValue: 0, targetValue: 500, earnedDateUtc: null }),
          buildBadgeItem({ badgeDefinitionId: 'b', name: 'Beta', isCompleted: false, currentValue: 180, targetValue: 250, earnedDateUtc: null }),
        ])
      )
    );

    fixture.detectChanges();
    const compiled: HTMLElement = fixture.nativeElement;
    const buttons = Array.from(compiled.querySelectorAll<HTMLButtonElement>('button.ms-badge-item'));

    expect(buttons.map((b) => b.getAttribute('aria-label'))).toEqual(['Kilitli: Alfa, 0/500', 'İlerlemede: Beta, 180/250']);
    expect(buttons.every((b) => b.getAttribute('type') === 'button')).toBeTrue();
    expect(buttons[0].querySelector('app-badge-medallion')?.getAttribute('data-state')).toBe('locked');
    expect(buttons[0].querySelector('.ms-badge-time')?.textContent?.trim()).toBe('Kilitli · 0 / 500');
    // İlk seçim: ilk tekil rozet → detay açık
    expect(buttons[0].getAttribute('aria-expanded')).toBe('true');

    buttons[1].click();
    fixture.detectChanges();

    expect(buttons[1].getAttribute('aria-expanded')).toBe('true');
    const bar = compiled.querySelector('app-badge-detail [role="progressbar"]') as HTMLElement;
    expect(bar.getAttribute('aria-valuenow')).toBe('180');
    expect(bar.getAttribute('aria-valuemax')).toBe('250');
    expect(bar.getAttribute('aria-label')).toBe('Beta ilerlemesi');
  });
});

