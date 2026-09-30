import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { CommentReportListComponent } from './comment-report-list.component';
import {
  WorksheetComment,
  WorksheetCommentReportItem,
  WorksheetCommentReportsPage,
} from '../../../models/worksheet-comment.model';
import { LocaleService } from '../../../services/locale.service';
import { localeDefinitionOf } from '../../../models/locale';
import { translocoTestingModule } from '../../testing/transloco-testing';
import rootTr from '../../../../../public/i18n/tr.json';
import commentsTr from '../../../../../public/i18n/comments/tr.json';

const WS_URL = '/api/exam/worksheet/12/comments/reports';
const ADMIN_URL = '/api/exam/admin/comments/reports';

function comment(overrides: Partial<WorksheetComment> = {}): WorksheetComment {
  return {
    id: 5,
    worksheetId: 12,
    questionId: 3,
    questionOrder: 2,
    parentCommentId: 1,
    authorDisplayName: 'Ali K.',
    authorRole: 'Student',
    isMine: false,
    body: 'Numaram 0532…',
    createdAt: '2026-09-30T10:00:00Z',
    isHidden: false,
    reportedByMe: false,
    canModerate: true,
    reportCount: 3,
    hiddenReason: null,
    hiddenAt: null,
    ...overrides,
  };
}

function item(overrides: Partial<WorksheetCommentReportItem> = {}): WorksheetCommentReportItem {
  return {
    comment: comment(),
    worksheetTitle: 'Kesirler 1',
    reportCount: 3,
    reasons: { spam: 0, abuse: 1, personalInfo: 2, other: 0 },
    lastReportedAt: '2026-09-30T11:00:00Z',
    notes: ['telefon paylaşmış', ''],
    ...overrides,
  };
}

function reportsPage(overrides: Partial<WorksheetCommentReportsPage> = {}): WorksheetCommentReportsPage {
  return { items: [item()], page: 1, pageSize: 20, totalCount: 1, ...overrides };
}

describe('CommentReportListComponent (issue #305)', () => {
  let fixture: ComponentFixture<CommentReportListComponent>;
  let http: HttpTestingController;

  const el = () => fixture.nativeElement as HTMLElement;
  const q = <T extends HTMLElement = HTMLElement>(id: string) => el().querySelector<T>(`[data-testid="${id}"]`);
  const qa = (id: string) => Array.from(el().querySelectorAll<HTMLElement>(`[data-testid="${id}"]`));

  function create(worksheetId: number | null, inputs: Record<string, unknown> = {}): void {
    TestBed.configureTestingModule({
      imports: [
        CommentReportListComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { tr: { ...rootTr, comments: commentsTr }, 'comments/tr': commentsTr } }),
      ],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: LocaleService,
          useValue: { locale: signal('tr').asReadonly(), localeDefinition: signal(localeDefinitionOf('tr')).asReadonly() },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CommentReportListComponent);
    fixture.componentRef.setInput('worksheetId', worksheetId);
    Object.entries(inputs).forEach(([k, v]) => fixture.componentRef.setInput(k, v));
    fixture.detectChanges();
  }

  function expectReports(url: string, page = 1, pageSize = 20): TestRequest {
    const req = http.expectOne((r) => r.method === 'GET' && r.url === url);
    expect(req.request.params.get('page')).toBe(String(page));
    expect(req.request.params.get('pageSize')).toBe(String(pageSize));
    return req;
  }

  afterEach(() => http.verify());

  it('worksheet_LoadingThenRowsWithCountsReasonsNotesAndLink', () => {
    create(12);
    expect(q('reports-loading')).not.toBeNull();
    const totals: number[] = [];
    fixture.componentInstance.totalCountChange.subscribe((n) => totals.push(n));

    expectReports(WS_URL).flush(reportsPage());
    fixture.detectChanges();

    expect(q('reports-loading')).toBeNull();
    expect(q('reports-title')?.textContent).toContain('Şikayetler (1)');
    expect(totals).toEqual([1]);
    const row = qa('report-row')[0];
    expect(row.textContent).toContain('Soru 2');
    expect(q('report-count')?.textContent).toContain('3 şikayet');
    expect(q('report-body')?.textContent).toContain('Numaram 0532…');
    const reasons = Array.from(row.querySelectorAll<HTMLElement>('.rl__reason')).map((r) => r.dataset['reason']);
    expect(reasons).toEqual(['abuse', 'personalInfo']);
    expect(row.textContent).toContain(commentsTr.reportDialog.reasons.personalInfo);
    expect(row.querySelectorAll('.rl__notes li').length).toBe(1);
    // Worksheet bölümünde başlık tekrar edilmez.
    expect(q('report-worksheet')).toBeNull();
    const href = q<HTMLAnchorElement>('report-go')!.getAttribute('href');
    expect(href).toBe('/test/12?commentId=5&questionId=3&rootCommentId=1');
    expect(q('report-hide')).not.toBeNull();
    expect(q('report-unhide')).toBeNull();
  });

  it('empty_ShowsEmptyState', () => {
    create(12);
    expectReports(WS_URL).flush(reportsPage({ items: [], totalCount: 0 }));
    fixture.detectChanges();

    expect(q('reports-empty')?.textContent).toContain(commentsTr.reports.empty);
    expect(q('reports-title')?.textContent).toContain('Şikayetler (0)');
  });

  it('error_ShowsMessageAndRetryReloads', () => {
    create(12);
    expectReports(WS_URL).flush({ message: 'Test bulunamadı.', errorCode: 'WorksheetNotFound' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(q('reports-error')?.textContent).toContain('Test bulunamadı.');
    q('reports-retry')!.click();
    fixture.detectChanges();
    expectReports(WS_URL).flush(reportsPage());
    fixture.detectChanges();
    expect(q('reports-error')).toBeNull();
    expect(qa('report-row').length).toBe(1);
  });

  it('paging_RequestsNextPage', () => {
    create(12);
    expectReports(WS_URL).flush(reportsPage({ totalCount: 45 }));
    fixture.detectChanges();

    el().querySelector<HTMLButtonElement>('.mat-mdc-paginator-navigation-next')!.click();
    fixture.detectChanges();

    expectReports(WS_URL, 2).flush(reportsPage({ page: 2, totalCount: 45 }));
  });

  it('hide_OpensDialogAndMarksRowHidden', () => {
    create(12);
    expectReports(WS_URL).flush(reportsPage());
    fixture.detectChanges();
    const dialog = fixture.debugElement.injector.get(MatDialog);
    const open = spyOn(dialog, 'open').and.returnValue({
      afterClosed: () => of(comment({ isHidden: true, hiddenReason: 'kişisel bilgi' })),
    } as MatDialogRef<unknown>);

    q('report-hide')!.click();
    fixture.detectChanges();

    expect(open.calls.mostRecent().args[1]).toEqual(jasmine.objectContaining({ data: { worksheetId: 12, commentId: 5 } }));
    expect(q('report-hidden')).not.toBeNull();
    expect(q('report-hidden-reason')?.textContent).toContain('kişisel bilgi');
    expect(q('report-unhide')).not.toBeNull();
  });

  it('unhide_PostsAndMarksVisible; errorShown', () => {
    create(12);
    expectReports(WS_URL).flush(reportsPage({ items: [item({ comment: comment({ isHidden: true, hiddenReason: 'spam' }) })] }));
    fixture.detectChanges();

    q('report-unhide')!.click();
    http.expectOne('/api/exam/worksheet/12/comments/5/unhide').flush({}, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(q('reports-action-error')?.textContent).toContain(commentsTr.reports.unhideError);

    q('report-unhide')!.click();
    http.expectOne('/api/exam/worksheet/12/comments/5/unhide').flush(comment({ isHidden: false }));
    fixture.detectChanges();
    expect(q('report-hidden')).toBeNull();
    expect(q('report-hide')).not.toBeNull();
  });

  it('admin_UsesAdminEndpoint_ShowsWorksheetTitle_NoHeadingWhenDisabled', () => {
    create(null, { showHeading: false });
    expectReports(ADMIN_URL).flush(reportsPage({ items: [item({ comment: comment({ questionId: null, questionOrder: null, parentCommentId: null }) })] }));
    fixture.detectChanges();

    expect(q('reports-title')).toBeNull();
    expect(q('report-worksheet')?.textContent).toContain('Kesirler 1');
    expect(qa('report-row')[0].textContent).toContain(commentsTr.reports.worksheetLevel);
    expect(q<HTMLAnchorElement>('report-go')!.getAttribute('href')).toBe('/test/12?commentId=5');
  });

  it('hiddenReportedComment_NullBody_SafeRender', () => {
    create(12);
    expectReports(WS_URL).flush(reportsPage({ items: [item({ comment: comment({ body: null, isHidden: true }) })] }));
    fixture.detectChanges();

    expect(q('report-body')?.textContent?.trim()).toBe('');
    expect(el().textContent).not.toContain('null');
  });

  it('notModerator_NoHideOrUnhideActions_OnlyGoToComment', () => {
    create(12);
    expectReports(WS_URL).flush(
      reportsPage({
        items: [
          item({ comment: comment({ id: 5, canModerate: false }) }),
          item({ comment: comment({ id: 6, canModerate: false, isHidden: true }) }),
        ],
        totalCount: 2,
      })
    );
    fixture.detectChanges();

    expect(qa('report-go').length).toBe(2);
    expect(q('report-hide')).toBeNull();
    expect(q('report-unhide')).toBeNull();
  });
});
