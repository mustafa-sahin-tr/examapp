import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

import { PlacementComponent } from './placement.component';
import { PlacementQuestion, PlacementReport, PlacementStatus } from '../../models/placement.model';
import { PlacementService } from '../../services/placement.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import placementTr from '../../../../public/i18n/placement/tr.json';

/** Issue #1114: seviye tespiti — giriş, sorular, tek gönderim, rapor ve öneri. */
describe('PlacementComponent (issue #1114)', () => {
  const questions: PlacementQuestion[] = [
    {
      id: 1,
      subjectName: 'Matematik',
      text: '2+2?',
      imageUrl: null,
      options: [
        { id: 11, text: '4', imageUrl: null },
        { id: 12, text: '5', imageUrl: null },
      ],
    },
    {
      id: 2,
      subjectName: 'Fen Bilimleri',
      text: 'Su?',
      imageUrl: null,
      options: [
        { id: 21, text: 'H2O', imageUrl: null },
        { id: 22, text: 'CO2', imageUrl: null },
      ],
    },
  ];
  const report: PlacementReport = {
    gradeName: '7. Sınıf',
    submittedAtUtc: '2026-11-11T10:00:00Z',
    correct: 1,
    total: 2,
    percent: 50,
    subjects: [
      { subjectId: 1, subjectName: 'Matematik', correct: 1, total: 1, percent: 100 },
      { subjectId: 2, subjectName: 'Fen Bilimleri', correct: 0, total: 1, percent: 0 },
    ],
    strongTopics: [{ topicId: 5, topicName: 'Kesirler', subjectName: 'Matematik', correct: 1, total: 1 }],
    weakTopics: [{ topicId: 6, topicName: 'Madde', subjectName: 'Fen Bilimleri', correct: 0, total: 1 }],
    suggestion: {
      tier: 'essential',
      planKey: 'student-essential',
      subjectId: 2,
      subjectName: 'Fen Bilimleri',
      band: 'Middle',
    },
  };

  function create(
    status: PlacementStatus,
    service: Partial<Record<keyof PlacementService, unknown>> = {},
    confirm = true
  ) {
    const api = {
      status: vi.fn().mockReturnValue(of(status)),
      questions: vi.fn().mockReturnValue(of(questions)),
      submit: vi.fn().mockReturnValue(of(report)),
      report: vi.fn().mockReturnValue(of(report)),
      ...service,
    };
    const dialog = { open: vi.fn().mockReturnValue({ afterClosed: () => of(confirm) }) };
    TestBed.configureTestingModule({
      imports: [PlacementComponent, translocoTestingModule({ langs: { 'placement/tr': placementTr } })],
      providers: [provideRouter([]), { provide: PlacementService, useValue: api }],
    });
    TestBed.overrideProvider(MatDialog, { useValue: dialog });
    const fixture = TestBed.createComponent(PlacementComponent);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, api, dialog };
  }

  const available: PlacementStatus = { available: true, taken: false, gradeName: '7. Sınıf', questionCount: 2 };
  const q = (el: HTMLElement, id: string) => el.querySelector<HTMLElement>(`[data-testid="${id}"]`);

  it('says so when no test is published for the grade', () => {
    const { el } = create({ available: false, taken: false, gradeName: '7. Sınıf', questionCount: 0 });
    expect(q(el, 'placement-unavailable')!.textContent).toContain('henüz hazır değil');
  });

  it('runs the test once and shows the report with the suggested plan', () => {
    const { fixture, el, api, dialog } = create(available);
    q(el, 'placement-start')!.click();
    fixture.detectChanges();
    expect(el.querySelectorAll('[data-testid="placement-question"]').length).toBe(2);

    fixture.componentInstance.choose(1, 11);
    fixture.detectChanges();
    expect(el.textContent).toContain('1 / 2 cevaplandı');
    q(el, 'placement-submit')!.click();
    fixture.detectChanges();

    expect(dialog.open).toHaveBeenCalledTimes(1);
    expect(api.submit).toHaveBeenCalledWith([
      { questionId: 1, answerId: 11 },
      { questionId: 2, answerId: null },
    ]);
    expect(q(el, 'placement-score')!.textContent).toContain('2 sorudan 1 doğru (%50)');
    expect(q(el, 'placement-weak')!.textContent).toContain('Madde');
    expect(q(el, 'placement-suggestion')!.textContent).toContain('Essential: yalnız Fen Bilimleri');
    expect(q(el, 'placement-see-plans')!.getAttribute('href')).toBe('/plans');
  });

  it('does not repeat the letter on image options (kitap kırpımında harf görselde)', () => {
    const imageQuestion: PlacementQuestion = {
      ...questions[0],
      options: [
        { id: 11, text: 'A', imageUrl: 'https://x/a.png' },
        { id: 12, text: '5', imageUrl: null },
      ],
    };
    const { fixture, el } = create(available, { questions: vi.fn().mockReturnValue(of([imageQuestion])) });
    q(el, 'placement-start')!.click();
    fixture.detectChanges();

    const [imageOption, textOption] = Array.from(el.querySelectorAll<HTMLElement>('[data-testid="placement-option"]'));
    expect(imageOption.querySelector('.placement__tag')).toBeNull();
    expect(imageOption.querySelector('img')!.getAttribute('alt')).toContain('A');
    expect(imageOption.textContent!.trim()).toBe('');
    expect(textOption.querySelector('.placement__tag')!.textContent).toBe('B)');
    expect(textOption.textContent).toContain('5');
  });

  it('keeps the answers when the blank-question warning is dismissed', () => {
    const { fixture, el, api } = create(available, {}, false);
    q(el, 'placement-start')!.click();
    fixture.detectChanges();
    q(el, 'placement-submit')!.click();
    fixture.detectChanges();
    expect(api.submit).not.toHaveBeenCalled();
    expect(el.querySelectorAll('[data-testid="placement-question"]').length).toBe(2);
  });

  it('goes straight to the report when the test was already taken', () => {
    const { el, api } = create({ ...available, taken: true });
    expect(api.questions).not.toHaveBeenCalled();
    expect(q(el, 'placement-report')).not.toBeNull();
  });

  it('shows the stored report when another tab already submitted (409)', () => {
    const { fixture, el, api } = create(available, {
      submit: vi.fn().mockReturnValue(throwError(() => ({ status: 409 }))),
    });
    q(el, 'placement-start')!.click();
    fixture.detectChanges();
    fixture.componentInstance.choose(1, 11);
    fixture.componentInstance.choose(2, 21);
    q(el, 'placement-submit')!.click();
    fixture.detectChanges();
    expect(api.report).toHaveBeenCalled();
    expect(q(el, 'placement-report')).not.toBeNull();
  });
});
