import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { AdminImportCostsComponent } from './admin-import-costs.component';
import { BookImportCostReport } from '../../../models/book-import';
import { BookImportCostsService } from '../../../services/book-import-costs.service';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import bookImportsTr from '../../../../../public/i18n/book-imports/tr.json';

const REPORT: BookImportCostReport = {
  from: '2026-11-01',
  to: '2026-11-30',
  totals: { jobs: 2, pages: 12, questionsSaved: 8, costUsd: 1.5, costPerQuestionUsd: 0.1875 },
  users: [{ userId: 7, fullName: 'Ayşe Öğretmen', role: 'Teacher', planKey: 'free', planName: 'Free', jobs: 2, costUsd: 1.5 }],
  jobCount: 2,
  jobs: [
    {
      id: 'b1', kind: 'Book', bookName: 'Puan BTS', ownerId: 7, ownerName: 'Ayşe Öğretmen', createdAt: '2026-11-08T10:00:00Z',
      status: 'Failed', pages: 10, questionsSaved: 8, costUsd: 1.25,
    },
    {
      id: 'q1', kind: 'QuickAdd', bookName: 'Hızlı ekleme', ownerId: 7, ownerName: null, createdAt: '2026-11-08T11:00:00Z',
      status: 'ReadyForReview', pages: 2, questionsSaved: 0, costUsd: 0.25,
    },
  ],
  stages: [{ stage: 'Questions', modelId: 'claude-test', calls: 3, inputTokens: 100, outputTokens: 50, costUsd: 1.5 }],
};

const EMPTY: BookImportCostReport = {
  ...REPORT,
  totals: { jobs: 0, pages: 0, questionsSaved: 0, costUsd: 0, costPerQuestionUsd: null },
  users: [],
  jobCount: 0,
  jobs: [],
  stages: [],
};

describe('AdminImportCostsComponent (#1056)', () => {
  let fixture: ComponentFixture<AdminImportCostsComponent>;
  let api: { report: ReturnType<typeof vi.fn>; exportJobs: ReturnType<typeof vi.fn> };

  const text = (testId: string): string =>
    (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${testId}"]`)?.textContent?.trim() ?? '';

  const render = async (report = of(REPORT)): Promise<void> => {
    api = { report: vi.fn().mockReturnValue(report), exportJobs: vi.fn().mockReturnValue(of(new Blob(['x']))) };
    await TestBed.configureTestingModule({
      imports: [
        AdminImportCostsComponent,
        translocoTestingModule({ langs: { 'book-imports/tr': bookImportsTr } }),
      ],
      providers: [provideRouter([]), provideNativeDateAdapter(), provideNoopAnimations(), { provide: BookImportCostsService, useValue: api }],
    }).compileComponents();
    fixture = TestBed.createComponent(AdminImportCostsComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };

  it('loads the current month by default and shows totals, users, jobs and stages', async () => {
    await render();

    const [filter, page, pageSize] = api.report.mock.calls[0];
    const now = new Date();
    const month = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
    expect(filter.from).toBe(`${month}-01`);
    expect(filter.to.startsWith(month)).toBe(true);
    expect(filter.kind).toBeNull();
    expect(filter.userId).toBeNull();
    expect([page, pageSize]).toEqual([1, 20]);

    expect(text('cost-total')).toContain('1.50');
    expect(text('cost-users')).toContain('Ayşe Öğretmen');
    expect(text('cost-users')).toContain('Öğretmen');
    expect(text('cost-jobs')).toContain('Puan BTS');
    expect(text('cost-stages')).toContain('claude-test');
  });

  it('links each job to the detail page of its kind and marks failed jobs', async () => {
    await render();
    const component = fixture.componentInstance;

    expect(component.jobLink(REPORT.jobs[0])).toEqual(['/book-imports', 'b1']);
    expect(component.jobLink(REPORT.jobs[1])).toEqual(['/quick-add', 'q1']);
    const unfinished = (fixture.nativeElement as HTMLElement).querySelectorAll('.status--unfinished');
    expect(unfinished.length).toBe(1);
    expect(unfinished[0].textContent).toContain('Hata');
  });

  it('filters by user from the table, back to page one, and clears the filter', async () => {
    await render();
    const component = fixture.componentInstance;
    component.pageIndex.set(3);

    component.selectUser(REPORT.users[0]);

    expect(api.report.mock.calls.at(-1)![0].userId).toBe(7);
    expect(api.report.mock.calls.at(-1)![1]).toBe(1);

    component.clearUser();
    expect(api.report.mock.calls.at(-1)![0].userId).toBeNull();
  });

  it('sends the kind filter and pages the job table', async () => {
    await render();
    const component = fixture.componentInstance;

    component.kind.set('QuickAdd');
    component.applyFilters();
    expect(api.report.mock.calls.at(-1)![0].kind).toBe('QuickAdd');

    component.onPage({ pageIndex: 1, pageSize: 50, length: 100 });
    expect(api.report.mock.calls.at(-1)!.slice(1)).toEqual([2, 50]);
  });

  it('shows the empty state', async () => {
    await render(of(EMPTY));

    expect(text('cost-empty')).toBe(bookImportsTr.costReport.empty);
    expect(text('cost-tiles')).toBe('');
  });

  it('does not request a reversed range', async () => {
    await render();
    const component = fixture.componentInstance;
    const calls = api.report.mock.calls.length;

    component.from.set(new Date(2026, 10, 20));
    component.to.set(new Date(2026, 10, 10));
    component.applyFilters();
    fixture.detectChanges();

    expect(api.report.mock.calls.length).toBe(calls);
    expect(text('cost-range-error')).toBe(bookImportsTr.costReport.errors.range);
  });

  it('shows a load error', async () => {
    await render(throwError(() => new Error('boom')));

    expect(text('cost-error')).toBe(bookImportsTr.costReport.errors.load);
  });

  it('exports the filtered jobs as a CSV', async () => {
    await render();
    const component = fixture.componentInstance;
    const createUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:x');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);

    component.kind.set('Book');
    component.exportCsv();

    expect(api.exportJobs.mock.calls[0][0].kind).toBe('Book');
    expect(createUrl).toHaveBeenCalled();
    expect(revoke).toHaveBeenCalledWith('blob:x');
    createUrl.mockRestore();
    revoke.mockRestore();
  });
});
