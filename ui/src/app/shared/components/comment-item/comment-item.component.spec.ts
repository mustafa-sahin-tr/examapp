import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

import { CommentItemComponent } from './comment-item.component';
import { CommentView, ThreadView, serverCommentView } from '../comment-thread/comment-view';
import { WorksheetComment } from '../../../models/worksheet-comment.model';
import { translocoTestingModule } from '../../testing/transloco-testing';
import rootTr from '../../../../../public/i18n/tr.json';
import commentsTr from '../../../../../public/i18n/comments/tr.json';

function comment(overrides: Partial<WorksheetComment> = {}): WorksheetComment {
  return {
    id: 1,
    worksheetId: 12,
    questionId: null,
    parentCommentId: null,
    authorDisplayName: 'Ali K.',
    authorRole: 'Student',
    isMine: false,
    body: 'Merhaba',
    createdAt: '2026-09-30T10:00:00Z',
    ...overrides,
  };
}

function thread(overrides: Partial<ThreadView> = {}): ThreadView {
  return {
    key: 't1',
    root: serverCommentView(comment()),
    replies: [],
    replyCount: 0,
    canReply: true,
    olderCursor: null,
    olderStarted: false,
    loadingOlder: false,
    olderError: null,
    ...overrides,
  };
}

describe('CommentItemComponent (issue #105)', () => {
  let fixture: ComponentFixture<CommentItemComponent>;
  const el = () => fixture.nativeElement as HTMLElement;
  const q = (id: string) => el().querySelector<HTMLElement>(`[data-testid="${id}"]`);
  const qa = (id: string) => Array.from(el().querySelectorAll<HTMLElement>(`[data-testid="${id}"]`));

  function create(value: ThreadView, inputs: Record<string, unknown> = {}): void {
    TestBed.configureTestingModule({
      imports: [
        CommentItemComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { tr: { ...rootTr, comments: commentsTr }, 'comments/tr': commentsTr } }),
      ],
    });
    fixture = TestBed.createComponent(CommentItemComponent);
    fixture.componentRef.setInput('thread', value);
    fixture.componentRef.setInput('now', Date.parse('2026-09-30T10:05:00Z'));
    fixture.componentRef.setInput('locale', 'tr');
    Object.entries(inputs).forEach(([key, v]) => fixture.componentRef.setInput(key, v));
    fixture.detectChanges();
  }

  /** mat-menu overlay'de açılır (fixture dışında). */
  function openMenu(index = 0): void {
    qa('comment-menu')[index].click();
    fixture.detectChanges();
  }
  const menuItem = (id: string) => document.querySelector<HTMLButtonElement>(`.mat-mdc-menu-panel [data-testid="${id}"]`);

  it('teacherReply_ShowsBadgeWithTextAndTeacherAvatar', () => {
    create(
      thread({
        replies: [serverCommentView(comment({ id: 2, parentCommentId: 1, authorRole: 'Teacher', authorDisplayName: 'Ayşe Öğretmen' }))],
        replyCount: 1,
      })
    );

    const badges = qa('teacher-badge');
    expect(badges.length).toBe(1);
    expect(badges[0].textContent).toContain(commentsTr.item.teacherBadge);
    expect(el().querySelectorAll('.ci__avatar--teacher').length).toBe(1);
  });

  it('studentComment_NoBadge', () => {
    create(thread());

    expect(q('teacher-badge')).toBeNull();
  });

  it('bodyAndAuthor_RenderedAsIsolatedPlainText', () => {
    const hostile = '<img src=x onerror=alert(1)>‮evil\nikinci satır';
    create(thread({ root: serverCommentView(comment({ body: hostile, authorDisplayName: '‫علي' })) }));

    const body = q('comment-body')!;
    const bdi = body.querySelector('bdi')!;
    expect(bdi.getAttribute('dir')).toBe('auto');
    expect(bdi.textContent).toBe(hostile);
    expect(body.querySelector('img')).toBeNull();
    expect(getComputedStyle(body).whiteSpace).toBe('pre-wrap');

    const author = q('comment-author')!;
    expect(author.tagName.toLowerCase()).toBe('bdi');
    expect(author.getAttribute('dir')).toBe('auto');
  });

  it('relativeTime_AndDatetimeAttribute', () => {
    create(thread());

    const time = el().querySelector('time')!;
    expect(time.getAttribute('datetime')).toBe('2026-09-30T10:00:00Z');
    expect(time.textContent?.trim()).toBe('5 dakika önce');
  });

  it('mine_ShowsMarker', () => {
    create(thread({ root: serverCommentView(comment({ isMine: true })) }));

    expect(el().querySelector('.ci__mine')?.textContent).toContain(commentsTr.item.mine);
  });

  it('noCanReply_NoReplyButton', () => {
    create(thread({ canReply: false }));

    expect(q('reply-button')).toBeNull();
  });

  it('replyButton_OpensComposerAndSubmitEmits', () => {
    create(thread());
    const submitted: string[] = [];
    fixture.componentInstance.replySubmit.subscribe((b) => submitted.push(b));

    q('reply-button')!.click();
    fixture.detectChanges();
    const textarea = q('comment-textarea') as HTMLTextAreaElement;
    expect(textarea.getAttribute('placeholder')).toContain('Ali K.');
    textarea.value = ' cevap ';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (q('comment-send') as HTMLButtonElement).click();

    expect(submitted).toEqual(['cevap']);
  });

  it('olderReplies_ButtonShowsHiddenCountAndEmits', () => {
    create(thread({ replies: [serverCommentView(comment({ id: 3, parentCommentId: 1 }))], replyCount: 4 }));
    let clicks = 0;
    fixture.componentInstance.loadOlder.subscribe(() => clicks++);

    expect(q('load-older-replies')?.textContent).toContain('(3)');
    q('load-older-replies')!.click();
    expect(clicks).toBe(1);
  });

  it('olderReplies_AllLoaded_Hidden', () => {
    create(thread({ replies: [], replyCount: 2, olderStarted: true, olderCursor: null }));

    expect(q('load-older-replies')).toBeNull();
  });

  it('failedReply_RetryEmitsKey; editRestoresDraftAndDiscards', () => {
    const failed: CommentView = {
      key: 'p1',
      comment: comment({ id: -1, parentCommentId: 1, body: 'geri al', authorDisplayName: '' }),
      state: 'failed',
      error: 'Hata oldu',
    };
    create(thread({ replies: [failed] }));
    const retried: string[] = [];
    const discarded: string[] = [];
    fixture.componentInstance.retry.subscribe((k) => retried.push(k));
    fixture.componentInstance.discard.subscribe((k) => discarded.push(k));

    expect(q('comment-failed')?.textContent).toContain('Hata oldu');
    expect(qa('comment-author')[1].textContent).toContain(commentsTr.item.you);
    q('comment-retry')!.click();
    expect(retried).toEqual(['p1']);

    q('comment-edit')!.click();
    fixture.detectChanges();
    expect(discarded).toEqual(['p1']);
    expect((q('comment-textarea') as HTMLTextAreaElement).value).toBe('geri al');
  });

  it('failedRoot_EditEmitsEditRoot', () => {
    create(thread({ root: { key: 'p2', comment: comment({ id: -2 }), state: 'failed', error: null } }));
    const edited: string[] = [];
    fixture.componentInstance.editRoot.subscribe((k) => edited.push(k));

    q('comment-edit')!.click();

    expect(edited).toEqual(['p2']);
    // Gönderilemeyen kökün altında cevap alanı/butonu yoktur.
    expect(q('reply-button')).toBeNull();
  });

  // ---- Issue #305: moderasyon ---------------------------------------------------------------------------------

  const hidden = (overrides: Partial<WorksheetComment> = {}) =>
    comment({ isHidden: true, body: null, authorDisplayName: 'Kaldırıldı', ...overrides });

  it('hiddenComment_ShowsNeutralPlaceholderWithoutAuthorOrBody', () => {
    create(thread({ root: serverCommentView(hidden()) }));

    expect(q('comment-removed')?.textContent).toContain(commentsTr.item.removed);
    expect(q('comment-body')).toBeNull();
    expect(q('comment-author')).toBeNull();
    expect(el().querySelector('.ci__avatar--removed mat-icon')?.textContent).toContain('visibility_off');
    expect(q('comment-menu')).toBeNull();
  });

  it('nullBody_NotHidden_RendersEmptyWithoutNullText', () => {
    create(thread({ root: serverCommentView(comment({ body: null })) }));

    expect(q('comment-body')?.textContent?.trim()).toBe('');
    expect(el().textContent).not.toContain('null');
  });

  it('hiddenInModeratorView_ShowsFadedBodyReasonDateAndUnhide', () => {
    const view = serverCommentView(
      hidden({ body: 'Telefonum 0532…', authorDisplayName: 'Ali K.', canModerate: true, hiddenReason: 'kişisel bilgi', hiddenAt: '2026-09-30T11:00:00Z' })
    );
    create(thread({ root: view }), { moderatorView: true });
    const unhidden: string[] = [];
    fixture.componentInstance.unhide.subscribe((k) => unhidden.push(k));

    expect(q('comment-removed')).toBeNull();
    const body = q('comment-body')!;
    expect(body.classList).toContain('ci__body--hidden');
    expect(body.textContent).toContain('Telefonum 0532…');
    expect(q('hidden-badge')).not.toBeNull();
    expect(q('hidden-reason')?.textContent).toContain('kişisel bilgi');
    expect(q('hidden-at')?.textContent).toContain('2026');
    q('unhide-button')!.click();
    expect(unhidden).toEqual([view.key]);
  });

  it('hiddenWithoutModeratorView_StaysPlaceholderEvenIfBodyPresent', () => {
    create(thread({ root: serverCommentView(hidden({ body: 'gizli içerik', canModerate: true })) }), { moderatorView: false });

    expect(q('comment-removed')).not.toBeNull();
    expect(el().textContent).not.toContain('gizli içerik');
    // Moderatör menüden açabilir.
    openMenu();
    expect(menuItem('menu-unhide')).not.toBeNull();
    expect(menuItem('menu-report')).toBeNull();
  });

  it('othersComment_MenuHasReport; click emits key', () => {
    const view = serverCommentView(comment());
    create(thread({ root: view }));
    const reported: string[] = [];
    fixture.componentInstance.report.subscribe((k) => reported.push(k));

    const trigger = q('comment-menu')!;
    expect(trigger.getAttribute('aria-label')).toBe(commentsTr.item.menuAria);
    openMenu();
    expect(menuItem('menu-hide')).toBeNull();
    const item = menuItem('menu-report')!;
    expect(item.disabled).toBeFalse();
    item.click();
    expect(reported).toEqual([view.key]);
  });

  it('ownComment_NoReport; notModerator_NoMenu', () => {
    create(thread({ root: serverCommentView(comment({ isMine: true })) }));

    expect(q('comment-menu')).toBeNull();
  });

  it('canReportFalse_(admin)_NoReportItem', () => {
    create(thread({ root: serverCommentView(comment()) }), { canReport: false });

    expect(q('comment-menu')).toBeNull();
  });

  it('reportedByMe_ShowsStateAndDisabledItem', () => {
    create(thread({ root: serverCommentView(comment({ reportedByMe: true })) }));

    expect(q('reported-by-me')?.textContent).toContain(commentsTr.item.reportedByMe);
    openMenu();
    const item = menuItem('menu-report')!;
    expect(item.disabled).toBeTrue();
    expect(item.textContent).toContain(commentsTr.item.reportedByMe);
  });

  it('canModerate_MenuHasHide; reportCountBadge', () => {
    const view = serverCommentView(comment({ canModerate: true, reportCount: 3 }));
    create(thread({ root: view }));
    const hides: string[] = [];
    fixture.componentInstance.hide.subscribe((k) => hides.push(k));

    expect(q('report-count')?.textContent).toContain('3 şikayet');
    openMenu();
    menuItem('menu-hide')!.click();
    expect(hides).toEqual([view.key]);
  });

  it('reportCount_OnlyForModerator_AndSingular', () => {
    create(thread({ root: serverCommentView(comment({ canModerate: false, reportCount: 2 })) }));
    expect(q('report-count')).toBeNull();

    TestBed.resetTestingModule();
    create(thread({ root: serverCommentView(comment({ canModerate: true, reportCount: 1 })) }));
    expect(q('report-count')?.textContent).toContain(commentsTr.item.reportCountOne);

    TestBed.resetTestingModule();
    create(thread({ root: serverCommentView(comment({ canModerate: true, reportCount: 0 })) }));
    expect(q('report-count')).toBeNull();
  });

  it('hiddenRoot_NoReplyButton_ShowsLockButKeepsReplies', () => {
    create(
      thread({
        root: serverCommentView(hidden()),
        canReply: true,
        replies: [serverCommentView(comment({ id: 2, parentCommentId: 1, body: 'görünür cevap' }))],
        replyCount: 1,
      })
    );

    expect(q('reply-button')).toBeNull();
    expect(q('root-hidden-lock')?.textContent).toContain(commentsTr.item.rootHiddenLock);
    expect(qa('comment-body').map((b) => b.textContent?.trim())).toEqual(['görünür cevap']);
  });

  it('pendingComment_NoMenu', () => {
    create(thread({ root: { key: 'p3', comment: comment({ id: -3 }), state: 'sending', error: null } }));

    expect(q('comment-menu')).toBeNull();
  });
});
