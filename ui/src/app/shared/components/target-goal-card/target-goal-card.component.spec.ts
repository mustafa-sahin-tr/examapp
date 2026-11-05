import type { MockedObject } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Observable, of, throwError } from 'rxjs';

import { TargetGoal, TargetProgram } from '../../../models/target-goal.model';
import { TargetGoalService } from '../../../services/target-goal.service';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { TargetGoalCardComponent } from './target-goal-card.component';
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

  it('both languages carry the same keys', () => {
    expect(keys(goalsEn).sort()).toEqual(keys(goalsTr).sort());
  });
});
