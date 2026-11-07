import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { QuestionReportItem } from '../../models/question-report';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import reportsTr from '../../../../public/i18n/question-reports/tr.json';
import reportsEn from '../../../../public/i18n/question-reports/en.json';
import { QuestionReportsComponent } from './question-reports.component';

const LIST_URL = '/api/exam/questions/reports';

const report = (overrides: Partial<QuestionReportItem> = {}): QuestionReportItem => ({
  id: 7,
  questionId: 42,
  questionImageUrl: 'https://img/q.png',
  reason: 'wrongAnswer',
  reasons: ['wrongAnswer'],
  note: 'The key looks wrong',
  notes: ['The key looks wrong'],
  reportCount: 1,
  testNames: ['Fractions'],
  testCount: 1,
  answeredCount: 12,
  status: 'open',
  createTime: '2026-11-05T08:00:00Z',
  outcome: null,
  outcomeNote: null,
  reviewedAt: null,
  reviewedByMe: false,
  ...overrides,
});

describe('QuestionReportsComponent (#781)', () => {
  let fixture: ComponentFixture<QuestionReportsComponent>;
  let http: HttpTestingController;

  const root = () => fixture.nativeElement as HTMLElement;
  const flushList = (items: QuestionReportItem[], totalCount = items.length) => {
    http.expectOne((r) => r.url === LIST_URL).flush({ items, totalCount });
    fixture.detectChanges();
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        QuestionReportsComponent,
        NoopAnimationsModule,
        translocoTestingModule({
          langs: { tr: { 'question-reports': reportsTr }, 'question-reports/tr': reportsTr },
        }),
      ],
      providers: [provideHttpClient(withXhr()), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(QuestionReportsComponent);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('lists open reports with reason, note, and a link to the question', () => {
    const request = http.expectOne((r) => r.url === LIST_URL);
    expect(request.request.params.get('status')).toBe('Open');
    request.flush({ items: [report()], totalCount: 1 });
    fixture.detectChanges();

    const row = root().querySelector('[data-testid="question-report-row"]')!;
    expect(row.textContent).toContain(reportsTr.reason.wrongAnswer);
    expect(row.textContent).toContain('The key looks wrong');
    expect(row.querySelector('[data-testid="report-fix"]')?.getAttribute('href')).toBe('/question/42');
    expect(row.querySelector('[data-testid="report-fix"]')?.getAttribute('target')).toBe('_blank');
  });

  it('#865: shows merged reports with impact', () => {
    flushList([
      report({
        reasons: ['unreadable', 'wrongAnswer'],
        notes: ['blurry', 'cut off'],
        reportCount: 3,
        testNames: ['Fractions', 'Theme 1'],
        testCount: 4,
        answeredCount: 46,
      }),
    ]);

    const row = root().querySelector('[data-testid="question-report-row"]')!;
    expect(row.querySelector('[data-testid="report-count"]')?.textContent).toContain('3 bildirim');
    expect(row.textContent).toContain(reportsTr.reason.unreadable);
    expect(row.textContent).toContain('blurry');
    expect(row.textContent).toContain('cut off');
    const impact = row.querySelector('[data-testid="report-impact"]')!.textContent!;
    expect(impact).toContain('Fractions, Theme 1');
    expect(impact).toContain('+2');
    expect(impact).toContain('4 testte');
    expect(impact).toContain('46 öğrenci cevapladı');
  });

  it('#865: fixing opens the question, then "fixed" closes the report', () => {
    flushList([report()]);

    // Bağlantı yeni sekmede açılır; testte tıklamak sekme açardı, durumu doğrudan kuruyoruz.
    (fixture.componentInstance as unknown as { startFix(item: QuestionReportItem): void }).startFix(report());
    fixture.detectChanges();
    root().querySelector<HTMLButtonElement>('[data-testid="report-fixed"]')!.click();
    const put = http.expectOne('/api/exam/questions/reports/7/reviewed');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ outcome: 'fixed', note: null });
    put.flush(null);

    flushList([]);
    expect(root().textContent).toContain(reportsTr.emptyOpen);
  });

  it('#865: "no issue" asks for a short note and sends it', () => {
    flushList([report()]);

    root().querySelector<HTMLButtonElement>('[data-testid="report-no-issue-btn"]')!.click();
    fixture.detectChanges();
    const note = root().querySelector<HTMLTextAreaElement>('[data-testid="report-no-issue-note"]')!;
    note.value = '  The key is right  ';
    note.dispatchEvent(new Event('input'));
    root().querySelector<HTMLButtonElement>('[data-testid="report-no-issue-confirm"]')!.click();

    const put = http.expectOne('/api/exam/questions/reports/7/reviewed');
    expect(put.request.body).toEqual({ outcome: 'noIssue', note: 'The key is right' });
    put.flush(null);
    flushList([]);
    expect(root().querySelector('[data-testid="report-no-issue"]')).toBeNull();
  });

  it('#865: resolved reports show the outcome and who closed them', () => {
    flushList([]);
    root().querySelectorAll<HTMLButtonElement>('.reports__status button')[1].click();
    const request = http.expectOne((r) => r.url === LIST_URL);
    expect(request.request.params.get('status')).toBe('Reviewed');
    request.flush({
      items: [report({ status: 'reviewed', outcome: 'noIssue', outcomeNote: 'Key is right', reviewedAt: '2026-11-06T10:00:00Z', reviewedByMe: true })],
      totalCount: 1,
    });
    fixture.detectChanges();

    const outcome = root().querySelector('[data-testid="report-outcome"]')!.textContent!;
    expect(outcome).toContain(reportsTr.outcome.noIssue);
    expect(outcome).toContain('Key is right');
    expect(outcome).toContain('Sen · 06.11.2026');
    expect(root().querySelector('[data-testid="report-fix"]')).toBeNull();
  });

  it('shows a retry when the list cannot load', () => {
    http.expectOne((r) => r.url === LIST_URL).flush('down', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(root().querySelector('[role="alert"]')?.textContent).toContain(reportsTr.loadError);
    root().querySelector<HTMLButtonElement>('[role="alert"] button')!.click();
    flushList([]);
    expect(root().querySelector('[role="alert"]')).toBeNull();
  });

  it('keeps the list when marking reviewed fails and says so', () => {
    flushList([report()]);

    root().querySelector<HTMLButtonElement>('[data-testid="report-no-issue-btn"]')!.click();
    fixture.detectChanges();
    root().querySelector<HTMLButtonElement>('[data-testid="report-no-issue-confirm"]')!.click();
    http.expectOne('/api/exam/questions/reports/7/reviewed').flush('down', { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(root().querySelector('[role="alert"]')?.textContent).toContain(reportsTr.actionError);
    expect(root().querySelector('[data-testid="question-report-row"]')).not.toBeNull();
  });

  it('English copy has the same keys', () => {
    flushList([]);
    const keys = (value: object): string[] =>
      Object.entries(value).flatMap(([key, child]) =>
        typeof child === 'object' ? keys(child).map((inner) => `${key}.${inner}`) : [key],
      );
    expect(keys(reportsEn).sort()).toEqual(keys(reportsTr).sort());
  });
});
