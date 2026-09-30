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

  function create(value: ThreadView): void {
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
    fixture.detectChanges();
  }

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
});
