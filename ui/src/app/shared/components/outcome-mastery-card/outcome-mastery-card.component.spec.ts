import type { MockedObject } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { Observable, of, throwError } from 'rxjs';

import { LearningOutcomeMastery, MasteryItem } from '../../../models/learning-outcome-mastery.model';
import { LearningOutcomeMasteryService } from '../../../services/learning-outcome-mastery.service';
import { LocaleService } from '../../../services/locale.service';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { OutcomeMasteryCardComponent } from './outcome-mastery-card.component';
import masteryTr from '../../../../../public/i18n/outcome-mastery/tr.json';
import masteryEn from '../../../../../public/i18n/outcome-mastery/en.json';

@Component({
  standalone: true,
  imports: [OutcomeMasteryCardComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `<app-outcome-mastery-card [childId]="childId()" [teacherStudentId]="teacherStudentId()" />`,
})
class HostComponent {
  readonly childId = signal<number | null>(null);
  readonly teacherStudentId = signal<number | null>(null);
}

function outcome(id: number, overrides: Partial<MasteryItem> = {}): MasteryItem {
  return {
    kind: 'outcome',
    learningOutcomeId: id,
    code: `M.${id}`,
    statement: `Kazanım ${id}`,
    curriculumVersion: 'Maarif',
    subjectId: 3,
    subjectName: 'Matematik',
    topicId: 30,
    topicName: 'Açılar',
    subTopicId: null,
    subTopicName: null,
    level: 'familiar',
    isTentative: false,
    answered: 10,
    correct: 5,
    accuracyPct: 50,
    ...overrides,
  };
}

function report(items: MasteryItem[], unclassifiedCount = 0): LearningOutcomeMastery {
  return {
    to: '2026-11-05T00:00:00Z',
    items,
    unclassifiedCount,
    rules: { recentAnswers: 20, proficientPct: 70, masteredPct: 90, masteredMinAnswers: 10, reliableMinAnswers: 5 },
  };
}

describe('OutcomeMasteryCardComponent (#696)', () => {
  let service: MockedObject<LearningOutcomeMasteryService>;
  let fixture: ComponentFixture<HostComponent>;

  async function render(response: Observable<LearningOutcomeMastery>, who: { childId?: number; teacherStudentId?: number } = {}): Promise<HTMLElement> {
    service = {
      getMine: vi.fn().mockReturnValue(response),
      getChild: vi.fn().mockReturnValue(response),
      getTeacherStudent: vi.fn().mockReturnValue(response),
    } as unknown as MockedObject<LearningOutcomeMasteryService>;

    TestBed.configureTestingModule({
      imports: [
        HostComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { 'outcome-mastery/tr': masteryTr, 'outcome-mastery/en': masteryEn } }),
      ],
      providers: [
        provideRouter([]),
        { provide: LearningOutcomeMasteryService, useValue: service },
        { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: 'tr-TR' }) } },
      ],
    });
    TestBed.inject(TranslocoService).setActiveLang('tr');
    fixture = TestBed.createComponent(HostComponent);
    fixture.componentInstance.childId.set(who.childId ?? null);
    fixture.componentInstance.teacherStudentId.set(who.teacherStudentId ?? null);
    await settle();
    return fixture.nativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const text = (el: Element | null) => el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
  const q = (el: HTMLElement, test: string) => el.querySelector(`[data-test="${test}"]`);
  const all = (el: HTMLElement, test: string) => [...el.querySelectorAll(`[data-test="${test}"]`)];

  it('student: reads own report and shows level, stats, and the rules from the server', async () => {
    const el = await render(of(report([outcome(1)])));

    expect(service.getMine).toHaveBeenCalled();
    expect(text(el.querySelector('#outcome-mastery-title'))).toBe('Kazanım haritam');
    expect(text(q(el, 'outcome-mastery-name'))).toBe('M.1 Kazanım 1');
    expect(text(q(el, 'outcome-mastery-level'))).toContain('Tanışık');
    expect(text(q(el, 'outcome-mastery-stats'))).toBe('%50 doğru · son 10 cevap');
    expect(text(q(el, 'outcome-mastery-rules'))).toContain('son 20 cevaba');
  });

  it('student: weak row links to practice; other levels do not', async () => {
    const el = await render(
      of(report([outcome(1), outcome(2, { level: 'mastered' }), outcome(3, { level: 'notStarted', answered: 0, correct: 0, accuracyPct: null })]))
    );

    const links = all(el, 'outcome-mastery-practice') as HTMLAnchorElement[];
    expect(links.length).toBe(1);
    expect(links[0].getAttribute('href')).toBe('/practice?subjectId=3&topicId=30');
    expect(links[0].getAttribute('aria-label')).toBe('Kazanım 1 için pratik yap');
  });

  it('student: outcome with no topic practises the subject', async () => {
    const el = await render(of(report([outcome(1, { topicId: null, topicName: null })])));

    expect((q(el, 'outcome-mastery-practice') as HTMLAnchorElement).getAttribute('href')).toBe('/practice?subjectId=3');
  });

  it('marks few answers as not certain', async () => {
    const el = await render(of(report([outcome(1, { isTentative: true, answered: 2, correct: 1 }), outcome(2)])));

    expect(all(el, 'outcome-mastery-tentative').length).toBe(1);
    expect(text(q(el, 'outcome-mastery-tentative'))).toContain('5 cevabın altında');
  });

  it('parent: reads the child and offers no practice link', async () => {
    const el = await render(of(report([outcome(1)])), { childId: 7 });

    expect(service.getChild).toHaveBeenCalledWith(7);
    expect(service.getMine).not.toHaveBeenCalled();
    expect(q(el, 'outcome-mastery-practice')).toBeNull();
  });

  it('parent: 404 hides the card', async () => {
    const el = await render(throwError(() => new HttpErrorResponse({ status: 404 })), { childId: 7 });

    expect(q(el, 'outcome-mastery')).toBeNull();
  });

  it('teacher: reads the student, offers no practice link, and explains a 404', async () => {
    const el = await render(of(report([outcome(1)])), { teacherStudentId: 9 });
    expect(service.getTeacherStudent).toHaveBeenCalledWith(9);
    expect(q(el, 'outcome-mastery-practice')).toBeNull();

    TestBed.resetTestingModule();
    const denied = await render(throwError(() => new HttpErrorResponse({ status: 404 })), { teacherStudentId: 9 });
    expect(text(q(denied, 'outcome-mastery-not-found'))).toContain('Yalnız kendi okulunuzdaki');
  });

  it('other errors offer a retry', async () => {
    const el = await render(throwError(() => new HttpErrorResponse({ status: 500 })));

    expect(q(el, 'outcome-mastery-error')).not.toBeNull();
    (q(el, 'outcome-mastery-error')!.querySelector('button') as HTMLButtonElement).click();
    expect(service.getMine).toHaveBeenCalledTimes(2);
  });

  it('groups by subject in server order', async () => {
    const el = await render(of(report([outcome(1), outcome(2, { subjectId: 4, subjectName: 'Fen' })])));

    expect(all(el, 'outcome-mastery-subject').length).toBe(2);
    expect(text(q(el, 'outcome-mastery-subject'))).toContain('Matematik');
  });

  it('opens the subject the student has worked on, with weak rows first', async () => {
    const untouched = outcome(1, { subjectId: 4, subjectName: 'Din', level: 'notStarted', answered: 0, correct: 0, accuracyPct: null });
    const el = await render(of(report([untouched, outcome(2, { level: 'mastered' }), outcome(3), outcome(4, { level: 'proficient' })])));

    expect(text(q(el, 'outcome-mastery-subject'))).toContain('Matematik');
    // Yalnız açık panelin içeriği çizilir.
    expect(all(el, 'outcome-mastery-name').map(text)).toEqual(['M.3 Kazanım 3', 'M.4 Kazanım 4', 'M.2 Kazanım 2']);
  });

  it('with both curricula, starts on Maarif and keeps rows that have no curriculum', async () => {
    const fallback = outcome(0, { kind: 'subTopic', learningOutcomeId: null, code: null, statement: null, curriculumVersion: null, subTopicId: 5, subTopicName: 'Üçgen' });
    const el = await render(of(report([outcome(1, { curriculumVersion: 'Previous' }), outcome(2), fallback])));

    expect(q(el, 'outcome-mastery-curricula')).not.toBeNull();
    expect(all(el, 'outcome-mastery-name').map(text)).toEqual(['M.2 Kazanım 2', 'Üçgen']);
  });

  it('with one curriculum, shows no switch', async () => {
    const el = await render(of(report([outcome(1)])));

    expect(q(el, 'outcome-mastery-curricula')).toBeNull();
  });

  it('shows empty and unclassified notes', async () => {
    const el = await render(of(report([], 2)));

    expect(q(el, 'outcome-mastery-empty')).not.toBeNull();
    expect(text(q(el, 'outcome-mastery-unclassified'))).toContain('2 soru');
  });

  it('English and Turkish translations have the same keys', () => {
    const keys = (o: object, prefix = ''): string[] =>
      Object.entries(o).flatMap(([k, v]) => (typeof v === 'object' ? keys(v, `${prefix}${k}.`) : [`${prefix}${k}`]));
    expect(keys(masteryEn).sort()).toEqual(keys(masteryTr).sort());
  });
});
