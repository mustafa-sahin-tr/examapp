// Issue #583: açık uçlu cevap oynatıcısı — yazma + gecikmeli kayıt, sayaç / sınırlar, değerlendirme durumları, örnek cevap, XSS.
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { QuestionRegion } from '../../../models/draws';
import { OpenTextStudentReview } from '../../../models/open-text';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { OPEN_TEXT_SAVE_DEBOUNCE_MS, QuestionCanvasOpenTextComponent } from './question-canvas-open-text.component';

const PLAN = { version: 1, type: 'openText', minWords: 3, maxWords: 5, lines: 4 };

function region(): QuestionRegion {
  return { imageUrl: 'https://cdn.example.test/q1.webp', width: 200, height: 100, passage: null } as unknown as QuestionRegion;
}

const answer = (text: string) => JSON.stringify({ version: 1, type: 'openText', text });

describe('QuestionCanvasOpenTextComponent (#583)', () => {
  let fixture: ComponentFixture<QuestionCanvasOpenTextComponent>;
  let emitted: string[];

  const el = () => fixture.nativeElement as HTMLElement;
  const q = (testId: string) => el().querySelector(`[data-testid="${testId}"]`) as HTMLElement | null;
  const textarea = () => q('ot-text') as HTMLTextAreaElement;

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  function setup(
    inputs: {
      plan?: object;
      answer?: string | null;
      review?: OpenTextStudentReview | null;
      solution?: object | null;
      showSolution?: boolean;
      activity?: boolean;
      disabled?: boolean;
    } = {}
  ): void {
    TestBed.configureTestingModule({ imports: [QuestionCanvasOpenTextComponent, translocoTestingModule()] });
    fixture = TestBed.createComponent(QuestionCanvasOpenTextComponent);
    emitted = [];
    fixture.componentInstance.answerPayloadChange.subscribe((p) => emitted.push(p));
    fixture.componentRef.setInput('questionRegion', region());
    fixture.componentRef.setInput('interactionPlanJson', JSON.stringify(inputs.plan ?? PLAN));
    fixture.componentRef.setInput('interactionSolutionJson', inputs.solution ? JSON.stringify(inputs.solution) : null);
    fixture.componentRef.setInput('answerPayloadJson', inputs.answer ?? null);
    fixture.componentRef.setInput('review', inputs.review ?? null);
    fixture.componentRef.setInput('showSolution', !!inputs.showSolution);
    fixture.componentRef.setInput('activityMode', inputs.activity ?? true);
    fixture.componentRef.setInput('disabled', !!inputs.disabled);
    fixture.detectChanges();
  }

  function type(value: string) {
    const field = textarea();
    field.value = value;
    field.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  it('render_ImageAndMaterialTextareaWithPlanLines', () => {
    setup();
    expect(el().querySelector('img.ot-image')?.getAttribute('src')).toBe('https://cdn.example.test/q1.webp');
    expect(textarea().getAttribute('rows')).toBe('4');
    expect(textarea().getAttribute('maxlength')).toBe('8000');
    expect(el().querySelector('mat-form-field')).not.toBeNull();
  });

  it('typing_SavesAfterDebounce_BlurSavesImmediately_FlushSendsPending', async () => {
    vi.useFakeTimers();
    setup();
    type('bir iki');
    expect(emitted).toEqual([]);
    await vi.advanceTimersByTimeAsync(OPEN_TEXT_SAVE_DEBOUNCE_MS);
    expect(emitted.map((p) => JSON.parse(p))).toEqual([{ version: 1, type: 'openText', text: 'bir iki' }]);

    type('bir iki üç');
    textarea().dispatchEvent(new Event('blur'));
    expect(JSON.parse(emitted.at(-1)!).text).toBe('bir iki üç');

    type('bir iki üç dört');
    fixture.componentInstance.flush();
    expect(JSON.parse(emitted.at(-1)!).text).toBe('bir iki üç dört');
    const count = emitted.length;
    await vi.advanceTimersByTimeAsync(OPEN_TEXT_SAVE_DEBOUNCE_MS);
    expect(emitted.length).toBe(count); // aynı metin ikinci kez gönderilmez
  });

  it('counter_UnderOverBlocked_BlockedTextIsNotSaved', () => {
    setup();
    type('bir iki');
    expect(q('ot-counter')?.textContent).toContain('2 sözcük');
    expect(q('ot-counter')?.textContent).toContain('en az 3, en çok 5 sözcük');
    expect(q('ot-under')).not.toBeNull();
    fixture.componentInstance.flush();
    expect(emitted.length).toBe(1);

    type('1 2 3 4 5 6');
    expect(q('ot-over')).not.toBeNull();
    expect(q('ot-counter')?.getAttribute('data-state')).toBe('over');
    fixture.componentInstance.flush();
    expect(JSON.parse(emitted.at(-1)!).text).toBe('1 2 3 4 5 6'); // aşım yalnız uyarı

    type('1 2 3 4 5 6 7 8 9 10 11');
    expect(q('ot-blocked')).not.toBeNull();
    fixture.componentInstance.flush();
    expect(JSON.parse(emitted.at(-1)!).text).toBe('1 2 3 4 5 6'); // 2 katını aşan metin kaydedilmez
  });

  it('controlCharactersAreStripped', () => {
    setup();
    type('a\u0000b\u0007c');
    expect(textarea().value).toBe('abc');
    fixture.componentInstance.flush();
    expect(JSON.parse(emitted.at(-1)!).text).toBe('abc');
  });

  it('restore_EchoOfOwnPayloadDoesNotOverwriteNewerTyping', () => {
    setup({ answer: answer('ilk') });
    expect(textarea().value).toBe('ilk');
    type('ilk ikinci');
    fixture.componentInstance.flush();
    const sent = emitted.at(-1)!;
    type('ilk ikinci üçüncü');
    fixture.componentRef.setInput('answerPayloadJson', sent);
    fixture.detectChanges();
    expect(textarea().value).toBe('ilk ikinci üçüncü');
  });

  it('sameInstance_ItemAThenEmptyBThenA_ShowsAText (review #1)', () => {
    setup();
    const regionA = region();
    fixture.componentRef.setInput('questionRegion', regionA);
    fixture.detectChanges();
    type('A maddesinin cevabı');
    fixture.componentInstance.flush();
    const sentA = emitted.at(-1)!;
    fixture.componentRef.setInput('answerPayloadJson', sentA); // ebeveynin yankısı
    fixture.detectChanges();

    fixture.componentRef.setInput('questionRegion', region()); // B (boş)
    fixture.componentRef.setInput('answerPayloadJson', null);
    fixture.detectChanges();
    expect(textarea().value).toBe('');

    fixture.componentRef.setInput('questionRegion', regionA);
    fixture.componentRef.setInput('answerPayloadJson', sentA);
    fixture.detectChanges();
    expect(textarea().value).toBe('A maddesinin cevabı');

    // Girdi sırası ters olsa da (önce payload, sonra madde) doğru metin.
    fixture.componentRef.setInput('answerPayloadJson', null);
    fixture.componentRef.setInput('questionRegion', region());
    fixture.detectChanges();
    expect(textarea().value).toBe('');
  });

  it('isBlocked_OnlyAboveTwiceMaxWords', () => {
    setup();
    type('1 2 3 4 5 6');
    expect(fixture.componentInstance.isBlocked()).toBe(false);
    type('1 2 3 4 5 6 7 8 9 10 11');
    expect(fixture.componentInstance.isBlocked()).toBe(true);
  });

  it('clear_SendsBlankAnswer', () => {
    setup({ answer: answer('bir iki üç') });
    (q('ot-clear') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(JSON.parse(emitted.at(-1)!).text).toBe('');
    expect(textarea().value).toBe('');
  });

  describe('değerlendirme durumu (etkinlik)', () => {
    it('noReview_SavedText_ShowsSubmitHint', () => {
      setup({ answer: answer('bir iki üç') });
      expect(q('ot-submit-hint')?.textContent).toContain('Cevapları kontrol et');
      expect(q('ot-review')).toBeNull();
    });

    it('noReview_EmptyText_NoHint', () => {
      setup();
      expect(q('ot-submit-hint')).toBeNull();
    });

    it('pending_ShowsWaitingForTeacher', () => {
      setup({ answer: answer('bir iki üç'), review: { status: 'pending', evaluation: null, feedback: null, reviewedAt: null } });
      expect(q('ot-pending')?.textContent).toContain('Öğretmen değerlendirmesi bekliyor');
      expect(q('ot-submit-hint')).toBeNull();
    });

    it('reviewed_ShowsBadgeAndFeedback', () => {
      setup({
        answer: answer('bir iki üç'),
        review: { status: 'reviewed', evaluation: 'partial', feedback: 'Bir örnek daha ekle.', reviewedAt: '2026-10-27T10:00:00Z' },
      });
      const badge = q('ot-evaluation');
      expect(badge?.getAttribute('data-evaluation')).toBe('partial');
      expect(badge?.textContent).toContain('Kısmen');
      expect(badge?.classList).toContain('otr__badge--partial');
      expect(q('ot-feedback')?.textContent).toContain('Bir örnek daha ekle.');
      expect(q('ot-outdated')).toBeNull();
    });

    it('outdated_ShowsOldReviewWithChangedNote', () => {
      setup({
        answer: answer('yeni metin burada'),
        review: { status: 'outdated', evaluation: 'insufficient', feedback: 'Eksik.', reviewedAt: '2026-10-27T10:00:00Z' },
      });
      expect(q('ot-evaluation')?.textContent).toContain('Eksik');
      expect(q('ot-outdated')?.textContent).toContain('Cevabın değişti; yeniden değerlendirilecek.');
    });

    it('feedbackIsPlainText_NoHtmlInjection', () => {
      setup({
        answer: answer('<img src=x onerror=alert(1)>'),
        review: { status: 'reviewed', evaluation: 'sufficient', feedback: '<script>alert(1)</script><b>kalın</b>', reviewedAt: null },
      });
      const feedback = q('ot-feedback')!;
      expect(feedback.querySelector('script, b')).toBeNull();
      expect(feedback.textContent).toContain('<script>alert(1)</script><b>kalın</b>');
      expect(textarea().value).toBe('<img src=x onerror=alert(1)>');
      expect(el().querySelector('img[src="x"]')).toBeNull();
    });
  });

  it('reveal_ReadOnlyWithSampleAnswer_NoReviewArea', () => {
    setup({
      answer: answer('bir iki üç'),
      solution: { sampleAnswer: 'Örnek:\nikinci satır' },
      showSolution: true,
      review: { status: 'pending', evaluation: null, feedback: null, reviewedAt: null },
    });
    expect(textarea().readOnly).toBe(true);
    expect(q('ot-sample')?.textContent).toContain('Örnek cevap');
    expect(el().querySelector('.ot-sample__text')?.textContent).toBe('Örnek:\nikinci satır');
    expect(q('ot-pending')).toBeNull();
    expect(q('ot-clear')).toBeNull();
    type('değişiklik');
    fixture.componentInstance.flush();
    expect(emitted).toEqual([]);
  });

  it('reveal_WithoutSample_ShowsNoSampleNote', () => {
    setup({ answer: answer('bir iki üç'), showSolution: true });
    expect(q('ot-no-sample')).not.toBeNull();
  });

  it('disabled_IgnoresTyping', () => {
    setup({ disabled: true });
    type('yazı');
    fixture.componentInstance.flush();
    expect(emitted).toEqual([]);
  });
});
