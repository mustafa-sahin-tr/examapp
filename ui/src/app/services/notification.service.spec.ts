import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { NOTIFICATION_LIST_TAKE, NotificationService } from './notification.service';
import { AppNotification } from '../models/notification.model';

describe('NotificationService (issue #146)', () => {
  const base = '/api/badge/notifications';
  let service: NotificationService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(NotificationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list_UnreadOnlyTrue_GetsMeWithUnreadOnlyAndTakeParams', () => {
    const items: AppNotification[] = [
      { id: 1, type: 'BadgeEarned', title: 'T', body: 'B', data: null, isRead: false, createdAt: '2026-09-29T10:00:00Z' },
    ];
    let result: AppNotification[] | undefined;

    service.list(true).subscribe((r) => (result = r));

    const req = httpMock.expectOne((r) => r.url === `${base}/me`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('unreadOnly')).toBe('true');
    expect(req.request.params.get('take')).toBe(String(NOTIFICATION_LIST_TAKE));
    req.flush(items);
    expect(result).toEqual(items);
  });

  it('list_UnreadOnlyFalse_SendsFalse', () => {
    service.list(false).subscribe();

    const req = httpMock.expectOne((r) => r.url === `${base}/me`);
    expect(req.request.params.get('unreadOnly')).toBe('false');
    req.flush([]);
  });

  it('refreshUnreadCount_Success_GetsUnreadCountAndUpdatesSignal', () => {
    service.refreshUnreadCount().subscribe();

    const req = httpMock.expectOne(`${base}/me/unread-count`);
    expect(req.request.method).toBe('GET');
    req.flush(4);
    expect(service.unreadCount()).toBe(4);
  });

  it('markRead_Success_PostsToIdReadAndDecrementsCount', () => {
    service.refreshUnreadCount().subscribe();
    httpMock.expectOne(`${base}/me/unread-count`).flush(2);

    service.markRead(42).subscribe();

    const req = httpMock.expectOne(`${base}/42/read`);
    expect(req.request.method).toBe('POST');
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(service.unreadCount()).toBe(1);
  });

  it('markRead_Pending_DecrementsOptimisticallyBeforeResponse', () => {
    service.refreshUnreadCount().subscribe();
    httpMock.expectOne(`${base}/me/unread-count`).flush(2);

    service.markRead(9).subscribe();
    expect(service.unreadCount()).toBe(1);

    httpMock.expectOne(`${base}/9/read`).flush(null, { status: 204, statusText: 'No Content' });
    expect(service.unreadCount()).toBe(1);
  });

  it('resetUnreadCount_SetsZero', () => {
    service.refreshUnreadCount().subscribe();
    httpMock.expectOne(`${base}/me/unread-count`).flush(5);

    service.resetUnreadCount();

    expect(service.unreadCount()).toBe(0);
  });

  it('markRead_Error_RestoresOptimisticDecrement', () => {
    service.refreshUnreadCount().subscribe();
    httpMock.expectOne(`${base}/me/unread-count`).flush(1);

    service.markRead(7).subscribe({ error: () => undefined });
    httpMock.expectOne(`${base}/7/read`).flush(null, { status: 404, statusText: 'Not Found' });

    expect(service.unreadCount()).toBe(1);
  });

  it('markRead_CountAlreadyZero_NeverGoesNegative', () => {
    service.markRead(3).subscribe();
    httpMock.expectOne(`${base}/3/read`).flush(null, { status: 204, statusText: 'No Content' });

    expect(service.unreadCount()).toBe(0);
  });
});
