import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';

import { BADGE_PROGRESS_ROUTE, NotificationsComponent, RELATIVE_TIME_TICK_MS } from './notifications.component';
import { AppNotification } from '../../models/notification.model';
import { NotificationService } from '../../services/notification.service';
import { LocaleService } from '../../services/locale.service';
import { localeDefinitionOf } from '../../models/locale';
import { routes } from '../../app.routes';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import notificationsTr from '../../../../public/i18n/notifications/tr.json';

const LIST_URL = '/api/badge/notifications/me';
/** Uygulamanın kendi rozet ikon yolu (`BadgeIconValidator`: `achievements/<dosya>.svg`). */
const BADGE_ICON = 'achievements/first-step.svg';

function notification(overrides: Partial<AppNotification> = {}): AppNotification {
  return {
    id: 1,
    type: 'WorksheetReminderDue',
    title: 'Hatırlatma',
    body: 'Sınavın yaklaşıyor',
    data: null,
    isRead: false,
    createdAt: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
    ...overrides,
  };
}

function badgeEarned(overrides: Partial<AppNotification> = {}, iconUrl: string | null = BADGE_ICON): AppNotification {
  return notification({
    id: 10,
    type: 'BadgeEarned',
    title: 'Yeni rozet kazandın!',
    body: 'İlk Adım rozetini kazandın.',
    data: JSON.stringify({ badgeDefinitionId: 'badge-definition-1', badgeCode: 'FIRST_STEP', iconUrl }),
    ...overrides,
  });
}

describe('NotificationsComponent (issue #146)', () => {
  let fixture: ComponentFixture<NotificationsComponent>;
  let httpMock: HttpTestingController;
  let navigateSpy: jasmine.Spy;

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function items(): HTMLButtonElement[] {
    return Array.from(el().querySelectorAll<HTMLButtonElement>('.ntf__item'));
  }

  function flushList(list: AppNotification[], unreadOnly = false): void {
    const req = httpMock.expectOne((r) => r.url === LIST_URL && r.params.get('unreadOnly') === String(unreadOnly));
    expect(req.request.method).toBe('GET');
    req.flush(list);
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        NotificationsComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { 'notifications/tr': notificationsTr } }),
      ],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: LocaleService,
          useValue: {
            locale: signal('tr').asReadonly(),
            localeDefinition: signal(localeDefinitionOf('tr')).asReadonly(),
          },
        },
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.returnValue(Promise.resolve(true));
    fixture = TestBed.createComponent(NotificationsComponent);
    fixture.detectChanges();
  });

  afterEach(() => httpMock.verify());

  it('init_ListsAllNotificationsWithTitleBodyAndRelativeTime', () => {
    flushList([notification({ id: 1 }), notification({ id: 2, title: 'Randevu', body: 'Yeni talep', isRead: true })]);

    const rows = items();
    expect(rows.length).toBe(2);
    expect(rows[0].querySelector('.ntf__title')?.textContent).toContain('Hatırlatma');
    expect(rows[0].querySelector('.ntf__body')?.textContent).toContain('Sınavın yaklaşıyor');
    expect(rows[0].querySelector('.ntf__time')?.textContent?.trim()).toBe('5 dakika önce');
    expect(rows[0].classList).toContain('is-unread');
    expect(rows[1].classList).not.toContain('is-unread');
  });

  it('badgeEarned_WithIconUrl_ShowsBadgeImage', () => {
    flushList([badgeEarned()]);

    const img = items()[0].querySelector<HTMLImageElement>('.ntf__badge-img');
    expect(img).not.toBeNull();
    expect(img?.getAttribute('src')).toBe(`/${BADGE_ICON}`);
    expect(items()[0].querySelector('mat-icon')?.textContent).not.toContain('emoji_events');
  });

  it('badgeEarned_WithoutIconUrl_FallsBackToEmojiEventsIcon', () => {
    flushList([badgeEarned({}, null)]);

    expect(items()[0].querySelector('.ntf__badge-img')).toBeNull();
    expect(items()[0].querySelector('.ntf__icon mat-icon')?.textContent?.trim()).toBe('emoji_events');
  });

  for (const [label, iconUrl] of [
    ['externalUrl', 'https://example.org/achievements/x.svg'],
    ['protocolRelative', '//example.org/achievements/x.svg'],
    ['pathTraversal', 'achievements/../secret.svg'],
    ['nonSvg', 'achievements/first-step.png'],
    ['otherFolder', 'assets/first-step.svg'],
  ] as const) {
    it(`badgeEarned_IconUrlOutsideAchievements_${label}_FallsBackToEmojiEvents`, () => {
      flushList([badgeEarned({}, iconUrl)]);

      expect(items()[0].querySelector('.ntf__badge-img')).toBeNull();
      expect(items()[0].querySelector('.ntf__icon mat-icon')?.textContent?.trim()).toBe('emoji_events');
    });
  }

  it('badgeEarned_Click_MarksReadAndNavigatesToBadgeProgressPage', () => {
    flushList([badgeEarned()]);

    items()[0].click();
    const req = httpMock.expectOne('/api/badge/notifications/10/read');
    expect(req.request.method).toBe('POST');
    // Navigasyon isteğin sonucuna bağlı: yanıt gelmeden gidilmez.
    expect(navigateSpy).not.toHaveBeenCalled();
    req.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(navigateSpy).toHaveBeenCalledWith([BADGE_PROGRESS_ROUTE]);
    expect(items()[0].classList).not.toContain('is-unread');
  });

  it('badgeProgressRoute_ExistsInAppRoutes', () => {
    flushList([]);
    const layoutRoute = routes.find((r) => Array.isArray(r.children));
    const childPaths = layoutRoute?.children?.map((r) => `/${r.path}`) ?? [];
    expect(childPaths).toContain(BADGE_PROGRESS_ROUTE);
    expect(childPaths).toContain('/notifications');
  });

  it('unknownType_ShowsGenericIconAndDoesNotInterpretDataOrNavigate', () => {
    flushList([notification({ id: 5, type: 'SomethingNew', data: JSON.stringify({ iconUrl: BADGE_ICON }) })]);

    const row = items()[0];
    expect(row.querySelector('.ntf__badge-img')).toBeNull();
    expect(row.querySelector('.ntf__icon mat-icon')?.textContent?.trim()).toBe('notifications');

    row.click();
    httpMock.expectOne('/api/badge/notifications/5/read').flush(null, { status: 204, statusText: 'No Content' });
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('badgeEarned_MalformedData_FallsBackToEmojiEventsIcon', () => {
    flushList([badgeEarned({ data: '{not json' })]);

    expect(items()[0].querySelector('.ntf__badge-img')).toBeNull();
    expect(items()[0].querySelector('.ntf__icon mat-icon')?.textContent?.trim()).toBe('emoji_events');
  });

  it('badgeEarned_ClickThenDestroy_MarkReadStillCompletesAndCountDrops', () => {
    const service = TestBed.inject(NotificationService);
    service.refreshUnreadCount().subscribe();
    httpMock.expectOne('/api/badge/notifications/me/unread-count').flush(2);
    flushList([badgeEarned()]);

    items()[0].click();
    expect(service.unreadCount()).toBe(1);
    fixture.destroy();

    // Abonelik komponent ömrüne bağlı olsaydı istek iptal edilir, flush hata verirdi.
    const req = httpMock.expectOne('/api/badge/notifications/10/read');
    expect(req.cancelled).toBeFalse();
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(service.unreadCount()).toBe(1);
    expect(navigateSpy).toHaveBeenCalledWith([BADGE_PROGRESS_ROUTE]);
  });

  it('badgeEarned_MarkReadFails_StillNavigatesAndRestoresCount', () => {
    const service = TestBed.inject(NotificationService);
    service.refreshUnreadCount().subscribe();
    httpMock.expectOne('/api/badge/notifications/me/unread-count').flush(1);
    flushList([badgeEarned()]);

    items()[0].click();
    httpMock.expectOne('/api/badge/notifications/10/read').flush(null, { status: 500, statusText: 'Error' });

    expect(service.unreadCount()).toBe(1);
    expect(navigateSpy).toHaveBeenCalledWith([BADGE_PROGRESS_ROUTE]);
  });

  it('worksheetComment_Click_MarksReadAndNavigatesToWorksheetDetailDeepLink', () => {
    flushList([
      notification({
        id: 21,
        type: 'WorksheetCommentReplied',
        title: 'Yeni cevap',
        data: JSON.stringify({ worksheetId: 12, questionId: 34, commentId: 57, rootCommentId: 56 }),
      }),
    ]);

    expect(items()[0].querySelector('.ntf__icon mat-icon')?.textContent?.trim()).toBe('forum');
    // Sunucu metni bidi-izole gösterilir (security D1).
    expect(items()[0].querySelector('.ntf__title bdi')?.getAttribute('dir')).toBe('auto');
    expect(items()[0].querySelector('.ntf__body bdi')?.getAttribute('dir')).toBe('auto');
    expect(items()[0].querySelector('.ntf__chevron')).not.toBeNull();
    items()[0].click();
    httpMock.expectOne('/api/badge/notifications/21/read').flush(null, { status: 204, statusText: 'No Content' });

    expect(navigateSpy).toHaveBeenCalledWith(['/test', 12], {
      queryParams: { commentId: 57, questionId: 34, rootCommentId: 56 },
    });
  });

  // Issue #309: soru yorumunda "Soru {n}" etiketi; worksheet seviyesinde ya da sıra yoksa etiket yok.
  it('questionComment_ShowsQuestionOrderTag; worksheetLevel_NoTag', () => {
    flushList([
      notification({
        id: 22,
        type: 'WorksheetCommentCreated',
        title: 'Yeni soru',
        data: JSON.stringify({ worksheetId: 12, questionId: 34, commentId: 58, rootCommentId: 58, questionOrder: 4 }),
      }),
      notification({
        id: 23,
        type: 'WorksheetCommentCreated',
        title: 'Yeni yorum',
        data: JSON.stringify({ worksheetId: 12, questionId: null, commentId: 59, rootCommentId: 59, questionOrder: null }),
      }),
    ]);

    const tag = items()[0].querySelector('[data-testid="notification-question-tag"]');
    expect(tag?.textContent?.trim()).toBe('Soru 4');
    expect(items()[0].getAttribute('aria-label')).toContain('Soru 4');
    expect(items()[1].querySelector('[data-testid="notification-question-tag"]')).toBeNull();
  });

  it('readBadgeItem_Click_NavigatesWithoutMarkRead', () => {
    flushList([badgeEarned({ isRead: true })]);

    items()[0].click();

    httpMock.expectNone('/api/badge/notifications/10/read');
    expect(navigateSpy).toHaveBeenCalledWith([BADGE_PROGRESS_ROUTE]);
  });

  it('relativeTime_RefreshesEveryMinuteWhilePageOpen', fakeAsync(() => {
    // beforeEach'teki örnek gerçek zamanlayıcıyla kuruldu; tick testi için fakeAsync içinde yenisi oluşturulur.
    flushList([]);
    fixture.destroy();
    const local = TestBed.createComponent(NotificationsComponent);
    local.detectChanges();
    httpMock
      .expectOne((r) => r.url === LIST_URL)
      .flush([notification({ createdAt: new Date(Date.now() - 30 * 1000).toISOString() })]);
    local.detectChanges();
    const time = () => (local.nativeElement as HTMLElement).querySelector('.ntf__time')?.textContent?.trim();
    expect(time()).toBe('şimdi');

    tick(RELATIVE_TIME_TICK_MS);
    local.detectChanges();

    expect(time()).toBe('1 dakika önce');
    local.destroy();
  }));

  it('readItem_Click_DoesNotCallMarkRead', () => {
    flushList([notification({ id: 3, isRead: true })]);

    items()[0].click();
    httpMock.expectNone('/api/badge/notifications/3/read');
  });

  it('markRead_Fails_RestoresUnreadHighlight', () => {
    flushList([notification({ id: 4 })]);

    items()[0].click();
    httpMock.expectOne('/api/badge/notifications/4/read').flush(null, { status: 500, statusText: 'Error' });
    fixture.detectChanges();

    expect(items()[0].classList).toContain('is-unread');
  });

  it('markRead_Success_DecrementsUnreadCount', () => {
    const service = TestBed.inject(NotificationService);
    service.refreshUnreadCount().subscribe();
    httpMock.expectOne('/api/badge/notifications/me/unread-count').flush(3);
    flushList([notification({ id: 8 })]);

    items()[0].click();
    httpMock.expectOne('/api/badge/notifications/8/read').flush(null, { status: 204, statusText: 'No Content' });

    expect(service.unreadCount()).toBe(2);
  });

  it('emptyList_ShowsEmptyState', () => {
    flushList([]);

    expect(items().length).toBe(0);
    expect(el().querySelector('.ntf__state--empty')?.textContent).toContain(notificationsTr.empty.all);
  });

  it('unreadFilter_RequestsUnreadOnlyAndShowsUnreadEmptyText', () => {
    flushList([notification()]);

    const unreadToggle = Array.from(el().querySelectorAll<HTMLButtonElement>('mat-button-toggle button')).find((b) =>
      b.textContent?.includes(notificationsTr.filter.unread)
    );
    unreadToggle?.click();
    fixture.detectChanges();
    flushList([], true);

    expect(el().querySelector('.ntf__state--empty')?.textContent).toContain(notificationsTr.empty.unread);
  });

  it('loadError_ShowsErrorAndRetryReloads', () => {
    httpMock.expectOne((r) => r.url === LIST_URL).flush(null, { status: 500, statusText: 'Error' });
    fixture.detectChanges();

    const alert = el().querySelector('[role="alert"]');
    expect(alert?.textContent).toContain(notificationsTr.loadError);

    alert?.querySelector('button')?.click();
    fixture.detectChanges();
    flushList([notification()]);
    expect(items().length).toBe(1);
  });
});
