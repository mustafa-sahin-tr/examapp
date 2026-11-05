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
  note: 'The key looks wrong',
  status: 'open',
  createTime: '2026-11-05T08:00:00Z',
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
    expect(row.querySelector('a')?.getAttribute('href')).toBe('/question/42');
  });

  it('marks a report reviewed and reloads the list', () => {
    flushList([report()]);

    root().querySelector<HTMLButtonElement>('.report-row__actions button')!.click();
    const put = http.expectOne('/api/exam/questions/reports/7/reviewed');
    expect(put.request.method).toBe('PUT');
    put.flush(null);

    flushList([]);
    expect(root().textContent).toContain(reportsTr.emptyOpen);
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

    root().querySelector<HTMLButtonElement>('.report-row__actions button')!.click();
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
