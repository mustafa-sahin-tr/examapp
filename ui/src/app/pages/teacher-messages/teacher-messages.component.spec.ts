import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { TeacherMessagesComponent } from './teacher-messages.component';
import { ConversationSummary, MessageableTeacher } from '../../models/direct-message.model';
import { DirectMessageService } from '../../services/direct-message.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import rootTr from '../../../../public/i18n/tr.json';
import dmTr from '../../../../public/i18n/direct-messages/tr.json';

const BASE = '/api/exam/direct-messages';

function conv(id: number, unread = 0): ConversationSummary {
  return {
    conversationId: id,
    counterpartName: `Öğretmen ${id}`,
    counterpartAvatar: '',
    teacherId: 100 + id,
    lastMessageAt: '2026-10-05T10:00:00',
    lastMessagePreview: `önizleme ${id}`,
    lastMessageIsMine: false,
    unreadCount: unread,
  };
}

function teacher(id: number, relation: MessageableTeacher['relation'], conversationId: number | null = null): MessageableTeacher {
  return { teacherId: id, fullName: `Hoca ${id}`, avatar: '', relation, conversationId };
}

describe('TeacherMessagesComponent — öğrenci "Öğretmenime Yaz" (issue #106)', () => {
  let fixture: ComponentFixture<TeacherMessagesComponent>;
  let http: HttpTestingController;
  let queryConversation: string | null;

  const el = () => fixture.nativeElement as HTMLElement;
  const q = <T extends HTMLElement = HTMLElement>(id: string) => el().querySelector<T>(`[data-testid="${id}"]`);
  const qa = (id: string) => Array.from(el().querySelectorAll<HTMLElement>(`[data-testid="${id}"]`));

  const isConversations = (r: { url: string }) => r.url === `${BASE}/conversations`;
  const isTeachers = (r: { url: string }) => r.url === `${BASE}/teachers`;

  function create(): void {
    TestBed.configureTestingModule({
      imports: [
        TeacherMessagesComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { tr: { ...rootTr, 'direct-messages': dmTr }, 'direct-messages/tr': dmTr } }),
      ],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(queryConversation ? { conversation: queryConversation } : {}) } },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TeacherMessagesComponent);
    fixture.detectChanges();
  }

  /** Konuşma listesi isteği + ardından gelen rozet (pageSize=50) isteği. */
  function flushConversations(items: ConversationSummary[], totalCount = items.length): void {
    const list = http.expectOne((r) => isConversations(r) && r.params.get('pageSize') === '20');
    list.flush({ items, page: Number(list.request.params.get('page')), pageSize: 20, totalCount });
    http.expectOne((r) => isConversations(r) && r.params.get('pageSize') === '50').flush({ items, page: 1, pageSize: 50, totalCount });
    fixture.detectChanges();
  }

  function expectTeachers(): TestRequest {
    return http.expectOne(isTeachers);
  }

  function flushTeachers(req: TestRequest, items: MessageableTeacher[], extra: Record<string, unknown> = {}): void {
    req.flush({ items, page: 1, pageSize: 20, totalCount: items.length, ...extra });
    fixture.detectChanges();
  }

  function openTeachersTab(): void {
    el().querySelectorAll<HTMLElement>('[role="tab"]')[1].click();
    fixture.detectChanges();
  }

  /** mat-tab gövdesi sekme değişince asenkron bağlanır. */
  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => (queryConversation = null));
  afterEach(() => http.verify());

  it('conversations_LoadingThenRows_WithUnreadAria_AndBadgeRefreshed', () => {
    create();
    expect(q('tm-conversations-loading')).not.toBeNull();

    flushConversations([conv(1, 2), conv(2)]);

    expect(qa('dm-conversation-row').length).toBe(2);
    expect(qa('dm-row-unread').length).toBe(1);
    expect(el().textContent).toContain('2 okunmamış mesaj');
    expect(TestBed.inject(DirectMessageService).unreadCount()).toBe(1);
  });

  it('conversationsError_ShowsRetry', () => {
    create();
    http.expectOne(isConversations).flush({}, { status: 500, statusText: 'err' });
    fixture.detectChanges();

    expect(q('tm-conversations-error')?.textContent).toContain(dmTr.student.conversationsError);
    q('tm-conversations-error')!.querySelector('button')!.click();
    flushConversations([conv(1)]);
    expect(qa('dm-conversation-row').length).toBe(1);
  });

  it('noConversations_SwitchesToTeachersTab_AndLoadsTeachersWithRelationChips', async () => {
    create();
    flushConversations([]);
    await settle();

    const req = expectTeachers();
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.has('search')).toBeFalse();
    flushTeachers(req, [teacher(1, 'school'), teacher(2, 'assignment'), teacher(3, 'both')]);
    await settle();

    const rows = qa('tm-teacher');
    expect(rows.length).toBe(3);
    expect(rows[0].textContent).toContain('Okulun');
    expect(rows[1].textContent).toContain('Sana sınav atadı');
    expect(rows[2].querySelectorAll('[data-testid="tm-relation"]').length).toBe(2);
    expect(rows[2].querySelector('[data-testid="tm-relation-both"]')?.textContent?.trim()).toBe('Okulun · Sana sınav atadı');
  });

  it('teachersEmpty_ShowsEmptyText', async () => {
    create();
    flushConversations([]);
    await settle();
    flushTeachers(expectTeachers(), []);
    await settle();
    expect(q('tm-teachers-empty')?.textContent?.trim()).toBe(dmTr.student.teachersEmpty);
  });

  it('search_Debounced300ms_MinTwoChars_ResetsPage', fakeAsync(() => {
    create();
    flushConversations([conv(1)]);
    openTeachersTab();
    tick();
    fixture.detectChanges();
    flushTeachers(expectTeachers(), [teacher(1, 'school')]);
    tick();
    fixture.detectChanges();

    const input = q<HTMLInputElement>('tm-search')!;
    input.value = 'A';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    tick(300);
    // Tek karakter: istek yok, ipucu görünür.
    http.expectNone(isTeachers);
    expect(q('tm-search-min')?.textContent).toContain('2');

    input.value = 'Ay';
    input.dispatchEvent(new Event('input'));
    tick(299);
    http.expectNone(isTeachers);
    tick(1);
    const req = expectTeachers();
    expect(req.request.params.get('search')).toBe('Ay');
    expect(req.request.params.get('page')).toBe('1');
    flushTeachers(req, []);
    tick();
    fixture.detectChanges();
    expect(q('tm-teachers-empty')?.textContent).toContain('"Ay"');
  }));

  it('truncated_ShowsNarrowSearchInfo', async () => {
    create();
    flushConversations([]);
    await settle();
    flushTeachers(expectTeachers(), [teacher(1, 'school')], { truncated: true });
    await settle();
    expect(q('tm-truncated')?.textContent).toContain(dmTr.student.searchTruncated);
  });

  it('nameLookupUnavailable503_ShowsMeaningfulErrorAndRetry', async () => {
    create();
    flushConversations([]);
    await settle();
    expectTeachers().flush({ errorCode: 'NameLookupUnavailable', message: 'x' }, { status: 503, statusText: 'Unavailable' });
    await settle();
    fixture.detectChanges();

    expect(q('tm-teachers-error')?.textContent).toContain(dmTr.errors.codes.NameLookupUnavailable);
    q('tm-teachers-retry')!.click();
    flushTeachers(expectTeachers(), [teacher(1, 'school')]);
    await settle();
    expect(qa('tm-teacher').length).toBe(1);
  });

  it('teachersPaging_RequestsNextPage', async () => {
    create();
    flushConversations([]);
    await settle();
    flushTeachers(expectTeachers(), [teacher(1, 'school')], { totalCount: 45 });
    await settle();

    q('tm-teachers-paginator')!.querySelector<HTMLButtonElement>('.mat-mdc-paginator-navigation-next')!.click();
    const req = expectTeachers();
    expect(req.request.params.get('page')).toBe('2');
    flushTeachers(req, [teacher(21, 'school')], { page: 2, totalCount: 45 });
    await settle();
  });

  it('writeToChosenTeacher_WithoutConversation_OpensNewConversationPanel', async () => {
    create();
    flushConversations([]);
    await settle();
    flushTeachers(expectTeachers(), [teacher(7, 'assignment')]);
    await settle();

    expect(q<HTMLButtonElement>('tm-write')!.disabled).toBeTrue();
    q('tm-teacher')!.querySelector<HTMLInputElement>('input')!.click();
    fixture.detectChanges();
    const write = q<HTMLButtonElement>('tm-write')!;
    expect(write.disabled).toBeFalse();
    expect(write.getAttribute('aria-label')).toBe('Hoca 7 adlı öğretmene mesaj yaz');

    write.click();
    fixture.detectChanges();

    expect(q('tm-placeholder')).toBeNull();
    expect(q('dm-counterpart')?.textContent?.trim()).toBe('Hoca 7');
    expect(el().querySelector('.dmp__layout--open')).not.toBeNull();
    // Yeni konuşma: geçmiş isteği yok.
    http.expectNone((r) => r.url.includes('/messages'));
  });

  it('openConversation_LoadsMessages_MarkReadDecrementsBadgeAndClearsDot', () => {
    create();
    flushConversations([conv(5, 3), conv(6)]);
    expect(TestBed.inject(DirectMessageService).unreadCount()).toBe(1);

    qa('dm-conversation-row')[0].click();
    fixture.detectChanges();
    http.expectOne((r) => r.url === `${BASE}/conversations/5/messages`).flush({
      conversationId: 5,
      counterpartName: 'Öğretmen 5',
      counterpartAvatar: '',
      canSend: true,
      items: [{ id: 50, conversationId: 5, body: 'selam', senderRole: 'Teacher', isMine: false, sentAt: '2026-10-05T10:00:00' }],
      hasMore: false,
    });
    fixture.detectChanges();
    http.expectOne(`${BASE}/conversations/5/read`).flush({ success: true, conversationId: 5, markedCount: 3 });
    fixture.detectChanges();

    expect(TestBed.inject(DirectMessageService).unreadCount()).toBe(0);
    expect(qa('dm-row-unread').length).toBe(0);
    expect(qa('dm-conversation-row')[0].getAttribute('aria-current')).toBe('true');
  });

  it('back_ClosesPanel_ShowsPlaceholder', () => {
    create();
    flushConversations([conv(5)]);
    qa('dm-conversation-row')[0].click();
    fixture.detectChanges();
    http.expectOne((r) => r.url === `${BASE}/conversations/5/messages`).flush({
      conversationId: 5, counterpartName: 'Öğretmen 5', counterpartAvatar: '', canSend: true, items: [], hasMore: false,
    });
    fixture.detectChanges();

    q('dm-back')!.click();
    fixture.detectChanges();
    expect(q('tm-placeholder')).not.toBeNull();
    expect(el().querySelector('.dmp__layout--open')).toBeNull();
  });

  it('deepLink_QueryParamOpensConversation', () => {
    queryConversation = '5';
    create();
    flushConversations([conv(5)]);
    http.expectOne((r) => r.url === `${BASE}/conversations/5/messages`).flush({
      conversationId: 5, counterpartName: 'Öğretmen 5', counterpartAvatar: '', canSend: true, items: [], hasMore: false,
    });
    fixture.detectChanges();
    expect(q('dm-counterpart')?.textContent?.trim()).toBe('Öğretmen 5');
  });

  it('clearSearch_ReturnsFocusToSearchInput', fakeAsync(() => {
    create();
    flushConversations([conv(1)]);
    openTeachersTab();
    tick();
    fixture.detectChanges();
    flushTeachers(expectTeachers(), []);
    tick();
    fixture.detectChanges();

    const input = q<HTMLInputElement>('tm-search')!;
    input.value = 'Ay';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    const focus = spyOn(input, 'focus').and.callThrough();
    q('tm-search-clear')!.click();
    fixture.detectChanges();

    expect(focus).toHaveBeenCalled();
    tick(300);
    // 'Ay' debounce'tan önce temizlendi: '' zaten yüklü olduğundan yeni istek yok.
    http.expectNone(isTeachers);
  }));
});
