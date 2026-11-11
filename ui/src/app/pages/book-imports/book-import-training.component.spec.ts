import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import type { MockedObject } from 'vitest';

import { BookImportTrainingSummary } from '../../models/book-import';
import { BookImportService } from '../../services/book-import.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import tr from '../../../../public/i18n/book-imports/tr.json';
import { BookImportTrainingComponent } from './book-import-training.component';

describe('BookImportTrainingComponent (#1133)', () => {
  let fixture: ComponentFixture<BookImportTrainingComponent>;
  let api: MockedObject<BookImportService>;

  const el = () => fixture.nativeElement as HTMLElement;
  const button = (label: string) =>
    Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.includes(label)) as HTMLButtonElement;

  const summary: BookImportTrainingSummary = {
    pages: 3,
    completePages: 2,
    emptyPages: 0,
    questions: 6,
    choiceCounts: { '3': 3, '4': 3 },
    books: [{ jobId: 'j1', bookName: 'LGS Matematik', pages: 3, questions: 6, manualPages: 1 }],
    skipped: [
      { jobId: 'j1', bookName: 'LGS Matematik', filePage: 4, reason: 'flagged' },
      { jobId: 'j1', bookName: 'LGS Matematik', filePage: 5, reason: 'flagged' },
      { jobId: 'j1', bookName: 'LGS Matematik', filePage: 6, reason: 'flagged' },
      { jobId: 'j1', bookName: 'LGS Matematik', filePage: 9, reason: 'noDetection' },
    ],
  };

  function setup(): void {
    api = {
      trainingSummary: vi.fn().mockReturnValue(of(summary)),
      trainingZip: vi.fn().mockReturnValue(of(new Blob(['zip']))),
    } as unknown as MockedObject<BookImportService>;
    TestBed.configureTestingModule({
      imports: [BookImportTrainingComponent, translocoTestingModule({ langs: { 'book-imports/tr': tr } })],
      providers: [{ provide: BookImportService, useValue: api }],
    });
    fixture = TestBed.createComponent(BookImportTrainingComponent);
    fixture.detectChanges();
  }

  it('summary_ShowsTotalsChoicesBooksAndGroupedSkips', () => {
    setup();
    expect(button(tr.training.download).disabled).toBe(true);

    button(tr.training.summarize).click();
    fixture.detectChanges();

    expect(el().querySelector('[data-testid="training-totals"]')!.textContent).toContain('3 sayfa (2 tam sayfa, 0 sorusuz) · 6 soru');
    expect(el().textContent).toContain('3 şık: 3 · 4 şık: 3');
    expect(el().textContent).toContain('LGS Matematik: 3 sayfa, 6 soru, 1 sayfa düzeltilmiş');
    const skipped = Array.from(el().querySelectorAll('[data-testid="training-skipped"] li')).map((li) => li.textContent!.trim());
    expect(skipped).toEqual(['LGS Matematik · uyarılı, düzeltilmedi · sayfa 4–6', 'LGS Matematik · tespit yok · sayfa 9']);
    expect(button(tr.training.download).disabled).toBe(false);
  });

  it('download_SavesTheZip', () => {
    setup();
    button(tr.training.summarize).click();
    fixture.detectChanges();
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);

    button(tr.training.download).click();

    expect(api.trainingZip).toHaveBeenCalled();
    expect(click).toHaveBeenCalled();
    click.mockRestore();
  });

  it('error_ShowsAMessage', () => {
    setup();
    api.trainingSummary.mockReturnValue(throwError(() => new Error('403')));
    button(tr.training.summarize).click();
    fixture.detectChanges();

    expect(el().querySelector('[role="alert"]')!.textContent).toContain(tr.training.error);
  });
});
