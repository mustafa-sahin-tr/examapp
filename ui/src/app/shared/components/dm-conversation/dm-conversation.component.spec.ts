import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of } from 'rxjs';

import { DmConversationComponent, DmMessageSentEvent } from './dm-conversation.component';
import { ConversationMessages, DirectMessage } from '../../../models/direct-message.model';
import { translocoTestingModule } from '../../testing/transloco-testing';
import rootTr from '../../../../../public/i18n/tr.json';
import dmTr from '../../../../../public/i18n/direct-messages/tr.json';

const BASE = '/api/exam/direct-messages';

function msg(id: number, isMine: boolean, body = `mesaj ${id}`): DirectMessage {
  return {
    id,
    conversationId: 5,
    body,
    senderRole: isMine ? 'Student' : 'Teacher',
    isMine,
    sentAt: '2026-10-05T10:00:00',
  };
}

function page(overrides: Partial<ConversationMessages> = {}): ConversationMessages {
  return {
    conversationId: 5,
    counterpartName: 'Ayşe Öğretmen',
    counterpartAvatar: '',
    canSend: true,
    items: [msg(10, false), msg(11, true)],
    hasMore: false,
    nextBeforeId: null,
    ...overrides,
  };
}

describe('DmConversationComponent (issue #106)', () => {
  let fixture: ComponentFixture<DmConversationComponent>;
  let http: HttpTestingController;
  let dialog: { open: jasmine.Spy };
  let snackBar: jasmine.SpyObj<MatSnackBar>;
  let dialogResult: unknown;

  const el = () => fixture.nativeElement as HTMLElement;
  const q = <T extends HTMLElement = HTMLElement>(id: string) => el().querySelector<T>(`[data-testid="${id}"]`);
  const qa = (id: string) => Array.from(el().querySelectorAll<HTMLElement>(`[data-testid="${id}"]`));

  function create(inputs: { role?: 'Student' | 'Teacher'; conversationId?: number | null; teacherId?: number | null; unread?: number | null; name?: string }): void {
    dialogResult = undefined;
    dialog = { open: jasmine.createSpy('open').and.callFake(() => ({ afterClosed: () => of(dialogResult) })) };
    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);
    TestBed.configureTestingModule({
      imports: [
        DmConversationComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { tr: { ...rootTr, 'direct-messages': dmTr }, 'direct-messages/tr': dmTr } }),
      ],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(DmConversationComponent);
    fixture.componentRef.setInput('viewerRole', inputs.role ?? 'Student');
    fixture.componentRef.setInput('conversationId', inputs.conversationId === undefined ? 5 : inputs.conversationId);
    fixture.componentRef.setInput('teacherId', inputs.teacherId ?? null);
    fixture.componentRef.setInput('unreadCount', inputs.unread === undefined ? 1 : inputs.unread);
    fixture.componentRef.setInput('counterpartName', inputs.name ?? 'Ayşe Öğretmen');
    fixture.detectChanges();
  }

  function flushLoad(body: ConversationMessages = page()): void {
    http.expectOne((r) => r.url === `${BASE}/conversations/5/messages`).flush(body);
    fixture.detectChanges();
  }

  function type(value: string): void {
    const input = q<HTMLTextAreaElement>('dm-input')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('load_ShowsSpinnerThenLog_WithRoleLogAndAriaLivePolite', () => {
    create({});
    expect(q('dm-loading')).not.toBeNull();

    flushLoad();
    http.expectOne(`${BASE}/conversations/5/read`).flush({ success: true, markedCount: 1 });

    const log = q('dm-log')!;
    expect(log.getAttribute('role')).toBe('log');
    expect(log.getAttribute('aria-live')).toBe('polite');
    expect(qa('dm-message').length).toBe(2);
    expect(qa('dm-message')[1].classList).toContain('dmc__row--mine');
    expect(q('dm-counterpart')?.textContent?.trim()).toBe('Ayşe Öğretmen');
  });

  it('body_RenderedAsPlainText_NoHtmlInterpretation', () => {
    create({ unread: 0 });
    flushLoad(page({ items: [msg(10, false, '<b>kalın</b><img src=x onerror=alert(1)>')] }));

    const body = q('dm-message-body')!;
    expect(body.querySelector('b')).toBeNull();
    expect(body.querySelector('img')).toBeNull();
    expect(body.textContent).toContain('<b>kalın</b>');
  });

  it('markRead_CalledWithNewestLoadedId_EmitsMarkedRead; olderPage_DoesNotMarkRead', () => {
    create({ unread: 2 });
    const marked: unknown[] = [];
    fixture.componentInstance.markedRead.subscribe((e) => marked.push(e));
    flushLoad(page({ items: [msg(40, false), msg(41, false)], hasMore: true, nextBeforeId: 40 }));

    const read = http.expectOne(`${BASE}/conversations/5/read`);
    expect(read.request.body).toEqual({ upToMessageId: 41 });
    read.flush({ success: true, conversationId: 5, markedCount: 2 });
    expect(marked).toEqual([{ conversationId: 5, markedCount: 2 }]);

    q('dm-load-older')!.click();
    fixture.detectChanges();
    // U4: eski sayfa yüklenirken log meşgul — ekran okuyucu eklenen eski mesajları okumasın.
    expect(q('dm-log')?.getAttribute('aria-busy')).toBe('true');
    const older = http.expectOne((r) => r.url === `${BASE}/conversations/5/messages`);
    expect(older.request.params.get('beforeId')).toBe('40');
    older.flush(page({ items: [msg(30, false), msg(31, true)], hasMore: false }));
    fixture.detectChanges();

    http.expectNone(`${BASE}/conversations/5/read`);
    expect(q('dm-log')?.hasAttribute('aria-busy')).toBeFalse();
    expect(qa('dm-message').length).toBe(4);
    expect(q('dm-load-older')).toBeNull();
  });

  it('markRead_NotCalled_WhenListSaysNoUnread', () => {
    create({ unread: 0 });
    flushLoad();
    http.expectNone(`${BASE}/conversations/5/read`);
    expect(qa('dm-message').length).toBe(2);
    expect(q('dm-log')).not.toBeNull();
  });

  it('loadError_ShowsRetry_ThatReloads', () => {
    create({ unread: 0 });
    http
      .expectOne((r) => r.url === `${BASE}/conversations/5/messages`)
      .flush({ errorCode: 'ConversationNotFound' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(q('dm-load-error')?.textContent).toContain(dmTr.errors.codes.ConversationNotFound);
    q('dm-retry')!.click();
    fixture.detectChanges();
    flushLoad();
    expect(qa('dm-message').length).toBe(2);
  });

  it('counter_And_TooLong_DisableSend', () => {
    create({ unread: 0 });
    flushLoad();

    expect(q<HTMLButtonElement>('dm-send')!.disabled).toBeTrue();
    type('Merhaba');
    expect(q('dm-counter')?.textContent?.trim()).toBe('7/2000');
    expect(q<HTMLButtonElement>('dm-send')!.disabled).toBeFalse();
    expect(q('dm-send')?.getAttribute('aria-label')).toBe(dmTr.conversation.sendAria);

    type('x'.repeat(2001));
    expect(q('dm-too-long')).not.toBeNull();
    expect(q<HTMLButtonElement>('dm-send')!.disabled).toBeTrue();
  });

  it('send_PostsTrimmedBody_AppendsMessage_ClearsDraft', () => {
    create({ unread: 0 });
    flushLoad();
    const sent: DmMessageSentEvent[] = [];
    fixture.componentInstance.messageSent.subscribe((e) => sent.push(e));

    type('  Ödev hakkında  ');
    q('dm-send')!.click();
    const req = http.expectOne(`${BASE}/conversations/5/messages`);
    expect(req.request.body).toEqual({ body: 'Ödev hakkında' });
    req.flush({ success: true, conversationId: 5, conversationCreated: false, directMessage: msg(12, true, 'Ödev hakkında') });
    fixture.detectChanges();

    expect(qa('dm-message').length).toBe(3);
    expect(q<HTMLTextAreaElement>('dm-input')!.value).toBe('');
    expect(sent[0]).toEqual(jasmine.objectContaining({ conversationId: 5, conversationCreated: false }));
  });

  it('ctrlEnter_Sends', () => {
    create({ unread: 0 });
    flushLoad();
    type('Kısa');
    q('dm-input')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true }));
    const req = http.expectOne(`${BASE}/conversations/5/messages`);
    expect(req.request.body).toEqual({ body: 'Kısa' });
    req.flush({ success: true, conversationId: 5 });
  });

  it('student_Send403CannotMessageTeacher_ShowsNeutralInfo_DisablesInput_NoBlockWording', () => {
    create({ unread: 0 });
    flushLoad();
    type('Merhaba');
    q('dm-send')!.click();
    http
      .expectOne(`${BASE}/conversations/5/messages`)
      .flush(
        { success: false, message: 'Öğretmen sizi engelledi', errorCode: 'CannotMessageTeacher' },
        { status: 403, statusText: 'Forbidden' },
      );
    fixture.detectChanges();

    const info = q('dm-cannot-send')!;
    expect(info.querySelector('span')?.textContent?.trim()).toBe(dmTr.conversation.cannotSend);
    expect(info.getAttribute('role')).toBe('status');
    expect(el().textContent).not.toContain('engel');
    expect(q('dm-send-error')).toBeNull();
    expect(q<HTMLTextAreaElement>('dm-input')!.disabled).toBeTrue();
    expect(q<HTMLButtonElement>('dm-send')!.disabled).toBeTrue();
  });

  it('student_CanSendFalseOnLoad_ShowsNeutralInfo', () => {
    create({ unread: 0 });
    flushLoad(page({ canSend: false }));

    expect(q('dm-cannot-send')?.querySelector('span')?.textContent?.trim()).toBe(dmTr.conversation.cannotSend);
    expect(q<HTMLTextAreaElement>('dm-input')!.disabled).toBeTrue();
  });

  it('teacher_Send403RelationshipEnded_ShowsRelationshipBand', () => {
    create({ role: 'Teacher', unread: 0, name: 'Ali Öğrenci' });
    flushLoad(page({ counterpartName: 'Ali Öğrenci', isBlocked: false }));
    type('Cevap');
    q('dm-send')!.click();
    http
      .expectOne(`${BASE}/conversations/5/messages`)
      .flush({ errorCode: 'RelationshipEnded' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(q('dm-cannot-send')?.querySelector('span')?.textContent?.trim()).toBe(dmTr.conversation.relationshipEnded);
    expect(q<HTMLTextAreaElement>('dm-input')!.disabled).toBeTrue();
  });

  it('send_RateLimited_ShowsAlert_KeepsDraft', () => {
    create({ unread: 0 });
    flushLoad();
    type('Tekrar');
    q('dm-send')!.click();
    http
      .expectOne(`${BASE}/conversations/5/messages`)
      .flush({ errorCode: 'RateLimited', message: 'x' }, { status: 429, statusText: 'Too Many', headers: { 'Retry-After': '30' } });
    fixture.detectChanges();

    expect(q('dm-send-error')?.getAttribute('role')).toBe('alert');
    expect(q('dm-send-error')?.textContent).toContain('30');
    expect(q<HTMLTextAreaElement>('dm-input')!.value).toBe('Tekrar');
  });

  it('newConversation_SendsToTeacher_ThenBindsConversation', () => {
    create({ conversationId: null, teacherId: 7, unread: null });
    http.expectNone((r) => r.url.includes('/messages'));
    expect(q('dm-empty')?.textContent?.trim()).toBe(dmTr.conversation.emptyNew);
    expect(q('dm-report-conversation')).toBeNull();
    const sent: DmMessageSentEvent[] = [];
    fixture.componentInstance.messageSent.subscribe((e) => sent.push(e));

    type('İlk mesaj');
    q('dm-send')!.click();
    const req = http.expectOne(`${BASE}/teachers/7/messages`);
    expect(req.request.body).toEqual({ body: 'İlk mesaj' });
    req.flush({ success: true, conversationId: 9, conversationCreated: true, directMessage: { ...msg(1, true, 'İlk mesaj'), conversationId: 9 } });
    fixture.detectChanges();

    expect(sent[0]).toEqual(jasmine.objectContaining({ conversationId: 9, conversationCreated: true }));
    expect(q('dm-report-conversation')).not.toBeNull();

    // U2: sayfa yeni Id'yi input'a geri yazınca panel yeniden yüklenmez, gönderilen mesaj yerinde kalır.
    fixture.componentRef.setInput('conversationId', 9);
    fixture.detectChanges();
    http.expectNone((r) => r.url.includes('/conversations/9/messages'));
    expect(qa('dm-message').length).toBe(1);
  });

  it('teacher_BlockFlow_ConfirmThenPost_UpdatesBandAndEmits', () => {
    create({ role: 'Teacher', unread: 0, name: 'Ali Öğrenci' });
    flushLoad(page({ counterpartName: 'Ali Öğrenci', isBlocked: false }));
    const changes: unknown[] = [];
    fixture.componentInstance.blockedChange.subscribe((e) => changes.push(e));

    const toggle = q<HTMLButtonElement>('dm-block-toggle')!;
    expect(toggle.getAttribute('aria-label')).toBe('Ali Öğrenci adlı öğrenciyi engelle');
    dialogResult = true;
    toggle.click();
    expect(dialog.open.calls.mostRecent().args[1].data).toEqual({ block: true, studentName: 'Ali Öğrenci' });
    http
      .expectOne(`${BASE}/conversations/5/block`)
      .flush({ success: true, conversationId: 5, isBlocked: true, changed: true });
    fixture.detectChanges();

    expect(q('dm-blocked-band')?.querySelector('span')?.textContent?.trim()).toBe(dmTr.conversation.blockedBand);
    expect(q('dm-blocked-tag')).not.toBeNull();
    expect(changes).toEqual([{ conversationId: 5, isBlocked: true }]);
    expect(snackBar.open).toHaveBeenCalledWith(dmTr.conversation.blockedDone, jasmine.any(String), jasmine.any(Object));
    // Engel öğretmenin cevabını kapatmaz.
    expect(q<HTMLTextAreaElement>('dm-input')!.disabled).toBeFalse();
  });

  it('teacher_Unblock_CancelledDialog_NoRequest', () => {
    create({ role: 'Teacher', unread: 0 });
    flushLoad(page({ isBlocked: true }));

    dialogResult = false;
    q('dm-block-toggle')!.click();
    http.expectNone(`${BASE}/conversations/5/unblock`);

    dialogResult = true;
    q('dm-block-toggle')!.click();
    http.expectOne(`${BASE}/conversations/5/unblock`).flush({ success: true, isBlocked: false });
    fixture.detectChanges();
    expect(q('dm-blocked-band')).toBeNull();
  });

  it('student_HasNoBlockAction', () => {
    create({ unread: 0 });
    flushLoad();
    expect(q('dm-block-toggle')).toBeNull();
  });

  it('reportMessage_OnlyOnCounterpartMessages_SuccessShowsReceived', () => {
    create({ unread: 0 });
    flushLoad();

    expect(qa('dm-report-message').length).toBe(1);
    dialogResult = { alreadyReported: false };
    qa('dm-report-message')[0].click();

    expect(dialog.open.calls.mostRecent().args[1].data).toEqual({ conversationId: 5, messageId: 10 });
    expect(snackBar.open).toHaveBeenCalledWith(dmTr.conversation.reported, jasmine.any(String), jasmine.any(Object));
  });

  it('reportConversation_NoMessageId_AlreadyReportedText', () => {
    create({ unread: 0 });
    flushLoad();

    dialogResult = { alreadyReported: true };
    q('dm-report-conversation')!.click();

    expect(dialog.open.calls.mostRecent().args[1].data).toEqual({ conversationId: 5, messageId: undefined });
    expect(snackBar.open).toHaveBeenCalledWith(dmTr.conversation.alreadyReported, jasmine.any(String), jasmine.any(Object));
  });

  it('back_EmitsAndHasAriaLabel', () => {
    create({ unread: 0 });
    flushLoad();
    let backs = 0;
    fixture.componentInstance.back.subscribe(() => backs++);

    const back = q('dm-back')!;
    expect(back.getAttribute('aria-label')).toBe(dmTr.conversation.back);
    back.click();
    expect(backs).toBe(1);
  });

  it('sending_TextareaReadonlyNotDisabled_KeepsFocusable', () => {
    create({ unread: 0 });
    flushLoad();
    type('Bekle');
    q('dm-send')!.click();
    fixture.detectChanges();

    const input = q<HTMLTextAreaElement>('dm-input')!;
    expect(input.readOnly).toBeTrue();
    expect(input.disabled).toBeFalse();
    http.expectOne(`${BASE}/conversations/5/messages`).flush({ success: true, conversationId: 5 });
    fixture.detectChanges();
    expect(input.readOnly).toBeFalse();
  });

  it('avatar_OnlyRootRelativePathRendered_WithNoReferrer', () => {
    create({ unread: 0 });
    flushLoad(page({ counterpartAvatar: '/media/avatars/a.png' }));
    const img = q<HTMLImageElement>('dm-avatar-img')!;
    expect(img.getAttribute('src')).toBe('/media/avatars/a.png');
    expect(img.getAttribute('referrerpolicy')).toBe('no-referrer');
  });

  for (const avatar of [
    'https://tracker.example/p.png',
    '//evil.example/a.png',
    '/\\evil.example/a.png',
    'data:image/png;base64,AAA',
    'javascript:alert(1)',
  ]) {
    it(`avatar_Rejected_FallsBackToInitial: ${avatar}`, () => {
      create({ unread: 0 });
      flushLoad(page({ counterpartAvatar: avatar }));
      expect(q('dm-avatar-img')).toBeNull();
      expect(el().querySelector('.dmc__avatar')?.textContent?.trim()).toBe('A');
    });
  }

  it('section_LabelledByHeading_LogKeepsOwnLabel', () => {
    create({ unread: 0 });
    flushLoad();
    const section = el().querySelector('section.dmc')!;
    expect(section.getAttribute('aria-labelledby')).toBe('dm-conversation-title');
    expect(section.hasAttribute('aria-label')).toBeFalse();
    expect(q('dm-counterpart')?.id).toBe('dm-conversation-title');
  });
});
