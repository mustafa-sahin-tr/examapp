import type { MockedObject } from "vitest";
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
    let badgeServiceSpy: MockedObject<BadgeService>;
    let authServiceSpy: MockedObject<AuthService>;

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
        badgeServiceSpy = {
            getUserBadgeProgress: vi.fn().mockName("BadgeService.getUserBadgeProgress")
        } as unknown as MockedObject<BadgeService>;
        authServiceSpy = {
            getUserIdFromLocalStorage: vi.fn().mockName("AuthService.getUserIdFromLocalStorage")
        } as unknown as MockedObject<AuthService>;
        authServiceSpy.getUserIdFromLocalStorage.mockReturnValue(null);

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
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse([])));
        fixture.detectChanges();
        expect(component).toBeTruthy();
    });

    it('ngOnInit_WhileRequestPending_ShowsLoadingSpinner', () => {
        // A Subject that never emits lets us assert the loading state before resolution.
        const pending = new Subject<BadgeProgressResponse>();
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(pending.asObservable());

        fixture.detectChanges();

        expect(component.isLoading()).toBe(true);
        const compiled: HTMLElement = fixture.nativeElement;
        expect(compiled.querySelector('mat-spinner')).toBeTruthy();
        expect(compiled.textContent).toContain('Rozetler yükleniyor');
    });

    it('loadBadges_ApiReturnsEmptyList_ShowsEmptyState', () => {
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse([])));

        fixture.detectChanges();

        expect(component.isLoading()).toBe(false);
        expect(component.loadError()).toBe(false);
        expect(component.badgePaths().length).toBe(0);
        expect(component.standaloneBadges().length).toBe(0);

        const compiled: HTMLElement = fixture.nativeElement;
        expect(compiled.textContent).toContain('Henüz kazanılmış veya takip edilen bir rozet yok.');
    });

    it('loadBadges_ApiFails_ShowsErrorStateWithRetryButton', () => {
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(throwError(() => new Error('network error')));

        fixture.detectChanges();

        expect(component.isLoading()).toBe(false);
        expect(component.loadError()).toBe(true);
        expect(component.badges().length).toBe(0);

        const compiled: HTMLElement = fixture.nativeElement;
        expect(compiled.textContent).toContain('Rozet bilgisi alınırken bir hata oluştu.');
        const retryButton = compiled.querySelector('button');
        expect(retryButton).toBeTruthy();
        expect(retryButton?.textContent).toContain('Tekrar dene');
    });

    it('reload_AfterPreviousFailure_ClearsErrorAndRefetches', () => {
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(throwError(() => new Error('network error')));
        fixture.detectChanges();
        expect(component.loadError()).toBe(true);

        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse([buildBadgeItem()])));
        component.reload();

        expect(component.loadError()).toBe(false);
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
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse([badge])));

        fixture.detectChanges();

        expect(component.badgePaths().length).toBe(0);
        expect(component.standaloneBadges().length).toBe(1);
        const mapped = component.standaloneBadges()[0];
        expect(mapped.pathKey).toBeNull();
        expect(mapped.progressPercent).toBe(60);
        expect(mapped.isCompleted).toBe(false);
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
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse(badges)));

        fixture.detectChanges();

        expect(component.standaloneBadges().length).toBe(0);
        expect(component.badgePaths().length).toBe(1);
        const path = component.badgePaths()[0];
        expect(path.key).toBe('correct-streak');
        expect(path.badges.length).toBe(2);
        expect(path.completedCount).toBe(1);

        // #889: yol kapalı başlık; açılınca yol çizilir.
        const compiled: HTMLElement = fixture.nativeElement;
        expect(compiled.querySelector('app-badge-path')).toBeNull();
        const header = compiled.querySelector<HTMLButtonElement>('button.ms-badge-path-header')!;
        expect(header.getAttribute('aria-expanded')).toBe('false');
        expect(header.textContent).toContain('Doğru Yolu Bul');
        expect(header.textContent).toContain('1 / 2 kazanıldı');
        expect(header.textContent).toContain('sıradaki: Hızlı Başlangıç');
        header.click();
        fixture.detectChanges();
        expect(header.getAttribute('aria-expanded')).toBe('true');
        expect(compiled.querySelector('app-badge-path')).toBeTruthy();
    });

    it('loadBadges_UsesInputUserIdOverStoredWhenBothAvailable', () => {
        authServiceSpy.getUserIdFromLocalStorage.mockReturnValue(42);
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse([])));

        component.userId = 7;
        fixture.detectChanges();

        expect(badgeServiceSpy.getUserBadgeProgress).toHaveBeenCalledWith(7);
    });

    it('loadBadges_InputUserIdMissing_FallsBackToStoredUserId', () => {
        authServiceSpy.getUserIdFromLocalStorage.mockReturnValue(42);
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse([])));

        component.userId = 0;
        fixture.detectChanges();

        expect(badgeServiceSpy.getUserBadgeProgress).toHaveBeenCalledWith(42);
    });

    it('loadBadges_NeitherInputNorStoredUserId_FallsBackToZero', () => {
        authServiceSpy.getUserIdFromLocalStorage.mockReturnValue(null);
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse([])));

        component.userId = 0;
        fixture.detectChanges();

        expect(badgeServiceSpy.getUserBadgeProgress).toHaveBeenCalledWith(0);
    });

    // ===== Issue #149 =====
    const DAY = 24 * 60 * 60 * 1000;

    it('mapBadge_DerivesMedallionState_NewLockedInProgressEarned', () => {
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse([
            buildBadgeItem({ badgeDefinitionId: 'a', name: 'A', earnedDateUtc: new Date(Date.now() - DAY).toISOString() }),
            buildBadgeItem({ badgeDefinitionId: 'b', name: 'B', earnedDateUtc: new Date(Date.now() - 30 * DAY).toISOString() }),
            buildBadgeItem({ badgeDefinitionId: 'c', name: 'C', isCompleted: false, currentValue: 0, earnedDateUtc: null }),
            buildBadgeItem({ badgeDefinitionId: 'd', name: 'D', isCompleted: false, currentValue: 2, earnedDateUtc: null }),
        ])));

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
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse([buildBadgeItem({ badgeDefinitionId: 'x', icon: 'gps_fixed' }), buildBadgeItem({ badgeDefinitionId: 'y' })])));

        fixture.detectChanges();

        const byId = new Map(component.standaloneBadges().map((b) => [b.id, b.icon]));
        expect(byId.get('x')).toBe('gps_fixed');
        expect(byId.get('y')).toBeNull();
    });

    it('render_HeaderSummaryAndFourStateLegendBehindHelpButton', () => {
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse([buildBadgeItem(), buildBadgeItem({ badgeDefinitionId: 'z', isCompleted: false, currentValue: 0 })])));

        fixture.detectChanges();
        const compiled: HTMLElement = fixture.nativeElement;

        expect(compiled.querySelector('[data-testid="badge-earned-summary"]')?.textContent?.trim()).toBe('1 rozet kazandın · 2 rozet var');
        // #889: açıklama varsayılan kapalı, "?" ile açılır.
        expect(compiled.querySelector('.ms-badge-legend')).toBeNull();
        const help = compiled.querySelector<HTMLButtonElement>('[data-testid="badge-legend-toggle"]')!;
        expect(help.getAttribute('aria-expanded')).toBe('false');
        help.click();
        fixture.detectChanges();
        expect(help.getAttribute('aria-expanded')).toBe('true');
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

    // CR U1 + #889: açılışta seçim yok; seçim tek kaynaktan, her an tek detay paneli.
    it('pathSelection_NoInitialSelection_SinglePanelAcrossSections', () => {
        const badges = [
            buildBadgeItem({ badgeDefinitionId: 'p1', pathKey: 'hunter', pathName: 'Soru Avcısı', pathOrder: 1 }),
            buildBadgeItem({ badgeDefinitionId: 'p2', pathKey: 'hunter', pathName: 'Soru Avcısı', pathOrder: 2, isCompleted: false, currentValue: 2, earnedDateUtc: null }),
            buildBadgeItem({ badgeDefinitionId: 's1', pathKey: 'streak', pathName: 'İstikrar', pathOrder: 1, isCompleted: false, currentValue: 1, earnedDateUtc: null }),
            buildBadgeItem({ badgeDefinitionId: 'solo', pathKey: null, isCompleted: false, currentValue: 0, earnedDateUtc: null }),
        ];
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse(badges)));
        fixture.detectChanges();
        const compiled: HTMLElement = fixture.nativeElement;
        const panels = () => compiled.querySelectorAll('app-badge-detail:not(.ms-path-inline-detail)');

        expect(component.selectedBadge()).toBeNull();
        expect(panels().length).toBe(0);

        // Yolu aç, düğümü seç: tek panel.
        compiled.querySelector<HTMLButtonElement>('button.ms-badge-path-header[data-path="hunter"]')!.click();
        fixture.detectChanges();
        const hunterNode = compiled.querySelectorAll<HTMLButtonElement>('button.ms-path-node')[1];
        hunterNode.click();
        fixture.detectChanges();
        expect(component.selectedBadge()?.id).toBe('p2');
        expect(component.selectedSource()).toBe('path');
        expect(hunterNode.getAttribute('aria-expanded')).toBe('true');
        expect(panels().length).toBe(1);

        // Aynı rozet "Neredeyse"de: oradan seçilince yol düğümü bırakılır, panel üstte.
        const nearly = compiled.querySelector<HTMLButtonElement>('[data-testid="badge-nearly"] button[aria-label*="Hızlı Başlangıç"]')!;
        nearly.click();
        fixture.detectChanges();
        expect(component.selectedSource()).toBe('top');
        expect(hunterNode.getAttribute('aria-expanded')).toBe('false');
        expect(panels().length).toBe(1);
        expect(compiled.querySelector('#badge-top-detail')).not.toBeNull();

        // Tekrar: kapanır.
        nearly.click();
        fixture.detectChanges();
        expect(component.selectedBadge()).toBeNull();
        expect(panels().length).toBe(0);

        // Tekil rozet: kendi paneli.
        const solo = compiled.querySelector<HTMLButtonElement>('button.ms-badge-item')!;
        solo.click();
        fixture.detectChanges();
        expect(panels().length).toBe(1);
        expect(solo.getAttribute('aria-controls')).toBe('badge-standalone-detail');
        expect(compiled.querySelector('#badge-standalone-detail')).not.toBeNull();
    });

    it('nearly_StartedBadgesByProgressThenNextOfEachPath_MaxThree_issue889', () => {
        const badges = [
            buildBadgeItem({ badgeDefinitionId: 'a1', name: 'Yeni Alışkanlıklar', pathKey: 'activity', pathName: 'Aktivite', pathOrder: 1, isCompleted: false, currentValue: 3, targetValue: 7, earnedDateUtc: null }),
            buildBadgeItem({ badgeDefinitionId: 'a2', name: 'Alışkanlık Sahibi', pathKey: 'activity', pathName: 'Aktivite', pathOrder: 2, isCompleted: false, currentValue: 3, targetValue: 21, earnedDateUtc: null }),
            buildBadgeItem({ badgeDefinitionId: 'q1', name: 'Soru Avcısı I', pathKey: 'hunter', pathName: 'Soru', pathOrder: 1 }),
            buildBadgeItem({ badgeDefinitionId: 'q2', name: 'Soru Avcısı II', pathKey: 'hunter', pathName: 'Soru', pathOrder: 2, isCompleted: false, currentValue: 7, targetValue: 10, earnedDateUtc: null }),
            buildBadgeItem({ badgeDefinitionId: 't1', name: 'Test Bitirici', pathKey: 'tests', pathName: 'Test', pathOrder: 1, isCompleted: false, currentValue: 0, targetValue: 3, earnedDateUtc: null }),
        ];
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse(badges)));
        fixture.detectChanges();

        // %70, %43 başlamış; üçüncü yer başlamamış yolun sıradakiyle dolar (a2 %14 başlamış, onu geçer).
        expect(component.nearlyBadges().map((b) => b.id)).toEqual(['q2', 'a1', 'a2']);

        const cards = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.ms-badge-nearly-card'));
        expect(cards.length).toBe(3);
        expect(cards[0].textContent).toContain('7/10');
        expect(cards[0].textContent).toContain('Soru Avcısı II');
        expect(cards[0].textContent).toContain('3 kaldı');
    });

    it('nearly_FewStarted_FillsWithNextPendingOfPaths_issue889', () => {
        const badges = [
            buildBadgeItem({ badgeDefinitionId: 'q2', pathKey: 'hunter', pathName: 'Soru', pathOrder: 2, isCompleted: false, currentValue: 7, targetValue: 10, earnedDateUtc: null }),
            buildBadgeItem({ badgeDefinitionId: 'a1', pathKey: 'activity', pathName: 'Aktivite', pathOrder: 1, isCompleted: false, currentValue: 0, targetValue: 7, earnedDateUtc: null }),
            buildBadgeItem({ badgeDefinitionId: 'a2', pathKey: 'activity', pathName: 'Aktivite', pathOrder: 2, isCompleted: false, currentValue: 0, targetValue: 21, earnedDateUtc: null }),
        ];
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse(badges)));
        fixture.detectChanges();

        expect(component.nearlyBadges().map((b) => b.id)).toEqual(['q2', 'a1']);
    });

    it('earned_NewestFirstWithNewLabel_AndHiddenFromOtherBadges_issue889', () => {
        const DAY_MS = 24 * 60 * 60 * 1000;
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse([
            buildBadgeItem({ badgeDefinitionId: 'old', name: 'İlk Cevap', earnedDateUtc: new Date(Date.now() - 30 * DAY_MS).toISOString() }),
            buildBadgeItem({ badgeDefinitionId: 'fresh', name: 'Soru Avcısı I', earnedDateUtc: new Date(Date.now() - DAY_MS).toISOString() }),
            buildBadgeItem({ badgeDefinitionId: 'todo', name: 'Bekleyen', isCompleted: false, currentValue: 0, earnedDateUtc: null }),
        ])));
        fixture.detectChanges();
        const compiled: HTMLElement = fixture.nativeElement;

        const earned = compiled.querySelector('[data-testid="badge-earned"]')!;
        expect(earned.querySelector('h3')?.textContent).toContain('Kazandıkların');
        const items = Array.from(earned.querySelectorAll('.ms-badge-earned-item'));
        expect(items.map((i) => i.querySelector('.ms-badge-name')?.textContent?.trim())).toEqual(['Soru Avcısı I', 'İlk Cevap']);
        expect(items[0].querySelector('.ms-badge-new')?.textContent?.trim()).toBe('Yeni');
        expect(items[1].querySelector('.ms-badge-new')).toBeNull();
        // Tekil bölümde yalnız kazanılmamış olan.
        expect(Array.from(compiled.querySelectorAll('button.ms-badge-item')).map((b) => b.getAttribute('aria-label'))).toEqual(['Kilitli: Bekleyen, 0/5']);
    });

    it('showHeadingFalse_HidesTitleButKeepsSummary', () => {
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse([buildBadgeItem()])));
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
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse(badges)));

        fixture.detectChanges();
        const compiled: HTMLElement = fixture.nativeElement;
        compiled.querySelectorAll<HTMLButtonElement>('button.ms-badge-path-header').forEach((b) => b.click());
        fixture.detectChanges();

        expect(compiled.querySelectorAll('app-badge-path').length).toBe(2);
        expect(compiled.querySelector('.ms-badge-path-canvas')).toBeNull();
        expect(compiled.querySelector('linearGradient')).toBeNull();
        const headers = Array.from(compiled.querySelectorAll('.ms-badge-path-progress')).map((h) => h.textContent?.trim());
        expect(headers.some((h) => h?.startsWith('1 / 2 kazanıldı'))).toBe(true);
        expect(headers.some((h) => h?.includes('hepsi kazanıldı'))).toBe(true);
        expect(compiled.querySelectorAll('button.ms-path-node').length).toBe(3);
    });

    it('render_StandaloneBadges_AreButtonsWithAriaAndDetailOnActivate', () => {
        badgeServiceSpy.getUserBadgeProgress.mockReturnValue(of(buildResponse([
            buildBadgeItem({ badgeDefinitionId: 'a', name: 'Alfa', isCompleted: false, currentValue: 0, targetValue: 500, earnedDateUtc: null }),
            buildBadgeItem({ badgeDefinitionId: 'b', name: 'Beta', isCompleted: false, currentValue: 180, targetValue: 250, earnedDateUtc: null }),
        ])));

        fixture.detectChanges();
        const compiled: HTMLElement = fixture.nativeElement;
        const buttons = Array.from(compiled.querySelectorAll<HTMLButtonElement>('button.ms-badge-item'));

        expect(buttons.map((b) => b.getAttribute('aria-label'))).toEqual(['Kilitli: Alfa, 0/500', 'İlerlemede: Beta, 180/250']);
        expect(buttons.every((b) => b.getAttribute('type') === 'button')).toBe(true);
        expect(buttons[0].querySelector('app-badge-medallion')?.getAttribute('data-state')).toBe('locked');
        expect(buttons[0].querySelector('.ms-badge-time')?.textContent?.trim()).toBe('Kilitli · 0 / 500');
        // #889: açılışta seçim yok.
        expect(buttons[0].getAttribute('aria-expanded')).toBe('false');

        buttons[1].click();
        fixture.detectChanges();

        expect(buttons[1].getAttribute('aria-expanded')).toBe('true');
        const bar = compiled.querySelector('app-badge-detail [role="progressbar"]') as HTMLElement;
        expect(bar.getAttribute('aria-valuenow')).toBe('180');
        expect(bar.getAttribute('aria-valuemax')).toBe('250');
        expect(bar.getAttribute('aria-label')).toBe('Beta ilerlemesi');
    });
});
