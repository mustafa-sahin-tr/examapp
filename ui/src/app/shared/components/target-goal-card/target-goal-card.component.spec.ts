import type { MockedObject } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Observable, of, throwError } from 'rxjs';

import { TargetGoal, TargetProgram } from '../../../models/target-goal.model';
import { TargetGoalService } from '../../../services/target-goal.service';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { TargetGoalCardComponent, examForGrade } from './target-goal-card.component';
import goalsTr from '../../../../../public/i18n/target-goals/tr.json';
import goalsEn from '../../../../../public/i18n/target-goals/en.json';

@Component({
  standalone: true,
  imports: [TargetGoalCardComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `<app-target-goal-card [childId]="childId()" />`,
})
class HostComponent {
  readonly childId = signal<number | null>(null);
}

const program: TargetProgram = {
  id: 7, scoreType: 'lgs', name: 'Örnek Lisesi', institution: '', city: 'Ankara', district: 'Çankaya', year: 2026, minimumScore: 450, percentile: null, sourceReference: 'Resmî belge',
};

const lgsResult = {
  id: 1, name: 'Deneme', templateName: 'LGS', templateCode: 'lgs', wrongAnswersPerCorrect: 3, complete: true, net: 0,
  sections: ['Türkçe', 'T.C. İnkılap Tarihi ve Atatürkçülük', 'Din Kültürü ve Ahlak Bilgisi', 'Yabancı Dil', 'Matematik', 'Fen Bilimleri']
    .map((name) => ({ groupName: null, name, questionCount: 20, correct: 5, wrong: 0, blank: 0, net: 5, pending: false })),
};

const keys = (value: object, prefix = ''): string[] =>
  Object.entries(value).flatMap(([key, child]) => (typeof child === 'object' ? keys(child, `${prefix}${key}.`) : [`${prefix}${key}`]));

describe('TargetGoalCardComponent (#695)', () => {
  let service: MockedObject<TargetGoalService>;
  let fixture: ComponentFixture<HostComponent>;

  async function render(response: Observable<TargetGoal>, childId: number | null = null): Promise<HTMLElement> {
    service = {
      getMine: vi.fn().mockReturnValue(response),
      getChild: vi.fn().mockReturnValue(response),
      setMine: vi.fn().mockReturnValue(of({ target: program, results: [] })),
      clearMine: vi.fn().mockReturnValue(of(undefined)),
      searchPrograms: vi.fn().mockReturnValue(of({ total: 1, items: [program] })),
      programCities: vi.fn().mockReturnValue(of(['Ankara', 'İstanbul'])),
    } as unknown as MockedObject<TargetGoalService>;

    TestBed.configureTestingModule({
      imports: [HostComponent, NoopAnimationsModule, translocoTestingModule({ langs: { 'target-goals/tr': goalsTr, 'target-goals/en': goalsEn } })],
      providers: [{ provide: TargetGoalService, useValue: service }],
    });
    fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.childId.set(childId);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const button = (el: HTMLElement, text: string) => Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes(text));

  it('invites a student without a target and keeps the gap hidden', async () => {
    const el = await render(of({ target: null, results: [] }));

    expect(el.textContent).toContain(goalsTr.self.empty);
    expect(button(el, goalsTr.choose)).toBeTruthy();
    expect(el.querySelector('[data-testid="target-gap"]')).toBeNull();
  });

  it('says the gap waits for a mock exam', async () => {
    const el = await render(of({ target: program, results: [] }));

    expect(el.textContent).toContain('Örnek Lisesi');
    expect(el.textContent).toContain(goalsTr.needsExam);
    expect(el.textContent).not.toContain(goalsTr.estimateNote);
  });

  it('shows the gap with the estimate warning after a mock exam', async () => {
    const el = await render(of({ target: program, results: [lgsResult] }));

    expect(el.querySelector('.tg__summary')).toBeTruthy();
    expect(el.querySelectorAll('.tg__subject').length).toBe(6);
    expect(el.textContent).toContain(goalsTr.estimateNote);
    expect(el.textContent).not.toContain(goalsTr.obpNote);
  });

  it('lists the rank in the latest mock exams (issue #1094)', async () => {
    const el = await render(of({ target: program, results: [{ ...lgsResult, ranking: { takers: 120, rank: 12, topPercent: 10 } }] }), 42);

    expect(el.querySelector('[data-testid="target-ranks"]')?.textContent?.trim())
      .toBe(`${lgsResult.name}: 120 öğrenci arasında 12. (ilk %10)`);
    expect(el.textContent).toContain(goalsTr.rankingNote);
  });

  it('a parent reads the child target and cannot change it', async () => {
    const el = await render(of({ target: { ...program, sourceReference: null }, results: [lgsResult] }), 42);

    expect(service.getChild).toHaveBeenCalledWith(42);
    expect(service.getMine).not.toHaveBeenCalled();
    expect(el.textContent).toContain(goalsTr.child.title);
    expect(button(el, goalsTr.change)).toBeUndefined();
    expect(button(el, goalsTr.remove)).toBeUndefined();
  });

  it('a 404 hides the card', async () => {
    const el = await render(throwError(() => new HttpErrorResponse({ status: 404 })), 42);

    expect(el.querySelector('mat-card')).toBeNull();
  });

  it('another failure offers a retry', async () => {
    const el = await render(throwError(() => new HttpErrorResponse({ status: 500 })));

    expect(el.textContent).toContain(goalsTr.loadFailed);
    expect(button(el, goalsTr.retry)).toBeTruthy();
  });

  it('a student searches and chooses a target', async () => {
    vi.useFakeTimers();
    try {
      const el = await render(of({ target: null, results: [] }));
      button(el, goalsTr.choose)!.click();
      fixture.detectChanges();
      await vi.advanceTimersByTimeAsync(400);
      fixture.detectChanges();

      expect(service.searchPrograms).toHaveBeenCalledWith({ exam: 'lgs', search: '', take: 20 });
      (el.querySelector('.tg__option') as HTMLButtonElement).click();
      fixture.detectChanges();

      expect(service.setMine).toHaveBeenCalledWith(7);
      expect(el.querySelector('.tg__picker')).toBeNull();
      expect(el.textContent).toContain('Örnek Lisesi');
    } finally {
      vi.useRealTimers();
    }
  });

  it('a student removes the target', async () => {
    const el = await render(of({ target: program, results: [] }));

    button(el, goalsTr.remove)!.click();
    fixture.detectChanges();

    expect(service.clearMine).toHaveBeenCalled();
    expect(el.textContent).toContain(goalsTr.self.empty);
  });

  // ── #891 ──────────────────────────────────────────────────────────────────

  async function openPicker(goal: TargetGoal, items: TargetProgram[] = [program]): Promise<HTMLElement> {
    const el = await render(of(goal));
    service.searchPrograms.mockReturnValue(of({ total: items.length, items }));
    button(el, goalsTr.choose)!.click();
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(400);
    fixture.detectChanges();
    return el;
  }

  it('examForGrade_DerivesExamExceptAtTheTransitionGrade_issue891', () => {
    expect(examForGrade(5)).toBe('lgs');
    expect(examForGrade(7)).toBe('lgs');
    expect(examForGrade(8)).toBeNull();
    expect(examForGrade(10)).toBe('yks');
    expect(examForGrade(null)).toBeNull();
  });

  it('grade 5: exam from the grade, years left and the far note, no exam switch; popular list first_issue891', async () => {
    vi.useFakeTimers();
    try {
      const el = await openPicker({ target: null, results: [], gradeLevel: 5 });

      expect(el.querySelector('[data-testid="target-context"]')?.textContent?.replace(/\s+/g, ' ').trim()).toBe("LGS · 5. sınıf · LGS'ye 3 yıl var");
      expect(el.querySelector('[data-testid="target-far-note"]')).not.toBeNull();
      expect(el.querySelector('mat-button-toggle-group')).toBeNull();
      expect(service.programCities).toHaveBeenCalledWith('lgs');
      expect(service.searchPrograms).toHaveBeenCalledWith({ exam: 'lgs', city: undefined, search: '', take: 20 });
      expect(el.querySelector('[data-testid="target-popular"]')?.textContent?.trim()).toBe('En çok seçilenler');
      expect(el.textContent).not.toContain(goalsTr.noResults);
    } finally {
      vi.useRealTimers();
    }
  });

  it('grade 8 keeps the exam switch_issue891', async () => {
    vi.useFakeTimers();
    try {
      const el = await openPicker({ target: null, results: [], gradeLevel: 8 });
      expect(el.querySelector('mat-button-toggle-group')).not.toBeNull();
      expect(el.querySelector('[data-testid="target-context"]')?.textContent).toContain('LGS bu yıl');
    } finally {
      vi.useRealTimers();
    }
  });

  it('grade 10 searches YKS_issue891', async () => {
    vi.useFakeTimers();
    try {
      const el = await openPicker({ target: null, results: [], gradeLevel: 10 });
      expect(el.querySelector('mat-button-toggle-group')).toBeNull();
      expect(service.searchPrograms).toHaveBeenCalledWith(expect.objectContaining({ exam: 'yks' }));
      expect(el.querySelector('[data-testid="target-context"]')?.textContent).toContain("YKS'ye 2 yıl var");
    } finally {
      vi.useRealTimers();
    }
  });

  it('an empty catalogue says scores are not loaded yet instead of no results_issue891', async () => {
    vi.useFakeTimers();
    try {
      const el = await openPicker({ target: null, results: [], gradeLevel: 6 }, []);
      expect(el.querySelector('[data-testid="target-catalog-empty"]')?.textContent?.trim()).toBe(goalsTr.catalogEmpty);
      expect(el.textContent).not.toContain(goalsTr.noResults);
    } finally {
      vi.useRealTimers();
    }
  });

  it('a chosen target opens the picker in its province_issue891', async () => {
    vi.useFakeTimers();
    try {
      const el = await render(of({ target: program, results: [], gradeLevel: 7 }));
      button(el, goalsTr.change)!.click();
      fixture.detectChanges();
      await vi.advanceTimersByTimeAsync(400);
      fixture.detectChanges();

      expect(service.searchPrograms).toHaveBeenCalledWith({ exam: 'lgs', city: 'Ankara', search: '', take: 20 });
      expect(el.querySelector('[data-testid="target-popular"]')?.textContent?.trim()).toBe('Ankara için en çok seçilenler');
    } finally {
      vi.useRealTimers();
    }
  });

  it('both languages carry the same keys', () => {
    expect(keys(goalsEn).sort()).toEqual(keys(goalsTr).sort());
  });
});
