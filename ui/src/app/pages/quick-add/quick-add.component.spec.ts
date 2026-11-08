import type { MockedObject } from 'vitest';
import { HttpErrorResponse, HttpEventType, HttpResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';

import { QUICK_ADD_GRADE_KEY, QUICK_ADD_POLL_MS, QuickAddComponent } from './quick-add.component';
import { BookImportJob, BookImportQuota } from '../../models/book-import';
import { QUICK_ADD_MAX_PHOTO_BYTES } from '../../models/quick-add';
import { GradesService } from '../../services/grades.service';
import { QuickAddService } from '../../services/quick-add.service';
import { SubjectService } from '../../services/subject.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import tr from '../../../../public/i18n/quick-add/tr.json';

function job(overrides: Partial<BookImportJob> = {}): BookImportJob {
  return {
    id: 'q1', bookName: 'Hızlı ekleme', gradeId: 4, subjectId: null, bookId: null,
    status: 'ReadyForReview', stage: 'Review', fileName: null, fileSizeBytes: 1000, pageCount: 2,
    stageTotal: 2, stageProcessed: 2, message: null, error: null, createdAt: '2026-11-08T08:00:00Z',
    startedAt: '2026-11-08T08:00:05Z', finishedAt: null, ownerId: 3, resumeAt: null, stalled: false, aiCostUsd: null,
    costCeilingUsd: null, ...overrides,
  };
}

const QUOTA: BookImportQuota = { limit: 5, used: 2, resetsAtUtc: null, allowed: true, planKey: 'free' };

describe('QuickAddComponent (#1053)', () => {
  let fixture: ComponentFixture<QuickAddComponent>;
  let component: QuickAddComponent;
  let api: MockedObject<QuickAddService>;
  let listResponse: Observable<BookImportJob[]>;
  let quota: BookImportQuota;

  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => el().textContent ?? '';
  const photo = (name = 'foto.jpg', size = 10, type = 'image/jpeg') => new File([new Uint8Array(size)], name, { type });

  function setup(): void {
    api = {
      list: vi.fn().mockName('list').mockImplementation(() => listResponse),
      quota: vi.fn().mockName('quota').mockImplementation(() => of(quota)),
      start: vi.fn().mockName('start'),
    } as unknown as MockedObject<QuickAddService>;
    TestBed.configureTestingModule({
      imports: [QuickAddComponent, translocoTestingModule({ langs: { 'quick-add/tr': tr } })],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        { provide: QuickAddService, useValue: api },
        { provide: GradesService, useValue: { getGrades: () => of([{ id: 4, name: '4. Sınıf' }]) } },
        { provide: SubjectService, useValue: { getSubjectsByGrade: () => of([{ id: 3, name: 'Matematik' }]) } },
      ],
    });
    fixture = TestBed.createComponent(QuickAddComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => {
    localStorage.removeItem(QUICK_ADD_GRADE_KEY);
    listResponse = of([job()]);
    quota = QUOTA;
  });

  afterEach(() => {
    vi.useRealTimers();
    localStorage.removeItem(QUICK_ADD_GRADE_KEY);
  });

  it('init_ShowsAllowanceAndRecentJobsWithPhase', () => {
    setup();

    expect(text()).toContain('Bu ay 2/5 fotoğraf');
    const rows = el().querySelectorAll('.qa-job');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain(tr.phase.ready);
    expect(rows[0].textContent).toContain('2 fotoğraf');
    expect(rows[0].getAttribute('href')).toBe('/quick-add/q1');
  });

  it('init_UnlimitedQuota_HidesAllowance', () => {
    quota = { ...QUOTA, limit: null };
    setup();

    expect(el().querySelector('.qa-quota')).toBeNull();
  });

  it('init_NoAllowanceLeft_ShowsBlockedMessage', () => {
    quota = { ...QUOTA, used: 5, allowed: false };
    setup();

    expect(el().querySelector('.qa-quota--blocked')?.textContent).toContain(tr.quotaBlocked);
  });

  it('init_RestoresLastGrade', () => {
    localStorage.setItem(QUICK_ADD_GRADE_KEY, '4');
    setup();

    expect(component.gradeId.value).toBe(4);
  });

  it('addFiles_WrongTypeTooLargeAndTooMany_RejectsWithMessages', () => {
    setup();

    component.addFiles([
      photo('belge.pdf', 10, 'application/pdf'),
      photo('dev.jpg', QUICK_ADD_MAX_PHOTO_BYTES + 1),
      ...Array.from({ length: 11 }, (_, i) => photo(`f${i}.jpg`)),
    ]);
    fixture.detectChanges();

    expect(component.photos().length).toBe(10);
    expect(text()).toContain('belge.pdf bir fotoğraf değil');
    expect(text()).toContain('dev.jpg çok büyük');
    expect(text()).toContain(tr.errors.tooMany);
    // Sınıra ulaşınca seçici gizlenir.
    expect(el().querySelector('.qa-picker')).toBeNull();
  });

  it('addFiles_ShowsThumbnailAndAnswerFieldPerPhoto_RemoveDropsIt', () => {
    setup();

    component.addFiles([photo('a.jpg'), photo('b.png', 10, 'image/png')]);
    fixture.detectChanges();
    expect(el().querySelectorAll('.qa-photo').length).toBe(2);
    expect(el().querySelectorAll('.qa-photo input').length).toBe(2);
    expect(text()).toContain(tr.form.answersHint);

    (el().querySelector('.qa-photo__remove') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el().querySelectorAll('.qa-photo').length).toBe(1);
    expect(component.photos()[0].file.name).toBe('b.png');
  });

  it('addFiles_MoreThanRemainingAllowance_Warns', () => {
    setup();

    component.addFiles([photo('a.jpg'), photo('b.jpg'), photo('c.jpg'), photo('d.jpg')]);
    fixture.detectChanges();

    expect(el().querySelector('.qa-warning')?.textContent).toContain('3 fotoğraf hakkın kaldı');
  });

  it('submit_WithoutPhotosOrGrade_DoesNotUpload', () => {
    setup();

    component.submit();
    fixture.detectChanges();

    expect(api.start).not.toHaveBeenCalled();
    expect(text()).toContain(tr.form.noPhotos);
  });

  it('submit_SendsPhotosWithAnswers_ShowsProgress_ThenOpensJob', () => {
    setup();
    const events = new Subject<unknown>();
    api.start.mockReturnValue(events as never);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    component.gradeId.setValue(4);
    component.addFiles([photo('a.jpg'), photo('b.jpg')]);
    component.setAnswers(component.photos()[0].id, 'a c b');

    component.submit();
    events.next({ type: HttpEventType.UploadProgress, loaded: 40, total: 100 });
    fixture.detectChanges();

    expect(api.start).toHaveBeenCalledWith({
      gradeId: 4,
      subjectId: null,
      photos: [
        { file: component.photos()[0].file, answers: 'a c b' },
        { file: component.photos()[1].file, answers: '' },
      ],
    });
    expect(text()).toContain('Yükleniyor… %40');

    events.next(new HttpResponse({ body: job({ id: 'new1', status: 'Queued' }) }));
    expect(navigate).toHaveBeenCalledWith(['/quick-add', 'new1']);
  });

  it('submit_ServerRejectsAnswerKey_ShowsPhotoNumberAndMarksPhoto', () => {
    setup();
    api.start.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { code: 'quickAdd.invalidAnswerKey', photo: 2 } }))
    );
    component.gradeId.setValue(4);
    component.addFiles([photo('a.jpg'), photo('b.jpg')]);

    component.submit();
    fixture.detectChanges();

    expect(text()).toContain('Fotoğraf 2: cevap anahtarı okunamadı');
    expect(el().querySelectorAll('.qa-photo')[1].classList).toContain('qa-photo--error');
  });

  it('submit_QuotaReached_ShowsMessageAndReloadsAllowance', () => {
    setup();
    api.start.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 429, error: { code: 'entitlement.limitReached', feature: 'quick-add' } }))
    );
    component.gradeId.setValue(4);
    component.addFiles([photo('a.jpg')]);

    component.submit();
    fixture.detectChanges();

    expect(text()).toContain(tr.errors.quotaReached);
    expect(api.quota).toHaveBeenCalledTimes(2);
  });

  it('list_PendingJob_PollsUntilDone', async () => {
    vi.useFakeTimers();
    listResponse = of([job({ status: 'Running', stageProcessed: 1 })]);
    setup();
    expect(api.list).toHaveBeenCalledTimes(1);
    expect(el().querySelector('.qa-job mat-progress-bar')).not.toBeNull();

    listResponse = of([job()]);
    await vi.advanceTimersByTimeAsync(QUICK_ADD_POLL_MS);
    expect(api.list).toHaveBeenCalledTimes(2);

    await vi.advanceTimersByTimeAsync(QUICK_ADD_POLL_MS * 2);
    expect(api.list).toHaveBeenCalledTimes(2);
  });

  it('list_Empty_ShowsEmptyState', () => {
    listResponse = of([]);
    setup();

    expect(text()).toContain(tr.list.empty);
  });
});
