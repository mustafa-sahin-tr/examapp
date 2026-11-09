import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, input, output } from '@angular/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { TargetGoal, TargetMockResult, TargetProgram } from '../../models/target-goal.model';
import { TargetGoalService } from '../../services/target-goal.service';
import { TargetGoalCardComponent } from '../../shared/components/target-goal-card/target-goal-card.component';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import { MyTargetComponent } from './my-target.component';
import targetGoalsTr from '../../../../public/i18n/target-goals/tr.json';

const program = (id: number, name: string, minimumScore: number): TargetProgram => ({
  id, scoreType: 'lgs', name, institution: '', city: 'İstanbul', district: 'Kadıköy', year: 2026, minimumScore, percentile: null, sourceReference: null,
});

const lgsResult = (id: number, correct: number): TargetMockResult => ({
  id,
  name: `Deneme ${id}`,
  templateName: 'LGS',
  templateCode: 'lgs',
  wrongAnswersPerCorrect: 3,
  complete: true,
  net: correct,
  finishedAt: `2026-10-0${id}T10:00:00Z`,
  sections: [
    { groupName: null, name: 'Türkçe', questionCount: 20, correct, wrong: 0, blank: 20 - correct, net: correct, pending: false },
    { groupName: null, name: 'Matematik', questionCount: 20, correct, wrong: 0, blank: 20 - correct, net: correct, pending: false },
    { groupName: null, name: 'Fen Bilimleri', questionCount: 20, correct, wrong: 0, blank: 20 - correct, net: correct, pending: false },
    { groupName: null, name: 'T.C. İnkılap Tarihi ve Atatürkçülük', questionCount: 10, correct: 5, wrong: 0, blank: 5, net: 5, pending: false },
    { groupName: null, name: 'Din Kültürü ve Ahlak Bilgisi', questionCount: 10, correct: 5, wrong: 0, blank: 5, net: 5, pending: false },
    { groupName: null, name: 'Yabancı Dil', questionCount: 10, correct: 5, wrong: 0, blank: 5, net: 5, pending: false },
  ],
});

@Component({ selector: 'app-target-goal-card', template: '', standalone: true })
class CardStub {
  readonly childId = input<number | null>(null);
  readonly goalChange = output<TargetGoal>();
  reload = vi.fn();
}

describe('MyTargetComponent', () => {
  let fixture: ComponentFixture<MyTargetComponent>;
  let service: Record<string, ReturnType<typeof vi.fn>>;

  const setup = async (history: TargetMockResult[] = []) => {
    service = {
      history: vi.fn(() => of(history)),
      programCities: vi.fn(() => of(['İstanbul'])),
      programsForScore: vi.fn(() =>
        of({ year: 2026, safe: [program(1, 'Rahat Lise', 300)], borderline: [program(2, 'Sınır Lise', 400)], stretch: [program(3, 'Zor Lise', 490)] }),
      ),
      setMine: vi.fn(() => of({ target: program(1, 'Rahat Lise', 300), results: [] })),
    };
    TestBed.configureTestingModule({
      imports: [MyTargetComponent, translocoTestingModule({ langs: { 'target-goals/tr': targetGoalsTr } })],
      providers: [provideNoopAnimations(), { provide: TargetGoalService, useValue: service }],
    });
    TestBed.overrideComponent(MyTargetComponent, {
      remove: { imports: [TargetGoalCardComponent] },
      add: { imports: [CardStub] },
    });
    fixture = TestBed.createComponent(MyTargetComponent);
    fixture.detectChanges();
    await fixture.whenStable();
  };

  const el = () => fixture.nativeElement as HTMLElement;
  const emitGoal = async (goal: TargetGoal) => {
    (fixture.componentInstance as unknown as { onGoal(g: TargetGoal): void }).onGoal(goal);
    await new Promise((resolve) => setTimeout(resolve, 350));
    fixture.detectChanges();
    await fixture.whenStable();
  };

  it('withoutMockExams_ShowsEmptyProgressAndAsksForAScore', async () => {
    await setup();
    await emitGoal({ target: null, results: [], gradeLevel: 8 });

    expect(el().querySelector('[data-testid="progress-empty"]')).not.toBeNull();
    expect(el().querySelector('[data-testid="for-score-prompt"]')).not.toBeNull();
    expect(service['programsForScore']).not.toHaveBeenCalled();
  });

  it('withMockExams_DrawsAPointPerExamAndUsesTheLastScoreForSchools', async () => {
    await setup([lgsResult(1, 10), lgsResult(2, 15)]);
    await emitGoal({ target: null, results: [], gradeLevel: 8 });

    expect(el().querySelectorAll('[data-testid="progress-point"]').length).toBe(2);
    expect(service['programsForScore']).toHaveBeenCalledTimes(1);
    expect(service['programsForScore'].mock.calls[0][0]).toBe('lgs');
    expect(el().querySelector('[data-testid="for-score-safe"]')?.textContent).toContain('Rahat Lise');
    expect(el().querySelector('[data-testid="for-score-borderline"]')?.textContent).toContain('Sınır Lise');
    expect(el().querySelector('[data-testid="for-score-stretch"]')?.textContent).toContain('Zor Lise');
  });

  it('withTarget_ShowsTheTargetLineAndTrend', async () => {
    await setup([lgsResult(1, 10), lgsResult(2, 15)]);
    await emitGoal({ target: program(9, 'Hedef Lise', 495), results: [], gradeLevel: 8 });

    expect(el().querySelector('[data-testid="target-line"]')).not.toBeNull();
    expect(el().querySelector('[data-testid="progress-trend"]')).not.toBeNull();
  });

  it('typedScore_SearchesWithIt_AndEmptyCatalogSaysSo', async () => {
    await setup();
    await emitGoal({ target: null, results: [], gradeLevel: 8 });
    service['programsForScore'].mockReturnValue(of({ year: null, safe: [], borderline: [], stretch: [] }));

    (fixture.componentInstance as unknown as { changeScore(v: string): void }).changeScore('420');
    await new Promise((resolve) => setTimeout(resolve, 350));
    fixture.detectChanges();

    expect(service['programsForScore']).toHaveBeenCalledWith('lgs', 420, undefined);
    expect(el().querySelector('[data-testid="for-score-empty"]')).not.toBeNull();
  });

  it('choosingASchool_SavesAndReloadsTheCard', async () => {
    await setup([lgsResult(1, 10)]);
    await emitGoal({ target: null, results: [], gradeLevel: 8 });

    (el().querySelector('[data-testid="for-score-safe"] button') as HTMLButtonElement).click();

    expect(service['setMine']).toHaveBeenCalledWith(1);
  });

  it('historyFailure_ShowsAnError', async () => {
    await setup();
    service['history'].mockReturnValue(throwError(() => new Error('boom')));
    await emitGoal({ target: null, results: [], gradeLevel: 8 });

    expect(el().querySelector('[data-testid="target-progress"] [role="alert"]')).not.toBeNull();
  });
});
