import { Component, signal, ChangeDetectionStrategy } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { MatSelectChange } from '@angular/material/select';
import { of } from 'rxjs';

import { DailyQuestionsCardComponent } from './daily-questions-card.component';
import { DailySet, DailyStartScope } from '../../../models/practice';
import { translocoTestingModule } from '../../testing/transloco-testing';
import { DailyScopePickerComponent } from '../daily-scope-picker/daily-scope-picker.component';
import { StudentProfile } from '../../../models/student-profile';
import { StudentService } from '../../../services/student.service';
import { SubjectService } from '../../../services/subject.service';

function dailySet(overrides: Partial<DailySet> = {}): DailySet {
  return {
    date: '2026-10-05',
    status: 'NotStarted',
    total: 5,
    targetCount: 5,
    answered: 0,
    correct: 0,
    wrong: 0,
    skipped: 0,
    sessionId: null,
    scope: null,
    ...overrides,
  };
}

@Component({
  standalone: true,
  imports: [DailyQuestionsCardComponent],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `<app-daily-questions-card [dailySet]="set()" [loading]="loading()" [error]="error()"
    [streak]="streak()" (start)="events.push('start'); scopes.push($event)" (retry)="events.push('retry')"
    (freePractice)="events.push('freePractice')" (goToProfile)="events.push('goToProfile')" />`,
})
class HostComponent {
  readonly set = signal<DailySet | null>(null);
  readonly loading = signal(false);
  readonly error = signal(false);
  readonly streak = signal<number | null>(null);
  readonly events: string[] = [];
  readonly scopes: Array<DailyStartScope | null> = [];
}

describe('DailyQuestionsCardComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  const q = (selector: string) => fixture.debugElement.query(By.css(selector));
  const text = (selector: string): string =>
    ((q(selector)?.nativeElement.textContent as string | undefined) ?? '').replace(/\s+/g, ' ').trim();
  const card = () => fixture.debugElement.query(By.directive(DailyQuestionsCardComponent))
    .componentInstance as DailyQuestionsCardComponent;

  function render(state: Partial<{ set: DailySet | null; loading: boolean; error: boolean; streak: number | null }>) {
    if (state.set !== undefined) host.set.set(state.set);
    if (state.loading !== undefined) host.loading.set(state.loading);
    if (state.error !== undefined) host.error.set(state.error);
    if (state.streak !== undefined) host.streak.set(state.streak);
    fixture.detectChanges();
  }

  beforeEach(() => {
    // Kapsam seçici (#66) açıldığında sınıf dersleri ve konular yüklenir.
    const studentService = jasmine.createSpyObj<StudentService>('StudentService', ['getProfile']);
    studentService.getProfile.and.returnValue(of({ gradeId: 7 } as Partial<StudentProfile> as StudentProfile));
    const subjectService = jasmine.createSpyObj<SubjectService>('SubjectService', [
      'getSubjectsByGrade',
      'getTopicsBySubjectAndGrade',
      'getSubTopicsByTopic',
    ]);
    subjectService.getSubjectsByGrade.and.returnValue(of([{ id: 3, name: 'Matematik' }]));
    subjectService.getTopicsBySubjectAndGrade.and.returnValue(of([{ id: 30, name: 'Kesirler', subjectId: 3, gradeId: 7 }]));
    subjectService.getSubTopicsByTopic.and.returnValue(of([]));

    TestBed.configureTestingModule({
      imports: [HostComponent, translocoTestingModule()],
      providers: [
        provideNoopAnimations(),
        { provide: StudentService, useValue: studentService },
        { provide: SubjectService, useValue: subjectService },
      ],
    });
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
  });

  it('region_Always_HasRegionRoleAndLabel', () => {
    render({ set: dailySet() });

    const region = q('section.daily-card');
    expect(region.attributes['role']).toBe('region');
    expect(region.attributes['aria-label']).toBe('Günün soruları');
  });

  it('loading_True_ShowsSkeletonWithStatusAndBusyAndNoCta', () => {
    render({ loading: true });

    expect(card().state()).toBe('loading');
    expect(q('section.daily-card').attributes['aria-busy']).toBe('true');
    expect(q('.daily-card__loading').attributes['role']).toBe('status');
    expect(text('.daily-card__loading')).toContain('Günün soruları hazırlanıyor...');
    expect(q('.daily-card__cta')).toBeNull();
  });

  it('error_True_ShowsAlertAndRetryEmits', () => {
    render({ error: true });

    expect(card().state()).toBe('error');
    expect(q('.daily-card__message--error').attributes['role']).toBe('alert');
    expect(text('.daily-card__message--error p')).toBe('Günün soruları getirilemedi, lütfen tekrar dene.');

    q('[data-cta="retry"]').nativeElement.click();
    expect(host.events).toEqual(['retry']);
  });

  it('notStarted_FullSet_ShowsStartTitleProgressAriaAndStartCta', () => {
    render({ set: dailySet() });

    expect(card().state()).toBe('notStarted');
    expect(text('.daily-card__title')).toBe('Bugün 5 soru seni bekliyor');
    expect(text('.daily-card__chips')).toContain('Sınıfından rastgele');
    expect(text('.daily-card__chips')).toContain('~8 dk');

    const bar = q('[role="progressbar"]');
    expect(bar.attributes['aria-valuemin']).toBe('0');
    expect(bar.attributes['aria-valuemax']).toBe('5');
    expect(bar.attributes['aria-valuenow']).toBe('0');
    expect(bar.attributes['aria-valuetext']).toBe('5 sorudan 0 tanesi çözüldü');
    expect(q('.daily-card__count').attributes['aria-hidden']).toBe('true');
    expect(q('.daily-card__note')).toBeNull();

    const cta = q('[data-cta="start"]');
    expect(text('[data-cta="start"]')).toBe('play_arrow Başla');
    expect(cta.attributes['mat-flat-button']).toBeDefined();
    cta.nativeElement.click();
    expect(host.events).toEqual(['start']);
  });

  it('inProgress_ThreeOfFive_ShowsContinueAndCurrentSegment', () => {
    render({ set: dailySet({ status: 'InProgress', answered: 3, correct: 2, wrong: 1, sessionId: 4 }) });

    expect(card().state()).toBe('inProgress');
    expect(text('.daily-card__title')).toBe('Bugünkü sete devam et');
    expect(text('.daily-card__sub')).toBe('2 soru kaldı, kaldığın yerden devam edersin.');
    expect(text('.daily-card__count')).toBe('3 / 5');

    const bar = q('[role="progressbar"]');
    expect(bar.attributes['aria-valuenow']).toBe('3');
    expect(bar.attributes['aria-valuetext']).toBe('5 sorudan 3 tanesi çözüldü');
    const segments = fixture.debugElement.queryAll(By.css('.daily-bar__segment'));
    expect(segments.filter((s) => s.classes['daily-bar__segment--done']).length).toBe(3);
    expect(segments[3].classes['daily-bar__segment--current']).toBeTrue();

    expect(text('[data-cta="start"]')).toBe('play_arrow Devam et');
    q('[data-cta="start"]').nativeElement.click();
    expect(host.events).toEqual(['start']);
  });

  it('completed_FullSet_ShowsDoneSummaryAndOutlinedFreePracticeCta', () => {
    render({ set: dailySet({ status: 'Completed', answered: 5, correct: 4, wrong: 1, sessionId: 4 }) });

    expect(card().state()).toBe('completed');
    expect(text('.daily-card__title')).toBe('Bugünkü set tamam');
    expect(text('.daily-card__sub')).toBe('4 doğru, 1 yanlış. Yarın yeni set gelir.');
    expect(text('.daily-card__chips')).toContain('Tamamlandı');
    expect(q('[data-cta="start"]')).toBeNull();

    const cta = q('[data-cta="freePractice"]');
    expect(cta.attributes['mat-stroked-button']).toBeDefined();
    cta.nativeElement.click();
    expect(host.events).toEqual(['freePractice']);
  });

  it('completed_WithSkipped_CountsSkippedInSummary', () => {
    render({ set: dailySet({ status: 'Completed', answered: 5, correct: 3, wrong: 1, skipped: 1, sessionId: 4 }) });

    expect(text('.daily-card__sub')).toBe('3 doğru, 1 yanlış, 1 pas. Yarın yeni set gelir.');
  });

  it('lowPool_ThreeOfFive_ShowsCountTitleAndInfoNote', () => {
    render({ set: dailySet({ total: 3, targetCount: 5 }) });

    expect(card().state()).toBe('lowPool');
    expect(text('.daily-card__title')).toBe('Bugün 3 soru seni bekliyor');
    expect(text('.daily-card__note')).toContain('günlük set 5 soruya çıkar');
    expect(q('[role="progressbar"]').attributes['aria-valuemax']).toBe('3');
    expect(fixture.debugElement.queryAll(By.css('.daily-bar__segment')).length).toBe(3);
    expect(text('[data-cta="start"]')).toBe('play_arrow Başla');
  });

  it('empty_PoolEmpty_ShowsProfileCtaOnlyWithoutFreePractice', () => {
    render({ set: dailySet({ status: 'Empty', total: 0 }) });

    expect(card().state()).toBe('empty');
    expect(text('.daily-card__title')).toBe('Bugün için soru bulunamadı');
    expect(text('.daily-card__note')).toContain('Profilinden kontrol edebilirsin');
    expect(q('[role="progressbar"]')).toBeNull();
    expect(q('[data-cta="freePractice"]')).toBeNull();
    expect(q('[data-cta="start"]')).toBeNull();

    q('[data-cta="profile"]').nativeElement.click();
    expect(host.events).toEqual(['goToProfile']);
  });

  it('streak_Positive_ShowsChip', () => {
    render({ set: dailySet(), streak: 4 });

    expect(q('.daily-card__chip--streak')).toBeTruthy();
    expect(text('.daily-card__chip--streak')).toContain('Seri: 4 gün');
  });

  describe('scope (issue #66)', () => {
    const picker = () =>
      fixture.debugElement.query(By.directive(DailyScopePickerComponent))?.componentInstance as
        | DailyScopePickerComponent
        | undefined;
    const select = (value: number | null) => ({ value }) as MatSelectChange;

    it('notStarted_WithoutSession_ShowsCollapsedToggle_StartEmitsNull', () => {
      render({ set: dailySet() });

      const toggle = q('[data-cta="scopeToggle"]');
      expect(toggle).toBeTruthy();
      expect(toggle.attributes['aria-expanded']).toBe('false');
      expect(text('[data-cta="scopeToggle"]')).toContain('Ders/konu seç (isteğe bağlı)');
      expect(picker()).toBeUndefined();

      q('[data-cta="start"]').nativeElement.click();
      expect(host.scopes).toEqual([null]);
    });

    it('toggleOpen_SelectSubjectAndTopic_StartEmitsScope', () => {
      render({ set: dailySet() });
      q('[data-cta="scopeToggle"]').nativeElement.click();
      fixture.detectChanges();

      expect(q('[data-cta="scopeToggle"]').attributes['aria-expanded']).toBe('true');
      picker()!.onSubjectChange(select(3));
      picker()!.onTopicChange(select(30));
      fixture.detectChanges();

      q('[data-cta="start"]').nativeElement.click();
      expect(host.scopes).toEqual([{ subjectId: 3, topicId: 30, subTopicId: null }]);
    });

    it('toggleClosedAfterSelection_StartEmitsNull', () => {
      render({ set: dailySet() });
      q('[data-cta="scopeToggle"]').nativeElement.click();
      fixture.detectChanges();
      picker()!.onSubjectChange(select(3));

      q('[data-cta="scopeToggle"]').nativeElement.click();
      fixture.detectChanges();
      expect(picker()).toBeUndefined();

      q('[data-cta="start"]').nativeElement.click();
      expect(host.scopes).toEqual([null]);
    });

    it('startedOrInProgressSet_HidesToggle', () => {
      render({ set: dailySet({ sessionId: 9 }) });
      expect(q('[data-cta="scopeToggle"]')).toBeNull();

      render({ set: dailySet({ status: 'InProgress', answered: 1, sessionId: 9 }) });
      expect(q('[data-cta="scopeToggle"]')).toBeNull();
      q('[data-cta="start"]').nativeElement.click();
      expect(host.scopes).toEqual([null]);
    });

    it('savedScope_ShowsScopeChipInsteadOfRandom_AndFilledNote', () => {
      render({
        set: dailySet({
          status: 'InProgress',
          answered: 1,
          sessionId: 9,
          scope: {
            subjectId: 3,
            subjectName: 'Matematik',
            topicId: 30,
            topicName: 'Kesirler',
            subTopicId: null,
            subTopicName: null,
            scopedCount: 2,
            filledFromGeneral: true,
          },
        }),
      });

      expect(text('[data-chip="scope"]')).toContain('Matematik · Kesirler');
      expect(text('.daily-card__chips')).not.toContain('Sınıfından rastgele');
      expect(text('[data-note="filled"]')).toContain('Bu konuda yeterli soru yok, kalanı genel havuzdan.');
    });

    it('savedScopeWithoutFill_HidesFilledNote', () => {
      render({
        set: dailySet({
          sessionId: 9,
          scope: {
            subjectId: 3,
            subjectName: 'Matematik',
            topicId: null,
            topicName: null,
            subTopicId: null,
            subTopicName: null,
            scopedCount: 5,
            filledFromGeneral: false,
          },
        }),
      });

      expect(text('[data-chip="scope"]')).toContain('Matematik');
      expect(q('[data-note="filled"]')).toBeNull();
    });
  });

  it('streak_NullOrZero_HidesChip', () => {
    render({ set: dailySet(), streak: null });
    expect(q('.daily-card__chip--streak')).toBeNull();

    render({ streak: 0 });
    expect(q('.daily-card__chip--streak')).toBeNull();
  });
});
