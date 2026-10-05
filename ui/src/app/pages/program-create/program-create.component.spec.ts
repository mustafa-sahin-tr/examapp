import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideNativeDateAdapter } from '@angular/material/core';
import { Subject, of, throwError } from 'rxjs';

import { ProgramCreateComponent } from './program-create.component';
import { ProgramService } from '../../services/program.service';
import { ProgramStep } from '../../models/programstep';
import { CreateProgramRequest, UserProgram } from '../../models/program.interfaces';

import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import programCreateTr from '../../../../public/i18n/program-create/tr.json';

/** Gerçek scope sözlüğü (issue #183): anahtar bozulursa test kırılır. */
const translocoTesting = translocoTestingModule({ langs: { 'program-create/tr': programCreateTr } });

/** Seed ile aynı şekil (ProgramStepSeed.cs): 1→2 süreli, 1→3 soru sayısı, 5'te birleşir; 6 ve 7 çoklu. */
function seedSteps(): ProgramStep[] {
  return [
    {
      id: 1,
      title: 'Süreli mi soru sayısı takipli mi?',
      description: 'Süreli mi soru sayısı takipli mi?',
      multiple: false,
      actions: [],
      options: [
        { label: 'Süreli Çalışma', value: 'time', icon: 'timer', nextStep: 2 },
        { label: 'Soru Sayısı Takipli Çalışma', value: 'question', icon: 'quiz', nextStep: 3 },
      ],
    },
    {
      id: 2,
      title: 'Çalışma süresi',
      description: 'Çalışma süresi',
      multiple: false,
      actions: [],
      options: [
        { label: '25 dakika çalışma 5 dakika ara', value: '25-5', icon: 'hourglass_empty', nextStep: 5 },
        { label: '30 dakika çalışma 10 dakika ara', value: '30-10', icon: 'hourglass_top', nextStep: 5 },
      ],
    },
    {
      id: 3,
      title: 'Günde kaç soru?',
      description: 'Günde kaç soru?',
      multiple: false,
      actions: [],
      options: [
        { label: '8', value: '8', icon: 'note', nextStep: 5 },
        { label: '12', value: '12', icon: 'library_books', nextStep: 5 },
      ],
    },
    {
      id: 5,
      title: 'Kaç farklı ders?',
      description: 'Kaç farklı ders?',
      multiple: false,
      actions: [],
      options: [
        { label: '1', value: '1', icon: 'filter_1', nextStep: 6 },
        { label: '2', value: '2', icon: 'filter_2', nextStep: 6 },
      ],
    },
    {
      id: 6,
      title: 'Çalışamayacağın gün?',
      description: 'Çalışamayacağın gün?',
      multiple: true,
      actions: [],
      options: [
        { label: 'Pazartesi', value: '1', icon: 'looks_one', nextStep: 7 },
        { label: 'Salı', value: '2', icon: 'looks_two', nextStep: 7 },
        { label: 'Yok', value: '8', icon: 'event_available', nextStep: 7 },
      ],
    },
    {
      id: 7,
      title: 'Zorlandığın dersler?',
      description: 'Zorlandığın dersler?',
      multiple: true,
      actions: [],
      options: [
        { label: 'Türkçe', value: '2', icon: 'spellcheck', nextStep: undefined },
        { label: 'Matematik', value: '3', icon: 'calculate', nextStep: undefined },
        { label: 'Yok', value: '5', icon: 'sentiment_satisfied', nextStep: undefined },
      ],
    },
  ];
}

describe('ProgramCreateComponent (issue #135)', () => {
  let fixture: ComponentFixture<ProgramCreateComponent>;
  let component: ProgramCreateComponent;
  let programService: jasmine.SpyObj<ProgramService>;
  let host: HTMLElement;

  async function setup(steps$ = of(seedSteps())): Promise<void> {
    programService = jasmine.createSpyObj<ProgramService>('ProgramService', ['getProgramSteps', 'createProgram']);
    programService.getProgramSteps.and.returnValue(steps$);
    programService.createProgram.and.returnValue(of({} as UserProgram));

    await TestBed.configureTestingModule({
      imports: [ProgramCreateComponent, translocoTesting],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        provideNativeDateAdapter(),
        { provide: ProgramService, useValue: programService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ProgramCreateComponent);
    component = fixture.componentInstance;
    host = fixture.nativeElement;
    fixture.detectChanges();
  }

  function cards(): HTMLButtonElement[] {
    return Array.from(host.querySelectorAll<HTMLButtonElement>('button[app-option-card]'));
  }

  function choose(label: string): void {
    const card = cards().find((c) => c.querySelector('.option-card__label')?.textContent?.trim() === label);
    if (!card) throw new Error(`Seçenek bulunamadı: ${label}`);
    card.click();
    fixture.detectChanges();
  }

  function nextButton(): HTMLButtonElement {
    return host.querySelector<HTMLButtonElement>('.pc-bar__next')!;
  }

  function continueWizard(): void {
    nextButton().click();
    fixture.detectChanges();
  }

  function answer(...labels: string[]): void {
    labels.forEach(choose);
    continueWizard();
  }

  function currentStepId(): number | undefined {
    return component.currentStep()?.id;
  }

  function fillFormAndSubmit(): CreateProgramRequest {
    component.programName.set('Planım');
    fixture.detectChanges();
    host.querySelector<HTMLButtonElement>('.pc-bar__submit')!.click();
    fixture.detectChanges();
    return programService.createProgram.calls.mostRecent().args[0];
  }

  afterEach(() => TestBed.resetTestingModule());

  describe('yükleme durumları', () => {
    it('loading_StepsPending_ShowsStatusSpinner', async () => {
      await setup(new Subject<ProgramStep[]>());
      expect(component.viewState()).toBe('loading');
      const status = host.querySelector('.pc-state[role="status"]');
      expect(status?.textContent).toContain(programCreateTr.wizard.loading);
    });

    it('error_LoadFails_ShowsAlertAndRetryReloads', async () => {
      spyOn(console, 'error');
      await setup(throwError(() => new Error('boom')));
      expect(component.viewState()).toBe('error');
      const alert = host.querySelector('.pc-state[role="alert"]');
      expect(alert?.textContent).toContain(programCreateTr.wizard.loadError);

      programService.getProgramSteps.and.returnValue(of(seedSteps()));
      alert!.querySelector('button')!.click();
      fixture.detectChanges();
      expect(component.viewState()).toBe('step');
      expect(currentStepId()).toBe(1);
    });

    it('empty_NoSteps_ShowsEmptyState', async () => {
      await setup(of([]));
      expect(component.viewState()).toBe('empty');
      expect(host.textContent).toContain(programCreateTr.wizard.emptyTitle);
    });
  });

  describe('ilerleme', () => {
    beforeEach(() => setup());

    it('total_FirstStepBeforeAnyChoice_IsSixStopsFromGraph', () => {
      expect(component.remainingPathLength()).toBe(4);
      expect(component.total()).toBe(6);
      expect(component.branchUnknown()).toBeTrue();
      expect(host.querySelector('.wp__counter')?.textContent?.trim()).toBe('Soru 1 / 6');
      expect(host.querySelector('[aria-current="step"]')).not.toBeNull();
    });

    it('continue_NoSelection_IsDisabledWithReason', () => {
      expect(nextButton().disabled).toBeTrue();
      expect(nextButton().getAttribute('aria-describedby')).toBe('pc-need');
      expect(host.querySelector('#pc-need')?.textContent).toContain(programCreateTr.wizard.needSelection);

      choose('Süreli Çalışma');
      expect(nextButton().disabled).toBeFalse();
    });
  });

  describe('dallar uçtan uca', () => {
    beforeEach(() => setup());

    it('timeBranch_CompletesAndSubmitsSteps_1_2_5_6_7', () => {
      answer('Süreli Çalışma');
      expect(currentStepId()).toBe(2);
      expect(component.total()).toBe(6);
      answer('30 dakika çalışma 10 dakika ara');
      answer('2');
      answer('Pazartesi', 'Salı');
      answer('Matematik');

      expect(component.viewState()).toBe('form');
      expect(component.total()).toBe(6);
      const request = fillFormAndSubmit();
      expect(request.userSelections).toEqual([
        { stepId: 1, selectedValues: ['time'] },
        { stepId: 2, selectedValues: ['30-10'] },
        { stepId: 5, selectedValues: ['2'] },
        { stepId: 6, selectedValues: ['1', '2'] },
        { stepId: 7, selectedValues: ['3'] },
      ]);
      expect(request.programName).toBe('Planım');
    });

    it('questionBranch_CompletesAndSubmitsSteps_1_3_5_6_7', () => {
      answer('Soru Sayısı Takipli Çalışma');
      expect(currentStepId()).toBe(3);
      answer('12');
      answer('1');
      answer('Yok');
      answer('Türkçe');

      const request = fillFormAndSubmit();
      expect(request.userSelections.map((s) => s.stepId)).toEqual([1, 3, 5, 6, 7]);
      expect(request.userSelections[1]).toEqual({ stepId: 3, selectedValues: ['12'] });
    });

    it('submit_Success_NavigatesToPrograms', () => {
      const router = TestBed.inject(Router);
      const navigate = spyOn(router, 'navigate').and.resolveTo(true);
      answer('Süreli Çalışma');
      answer('25 dakika çalışma 5 dakika ara');
      answer('1');
      answer('Yok');
      answer('Yok');
      fillFormAndSubmit();
      expect(navigate).toHaveBeenCalledWith(['/programs']);
    });
  });

  describe('çoklu seçim', () => {
    beforeEach(() => setup());

    function goToDays(): void {
      answer('Süreli Çalışma');
      answer('25 dakika çalışma 5 dakika ara');
      answer('1');
    }

    it('multi_TogglesOptionsAndUsesCheckboxRole', () => {
      goToDays();
      expect(host.querySelector('app-option-group')?.getAttribute('role')).toBe('group');
      expect(cards()[0].getAttribute('role')).toBe('checkbox');

      choose('Pazartesi');
      choose('Salı');
      expect(component.currentValues()).toEqual(['1', '2']);
      choose('Pazartesi');
      expect(component.currentValues()).toEqual(['2']);
      expect(cards()[1].getAttribute('aria-checked')).toBe('true');
    });

    it('multi_NoneIsExclusiveWithOtherOptions', () => {
      goToDays();
      choose('Pazartesi');
      choose('Salı');
      choose('Yok');
      expect(component.currentValues()).toEqual(['8']);

      choose('Salı');
      expect(component.currentValues()).toEqual(['2']);
    });
  });

  describe('dal değişimi', () => {
    beforeEach(() => setup());

    it('branchChange_DropsStaleAnswersShowsBandAndUndoRestores', () => {
      answer('Süreli Çalışma');
      answer('25 dakika çalışma 5 dakika ara');
      answer('1');
      expect(currentStepId()).toBe(6);

      // Özet rayından ilk soruya dön.
      const firstRow = host.querySelector<HTMLButtonElement>('.pc-rail button.ss-row')!;
      expect(firstRow.getAttribute('aria-label')).toBe('Süreli mi soru sayısı takipli mi?: Süreli Çalışma. Düzenle');
      firstRow.click();
      fixture.detectChanges();
      expect(currentStepId()).toBe(1);
      expect(cards()[0].getAttribute('aria-checked')).toBe('true');

      choose('Soru Sayısı Takipli Çalışma');
      continueWizard();

      expect(currentStepId()).toBe(3);
      expect(component.answers().has(2)).toBeFalse();
      // Adım 5 yeni yolda da var: cevabı korunur.
      expect(component.answers().get(5)).toEqual(['1']);
      const live = host.querySelector('.pc-live')!;
      expect(live.getAttribute('role')).toBe('status');
      expect(live.getAttribute('aria-live')).toBe('polite');
      const band = live.querySelector('.pc-band');
      expect(band?.textContent).toContain('1 seçim sıfırlandı');

      band!.querySelector<HTMLButtonElement>('.pc-band__undo')!.click();
      fixture.detectChanges();
      expect(currentStepId()).toBe(1);
      expect(component.answers().get(1)).toEqual(['time']);
      expect(component.answers().get(2)).toEqual(['25-5']);
      expect(host.querySelector('.pc-band')).toBeNull();
      // Live region kapsayıcısı kalıcıdır; yalnız içeriği değişir.
      expect(host.querySelector('.pc-live')).toBe(live);
    });

    it('sameNextStep_ChangedAnswerKeepsLaterAnswers', () => {
      answer('Süreli Çalışma');
      answer('25 dakika çalışma 5 dakika ara');
      answer('2');
      answer('Salı');
      expect(currentStepId()).toBe(7);

      component.goToStep(2);
      fixture.detectChanges();
      choose('30 dakika çalışma 10 dakika ara');
      continueWizard();

      expect(currentStepId()).toBe(5);
      expect(component.pathChange()).toBeNull();
      expect(component.answers().get(5)).toEqual(['2']);
      expect(component.answers().get(6)).toEqual(['2']);
      // Önceki seçim işaretli gelir.
      expect(cards()[1].getAttribute('aria-checked')).toBe('true');
    });

    it('submitAfterBranchChange_PayloadContainsOnlyCurrentPath', () => {
      answer('Süreli Çalışma');
      answer('25 dakika çalışma 5 dakika ara');
      component.goToStep(1);
      fixture.detectChanges();
      answer('Soru Sayısı Takipli Çalışma');
      answer('8');
      answer('1');
      answer('Yok');
      answer('Yok');

      const request = fillFormAndSubmit();
      expect(request.userSelections.map((s) => s.stepId)).toEqual([1, 3, 5, 6, 7]);
      expect(request.userSelections.some((s) => s.stepId === 2)).toBeFalse();
    });

    it('buildSelections_IgnoresAnswersOutsideNavigatedPath', () => {
      answer('Süreli Çalışma');
      // Yol dışındaki kalıntı bir cevap bile API'ye gitmez.
      component.answers.update((a) => new Map(a).set(3, ['8']));
      expect(component.buildSelections()).toEqual([{ stepId: 1, selectedValues: ['time'] }]);
    });
  });

  describe('geri gezinme', () => {
    beforeEach(() => setup());

    it('previous_WalksBackThroughHistoryOneStepAtATime', () => {
      answer('Soru Sayısı Takipli Çalışma');
      answer('8');
      answer('1');
      expect(component.path()).toEqual([1, 3, 5, 6]);

      const back = (): void => {
        host.querySelector<HTMLButtonElement>('.pc-bar__back')!.click();
        fixture.detectChanges();
      };
      back();
      expect(currentStepId()).toBe(5);
      back();
      expect(currentStepId()).toBe(3);
      back();
      expect(currentStepId()).toBe(1);
      expect(host.querySelector<HTMLButtonElement>('.pc-bar__back')!.disabled).toBeTrue();
      // Cevaplar korunur: ileri gidince önceki seçimler işaretli.
      expect(component.answers().get(3)).toEqual(['8']);
    });

    it('formBack_ReturnsToLastQuestion', () => {
      answer('Süreli Çalışma');
      answer('25 dakika çalışma 5 dakika ara');
      answer('1');
      answer('Yok');
      answer('Türkçe');
      expect(component.viewState()).toBe('form');

      host.querySelector<HTMLButtonElement>('.pc-bar__back')!.click();
      fixture.detectChanges();
      expect(currentStepId()).toBe(7);
      expect(component.currentValues()).toEqual(['2']);
    });

    it('progressDoneStop_JumpsToThatStep', () => {
      answer('Süreli Çalışma');
      answer('25 dakika çalışma 5 dakika ara');
      const doneStops = host.querySelectorAll<HTMLButtonElement>('button.wp__stop');
      expect(doneStops.length).toBe(2);
      doneStops[1].click();
      fixture.detectChanges();
      expect(currentStepId()).toBe(2);
      expect(component.path()).toEqual([1, 2]);
    });
  });

  describe('geçersiz nextStep savunması (issue #136)', () => {
    it('next_DanglingNextStep_FallsBackToFormWithWarning', async () => {
      const steps: ProgramStep[] = [
        {
          id: 1,
          title: 'Adım 1',
          description: 'Adım 1',
          multiple: false,
          actions: [],
          options: [{ label: 'Seçenek 1', value: 'opt1', nextStep: 999 }],
        },
      ];
      await setup(of(steps));
      const warn = spyOn(console, 'warn');

      // Döngü korumalı kalan yol: geçersiz hedef formda biter.
      expect(component.total()).toBe(2);
      answer('Seçenek 1');

      expect(component.viewState()).toBe('form');
      expect(component.path()).toEqual([1]);
      expect(warn).toHaveBeenCalled();
      expect(warn.calls.mostRecent().args[0]).toContain('nextStep=999');

      const request = fillFormAndSubmit();
      expect(request.userSelections).toEqual([{ stepId: 1, selectedValues: ['opt1'] }]);
    });

    it('next_TargetAlreadyOnPath_FallsBackToFormWithWarning', async () => {
      const steps: ProgramStep[] = [
        { id: 1, title: 'A', description: 'A', multiple: false, actions: [], options: [{ label: 'a', value: 'a', nextStep: 2 }] },
        { id: 2, title: 'B', description: 'B', multiple: false, actions: [], options: [{ label: 'b', value: 'b', nextStep: 1 }] },
      ];
      await setup(of(steps));
      const warn = spyOn(console, 'warn');

      answer('a');
      expect(component.path()).toEqual([1, 2]);
      answer('b');

      expect(component.viewState()).toBe('form');
      expect(component.path()).toEqual([1, 2]);
      expect(warn.calls.mostRecent().args[0]).toContain('döngü oluşturan nextStep=1');
      const request = fillFormAndSubmit();
      expect(request.userSelections.map((s) => s.stepId)).toEqual([1, 2]);
    });

    it('remaining_CyclicGraph_DoesNotLoopForever', async () => {
      const steps: ProgramStep[] = [
        { id: 1, title: 'A', description: 'A', multiple: false, actions: [], options: [{ label: 'a', value: 'a', nextStep: 2 }] },
        { id: 2, title: 'B', description: 'B', multiple: false, actions: [], options: [{ label: 'b', value: 'b', nextStep: 1 }] },
      ];
      await setup(of(steps));
      expect(component.total()).toBe(3);
    });
  });
});
