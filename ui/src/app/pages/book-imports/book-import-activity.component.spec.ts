import type { MockedObject } from 'vitest';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Observable, of, throwError } from 'rxjs';

import { BookImportActivityComponent } from './book-import-activity.component';
import { BOOK_IMPORTS_POLL_MS } from './book-imports.component';
import { BookImportActivity, BookImportEvent } from '../../models/book-import';
import { BookImportService } from '../../services/book-import.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import tr from '../../../../public/i18n/book-imports/tr.json';

function event(id: number, code: string, data: Record<string, unknown> | null = null, at = '2026-11-21T19:55:30Z'): BookImportEvent {
  return { id, at, stage: 'Pages', code, data };
}

describe('BookImportActivityComponent (#1265)', () => {
  let fixture: ComponentFixture<BookImportActivityComponent>;
  let api: MockedObject<BookImportService>;
  let responses: Observable<BookImportActivity>[];

  const el = () => fixture.nativeElement as HTMLElement;
  const lines = () => Array.from(el().querySelectorAll('.bia-row__text')).map((n) => n.textContent?.trim());

  function setup(active: boolean): void {
    api = {
      activity: vi.fn().mockImplementation(() => responses.shift() ?? of({ events: [], calls: null })),
    } as unknown as MockedObject<BookImportService>;
    TestBed.configureTestingModule({
      imports: [BookImportActivityComponent, translocoTestingModule({ langs: { 'book-imports/tr': tr } })],
      providers: [provideNoopAnimations(), { provide: BookImportService, useValue: api }],
    });
    fixture = TestBed.createComponent(BookImportActivityComponent);
    fixture.componentRef.setInput('jobId', 'a1');
    fixture.componentRef.setInput('active', active);
    fixture.detectChanges();
  }

  beforeEach(() => {
    vi.useFakeTimers();
    try {
      localStorage.removeItem('bookImports.activity.calls');
    } catch {
      /* yok */
    }
  });
  afterEach(() => vi.useRealTimers());

  it('shows the events as time-stamped lines and asks only for new ones while the import runs', () => {
    responses = [
      of({
        events: [
          event(1, 'pagesReady', { pages: 65 }),
          event(2, 'cleaningAnalysisStarted', { pages: 65, sample: 32, colourPicked: false }),
        ],
        calls: null,
      }),
      of({ events: [event(3, 'cleaningNothingFound', null, '2026-11-21T19:59:13Z')], calls: null }),
    ];
    setup(true);

    expect(lines()).toEqual([
      'Sayfalar hazır (65 sayfa)',
      'Sayfa temizliği: 32 örnek sayfada filigran ve cevap işareti aranıyor (birkaç dakika sürebilir)',
    ]);
    expect(el().querySelector('[data-testid="activity-live"]')).not.toBeNull();

    vi.advanceTimersByTime(BOOK_IMPORTS_POLL_MS);
    fixture.detectChanges();
    expect(api.activity).toHaveBeenLastCalledWith('a1', 2, false, 0);
    expect(lines()[2]).toBe('Analiz bitti · filigran ya da işaret yok, temizlik atlandı');
  });

  it('does not poll a finished import', () => {
    responses = [of({ events: [event(1, 'cancelled')], calls: null })];
    setup(false);

    vi.advanceTimersByTime(BOOK_IMPORTS_POLL_MS * 3);
    expect(api.activity).toHaveBeenCalledTimes(1);
    expect(lines()).toEqual(['Aktarım iptal edildi']);
    expect(el().querySelector('.bia-row--danger')).not.toBeNull();
  });

  it('builds test, pause and sharing lines from the event values', () => {
    responses = [
      of({
        events: [
          event(1, 'setupConfirmed', { sharing: 'PublicAssignable' }),
          event(2, 'testStarted', { order: 3, title: 'Güneş', fileFrom: 9, fileTo: 10 }),
          event(3, 'testFinished', { order: 3, title: 'Güneş', status: 'NeedsReview', questions: 10, error: null }),
          event(4, 'testFinished', { order: 4, title: null, status: 'Failed', questions: 0, error: 'timeout' }),
          event(5, 'paused', { reason: 'CostCeiling', cost: 10.25 }),
        ],
        calls: null,
      }),
    ];
    setup(false);

    expect(lines()).toEqual([
      'Kurulum onaylandı · paylaşım: Herkese açık (atanabilir); sorular alınıyor',
      'Güneş alınıyor (sayfa 9–10)',
      'Güneş kontrol gerekli · 10 soru',
      'Test 4 alınamadı: timeout',
      'Maliyet sınırına ulaşıldı ($10.2500); devam için onay gerekiyor',
    ]);
  });

  it('adds AI calls between the events when the toggle is on and remembers the choice', () => {
    responses = [
      of({ events: [event(1, 'stageStarted', null, '2026-11-21T19:59:00Z')], calls: null }),
      of({
        events: [],
        calls: [{
          id: 7, at: '2026-11-21T19:59:27Z', stage: 'Setup', testId: null, modelId: 'claude-haiku-5-5',
          inputTokens: 9295, outputTokens: 1815, costUsd: 0.001837,
        }],
      }),
    ];
    setup(false);
    expect(lines()).toEqual(['Sayfalar aşaması başladı']);

    fixture.componentInstance.toggleCalls(true);
    fixture.detectChanges();
    expect(api.activity).toHaveBeenLastCalledWith('a1', 1, true, 0);
    expect(lines().length).toBe(2);
    expect(lines()[1]).toContain('claude-haiku-5-5');
    expect(localStorage.getItem('bookImports.activity.calls')).toBe('1');
  });

  it('says when the log could not be refreshed', () => {
    responses = [throwError(() => new Error('x'))];
    setup(false);
    expect(el().querySelector('.bia-error')).not.toBeNull();
  });
});
