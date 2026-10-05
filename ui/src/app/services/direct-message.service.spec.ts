import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { DirectMessageService } from './direct-message.service';

const BASE = '/api/exam/direct-messages';

describe('DirectMessageService (issue #106)', () => {
  let service: DirectMessageService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(DirectMessageService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const emptyPage = { items: [], page: 1, pageSize: 20, totalCount: 0 };

  it('getMessageableTeachers_SendsTrimmedSearchAndPaging', () => {
    service.getMessageableTeachers('  Ayşe ', 2, 20).subscribe();

    const req = http.expectOne((r) => r.url === `${BASE}/teachers`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('search')).toBe('Ayşe');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('20');
    req.flush(emptyPage);
  });

  it('getMessageableTeachers_ShorterThanTwoChars_OmitsSearch', () => {
    service.getMessageableTeachers(' a ', 1).subscribe();

    const req = http.expectOne((r) => r.url === `${BASE}/teachers`);
    expect(req.request.params.has('search')).toBeFalse();
    req.flush(emptyPage);
  });

  it('sendToTeacher_PostsBodyToTeacherUrl', () => {
    service.sendToTeacher(7, 'Merhaba').subscribe();

    const req = http.expectOne(`${BASE}/teachers/7/messages`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ body: 'Merhaba' });
    req.flush({ success: true, conversationId: 3, conversationCreated: true });
  });

  it('getStudentConversations_And_GetInbox_UseExpectedParams', () => {
    service.getStudentConversations(3).subscribe();
    const conv = http.expectOne((r) => r.url === `${BASE}/conversations`);
    expect(conv.request.params.get('page')).toBe('3');
    expect(conv.request.params.get('pageSize')).toBe('20');
    conv.flush(emptyPage);

    service.getInbox('blocked', 1).subscribe();
    const inbox = http.expectOne((r) => r.url === `${BASE}/inbox`);
    expect(inbox.request.params.get('filter')).toBe('blocked');
    expect(inbox.request.params.get('page')).toBe('1');
    inbox.flush(emptyPage);
  });

  it('getMessages_OptionalBeforeId', () => {
    service.getMessages(5).subscribe();
    const first = http.expectOne((r) => r.url === `${BASE}/conversations/5/messages`);
    expect(first.request.params.has('beforeId')).toBeFalse();
    expect(first.request.params.get('take')).toBe('30');
    first.flush({ items: [] });

    service.getMessages(5, 40).subscribe();
    const older = http.expectOne((r) => r.url === `${BASE}/conversations/5/messages`);
    expect(older.request.params.get('beforeId')).toBe('40');
    older.flush({ items: [] });
  });

  it('markRead_PostsUpToMessageId', () => {
    let markedCount = -1;
    service.markRead(5, 99).subscribe((r) => (markedCount = r.markedCount));

    const req = http.expectOne(`${BASE}/conversations/5/read`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ upToMessageId: 99 });
    req.flush({ success: true, conversationId: 5, markedCount: 4 });
    expect(markedCount).toBe(4);
  });

  it('sendToConversation_Block_Unblock_Urls', () => {
    service.sendToConversation(5, 'Cevap').subscribe();
    const send = http.expectOne(`${BASE}/conversations/5/messages`);
    expect(send.request.body).toEqual({ body: 'Cevap' });
    send.flush({});

    service.block(5).subscribe();
    expect(http.expectOne(`${BASE}/conversations/5/block`).request.method).toBe('POST');
    service.unblock(5).subscribe();
    expect(http.expectOne(`${BASE}/conversations/5/unblock`).request.method).toBe('POST');
  });

  it('report_TrimsNote_OmitsEmptyNoteAndMissingMessageId', () => {
    service.report(5, { reason: 'abuse', messageId: 12, note: '  kaba dil ' }).subscribe();
    expect(http.expectOne(`${BASE}/conversations/5/report`).request.body).toEqual({
      reason: 'abuse',
      messageId: 12,
      note: 'kaba dil',
    });

    service.report(5, { reason: 'spam', note: '   ' }).subscribe();
    expect(http.expectOne(`${BASE}/conversations/5/report`).request.body).toEqual({ reason: 'spam' });
  });

  it('refreshUnreadCount_Teacher_UsesUnreadFilterTotalCount', () => {
    service.refreshUnreadCount('Teacher').subscribe();

    const req = http.expectOne((r) => r.url === `${BASE}/inbox`);
    expect(req.request.params.get('filter')).toBe('unread');
    expect(req.request.params.get('pageSize')).toBe('1');
    req.flush({ ...emptyPage, totalCount: 4 });
    expect(service.unreadCount()).toBe(4);
  });

  it('refreshUnreadCount_Student_CountsConversationsWithUnread', () => {
    service.refreshUnreadCount('Student').subscribe();

    const req = http.expectOne((r) => r.url === `${BASE}/conversations`);
    expect(req.request.params.get('pageSize')).toBe('50');
    req.flush({
      ...emptyPage,
      items: [{ unreadCount: 2 }, { unreadCount: 0 }, { unreadCount: 1 }],
    });
    expect(service.unreadCount()).toBe(2);
  });

  it('markConversationRead_DecrementsButNotBelowZero_ResetClears', () => {
    service.refreshUnreadCount('Teacher').subscribe();
    http.expectOne((r) => r.url === `${BASE}/inbox`).flush({ ...emptyPage, totalCount: 1 });

    service.markConversationRead();
    service.markConversationRead();
    expect(service.unreadCount()).toBe(0);

    service.refreshUnreadCount('Teacher').subscribe();
    http.expectOne((r) => r.url === `${BASE}/inbox`).flush({ ...emptyPage, totalCount: 3 });
    service.resetUnreadCount();
    expect(service.unreadCount()).toBe(0);
  });

  it('refreshUnreadCount_ConcurrentCalls_ShareOneRequest', () => {
    const results: number[] = [];
    service.refreshUnreadCount('Teacher').subscribe((n) => results.push(n));
    service.refreshUnreadCount('Teacher').subscribe((n) => results.push(n));

    http.expectOne((r) => r.url === `${BASE}/inbox`).flush({ ...emptyPage, totalCount: 2 });
    expect(results).toEqual([2, 2]);

    // Tamamlandıktan sonra yeni çağrı yeni istek atar.
    service.refreshUnreadCount('Teacher').subscribe();
    http.expectOne((r) => r.url === `${BASE}/inbox`).flush({ ...emptyPage, totalCount: 0 });
  });

  it('setUnreadCount_ClampsAtZero', () => {
    service.setUnreadCount(5);
    expect(service.unreadCount()).toBe(5);
    service.setUnreadCount(-3);
    expect(service.unreadCount()).toBe(0);
  });
});
