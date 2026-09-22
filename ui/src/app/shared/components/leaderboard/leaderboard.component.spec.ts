import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { LeaderboardComponent } from './leaderboard.component';
import { LeaderboardEntry, LeaderboardResult, LeaderboardScope } from '../../../models/leaderboard.model';
import { AuthService } from '../../../services/auth.service';
import { translocoTestingModule } from '../../testing/transloco-testing';
import leaderboardTr from '../../../../../public/i18n/leaderboard/tr.json';

/** Gerçek sözlük: bir anahtar bozulursa test kırılır (issue #183 deseni). */
const translocoTesting = translocoTestingModule({ langs: { 'leaderboard/tr': leaderboardTr } });

const LB_URL = '/api/exam/leaderboard';

function entry(rank: number, overrides: Partial<LeaderboardEntry> = {}): LeaderboardEntry {
  return { rank, xp: 1000 - rank * 10, level: 3, fullName: `Öğrenci ${rank}`, avatarUrl: '', isMe: false, ...overrides };
}

function result(scope: LeaderboardScope, overrides: Partial<LeaderboardResult> = {}): LeaderboardResult {
  return {
    success: true,
    scope,
    schoolId: scope === 'school' ? 7 : null,
    schoolScopeAvailable: true,
    totalCount: 0,
    skip: 0,
    take: 20,
    entries: [],
    myRank: null,
    myXp: null,
    ...overrides,
  };
}

describe('LeaderboardComponent', () => {
  let fixture: ComponentFixture<LeaderboardComponent>;
  let component: LeaderboardComponent;
  let httpMock: HttpTestingController;
  /** `AuthService.user()` stub'ı: `schoolId` verilirse "profilde okul var" senaryosu. */
  const authUser = signal<{ schoolId?: number } | null>(null);

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function expectRequest(scope: LeaderboardScope, skip = 0) {
    const req = httpMock.expectOne(
      (r) => r.url === LB_URL && r.params.get('scope') === scope && r.params.get('skip') === String(skip)
    );
    expect(req.request.params.get('take')).toBe('20');
    return req;
  }

  /** Okulsuz kullanıcı için tek istekle listeyi doldurur (toggle yok). */
  function flushGlobalOnly(entries: LeaderboardEntry[], overrides: Partial<LeaderboardResult> = {}): void {
    expectRequest('global').flush(
      result('global', { schoolScopeAvailable: false, entries, totalCount: entries.length, ...overrides })
    );
    fixture.detectChanges();
  }

  /** `beforeEach` içinde `fixture.detectChanges()` çağrılmaz: senaryolar `authUser`'ı önce ayarlar. */
  async function setup(user: { schoolId?: number } | null): Promise<void> {
    authUser.set(user);
    await TestBed.configureTestingModule({
      imports: [LeaderboardComponent, NoopAnimationsModule, translocoTesting],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { user: authUser.asReadonly() } },
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(LeaderboardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges(); // ngOnInit → ilk istek
  }

  afterEach(() => httpMock.verify());

  describe('initial load — profilde okul yok (global yoklama)', () => {
    beforeEach(() => setup(null));

    it('init_ServerSaysSchoolAvailable_ShowsToggleAndDefaultsToSchool', () => {
      expectRequest('global').flush(result('global', { schoolScopeAvailable: true, entries: [entry(1)], totalCount: 1 }));
      fixture.detectChanges();

      // Yoklama yanıtı listelenmez; okul kapsamı çekilirken yükleniyor kalır.
      expect(component.loading()).toBeTrue();
      expect(el().querySelectorAll('.lb__row').length).toBe(0);

      expectRequest('school').flush(result('school', { entries: [entry(1), entry(2)], totalCount: 2 }));
      fixture.detectChanges();

      expect(component.scope()).toBe('school');
      const toggle = el().querySelector('mat-button-toggle-group');
      expect(toggle).not.toBeNull();
      expect(toggle?.querySelector('.mat-button-toggle-checked')?.textContent?.trim()).toBe('Okulum');
      expect(el().querySelectorAll('.lb__row').length).toBe(2);
    });

    it('init_ServerSaysNoSchool_NoToggleAndGlobalListShown', () => {
      flushGlobalOnly([entry(1), entry(2), entry(3)]);

      expect(component.scope()).toBe('global');
      expect(el().querySelector('mat-button-toggle-group')).toBeNull();
      expect(el().querySelectorAll('.lb__row').length).toBe(3);
      httpMock.expectNone((r) => r.params.get('scope') === 'school');
    });

    it('init_WhileLoading_ShowsSpinnerTextAndAriaBusy', () => {
      expect(el().querySelector('.lb__state--loading')?.textContent).toContain('Sıralama yükleniyor');
      expect(el().querySelector('.lb')?.getAttribute('aria-busy')).toBe('true');

      flushGlobalOnly([]);

      expect(el().querySelector('.lb')?.hasAttribute('aria-busy')).toBeFalse();
    });
  });

  describe('initial load — profilde okul var (doğrudan school)', () => {
    beforeEach(() => setup({ schoolId: 7 }));

    it('init_CachedSchool_RequestsSchoolDirectlyWithSingleRequest', () => {
      expectRequest('school').flush(result('school', { entries: [entry(1)], totalCount: 1 }));
      fixture.detectChanges();

      expect(component.scope()).toBe('school');
      expect(el().querySelector('mat-button-toggle-group')).not.toBeNull();
      expect(el().querySelectorAll('.lb__row').length).toBe(1);
      httpMock.expectNone((r) => r.params.get('scope') === 'global');
    });

    it('init_CachedSchoolBut400_FallsBackToGlobalAndHidesToggle', () => {
      expectRequest('school').flush({ message: 'Okul bulunamadı' }, { status: 400, statusText: 'Bad Request' });
      fixture.detectChanges();

      expect(component.loading()).toBeTrue();
      expect(el().querySelector('.lb__state--error')).toBeNull();

      expectRequest('global').flush(result('global', { schoolScopeAvailable: false, entries: [entry(1)], totalCount: 1 }));
      fixture.detectChanges();

      expect(component.scope()).toBe('global');
      expect(component.schoolScopeAvailable()).toBeFalse();
      expect(el().querySelector('mat-button-toggle-group')).toBeNull();
      expect(el().querySelectorAll('.lb__row').length).toBe(1);
    });

    it('init_CachedSchoolBut500_ShowsErrorWithoutFallback', () => {
      expectRequest('school').flush(null, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(el().querySelector('.lb__state--error')?.textContent).toContain('Sıralama yüklenemedi.');
      httpMock.expectNone((r) => r.params.get('scope') === 'global');
    });
  });

  describe('scope toggle', () => {
    beforeEach(async () => {
      await setup({ schoolId: 7 });
      expectRequest('school').flush(result('school', { entries: [entry(1)], totalCount: 1 }));
      fixture.detectChanges();
    });

    it('setScope_Global_RequestsGlobalFromStart', () => {
      component.setScope('global');
      fixture.detectChanges();

      expectRequest('global', 0).flush(result('global', { entries: [entry(1), entry(2)], totalCount: 2 }));
      fixture.detectChanges();

      expect(component.scope()).toBe('global');
      expect(el().querySelectorAll('.lb__row').length).toBe(2);
    });

    it('setScope_SameScope_DoesNotRequest', () => {
      component.setScope('school');

      httpMock.expectNone((r) => r.url === LB_URL);
    });

    it('toggleClick_Global_DispatchesChangeWithNewScope', () => {
      const globalButton = Array.from(el().querySelectorAll<HTMLButtonElement>('mat-button-toggle button')).find(
        (b) => b.textContent?.trim() === 'Herkes'
      );
      globalButton?.click();
      fixture.detectChanges();

      expectRequest('global', 0).flush(result('global'));
      expect(component.scope()).toBe('global');
    });

    it('setScope_RapidToggle_CancelsPreviousRequestAndShowsOnlyLatest', () => {
      component.setScope('global');
      const globalReq = expectRequest('global', 0);

      component.setScope('school');
      const schoolReq = expectRequest('school', 0);

      expect(globalReq.cancelled).toBeTrue();
      expect(schoolReq.cancelled).toBeFalse();

      schoolReq.flush(result('school', { entries: [entry(1), entry(2), entry(3)], totalCount: 3 }));
      fixture.detectChanges();

      expect(component.scope()).toBe('school');
      expect(el().querySelectorAll('.lb__row').length).toBe(3);
    });
  });

  describe('list rendering', () => {
    beforeEach(() => setup(null));

    it('entries_Empty_ShowsNoPointsYet', () => {
      flushGlobalOnly([]);

      expect(el().querySelector('.lb__state--empty')?.textContent?.trim()).toBe('Henüz puan yok');
      expect(el().querySelector('.lb__mine')).toBeNull();
    });

    it('entries_IsMeRow_GetsHighlightClassAndAriaCurrent', () => {
      flushGlobalOnly([entry(1), entry(2, { isMe: true, fullName: 'Ben' })], { myRank: 2, myXp: 980 });

      const rows = el().querySelectorAll('.lb__row');
      expect(rows[0].classList.contains('lb__row--me')).toBeFalse();
      expect(rows[1].classList.contains('lb__row--me')).toBeTrue();
      expect(rows[1].getAttribute('aria-current')).toBe('true');
      expect(rows[1].querySelector('.lb__me-tag')?.textContent?.trim()).toBe('Sen');
      expect(el().querySelector('.lb__mine')?.textContent).toContain('Senin sıran: #2 · 980 XP');
    });

    it('myRank_Null_HidesMyRankFooter', () => {
      flushGlobalOnly([entry(1)], { myRank: null, myXp: null });

      expect(el().querySelector('.lb__mine')).toBeNull();
    });
  });

  describe('errors', () => {
    beforeEach(() => setup(null));

    it('init_ServerError_ShowsBodyMessageAndRetry', () => {
      expectRequest('global').flush({ message: 'Sunucu hatası' }, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      const errorBox = el().querySelector('.lb__state--error');
      expect(errorBox?.textContent).toContain('Sunucu hatası');
      expect(errorBox?.querySelector('button')).not.toBeNull();
    });

    it('init_ErrorWithoutMessage_ShowsTranslatedFallback', () => {
      expectRequest('global').flush(null, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(component.errorMessage()).toBeNull();
      expect(el().querySelector('.lb__state--error')?.textContent).toContain('Sıralama yüklenemedi.');
    });

    it('retry_AfterInitialError_ReprobesGlobal', () => {
      expectRequest('global').flush(null, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      component.retry();
      flushGlobalOnly([entry(1)]);

      expect(el().querySelector('.lb__state--error')).toBeNull();
      expect(el().querySelectorAll('.lb__row').length).toBe(1);
    });

    it('retry_AfterScopeChangeError_ReloadsSameScopeFromPageZero', () => {
      expectRequest('global').flush(result('global', { schoolScopeAvailable: true }));
      expectRequest('school').flush(result('school', { entries: [entry(1)], totalCount: 1 }));
      fixture.detectChanges();

      component.setScope('global');
      expectRequest('global', 0).flush(null, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();
      expect(el().querySelector('.lb__state--error')).not.toBeNull();

      component.retry();
      expectRequest('global', 0).flush(result('global', { entries: [entry(1), entry(2)], totalCount: 2 }));
      fixture.detectChanges();

      expect(component.scope()).toBe('global');
      expect(el().querySelector('.lb__state--error')).toBeNull();
      expect(el().querySelectorAll('.lb__row').length).toBe(2);
      expect(el().querySelector('mat-button-toggle-group')).not.toBeNull();
    });
  });

  describe('load more', () => {
    const firstPage = Array.from({ length: 20 }, (_, i) => entry(i + 1));

    beforeEach(() => setup(null));

    it('loadMore_HasMore_AppendsNextPageWithSkip', () => {
      flushGlobalOnly(firstPage, { totalCount: 25 });

      expect(component.hasMore()).toBeTrue();
      expect(el().querySelector('.lb__more button')).not.toBeNull();

      component.loadMore();
      expectRequest('global', 20).flush(
        result('global', { schoolScopeAvailable: false, entries: [entry(21), entry(22)], totalCount: 25, skip: 20 })
      );
      fixture.detectChanges();

      expect(component.entries().length).toBe(22);
      expect(el().querySelectorAll('.lb__row').length).toBe(22);
    });

    it('loadMore_WhileLoadingMore_ButtonDisabledAndNoSecondRequest', () => {
      flushGlobalOnly(firstPage, { totalCount: 25 });

      component.loadMore();
      fixture.detectChanges();

      const button = el().querySelector<HTMLButtonElement>('.lb__more button');
      expect(button?.disabled).toBeTrue();
      expect(el().querySelectorAll('.lb__row').length).toBe(20);

      component.loadMore();
      const pending = httpMock.match((r) => r.url === LB_URL);
      expect(pending.length).toBe(1);
      pending[0].flush(result('global', { schoolScopeAvailable: false, entries: [entry(21)], totalCount: 25 }));
      fixture.detectChanges();

      expect(el().querySelector<HTMLButtonElement>('.lb__more button')?.disabled).toBeFalse();
    });

    it('loadMore_Error_KeepsEntriesAndShowsInlineRetry', () => {
      flushGlobalOnly(firstPage, { totalCount: 25 });

      component.loadMore();
      expectRequest('global', 20).flush({ message: 'Zaman aşımı' }, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect(component.entries().length).toBe(20);
      expect(el().querySelectorAll('.lb__row').length).toBe(20);
      expect(el().querySelector('.lb__state--error')).toBeNull();
      const inline = el().querySelector('.lb__more [role="alert"]');
      expect(inline?.textContent).toContain('Zaman aşımı');
      expect(el().querySelector('.lb__more button')).not.toBeNull();
    });

    it('retryMore_AfterError_RetriesSameSkipAndAppends', () => {
      flushGlobalOnly(firstPage, { totalCount: 25 });

      component.loadMore();
      expectRequest('global', 20).flush(null, { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();
      expect(el().querySelector('.lb__more [role="alert"]')?.textContent).toContain('Devamı yüklenemedi.');

      (el().querySelector('.lb__more button') as HTMLButtonElement).click();
      fixture.detectChanges();

      expectRequest('global', 20).flush(
        result('global', { schoolScopeAvailable: false, entries: [entry(21), entry(22)], totalCount: 25 })
      );
      fixture.detectChanges();

      expect(component.moreFailed()).toBeFalse();
      expect(el().querySelector('.lb__more [role="alert"]')).toBeNull();
      expect(el().querySelectorAll('.lb__row').length).toBe(22);
    });

    it('loadMore_NoMore_DoesNotRequest', () => {
      flushGlobalOnly([entry(1)]);

      component.loadMore();

      expect(el().querySelector('.lb__more')).toBeNull();
      httpMock.expectNone((r) => r.url === LB_URL);
    });
  });
});
