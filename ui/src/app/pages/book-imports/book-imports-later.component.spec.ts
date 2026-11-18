import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { BookImportsLaterComponent } from './book-imports-later.component';
import { BookImportDeferredJob } from '../../models/book-import';
import { BookImportService } from '../../services/book-import.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import tr from '../../../../public/i18n/book-imports/tr.json';

function job(overrides: Partial<BookImportDeferredJob> = {}): BookImportDeferredJob {
  return {
    id: 'a1', bookName: 'Fen Bilimleri Yaprak Test', pageCount: 80, deferredAt: '2026-11-18T10:00:00Z', ownerId: 7,
    tests: [
      { id: 11, order: 4, unit: 2, testNo: 4, title: 'Kuvvet', status: 'NeedsReview', fileFrom: 23, fileTo: 24, flags: [],
        reviewFlags: [{ code: 'optionCount', pages: [23], numbers: [5], found: null, expected: null }], error: null },
      { id: 12, order: 5, unit: 3, testNo: 1, title: 'Madde', status: 'Failed', fileFrom: 41, fileTo: 41, flags: [],
        reviewFlags: [], error: 'boom' },
    ],
    ...overrides,
  };
}

describe('BookImportsLaterComponent (#1231)', () => {
  let fixture: ComponentFixture<BookImportsLaterComponent>;
  let api: MockedObject<BookImportService>;
  const el = () => fixture.nativeElement as HTMLElement;

  function create(deferred: ReturnType<BookImportService['deferred']>): void {
    api = {
      deferred: vi.fn().mockReturnValue(deferred),
      setDeferred: vi.fn().mockReturnValue(of(undefined)),
    } as unknown as MockedObject<BookImportService>;
    TestBed.configureTestingModule({
      imports: [BookImportsLaterComponent, translocoTestingModule({ langs: { 'book-imports/tr': tr } })],
      providers: [provideRouter([]), { provide: BookImportService, useValue: api }],
    });
    fixture = TestBed.createComponent(BookImportsLaterComponent);
    fixture.detectChanges();
  }

  it('lists the books with their unapproved tests linking to the review', () => {
    create(of([job()]));
    const book = el().querySelector('[data-testid="later-book"]') as HTMLElement;
    expect(book.textContent).toContain('Fen Bilimleri Yaprak Test');
    expect(book.textContent).toContain('2 test');
    const tests = Array.from(el().querySelectorAll<HTMLAnchorElement>('[data-testid="later-test"]'));
    expect(tests.map((a) => a.getAttribute('href'))).toEqual(['/book-imports/a1?test=11', '/book-imports/a1?test=12']);
    expect(tests[0].textContent!.replace(/\s+/g, ' ')).toContain('2-04 · Kuvvet');
    expect(tests[0].textContent).toContain('Kontrol gerekli');
    expect(tests[0].textContent).toContain('s. 23–24');
    expect(tests[1].textContent).toContain('Hata');
  });

  it('undo puts the book back on the import list', () => {
    create(of([job(), job({ id: 'b2', bookName: 'Puan BTS' })]));
    const undo = Array.from(el().querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent?.includes('Listeye geri al'))!;
    undo.click();
    fixture.detectChanges();
    expect(api.setDeferred).toHaveBeenCalledWith('a1', false);
    expect(el().querySelectorAll('[data-testid="later-book"]').length).toBe(1);
  });

  it('shows the empty state and the load error', () => {
    create(of([]));
    expect(el().querySelector('[data-testid="later-empty"]')).not.toBeNull();
    TestBed.resetTestingModule();
    create(throwError(() => new Error('x')));
    expect(el().textContent).toContain('Liste yüklenemedi.');
  });
});
