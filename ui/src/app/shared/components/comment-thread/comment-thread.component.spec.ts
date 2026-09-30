import { signal } from '@angular/core';
import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import {
  COMMENT_CONTEXT_DEBOUNCE_MS,
  COMMENT_HIGHLIGHT_MAX_PAGES,
  COMMENT_HIGHLIGHT_MS,
  CommentThreadComponent,
} from './comment-thread.component';
import {
  WorksheetComment,
  WorksheetCommentPage,
  WorksheetCommentRoot,
} from '../../../models/worksheet-comment.model';
import { LocaleService } from '../../../services/locale.service';
import { localeDefinitionOf } from '../../../models/locale';
import { translocoTestingModule } from '../../testing/transloco-testing';
import rootTr from '../../../../../public/i18n/tr.json';
import commentsTr from '../../../../../public/i18n/comments/tr.json';

const BASE = '/api/exam/worksheet/12/comments';

function comment(overrides: Partial<WorksheetComment> = {}): WorksheetComment {
  return {
    id: 1,
    worksheetId: 12,
    questionId: null,
    parentCommentId: null,
    authorDisplayName: 'Ali K.',
    authorRole: 'Student',
    isMine: false,
    body: 'Bu soruyu anlamadım',
    createdAt: '2026-09-30T10:00:00Z',
    ...overrides,
  };
}

function root(overrides: Partial<WorksheetCommentRoot> = {}): WorksheetCommentRoot {
  return { ...comment(), replies: [], replyCount: 0, canReply: true, ...overrides };
}

function page(overrides: Partial<WorksheetCommentPage> = {}): WorksheetCommentPage {
  return { items: [], nextCursor: null, canWrite: true, lockReason: null, ...overrides };
}

describe('CommentThreadComponent (issue #105)', () => {
  let fixture: ComponentFixture<CommentThreadComponent>;
  let http: HttpTestingController;

  const el = () => fixture.nativeElement as HTMLElement;
  const q = (id: string) => el().querySelector<HTMLElement>(`[data-testid="${id}"]`);
  const qa = (id: string) => Array.from(el().querySelectorAll<HTMLElement>(`[data-testid="${id}"]`));

  function create(inputs: Record<string, unknown> = {}): void {
    TestBed.configureTestingModule({
      imports: [
        CommentThreadComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { tr: { ...rootTr, comments: commentsTr }, 'comments/tr': commentsTr } }),
      ],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: LocaleService,
          useValue: { locale: signal('tr').asReadonly(), localeDefinition: signal(localeDefinitionOf('tr')).asReadonly() },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CommentThreadComponent);
    fixture.componentRef.setInput('worksheetId', 12);
    Object.entries(inputs).forEach(([key, value]) => fixture.componentRef.setInput(key, value));
    fixture.detectChanges();
  }

  function expectThread(questionId: number | null = null, cursor: string | null = null): TestRequest {
    const req = http.expectOne(
      (r) =>
        r.method === 'GET' &&
        r.url === BASE &&
        r.params.get('questionId') === (questionId === null ? null : String(questionId)) &&
        r.params.get('cursor') === cursor
    );
    expect(req.request.params.get('take')).toBe('20');
    return req;
  }

  function flushThread(p: WorksheetCommentPage, questionId: number | null = null): void {
    expectThread(questionId).flush(p);
    fixture.detectChanges();
  }

  function typeAndSend(text: string, index = 0): void {
    const textarea = el().querySelectorAll<HTMLTextAreaElement>('[data-testid="comment-textarea"]')[index];
    textarea.value = text;
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (el().querySelectorAll<HTMLButtonElement>('[data-testid="comment-send"]')[index]).click();
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('load_ShowsSpinnerThenRootsNewestFirstWithRepliesAndLiveRegion', () => {
    create();
    expect(q('comments-loading')).not.toBeNull();

    flushThread(
      page({
        items: [
          root({ id: 2, body: 'Yeni kök', replies: [comment({ id: 5, parentCommentId: 2, body: 'Cevap', authorRole: 'Teacher', authorDisplayName: 'Ayşe Öğretmen' })], replyCount: 1 }),
          root({ id: 1, body: 'Eski kök' }),
        ],
      })
    );

    expect(q('comments-loading')).toBeNull();
    const bodies = qa('comment-body').map((b) => b.textContent?.trim());
    expect(bodies).toEqual(['Yeni kök', 'Cevap', 'Eski kök']);
    expect(qa('teacher-badge').length).toBe(1);
    // Liste live region değil; duyurular ayrı, görsel olarak gizli span'dan.
    expect(el().querySelector('.ct__list')?.hasAttribute('aria-live')).toBeFalse();
    expect(q('comments-announcer')?.getAttribute('aria-live')).toBe('polite');
    expect(q('comments-announcer')?.textContent?.trim()).toBe('');
  });

  it('empty_CanWrite_ShowsInvite; cannotWrite_HidesInvite', () => {
    create();
    flushThread(page());
    expect(q('comments-empty')).not.toBeNull();
    expect(q('comments-invite')?.textContent).toContain(commentsTr.thread.emptyInvite);

    TestBed.resetTestingModule();
    create();
    flushThread(page({ canWrite: false, lockReason: 'question-not-answered' }));
    expect(q('comments-empty')).not.toBeNull();
    expect(q('comments-invite')).toBeNull();
  });

  it('loadError_ShowsBackendMessageAndRetryReloads', () => {
    create();
    expectThread().flush({ message: 'Bu teste erişimin yok.', errorCode: 'AccessDenied' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(q('comments-error')?.textContent).toContain('Bu teste erişimin yok.');
    expect(q('root-composer')).toBeNull();

    q('comments-retry')!.click();
    fixture.detectChanges();
    flushThread(page({ items: [root()] }));
    expect(q('comments-error')).toBeNull();
    expect(qa('comment').length).toBe(1);
  });

  it('canWrite_RendersComposer', () => {
    create();
    flushThread(page({ canWrite: true }));

    expect(q('root-composer')).not.toBeNull();
    expect(q('lock-strip')).toBeNull();
    expect(q('comment-textarea')?.getAttribute('aria-label')).toBe(commentsTr.composer.ariaLabel);
  });

  it('questionNotAnswered_LockStripNoInputButCommentsVisible', () => {
    create({ questionId: 34 });
    flushThread(page({ canWrite: false, lockReason: 'question-not-answered', items: [root({ questionId: 34 })] }), 34);

    const strip = q('lock-strip')!;
    expect(strip.getAttribute('role')).toBe('status');
    expect(strip.classList).toContain('ct__strip--lock');
    expect(strip.textContent).toContain(commentsTr.thread.lock['question-not-answered']);
    expect(strip.querySelector('mat-icon')?.textContent?.trim()).toBe('lock');
    expect(q('comment-textarea')).toBeNull();
    expect(qa('comment').length).toBe(1);
  });

  it('commentsDisabled_NeutralStripNoInputExistingVisible', () => {
    create();
    flushThread(page({ canWrite: false, lockReason: 'comments-disabled', items: [root({ canReply: false })] }));

    const strip = q('lock-strip')!;
    expect(strip.classList).toContain('ct__strip--neutral');
    expect(strip.textContent).toContain(commentsTr.thread.lock['comments-disabled']);
    expect(q('comment-textarea')).toBeNull();
    expect(q('reply-button')).toBeNull();
    expect(qa('comment').length).toBe(1);
  });

  it('teacherNotice_StudentsLocked_ShowsNeutralStripAndEditSettings', () => {
    create({ studentsLockedNotice: true, showEditSettings: true, viewerIsTeacher: true });
    flushThread(page({ canWrite: true }));
    const emitted = jasmine.createSpy('editSettings');
    fixture.componentInstance.editSettings.subscribe(emitted);

    expect(q('teacher-disabled-notice')?.textContent).toContain(commentsTr.thread.teacherDisabledNotice);
    q('edit-settings')!.click();
    expect(emitted).toHaveBeenCalled();
    expect(q('root-composer')).not.toBeNull();
  });

  it('cannotWriteWithoutLockReason_ShowsReadOnlyHint', () => {
    create({ viewerIsTeacher: true });
    flushThread(page({ canWrite: false, lockReason: null }));

    expect(q('read-only')).not.toBeNull();
    expect(q('comment-textarea')).toBeNull();
  });

  it('replyButton_OnlyWhenCanReply', () => {
    create();
    flushThread(page({ items: [root({ id: 2, canReply: true }), root({ id: 1, canReply: false })] }));

    expect(qa('reply-button').length).toBe(1);
  });

  it('questionIdChange_HidesOldThreadShowsSpinnerThenDebouncedReload', fakeAsync(() => {
    create({ questionId: 34 });
    flushThread(page({ items: [root({ id: 5, questionId: 34 })] }), 34);

    fixture.componentRef.setInput('questionId', 35);
    fixture.detectChanges();
    // Eski thread hemen gizlenir, spinner görünür; istek debounce sonrası.
    expect(qa('comment').length).toBe(0);
    expect(q('comments-loading')).not.toBeNull();
    http.expectNone((r) => r.params.get('questionId') === '35');

    tick(COMMENT_CONTEXT_DEBOUNCE_MS);
    flushThread(page({ items: [root({ questionId: 35 })] }), 35);

    expect(qa('comment').length).toBe(1);
    discardPeriodicTasks();
  }));

  it('questionIdChange_RapidNavigation_OnlyLastQuestionRequested', fakeAsync(() => {
    create({ questionId: 34 });
    flushThread(page(), 34);

    for (const id of [35, 36, 37]) {
      fixture.componentRef.setInput('questionId', id);
      fixture.detectChanges();
      tick(100);
    }
    tick(COMMENT_CONTEXT_DEBOUNCE_MS);

    http.expectNone((r) => r.params.get('questionId') === '35' || r.params.get('questionId') === '36');
    flushThread(page(), 37);
    discardPeriodicTasks();
  }));

  it('questionIdChange_PendingRootNotCarriedAndRetryUsesOriginalQuestion', fakeAsync(() => {
    create({ questionId: 34 });
    flushThread(page(), 34);

    // Soru 34'te gönderim başarısız olur.
    typeAndSend('Soru 34 hakkında');
    const first = http.expectOne((r) => r.method === 'POST');
    expect(first.request.body.questionId).toBe(34);
    first.flush({ message: 'Hata', errorCode: 'BodyRequired' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(q('comment-failed')).not.toBeNull();

    // Taslak + açık cevap alanı olan durumda soru değişir.
    const textarea = q('comment-textarea') as HTMLTextAreaElement;
    textarea.value = 'yarım taslak';
    textarea.dispatchEvent(new Event('input'));
    fixture.componentRef.setInput('questionId', 35);
    fixture.detectChanges();
    tick(COMMENT_CONTEXT_DEBOUNCE_MS);
    flushThread(page(), 35);

    expect(q('comment-failed')).toBeNull();
    expect(qa('comment').length).toBe(0);
    expect((q('comment-textarea') as HTMLTextAreaElement).value).toBe('');
    http.expectNone((r) => r.method === 'POST');
    discardPeriodicTasks();
  }));

  it('questionIdChange_OpenReplyClosed', fakeAsync(() => {
    create({ questionId: 34 });
    flushThread(page({ items: [root({ id: 2, questionId: 34 })] }), 34);
    q('reply-button')!.click();
    fixture.detectChanges();
    expect(qa('comment-textarea').length).toBe(2);

    fixture.componentRef.setInput('questionId', 35);
    fixture.detectChanges();
    tick(COMMENT_CONTEXT_DEBOUNCE_MS);
    flushThread(page({ items: [root({ id: 2, questionId: 35 })] }), 35);

    // Aynı kök anahtarı olsa bile cevap alanı yeni soruda kapalı başlar.
    expect(qa('comment-textarea').length).toBe(1);
    discardPeriodicTasks();
  }));

  it('retryAfterQuestionChange_PostsOriginalQuestion', fakeAsync(() => {
    create({ questionId: 34 });
    flushThread(page(), 34);

    typeAndSend('Soru 34');
    http.expectOne((r) => r.method === 'POST').flush({ message: 'Hata' }, { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    const component = fixture.componentInstance as unknown as {
      threads: () => { key: string; root: { key: string } }[];
      retry: (threadKey: string, key: string) => void;
      questionId: () => number | null;
    };
    const failed = component.threads()[0];

    // Bileşenin güncel sorusu değişmiş olsa bile (ör. input geç uygulanır), retry kaydın kendi sorusuyla gider.
    fixture.componentRef.setInput('questionId', 35);
    component.retry(failed.key, failed.root.key);
    const retry = http.expectOne((r) => r.method === 'POST');
    expect(retry.request.body.questionId).toBe(34);
    retry.flush(comment({ id: 60, questionId: 34 }));
    fixture.detectChanges();
    tick(COMMENT_CONTEXT_DEBOUNCE_MS);
    flushThread(page(), 35);
    // Eski sorunun gönderim yanıtı yeni soru thread'ine yazılmaz.
    expect(el().querySelector('[data-comment-id="60"]')).toBeNull();
    discardPeriodicTasks();
  }));

  it('questionIdChange_LateResponseOfOldQuestionIgnored', fakeAsync(() => {
    create({ questionId: 34 });
    const stale = expectThread(34);

    fixture.componentRef.setInput('questionId', 35);
    fixture.detectChanges();
    // Eski sorunun yanıtı debounce sırasında gelir: ekrana yazılmaz.
    stale.flush(page({ items: [root({ id: 1, questionId: 34, body: 'eski soru' })] }));
    fixture.detectChanges();
    expect(qa('comment').length).toBe(0);
    expect(q('comments-loading')).not.toBeNull();

    tick(COMMENT_CONTEXT_DEBOUNCE_MS);
    flushThread(page({ items: [root({ id: 2, questionId: 35, body: 'yeni soru' })] }), 35);
    expect(qa('comment-body').map((b) => b.textContent?.trim())).toEqual(['yeni soru']);
    discardPeriodicTasks();
  }));

  it('sendRoot_OptimisticThenSent', () => {
    create({ questionId: 34 });
    flushThread(page(), 34);

    typeAndSend('  Neden B şıkkı?  ');

    const req = http.expectOne((r) => r.method === 'POST' && r.url === BASE);
    expect(req.request.body).toEqual({ questionId: 34, parentCommentId: null, body: 'Neden B şıkkı?' });
    expect(q('comment-sending')).not.toBeNull();
    expect(qa('comment-body')[0].textContent?.trim()).toBe('Neden B şıkkı?');
    expect((q('comment-textarea') as HTMLTextAreaElement).value).toBe('');

    req.flush(comment({ id: 99, questionId: 34, body: 'Neden B şıkkı?', isMine: true, authorDisplayName: 'Ben S.' }), {
      status: 201,
      statusText: 'Created',
    });
    fixture.detectChanges();

    expect(q('comment-sending')).toBeNull();
    expect(el().querySelector('[data-comment-id="99"]')).not.toBeNull();
    expect(q('comment-author')?.textContent).toContain('Ben S.');
    expect(q('reply-button')).not.toBeNull();
  });

  it('sendRoot_Error_ShowsBackendMessageKeepsContentRetryResends', () => {
    create();
    flushThread(page());

    typeAndSend('Merhaba');
    http
      .expectOne((r) => r.method === 'POST')
      .flush({ message: 'Yorumlar kapalı.', errorCode: 'CommentsDisabled' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();

    expect(q('comment-failed')?.textContent).toContain('Yorumlar kapalı.');
    expect(qa('comment-body')[0].textContent?.trim()).toBe('Merhaba');

    q('comment-retry')!.click();
    fixture.detectChanges();
    const retry = http.expectOne((r) => r.method === 'POST');
    expect(retry.request.body.body).toBe('Merhaba');
    retry.flush(comment({ id: 7, body: 'Merhaba' }), { status: 201, statusText: 'Created' });
    fixture.detectChanges();
    expect(q('comment-failed')).toBeNull();
  });

  it('sendRoot_ErrorThenEdit_MovesTextBackToComposer', () => {
    create();
    flushThread(page());

    typeAndSend('Uzun metin');
    http.expectOne((r) => r.method === 'POST').flush({ message: '', errorCode: 'BodyInvalidCharacters' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    // message boşsa errorCode'un yerel karşılığı gösterilir.
    expect(q('comment-failed')?.textContent).toContain(commentsTr.errors.codes.BodyInvalidCharacters);

    q('comment-edit')!.click();
    fixture.detectChanges();
    expect(q('comment-failed')).toBeNull();
    expect((q('comment-textarea') as HTMLTextAreaElement).value).toBe('Uzun metin');
  });

  it('send_429PlainTextBody_ShowsServerText', () => {
    create();
    flushThread(page());

    typeAndSend('Spam');
    http.expectOne((r) => r.method === 'POST').flush('Çok fazla yorum gönderdin.', { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '30' } });
    fixture.detectChanges();

    expect(q('comment-failed')?.textContent).toContain('Çok fazla yorum gönderdin.');
  });

  it('send_429WithoutBody_UsesRetryAfter', () => {
    create();
    flushThread(page());

    typeAndSend('Spam');
    http.expectOne((r) => r.method === 'POST').flush(null, { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '42' } });
    fixture.detectChanges();

    expect(q('comment-failed')?.textContent).toContain('42 saniye');
  });

  it('ctrlEnter_Submits; overLimit_DisablesSend', () => {
    create();
    flushThread(page());
    const textarea = q('comment-textarea') as HTMLTextAreaElement;

    textarea.value = 'x'.repeat(2001);
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect((q('comment-send') as HTMLButtonElement).disabled).toBeTrue();
    expect(q('comment-counter')?.textContent).toContain('2001/2000');
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true }));
    http.expectNone((r) => r.method === 'POST');

    textarea.value = 'Kısa';
    textarea.dispatchEvent(new Event('input'));
    textarea.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', metaKey: true }));
    fixture.detectChanges();
    http.expectOne((r) => r.method === 'POST').flush(comment({ id: 3, body: 'Kısa' }));
  });

  it('reply_SendsParentCommentIdAndAppends', () => {
    create({ questionId: 34 });
    flushThread(page({ items: [root({ id: 2, questionId: 34, replyCount: 0 })] }), 34);

    q('reply-button')!.click();
    fixture.detectChanges();
    expect(q('comment-textarea')?.getAttribute('aria-label')).toBeTruthy();
    // 0: ana alan, 1: cevap alanı
    typeAndSend('Cevabım', 1);

    const req = http.expectOne((r) => r.method === 'POST');
    expect(req.request.body).toEqual({ questionId: 34, parentCommentId: 2, body: 'Cevabım' });
    req.flush(comment({ id: 8, parentCommentId: 2, body: 'Cevabım' }), { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(el().querySelector('[data-comment-id="8"]')).not.toBeNull();
  });

  it('loadMore_UsesCursorAndAppends', () => {
    create();
    flushThread(page({ items: [root({ id: 3 })], nextCursor: 'next-1' }));

    q('load-more')!.click();
    fixture.detectChanges();
    expectThread(null, 'next-1').flush(page({ items: [root({ id: 3 }), root({ id: 2 })], nextCursor: null }));
    fixture.detectChanges();

    expect(qa('comment').map((c) => c.getAttribute('data-comment-id'))).toEqual(['3', '2']);
    expect(q('load-more')).toBeNull();
  });

  it('olderReplies_LoadsFromRepliesEndpointAndMerges', () => {
    create();
    const preview = [4, 5, 6, 7, 8].map((id) => comment({ id, parentCommentId: 2, createdAt: `2026-09-30T10:0${id}:00Z` }));
    flushThread(page({ items: [root({ id: 2, replies: preview, replyCount: 7 })] }));

    expect(q('load-older-replies')?.textContent).toContain('(2)');
    q('load-older-replies')!.click();
    fixture.detectChanges();

    const req = http.expectOne((r) => r.url === `${BASE}/2/replies`);
    expect(req.request.params.has('cursor')).toBeFalse();
    expect(req.request.params.get('take')).toBe('50');
    const reply = (id: number, minute: number) =>
      comment({ id, parentCommentId: 2, createdAt: `2026-09-30T10:0${minute}:00Z` });
    req.flush({
      items: [reply(10, 2), reply(11, 3), ...preview],
      nextCursor: null,
      canReply: true,
      replyCount: 7,
    });
    fixture.detectChanges();

    const ids = qa('comment').map((c) => c.getAttribute('data-comment-id'));
    expect(ids).toEqual(['2', '10', '11', '4', '5', '6', '7', '8']);
    expect(q('load-older-replies')).toBeNull();
  });

  it('olderReplies_Error_ShowsMessage', () => {
    create();
    flushThread(page({ items: [root({ id: 2, replies: [comment({ id: 9, parentCommentId: 2 })], replyCount: 3 })] }));

    q('load-older-replies')!.click();
    fixture.detectChanges();
    http.expectOne((r) => r.url === `${BASE}/2/replies`).flush({ message: 'Yorum bulunamadı.', errorCode: 'RootCommentNotFound' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(q('older-replies-error')?.textContent).toContain('Yorum bulunamadı.');
  });

  it('highlight_MarksCommentFor2SecondsAndTeacherGetsReplyOpen', fakeAsync(() => {
    create({ highlightCommentId: 2, viewerIsTeacher: true });
    flushThread(page({ items: [root({ id: 2, canReply: true, authorDisplayName: 'Ayşe K.' })] }));
    tick();
    fixture.detectChanges();

    const target = el().querySelector('[data-comment-id="2"]')!;
    expect(target.classList).toContain('ci__comment--highlight');
    const replyArea = qa('comment-textarea').find((t) => t.getAttribute('aria-label') === commentsTr.composer.replyAriaLabel);
    expect(replyArea).toBeDefined();
    expect(replyArea!.getAttribute('placeholder')).toContain('Ayşe K.');

    tick(COMMENT_HIGHLIGHT_MS);
    fixture.detectChanges();
    expect(target.classList).not.toContain('ci__comment--highlight');
    discardPeriodicTasks();
  }));

  it('highlight_ReplyOutsidePreview_LoadsRootRepliesThenHighlights', fakeAsync(() => {
    create({ highlightCommentId: 20, highlightRootId: 2 });
    flushThread(page({ items: [root({ id: 2, replies: [comment({ id: 21, parentCommentId: 2 })], replyCount: 2 })] }));

    http.expectOne((r) => r.url === `${BASE}/2/replies`).flush({
      items: [comment({ id: 20, parentCommentId: 2, createdAt: '2026-09-30T09:00:00Z' })],
      nextCursor: null,
      canReply: true,
      replyCount: 2,
    });
    fixture.detectChanges();
    tick();

    expect(el().querySelector('[data-comment-id="20"]')?.classList).toContain('ci__comment--highlight');
    // Öğrenci görünümünde cevap alanı otomatik açılmaz.
    expect(qa('comment-textarea').length).toBe(1);
    tick(COMMENT_HIGHLIGHT_MS);
    discardPeriodicTasks();
  }));

  it('highlight_RootOnLaterPage_LoadsNextPage', fakeAsync(() => {
    create({ highlightCommentId: 1 });
    flushThread(page({ items: [root({ id: 3 })], nextCursor: 'n1' }));

    expectThread(null, 'n1').flush(page({ items: [root({ id: 1 })] }));
    fixture.detectChanges();
    tick();

    expect(el().querySelector('[data-comment-id="1"]')?.classList).toContain('ci__comment--highlight');
    tick(COMMENT_HIGHLIGHT_MS);
    discardPeriodicTasks();
  }));

  it('highlight_NotFound_StopsAfterMaxPages', fakeAsync(() => {
    create({ highlightCommentId: 999 });
    flushThread(page({ items: [root({ id: 100 })], nextCursor: 'c0' }));

    for (let i = 0; i < COMMENT_HIGHLIGHT_MAX_PAGES; i++) {
      expectThread(null, `c${i}`).flush(page({ items: [root({ id: 99 - i })], nextCursor: `c${i + 1}` }));
      fixture.detectChanges();
    }

    // Sınırda durur: sonraki sayfa otomatik istenmez, "Daha fazla" butonu elle kullanılabilir.
    http.expectNone((r) => r.params.get('cursor') === `c${COMMENT_HIGHLIGHT_MAX_PAGES}`);
    expect(q('load-more')).not.toBeNull();
    expect(el().querySelector('.ci__comment--highlight')).toBeNull();
    discardPeriodicTasks();
  }));

  it('highlight_LoadErrorKeepsPendingAndRetryApplies', fakeAsync(() => {
    create({ highlightCommentId: 2 });
    expectThread().flush({ message: 'Sunucu hatası' }, { status: 500, statusText: 'Error' });
    fixture.detectChanges();

    q('comments-retry')!.click();
    fixture.detectChanges();
    flushThread(page({ items: [root({ id: 2 })] }));
    tick();

    expect(el().querySelector('[data-comment-id="2"]')?.classList).toContain('ci__comment--highlight');
    tick(COMMENT_HIGHLIGHT_MS);
    discardPeriodicTasks();
  }));

  it('highlight_ConsumedOnce_NotReappliedWhenInputReturns', fakeAsync(() => {
    create({ highlightCommentId: 2, viewerIsTeacher: true });
    flushThread(page({ items: [root({ id: 2, canReply: true })] }));
    tick(COMMENT_HIGHLIGHT_MS);
    fixture.detectChanges();
    // Açılan cevap alanını kapat.
    (el().querySelector('.cc__cancel') as HTMLButtonElement).click();
    fixture.detectChanges();

    fixture.componentRef.setInput('highlightCommentId', null);
    fixture.detectChanges();
    fixture.componentRef.setInput('highlightCommentId', 2);
    fixture.detectChanges();

    http.expectNone((r) => r.url === BASE);
    expect(el().querySelector('.ci__comment--highlight')).toBeNull();
    expect(qa('comment-textarea').length).toBe(1);
    discardPeriodicTasks();
  }));

  it('send_Success_AnnouncedInLiveRegion', () => {
    create();
    flushThread(page());

    typeAndSend('Merhaba');
    expect(q('comments-announcer')?.textContent?.trim()).toBe('');
    http.expectOne((r) => r.method === 'POST').flush(comment({ id: 9, body: 'Merhaba' }), { status: 201, statusText: 'Created' });
    fixture.detectChanges();

    expect(q('comments-announcer')?.textContent?.trim()).toBe(commentsTr.thread.announceSent);
  });

  it('send_429HtmlOrLongBody_FallsBackToLocalizedText', () => {
    create();
    flushThread(page());

    typeAndSend('a');
    http.expectOne((r) => r.method === 'POST').flush('<html><body>Too many</body></html>', {
      status: 429,
      statusText: 'Too Many Requests',
    });
    fixture.detectChanges();
    expect(q('comment-failed')?.textContent).toContain(commentsTr.errors.rateLimited);
    expect(q('comment-failed')?.textContent).not.toContain('<html>');

    q('comment-retry')!.click();
    http.expectOne((r) => r.method === 'POST').flush('x'.repeat(301), { status: 429, statusText: 'Too Many Requests' });
    fixture.detectChanges();
    expect(q('comment-failed')?.textContent).toContain(commentsTr.errors.rateLimited);
  });

  // ---------------------------------------------------------------- issue #309
  describe('studentCommentsSummary strip (issue #309)', () => {
    const summaryText = () => q('teacher-summary-text')?.textContent?.replace(/\s+/g, ' ').trim();

    it('zeroOverrides_ShowsOnlyDefault', () => {
      create({ viewerIsTeacher: true, showStudentsSummary: true, showEditSettings: true });
      flushThread(
        page({ studentCommentsSummary: { worksheetDefault: true, assignmentOverrides: { enabled: 0, disabled: 0 } } })
      );

      expect(summaryText()).toBe('Öğrenci yorumları: varsayılan açık');
      expect(q('teacher-summary')?.getAttribute('data-default')).toBe('on');
      expect(q('edit-settings')).not.toBeNull();
      expect(q('teacher-disabled-notice')).toBeNull();
    });

    it('defaultOpen_DisabledOverrides_ShowsCount', () => {
      create({ viewerIsTeacher: true, showStudentsSummary: true });
      flushThread(
        page({ studentCommentsSummary: { worksheetDefault: true, assignmentOverrides: { enabled: 0, disabled: 2 } } })
      );

      expect(summaryText()).toBe('Öğrenci yorumları: varsayılan açık · 2 atamada kapalı');
      expect(q('edit-settings')).toBeNull();
    });

    it('defaultClosed_Mixed_ContraryCountFirst', () => {
      // commentsEnabled (studentsLockedNotice) sunucu özeti varken kullanılmaz.
      create({ viewerIsTeacher: true, showStudentsSummary: true, studentsLockedNotice: true });
      flushThread(
        page({ studentCommentsSummary: { worksheetDefault: false, assignmentOverrides: { enabled: 1, disabled: 3 } } })
      );

      expect(summaryText()).toBe('Öğrenci yorumları: varsayılan kapalı · 1 atamada açık · 3 atamada kapalı');
      expect(q('teacher-summary')?.getAttribute('data-default')).toBe('off');
      expect(q('teacher-disabled-notice')).toBeNull();
    });

    it('nullOrMalformedSummary_FallsBackToCommentsEnabledNotice', () => {
      create({ viewerIsTeacher: true, showStudentsSummary: true, studentsLockedNotice: true });
      flushThread(page({ studentCommentsSummary: null }));

      expect(q('teacher-summary')).toBeNull();
      expect(q('teacher-disabled-notice')?.textContent).toContain(commentsTr.thread.teacherDisabledNotice);

      fixture.componentInstance['load']();
      expectThread().flush({
        ...page(),
        studentCommentsSummary: { worksheetDefault: 'yes', assignmentOverrides: { enabled: -1, disabled: 0 } },
      });
      fixture.detectChanges();
      expect(q('teacher-summary')).toBeNull();
      expect(q('teacher-disabled-notice')).not.toBeNull();
    });

    it('nullSummary_DefaultOpen_NoStrip', () => {
      create({ viewerIsTeacher: true, showStudentsSummary: true, studentsLockedNotice: false });
      flushThread(page());

      expect(q('teacher-summary')).toBeNull();
      expect(q('teacher-disabled-notice')).toBeNull();
    });

    it('showStudentsSummaryOff_IgnoresSummary', () => {
      create({ viewerIsTeacher: true });
      flushThread(
        page({ studentCommentsSummary: { worksheetDefault: true, assignmentOverrides: { enabled: 0, disabled: 2 } } })
      );

      expect(q('teacher-summary')).toBeNull();
    });
  });

  describe('questionOrderChange (issue #309)', () => {
    function subscribe(): jasmine.Spy {
      const spy = jasmine.createSpy('questionOrderChange');
      fixture.componentInstance.questionOrderChange.subscribe(spy);
      return spy;
    }

    it('questionThread_EmitsPageLevelOrder', () => {
      create({ questionId: 34 });
      const spy = subscribe();
      flushThread(page({ questionOrder: 3, items: [root({ id: 5, questionId: 34, questionOrder: 3 })] }), 34);

      expect(spy).toHaveBeenCalledOnceWith(3);
    });

    it('emptyQuestionThread_StillEmitsPageLevelOrder', () => {
      create({ questionId: 34 });
      const spy = subscribe();
      flushThread(page({ questionOrder: 5 }), 34);

      expect(q('comments-empty')).not.toBeNull();
      expect(spy).toHaveBeenCalledOnceWith(5);
    });

    it('invalidOrMissingPageOrder_Ignored', () => {
      for (const questionOrder of [0, -1, 2.5, '4' as unknown as number, null, undefined]) {
        TestBed.resetTestingModule();
        create({ questionId: 34 });
        const spy = subscribe();
        // Öğe düzeyindeki değer artık taranmaz; yalnız sayfa düzeyi geçerlidir.
        flushThread(page({ questionOrder, items: [root({ id: 5, questionId: 34, questionOrder: 3 })] }), 34);
        expect(spy).withContext(String(questionOrder)).not.toHaveBeenCalled();
        http.verify();
      }
    });

    it('worksheetThread_NeverEmitsOrder', () => {
      create();
      const spy = subscribe();
      flushThread(page({ questionOrder: 3 }));

      expect(spy).not.toHaveBeenCalled();
    });

    it('questionIdChange_EmitsNullThenNewOrder', fakeAsync(() => {
      create({ questionId: 34 });
      const spy = subscribe();
      flushThread(page({ questionOrder: 3 }), 34);

      fixture.componentRef.setInput('questionId', 35);
      fixture.detectChanges();
      expect(spy.calls.mostRecent().args).toEqual([null]);
      tick(COMMENT_CONTEXT_DEBOUNCE_MS);
      flushThread(page({ questionOrder: 4 }), 35);

      expect(spy.calls.allArgs()).toEqual([[3], [null], [4]]);
      discardPeriodicTasks();
    }));
  });

  describe('429 JSON body (issue #309)', () => {
    it('send_429RateLimitedJson_ShowsServerMessage', () => {
      create();
      flushThread(page());

      typeAndSend('Spam');
      http.expectOne((r) => r.method === 'POST').flush(
        { message: 'Çok fazla yorum yazdın, 30 sn bekle.', errorCode: 'RateLimited' },
        { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '30' } }
      );
      fixture.detectChanges();

      expect(q('comment-failed')?.textContent).toContain('Çok fazla yorum yazdın, 30 sn bekle.');
    });

    it('load_429RateLimitedJson_ShowsServerMessageWithRetry', () => {
      create();
      expectThread().flush(
        { message: 'Yorumlar çok sık yenilendi.', errorCode: 'RateLimited' },
        { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '10' } }
      );
      fixture.detectChanges();

      expect(q('comments-error')?.textContent).toContain('Yorumlar çok sık yenilendi.');
      expect(q('comments-retry')).not.toBeNull();
    });

    it('send_429JsonEmptyMessageOrOtherCode_UsesRetryAfterOrLocalText', () => {
      create();
      flushThread(page());

      typeAndSend('a');
      http.expectOne((r) => r.method === 'POST').flush(
        { message: '', errorCode: 'RateLimited' },
        { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '15' } }
      );
      fixture.detectChanges();
      expect(q('comment-failed')?.textContent).toContain('15 saniye');

      q('comment-retry')!.click();
      http.expectOne((r) => r.method === 'POST').flush(
        { message: 'Sunucu mesajı', errorCode: 'Other' },
        { status: 429, statusText: 'Too Many Requests' }
      );
      fixture.detectChanges();
      expect(q('comment-failed')?.textContent).toContain(commentsTr.errors.rateLimited);
    });
  });

  it('teacherNotice_TextStatesWorksheetDefault', () => {
    create({ studentsLockedNotice: true, viewerIsTeacher: true });
    flushThread(page());

    expect(q('teacher-disabled-notice')?.textContent).toContain('Worksheet varsayılanı');
  });
});
