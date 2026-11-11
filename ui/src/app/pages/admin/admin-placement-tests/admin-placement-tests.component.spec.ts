import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { AdminPlacementTestsComponent } from './admin-placement-tests.component';
import { AdminPlacementTest } from '../../../models/placement.model';
import { PlacementService } from '../../../services/placement.service';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import placementTr from '../../../../../public/i18n/placement/tr.json';

/** Issue #1114: admin sınıf başına testi üretir ve yayımlar. */
describe('AdminPlacementTestsComponent (issue #1114)', () => {
  const empty: AdminPlacementTest = {
    gradeId: 7,
    gradeName: '7. Sınıf',
    questionCount: 0,
    generatedAtUtc: null,
    publishedAtUtc: null,
    attemptCount: 0,
    subjects: [],
  };
  const draft: AdminPlacementTest = {
    ...empty,
    questionCount: 20,
    generatedAtUtc: '2026-11-11T10:00:00Z',
    subjects: [{ subjectId: 1, subjectName: 'Matematik', correct: 0, total: 10, percent: 0 }],
  };

  function create(api: Record<string, unknown>) {
    TestBed.configureTestingModule({
      imports: [AdminPlacementTestsComponent, translocoTestingModule({ langs: { 'placement/tr': placementTr } })],
      providers: [{ provide: PlacementService, useValue: { adminList: () => of([empty]), ...api } }],
    });
    const fixture = TestBed.createComponent(AdminPlacementTestsComponent);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('generates a test and then publishes it', () => {
    const adminGenerate = vi.fn().mockReturnValue(of(draft));
    const adminSetPublished = vi.fn().mockReturnValue(of({ ...draft, publishedAtUtc: '2026-11-11T11:00:00Z' }));
    const { fixture, el } = create({ adminGenerate, adminSetPublished });
    expect(el.querySelector('[data-testid="placement-test-status"]')!.textContent).toContain('Yok');

    el.querySelector<HTMLButtonElement>('[data-testid="placement-generate"]')!.click();
    fixture.detectChanges();
    expect(adminGenerate).toHaveBeenCalledWith(7);
    expect(el.textContent).toContain('Matematik (10)');
    expect(el.querySelector('[data-testid="placement-test-status"]')!.textContent).toContain('Taslak');

    el.querySelector<HTMLButtonElement>('[data-testid="placement-publish"]')!.click();
    fixture.detectChanges();
    expect(adminSetPublished).toHaveBeenCalledWith(7, true);
    expect(el.querySelector('[data-testid="placement-test-status"]')!.textContent).toContain('Yayında');
  });

  it('explains a thin question bank', () => {
    const adminGenerate = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
    const { fixture, el } = create({ adminGenerate });
    el.querySelector<HTMLButtonElement>('[data-testid="placement-generate"]')!.click();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')!.textContent).toContain('yeterli çoktan seçmeli soru yok');
  });
});
