import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { BOOK_IMPORTS_POLL_MS, BookImportsComponent } from './book-imports.component';
import { BookImportJob, BookImportQuota } from '../../models/book-import';
import { BookImportService } from '../../services/book-import.service';
import { GradesService } from '../../services/grades.service';
import { SubjectService } from '../../services/subject.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import tr from '../../../../public/i18n/book-imports/tr.json';

function job(overrides: Partial<BookImportJob> = {}): BookImportJob {
  return {
    id: 'a1', bookName: 'Puan BTS 4 Matematik', gradeId: 4, subjectId: null, bookId: null,
    status: 'Running', stage: 'Pages', fileName: 'bts.pdf', fileSizeBytes: 1000, pageCount: 120,
    stageTotal: 120, stageProcessed: 30, message: null, error: null, createdAt: '2026-11-08T08:00:00Z',
    startedAt: '2026-11-08T08:00:05Z', finishedAt: null, ownerId: 3, ...overrides,
  };
}

const UNLIMITED: BookImportQuota = { limit: null, used: 0, resetsAtUtc: null, allowed: true, planKey: 'free' };

describe('BookImportsComponent (#1018)', () => {
  let fixture: ComponentFixture<BookImportsComponent>;
  let component: BookImportsComponent;
  let api: MockedObject<BookImportService>;
  let listResponse: Observable<BookImportJob[]>;
  let quota: BookImportQuota;

  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';
  const pdf = (name = 'kitap.pdf', size = 10) => new File([new Uint8Array(size)], name, { type: 'application/pdf' });

  function setup(): void {
    api = {
      list: vi.fn().mockName('list').mockImplementation(() => listResponse),
      get: vi.fn().mockName('get'),
      quota: vi.fn().mockName('quota').mockImplementation(() => of(quota)),
      start: vi.fn().mockName('start'),
    } as unknown as MockedObject<BookImportService>;
    TestBed.configureTestingModule({
      imports: [BookImportsComponent, translocoTestingModule({ langs: { 'book-imports/tr': tr } })],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        { provide: BookImportService, useValue: api },
        { provide: GradesService, useValue: { getGrades: () => of([{ id: 4, name: '4. Sınıf' }]) } },
        { provide: SubjectService, useValue: { getSubjectsByGrade: () => of([{ id: 3, name: 'Matematik' }]) } },
      ],
    });
    fixture = TestBed.createComponent(BookImportsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => {
    listResponse = of([job()]);
    quota = UNLIMITED;
  });

  afterEach(() => vi.useRealTimers());

  it('init_ListsJobsWithStageStatusAndProgress', () => {
    setup();

    const rows = el().querySelectorAll('.bi-table tbody tr');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('Puan BTS 4 Matematik');
    expect(rows[0].textContent).toContain(tr.stages.Pages);
    expect(rows[0].textContent).toContain('25%');
    expect(rows[0].textContent).toContain(tr.status.Running);
  });

  it('init_Empty_ShowsEmptyStateWithUploadAction', () => {
    listResponse = of([]);
    setup();

    expect(text()).toContain(tr.list.empty);
    expect(text()).toContain(tr.list.emptyAction);
  });

  it('init_Error_ShowsRetry', () => {
    listResponse = throwError(() => ({ status: 500 }));
    setup();

    expect(text()).toContain(tr.list.loadFailed);
    listResponse = of([job()]);
    (el().querySelector('.bi-state--error button') as HTMLButtonElement).click();
    expect(api.list).toHaveBeenCalledTimes(2);
  });

  it('filters_AwaitingAndFailed_AndBannerCountsAwaiting', () => {
    listResponse = of([
      job({ id: 'a', status: 'AwaitingConfirmation', stage: 'SetupConfirmation' }),
      job({ id: 'b', status: 'Failed' }),
      job({ id: 'c', status: 'Completed', stage: 'Approval' }),
    ]);
    setup();

    expect(text()).toContain('1 aktarım sizi bekliyor.');
    component.filter.set('awaiting');
    fixture.detectChanges();
    expect(el().querySelectorAll('.bi-table tbody tr').length).toBe(1);
    expect(el().querySelector('.bi-table tbody')!.textContent).toContain(tr.actions.openSetup);
    component.filter.set('failed');
    fixture.detectChanges();
    expect(el().querySelector('.bi-table tbody')!.textContent).toContain(tr.status.Failed);
  });

  it('poll_ReloadsWhileActive_StopsWhenFinished', async () => {
    vi.useFakeTimers();
    setup();
    expect(api.list).toHaveBeenCalledTimes(1);

    listResponse = of([job({ status: 'AwaitingConfirmation', stage: 'SetupConfirmation' })]);
    await vi.advanceTimersByTimeAsync(BOOK_IMPORTS_POLL_MS);
    expect(api.list).toHaveBeenCalledTimes(2);

    await vi.advanceTimersByTimeAsync(BOOK_IMPORTS_POLL_MS * 3);
    expect(api.list).toHaveBeenCalledTimes(2);
  });

  it('pickFile_RejectsNonPdf_AndFillsBookNameFromFile', () => {
    setup();
    component.openForm();

    component.pickFile(new File(['x'], 'notlar.docx', { type: 'application/msword' }));
    fixture.detectChanges();
    expect(text()).toContain(tr.errors.notPdf);

    component.pickFile(pdf('Puan BTS 4.pdf'));
    fixture.detectChanges();
    expect(component.file()?.name).toBe('Puan BTS 4.pdf');
    expect(text()).toContain('Puan BTS 4.pdf · 1 KB');
    expect(component.form.controls.bookName.value).toBe('Puan BTS 4');
    expect(text()).not.toContain(tr.errors.notPdf);
  });

  it('submit_StartsAndNavigatesToDetail', () => {
    setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    api.start.mockReturnValue(of(job({ id: 'new-id', status: 'Queued', stage: 'Upload' })));
    component.openForm();
    component.pickFile(pdf());
    component.form.controls.gradeId.setValue(4);

    component.submit();

    expect(api.start).toHaveBeenCalledWith(expect.objectContaining({ bookName: 'kitap', gradeId: 4, subjectId: null }));
    expect(navigate).toHaveBeenCalledWith(['/book-imports', 'new-id']);
  });

  it('submit_WithoutGrade_DoesNotStart', () => {
    setup();
    component.openForm();
    component.pickFile(pdf());

    component.submit();

    expect(api.start).not.toHaveBeenCalled();
  });

  it('submit_ServerError_ShowsMappedMessage', () => {
    setup();
    api.start.mockReturnValue(throwError(() => ({ status: 429, error: { code: 'entitlement.limitReached' } })));
    component.openForm();
    component.pickFile(pdf());
    component.form.controls.gradeId.setValue(4);

    component.submit();
    fixture.detectChanges();

    expect(text()).toContain(tr.errors.quotaReached);
  });

  it('quota_Blocked_DisablesStart', () => {
    quota = { limit: 5, used: 5, resetsAtUtc: '2026-12-01T00:00:00Z', allowed: false, planKey: 'pro' };
    setup();
    component.openForm();
    component.pickFile(pdf());
    fixture.detectChanges();

    expect(text()).toContain('Bu ay 5 / 5 kitap');
    expect(text()).toContain(tr.quota.blocked);
    const start = el().querySelector('button[type=submit]') as HTMLButtonElement;
    expect(start.disabled).toBe(true);
  });

  it('quota_Unlimited_HidesCard', () => {
    setup();
    component.openForm();
    fixture.detectChanges();

    expect(text()).not.toContain(tr.quota.title);
  });

  it('quota_NotInPlan_ExplainsAndPointsToDemo', () => {
    quota = { limit: 0, used: 0, resetsAtUtc: '2026-12-01T00:00:00Z', allowed: false, planKey: 'free' };
    setup();
    component.openForm();
    component.pickFile(pdf());
    fixture.detectChanges();

    expect(text()).toContain(tr.quota.notInPlan);
    expect(text()).toContain(tr.quota.demoSoon);
    expect((el().querySelector('button[type=submit]') as HTMLButtonElement).disabled).toBe(true);
  });

  it('errorKey_MapsKnownCodesAndFallsBack', () => {
    setup();
    expect(component.errorKey('bookImport.notPdf')).toBe('errors.notPdf');
    expect(component.errorKey('entitlement.limitReached')).toBe('errors.quotaReached');
    expect(component.errorKey('something.else')).toBe('errors.generic');
  });
});
