import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatSnackBar } from '@angular/material/snack-bar';
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
    it('loading_StepsPending_ShowsSkeletonStatusAndRailRows', async () => {
      await setup(new Subject<ProgramStep[]>());
      expect(component.viewState()).toBe('loading');
      const status = host.querySelector('.pc-loading');
      expect(status?.getAttribute('role')).toBe('status');
      expect(status?.getAttribute('aria-live')).toBe('polite');
      expect(status?.textContent).toContain(programCreateTr.wizard.loading);
      expect(host.querySelector('.pc-card.pc-skeleton')?.getAttribute('aria-busy')).toBe('true');
      expect(host.querySelectorAll('.pc-skeleton__tile').length).toBe(4);
      const rail = host.querySelector('.pc-rail.pc-skeleton--rail');
      expect(rail?.getAttribute('aria-hidden')).toBe('true');
      expect(rail?.querySelectorAll('.pc-skeleton__row').length).toBe(2);
    });

    it('error_LoadFails_ShowsAlertHidesRailAndRetryReloads', async () => {
      spyOn(console, 'error');
      await setup(throwError(() => new Error('boom')));
      expect(component.viewState()).toBe('error');
      // Hata ekranı kendi role=alert'ini taşır; snackbar açılmaz.
      expect(document.querySelector('.mat-mdc-snack-bar-container')).toBeNull();
      const alert = host.querySelector('.pc-state[role="alert"]');
      expect(alert?.textContent).toContain(programCreateTr.wizard.loadError);
      expect(alert?.querySelector('mat-icon')?.textContent?.trim()).toBe('cloud_off');
      expect(host.querySelector('.pc-rail')).toBeNull();

      programService.getProgramSteps.and.returnValue(of(seedSteps()));
      alert!.querySelector<HTMLButtonElement>('.pc-state__retry')!.click();
      fixture.detectChanges();
      expect(programService.getProgramSteps).toHaveBeenCalledTimes(2);
      expect(component.viewState()).toBe('step');
      expect(currentStepId()).toBe(1);
      expect(host.querySelector('.pc-rail')).not.toBeNull();
    });

    it('empty_NoSteps_ShowsInfoStateAndSecondaryRetry', async () => {
      await setup(of([]));
      expect(component.viewState()).toBe('empty');
      const state = host.querySelector('.pc-state[role="status"]')!;
      expect(state.textContent).toContain(programCreateTr.wizard.emptyTitle);
      expect(state.textContent).toContain(programCreateTr.wizard.emptyBody);
      expect(state.querySelector('mat-icon')?.textContent?.trim()).toBe('info');
      const retry = state.querySelector<HTMLButtonElement>('.pc-state__retry')!;
      expect(retry.hasAttribute('mat-stroked-button')).toBeTrue();

      programService.getProgramSteps.and.returnValue(of(seedSteps()));
      retry.click();
      fixture.detectChanges();
      expect(component.viewState()).toBe('step');
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

  describe('son adım formu (dilim 2)', () => {
    beforeEach(() => setup());

    function goToForm(): void {
      answer('Süreli Çalışma');
      answer('25 dakika çalışma 5 dakika ara');
      answer('1');
      answer('Pazartesi');
      answer('Matematik');
      expect(component.viewState()).toBe('form');
    }

    function submitButton(): HTMLButtonElement {
      return host.querySelector<HTMLButtonElement>('.pc-bar__submit')!;
    }

    function typeInto(selector: string, value: string): void {
      const input = host.querySelector<HTMLInputElement>(selector)!;
      input.value = value;
      input.dispatchEvent(new Event('input'));
      input.dispatchEvent(new Event('blur'));
      fixture.detectChanges();
    }

    function errorTexts(): string[] {
      return Array.from(host.querySelectorAll('mat-error')).map((e) => e.textContent!.trim());
    }

    async function settle(): Promise<void> {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    }

    it('defaults_TodayAndThirtyDaysLater_ReviewTitleAndPanelCount', () => {
      goToForm();
      const start = component.programStartDate()!;
      const end = component.programEndDate()!;
      expect(start.toDateString()).toBe(new Date().toDateString());
      expect(Math.round((end.getTime() - start.getTime()) / 86_400_000)).toBe(30);
      expect(host.querySelector('.pc-question')?.textContent?.trim()).toBe(programCreateTr.form.title);
      expect(host.querySelector('.pc-rail .ss__title')?.textContent?.trim()).toBe(programCreateTr.form.reviewTitle);
      expect(host.querySelectorAll('.pc-rail button.ss-row').length).toBe(5);
      expect(host.querySelector('.pc-review-panel__title')?.textContent?.trim()).toBe('5 cevap');
      expect(host.querySelector('.pc-review-panel__preview')?.textContent).toContain('Süreli Çalışma');
      // Çip şeridi formda gösterilmez (yerini katlanır panel alır).
      expect(host.querySelector('.pc-chips')).toBeNull();
      expect(host.querySelectorAll('mat-form-field.mat-form-field-appearance-outline').length).toBe(4);
    });

    it('name_EmptyAfterTouch_ShowsInlineErrorAndDisablesSubmit', async () => {
      goToForm();
      await settle();
      expect(submitButton().disabled).toBeTrue();
      expect(errorTexts()).toEqual([]);

      typeInto('.pc-form__name', '   ');
      expect(errorTexts()).toEqual([programCreateTr.form.nameRequired]);
      expect(submitButton().disabled).toBeTrue();

      typeInto('.pc-form__name', 'Planım');
      expect(errorTexts()).toEqual([]);
      expect(submitButton().disabled).toBeFalse();
    });

    it('dates_EndNotAfterStart_ShowsInlineErrorAndDisablesSubmit', async () => {
      goToForm();
      component.programName.set('Planım');
      const start = component.programStartDate()!;
      component.programEndDate.set(new Date(start.getFullYear(), start.getMonth(), start.getDate()));
      await settle();

      expect(component.dateOrderInvalid()).toBeTrue();
      expect(errorTexts()).toEqual([programCreateTr.form.endDateAfterStart]);
      expect(submitButton().disabled).toBeTrue();

      component.programEndDate.set(new Date(start.getFullYear(), start.getMonth(), start.getDate() + 1));
      await settle();
      expect(errorTexts()).toEqual([]);
      expect(submitButton().disabled).toBeFalse();
    });

    it('dates_EndCleared_DisablesSubmitAndShowsRequiredAfterTouch', async () => {
      goToForm();
      component.programName.set('Planım');
      await settle();
      typeInto('.pc-form__end', '');
      expect(component.programEndDate()).toBeNull();
      expect(errorTexts()).toEqual([programCreateTr.form.endDateRequired]);
      expect(submitButton().disabled).toBeTrue();
    });

    it('createProgram_InvalidFormCalledDirectly_DoesNothing', () => {
      goToForm();
      const open = spyOn(TestBed.inject(MatSnackBar), 'open');
      component.programName.set('Planım');
      component.programEndDate.set(component.programStartDate());
      component.createProgram();
      component.programName.set('  ');
      component.programEndDate.set(null);
      component.createProgram();
      expect(programService.createProgram).not.toHaveBeenCalled();
      expect(open).not.toHaveBeenCalled();
    });

    it('submit_SendsLocalCalendarDays_NotUtcShifted', () => {
      goToForm();
      // Datepicker yerel gece yarısı verir; TRT'de toISOString() bunu önceki güne kaydırırdı.
      component.programStartDate.set(new Date(2026, 9, 10));
      component.programEndDate.set(new Date(2026, 10, 9));
      const request = fillFormAndSubmit();
      expect(request.startDate).toBe('2026-10-10');
      expect(request.endDate).toBe('2026-11-09');
    });

    it('dates_UnparsableInput_ShowsInvalidDateMessage', async () => {
      goToForm();
      component.programName.set('Planım');
      await settle();
      typeInto('.pc-form__start', 'abc');
      await settle();
      expect(component.programStartDate()).toBeNull();
      expect(errorTexts()).toEqual([programCreateTr.form.dateInvalid]);
      expect(submitButton().disabled).toBeTrue();
    });

    it('submitting_ShowsSpinnerDisablesFormAndBlocksDoubleClick', () => {
      const response$ = new Subject<UserProgram>();
      programService.createProgram.and.returnValue(response$);
      goToForm();
      component.programName.set('Planım');
      fixture.detectChanges();

      submitButton().click();
      fixture.detectChanges();
      expect(component.submitting()).toBeTrue();
      expect(submitButton().disabled).toBeTrue();
      expect(submitButton().getAttribute('aria-busy')).toBe('true');
      expect(submitButton().querySelector('mat-spinner')).not.toBeNull();
      expect(host.querySelector<HTMLButtonElement>('.pc-bar__back')!.disabled).toBeTrue();

      component.createProgram();
      submitButton().click();
      expect(programService.createProgram).toHaveBeenCalledTimes(1);
    });

    it('submitError_ShowsAlertBandAndKeepsSelectionsAndValues', () => {
      spyOn(console, 'error');
      const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
      const response$ = new Subject<UserProgram>();
      programService.createProgram.and.returnValue(response$);
      goToForm();
      component.programName.set('Planım');
      component.programDescription.set('Açıklama');
      fixture.detectChanges();
      const answersBefore = component.answers();

      submitButton().click();
      fixture.detectChanges();
      response$.error(new Error('500'));
      fixture.detectChanges();

      const alert = host.querySelector('.pc-alert');
      expect(alert?.getAttribute('role')).toBe('alert');
      expect(alert?.textContent).toContain(programCreateTr.form.createFailedInline);
      expect(component.viewState()).toBe('form');
      expect(component.programName()).toBe('Planım');
      expect(component.programDescription()).toBe('Açıklama');
      expect(component.answers()).toBe(answersBefore);
      expect(component.path()).toEqual([1, 2, 5, 6, 7]);
      expect(submitButton().disabled).toBeFalse();
      expect(submitButton().querySelector('mat-spinner')).toBeNull();
      expect(navigate).not.toHaveBeenCalled();

      // Tekrar dene: bant kalkar, aynı değerlerle istek gider.
      programService.createProgram.and.returnValue(of({} as UserProgram));
      submitButton().click();
      fixture.detectChanges();
      expect(host.querySelector('.pc-alert')).toBeNull();
      expect(programService.createProgram.calls.mostRecent().args[0].programName).toBe('Planım');
      expect(navigate).toHaveBeenCalledWith(['/programs']);
    });

    it('submitSuccess_ShowsSnackbarAndNavigates', () => {
      const open = spyOn(TestBed.inject(MatSnackBar), 'open');
      const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
      goToForm();
      fillFormAndSubmit();
      expect(open).toHaveBeenCalledWith(programCreateTr.form.created, programCreateTr.actions.ok, jasmine.anything());
      expect(navigate).toHaveBeenCalledWith(['/programs']);
    });

    it('reviewEdit_RailRowReturnsToThatStepWithSelection', () => {
      goToForm();
      const rows = host.querySelectorAll<HTMLButtonElement>('.pc-rail button.ss-row');
      rows[1].click();
      fixture.detectChanges();
      expect(component.viewState()).toBe('step');
      expect(currentStepId()).toBe(2);
      expect(cards()[0].getAttribute('aria-checked')).toBe('true');
      expect(component.path()).toEqual([1, 2]);
    });

    it('reviewEdit_MobilePanelRowReturnsToThatStep', () => {
      goToForm();
      const rows = host.querySelectorAll<HTMLButtonElement>('.pc-review-panel button.ss-row');
      expect(rows.length).toBe(5);
      rows[3].click();
      fixture.detectChanges();
      expect(currentStepId()).toBe(6);
    });
  });

  describe('odak yönetimi', () => {
    let focus: jasmine.Spy;

    beforeEach(async () => {
      focus = spyOn(HTMLElement.prototype, 'focus').and.callThrough();
      await setup();
    });

    function headingFocusCount(): number {
      return focus.calls.all().filter((call) => (call.object as HTMLElement).id === 'pc-question').length;
    }

    it('initialLoad_DoesNotMoveFocus', () => {
      expect(headingFocusCount()).toBe(0);
      const heading = host.querySelector('#pc-question')!;
      expect(heading.tagName).toBe('H2');
      expect(heading.getAttribute('tabindex')).toBe('-1');
    });

    it('navigation_MovesFocusToQuestionHeading', () => {
      answer('Süreli Çalışma');
      expect(headingFocusCount()).toBe(1);
      expect(host.querySelector('#pc-question')?.textContent?.trim()).toBe('Çalışma süresi');

      host.querySelector<HTMLButtonElement>('.pc-bar__back')!.click();
      fixture.detectChanges();
      expect(headingFocusCount()).toBe(2);
    });

    it('retrySuccess_MovesFocusToHeading', async () => {
      TestBed.resetTestingModule();
      focus.calls.reset();
      spyOn(console, 'error');
      await setup(throwError(() => new Error('boom')));
      programService.getProgramSteps.and.returnValue(of(seedSteps()));
      host.querySelector<HTMLButtonElement>('.pc-state__retry')!.click();
      fixture.detectChanges();
      fixture.detectChanges();
      expect(headingFocusCount()).toBe(1);
    });

    it('submitError_MovesFocusToAlertBand', () => {
      spyOn(console, 'error');
      programService.createProgram.and.returnValue(throwError(() => new Error('500')));
      answer('Süreli Çalışma');
      answer('25 dakika çalışma 5 dakika ara');
      answer('1');
      answer('Pazartesi');
      answer('Matematik');
      fillFormAndSubmit();
      fixture.detectChanges();
      const alert = host.querySelector<HTMLElement>('.pc-alert')!;
      expect(alert.getAttribute('tabindex')).toBe('-1');
      expect(focus.calls.all().some((call) => call.object === alert)).toBeTrue();
    });

    it('enteringFormAndGoingBack_MovesFocusToHeading', () => {
      answer('Süreli Çalışma');
      answer('25 dakika çalışma 5 dakika ara');
      answer('1');
      answer('Pazartesi');
      const before = headingFocusCount();
      answer('Matematik');
      expect(headingFocusCount()).toBe(before + 1);
      expect(host.querySelector('#pc-question')?.textContent?.trim()).toBe(programCreateTr.form.title);

      host.querySelector<HTMLButtonElement>('.pc-bar__back')!.click();
      fixture.detectChanges();
      expect(currentStepId()).toBe(7);
      expect(headingFocusCount()).toBe(before + 2);
    });
  });

  describe('hareket azaltma', () => {
    it('reducedMotion_SubmittingShowsStaticIconInsteadOfSpinner', async () => {
      const original = window.matchMedia.bind(window);
      spyOn(window, 'matchMedia').and.callFake((query: string) =>
        query.includes('prefers-reduced-motion') ? ({ matches: true } as MediaQueryList) : original(query),
      );
      await setup();
      expect(component.reducedMotion).toBeTrue();
      programService.createProgram.and.returnValue(new Subject<UserProgram>());
      answer('Süreli Çalışma');
      answer('25 dakika çalışma 5 dakika ara');
      answer('1');
      answer('Pazartesi');
      answer('Matematik');
      fillFormAndSubmit();
      const submit = host.querySelector<HTMLButtonElement>('.pc-bar__submit')!;
      expect(submit.querySelector('mat-spinner')).toBeNull();
      expect(submit.querySelector('.pc-bar__busy-icon')?.textContent?.trim()).toBe('hourglass_top');
    });
  });
});
