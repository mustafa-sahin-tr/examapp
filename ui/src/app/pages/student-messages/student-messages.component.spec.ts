import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of } from 'rxjs';

import { StudentMessagesComponent } from './student-messages.component';
import { ConversationSummary } from '../../models/direct-message.model';
import { DirectMessageService } from '../../services/direct-message.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import rootTr from '../../../../public/i18n/tr.json';
import dmTr from '../../../../public/i18n/direct-messages/tr.json';

const BASE = '/api/exam/direct-messages';

function conv(id: number, unread = 0, isBlocked = false): ConversationSummary {
  return {
    conversationId: id,
    counterpartName: `Öğrenci ${id}`,
    counterpartAvatar: '',
    studentId: 200 + id,
    lastMessageAt: '2026-10-05T10:00:00',
    lastMessagePreview: `önizleme ${id}`,
    lastMessageIsMine: false,
    unreadCount: unread,
    isBlocked,
  };
}

describe('StudentMessagesComponent — öğretmen gelen kutusu (issue #106)', () => {
  let fixture: ComponentFixture<StudentMessagesComponent>;
  let http: HttpTestingController;
  let dialogResult: unknown;

  const el = () => fixture.nativeElement as HTMLElement;
  const q = <T extends HTMLElement = HTMLElement>(id: string) => el().querySelector<T>(`[data-testid="${id}"]`);
  const qa = (id: string) => Array.from(el().querySelectorAll<HTMLElement>(`[data-testid="${id}"]`));

  const isInboxList = (r: { url: string; params: { get(k: string): string | null } }) =>
    r.url === `${BASE}/inbox` && r.params.get('pageSize') === '20';
  const isBadge = (r: { url: string; params: { get(k: string): string | null } }) =>
    r.url === `${BASE}/inbox` && r.params.get('pageSize') === '1';

  function create(): void {
    dialogResult = undefined;
    TestBed.configureTestingModule({
      imports: [
        StudentMessagesComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { tr: { ...rootTr, 'direct-messages': dmTr }, 'direct-messages/tr': dmTr } }),
      ],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({}) } } },
        { provide: MatDialog, useValue: { open: () => ({ afterClosed: () => of(dialogResult) }) } },
        { provide: MatSnackBar, useValue: jasmine.createSpyObj('MatSnackBar', ['open']) },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StudentMessagesComponent);
    fixture.detectChanges();
  }

  function flushInbox(items: ConversationSummary[], unreadTotal = items.filter((i) => i.unreadCount > 0).length, totalCount = items.length) {
    const req = http.expectOne(isInboxList);
    req.flush({ items, page: 1, pageSize: 20, totalCount });
    http.expectOne(isBadge).flush({ items: [], page: 1, pageSize: 1, totalCount: unreadTotal });
    fixture.detectChanges();
    return req;
  }

  function openFirstRow(isBlocked = false): void {
    qa('dm-conversation-row')[0].click();
    fixture.detectChanges();
    http.expectOne((r) => r.url.endsWith('/messages')).flush({
      conversationId: 1,
      counterpartName: 'Öğrenci 1',
      counterpartAvatar: '',
      canSend: true,
      isBlocked,
      items: [{ id: 9, conversationId: 1, body: 'soru', senderRole: 'Student', isMine: false, sentAt: '2026-10-05T10:00:00' }],
      hasMore: false,
    });
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('initialLoad_AllFilter_RowsWithUnreadDotAndBlockedTag_BadgeSet', () => {
    create();
    expect(q('sm-loading')).not.toBeNull();

    const req = flushInbox([conv(1, 2), conv(2, 0, true)]);

    expect(req.request.params.get('filter')).toBe('all');
    expect(qa('dm-conversation-row').length).toBe(2);
    expect(qa('dm-row-unread').length).toBe(1);
    expect(qa('dm-row-blocked').length).toBe(1);
    expect(q('dm-row-blocked')?.textContent?.trim()).toBe('Engellendi');
    expect(TestBed.inject(DirectMessageService).unreadCount()).toBe(1);
    expect(q('sm-filter')?.getAttribute('aria-label')).toBe(dmTr.teacher.filterLabel);
  });

  it('filterChange_RequestsFilteredInbox_AndEmptyTextPerFilter', () => {
    create();
    flushInbox([conv(1)]);

    q('sm-filter-blocked')!.querySelector('button')!.click();
    fixture.detectChanges();
    const req = http.expectOne(isInboxList);
    expect(req.request.params.get('filter')).toBe('blocked');
    expect(req.request.params.get('page')).toBe('1');
    req.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    http.expectOne(isBadge).flush({ items: [], totalCount: 0 });
    fixture.detectChanges();

    expect(q('sm-empty')?.textContent?.trim()).toBe(dmTr.teacher.empty.blocked);

    q('sm-filter-unread')!.querySelector('button')!.click();
    fixture.detectChanges();
    expect(http.expectOne(isInboxList).request.params.get('filter')).toBe('unread');
  });

  it('error_ShowsRetry_ThatReloads', () => {
    create();
    http.expectOne(isInboxList).flush({}, { status: 500, statusText: 'err' });
    fixture.detectChanges();

    expect(q('sm-error')?.textContent).toContain(dmTr.teacher.error);
    q('sm-retry')!.click();
    flushInbox([conv(1)]);
    expect(qa('dm-conversation-row').length).toBe(1);
  });

  it('paging_RequestsSecondPage', () => {
    create();
    flushInbox([conv(1)], 0, 30);

    q('sm-paginator')!.querySelector<HTMLButtonElement>('.mat-mdc-paginator-navigation-next')!.click();
    const req = http.expectOne(isInboxList);
    expect(req.request.params.get('page')).toBe('2');
    req.flush({ items: [], page: 2, pageSize: 20, totalCount: 30 });
    http.expectOne(isBadge).flush({ totalCount: 0 });
  });

  it('openConversation_MarksRead_ClearsDot_DecrementsBadge', () => {
    create();
    flushInbox([conv(1, 2), conv(2, 1)]);
    expect(TestBed.inject(DirectMessageService).unreadCount()).toBe(2);

    openFirstRow();
    const read = http.expectOne(`${BASE}/conversations/1/read`);
    expect(read.request.body).toEqual({ upToMessageId: 9 });
    read.flush({ success: true, conversationId: 1, markedCount: 1 });
    fixture.detectChanges();

    expect(TestBed.inject(DirectMessageService).unreadCount()).toBe(1);
    expect(qa('dm-row-unread').length).toBe(1);
    expect(q('dm-block-toggle')).not.toBeNull();
  });

  it('blockFromPanel_UpdatesRowTag', () => {
    create();
    flushInbox([conv(1)]);
    openFirstRow(false);
    expect(qa('dm-row-blocked').length).toBe(0);

    dialogResult = true;
    q('dm-block-toggle')!.click();
    http.expectOne(`${BASE}/conversations/1/block`).flush({ success: true, conversationId: 1, isBlocked: true, changed: true });
    fixture.detectChanges();

    expect(qa('dm-row-blocked').length).toBe(1);
    expect(q('dm-blocked-band')).not.toBeNull();
  });

  it('reply_MovesRowToTopWithMinePreview', () => {
    create();
    flushInbox([conv(2), conv(1)]);
    qa('dm-conversation-row')[1].click();
    fixture.detectChanges();
    http.expectOne((r) => r.url.endsWith('/conversations/1/messages')).flush({
      conversationId: 1, counterpartName: 'Öğrenci 1', counterpartAvatar: '', canSend: true, isBlocked: false, items: [], hasMore: false,
    });
    fixture.detectChanges();

    const input = q<HTMLTextAreaElement>('dm-input')!;
    input.value = 'Cevabım';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    q('dm-send')!.click();
    http.expectOne(`${BASE}/conversations/1/messages`).flush({
      success: true,
      conversationId: 1,
      conversationCreated: false,
      directMessage: { id: 20, conversationId: 1, body: 'Cevabım', senderRole: 'Teacher', isMine: true, sentAt: '2026-10-06T08:00:00' },
    });
    fixture.detectChanges();

    const first = qa('dm-conversation-row')[0];
    expect(first.textContent).toContain('Öğrenci 1');
    expect(first.querySelector('[data-testid="dm-row-preview"]')?.textContent).toContain('Cevabım');
  });

  it('markRead_ZeroMarkedCount_DoesNotDecrementBadge', () => {
    create();
    flushInbox([conv(1, 2)]);

    openFirstRow();
    http.expectOne(`${BASE}/conversations/1/read`).flush({ success: true, conversationId: 1, markedCount: 0 });
    fixture.detectChanges();

    expect(TestBed.inject(DirectMessageService).unreadCount()).toBe(1);
    expect(qa('dm-row-unread').length).toBe(0);
  });

  it('unreadFilterFirstPage_SetsBadgeFromTotal_WithoutSecondRequest', () => {
    create();
    flushInbox([conv(1, 1)]);

    q('sm-filter-unread')!.querySelector('button')!.click();
    fixture.detectChanges();
    http.expectOne(isInboxList).flush({ items: [conv(1, 1)], page: 1, pageSize: 20, totalCount: 7 });
    fixture.detectChanges();

    http.expectNone(isBadge);
    expect(TestBed.inject(DirectMessageService).unreadCount()).toBe(7);
  });

  it('inboxSection_LabelledByHiddenHeading', () => {
    create();
    flushInbox([conv(1)]);
    const section = el().querySelector('section.dmp__list')!;
    expect(section.getAttribute('aria-labelledby')).toBe('sm-inbox-title');
    expect(section.hasAttribute('aria-label')).toBeFalse();
  });
});
