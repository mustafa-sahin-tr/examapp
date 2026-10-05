import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';

import { ProgramCreateComponent } from './program-create.component';
import { ProgramService } from '../../services/program.service';
import { ProgramStep } from '../../models/programstep';

import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import programCreateTr from '../../../../public/i18n/program-create/tr.json';

/**
 * Sayfa cevirileri kendi Transloco scope'undadir (issue #183); testte gercek sozluk verilir,
 * sahte ceviri kullanilmaz - boylece bir anahtar bozulursa test kirilir.
 */
const translocoTesting = translocoTestingModule({ langs: { 'program-create/tr': programCreateTr } });

describe('ProgramCreateComponent', () => {
  let component: ProgramCreateComponent;
  let fixture: ComponentFixture<ProgramCreateComponent>;
  let programService: jasmine.SpyObj<ProgramService>;

  const mockProgramSteps: ProgramStep[] = [
    {
      id: 1,
      title: 'Adım 1',
      description: 'Test adımı',
      options: [{ label: 'Seçenek 1', value: 'opt1' }],
      multiple: false,
      actions: [],
    },
  ];

  beforeEach(async () => {
    programService = jasmine.createSpyObj<ProgramService>('ProgramService', [
      'getProgramSteps',
      'createProgram',
      'getMyPrograms',
      'getProgramById',
      'addStudyPages',
    ]);
    programService.getProgramSteps.and.returnValue(of(mockProgramSteps));

    await TestBed.configureTestingModule({
      imports: [ProgramCreateComponent, translocoTesting],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ProgramService, useValue: programService },
      ],
    })
    .compileComponents();

    fixture = TestBed.createComponent(ProgramCreateComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('ngOnInit_OnLoad_FetchesProgramStepsFromService', () => {
    expect(programService.getProgramSteps).toHaveBeenCalled();
    expect(component.programSteps).toEqual(mockProgramSteps);
  });
});

describe('ProgramCreateComponent - next() NextStep fallback (issue #136)', () => {
  let component: ProgramCreateComponent;
  let fixture: ComponentFixture<ProgramCreateComponent>;
  let programService: jasmine.SpyObj<ProgramService>;

  async function setupWithSteps(steps: ProgramStep[]): Promise<void> {
    programService = jasmine.createSpyObj<ProgramService>('ProgramService', [
      'getProgramSteps',
      'createProgram',
      'getMyPrograms',
      'getProgramById',
      'addStudyPages',
    ]);
    programService.getProgramSteps.and.returnValue(of(steps));

    await TestBed.configureTestingModule({
      imports: [ProgramCreateComponent, translocoTesting],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ProgramService, useValue: programService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ProgramCreateComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('next_SelectedOptionNextStepDoesNotExistInProgramSteps_FallsBackToCreatingProgramForm', async () => {
    const stepsWithDanglingNextStep: ProgramStep[] = [
      {
        id: 1,
        title: 'Adım 1',
        description: 'Test adımı',
        options: [{ label: 'Seçenek 1', value: 'opt1', nextStep: 999 }],
        multiple: false,
        actions: [],
      },
      {
        id: 2,
        title: 'Adım 2',
        description: 'Test adımı 2',
        options: [{ label: 'Seçenek 2', value: 'opt2' }],
        multiple: false,
        actions: [],
      },
    ];

    await setupWithSteps(stepsWithDanglingNextStep);

    const initialIndex = component.currentIndex();
    const initialStepIndex = component.stepIndex();

    component.selectOption(stepsWithDanglingNextStep[0], stepsWithDanglingNextStep[0].options[0]);
    component.next();

    expect(component.isCreatingProgram()).toBeTrue();
    expect(component.currentIndex()).toBe(initialIndex);
    expect(component.stepIndex()).toBe(initialStepIndex);
  });

  it('next_SelectedOptionNextStepExists_AdvancesToThatStepWithoutFallback', async () => {
    const stepsWithValidNextStep: ProgramStep[] = [
      {
        id: 1,
        title: 'Adım 1',
        description: 'Test adımı',
        options: [{ label: 'Seçenek 1', value: 'opt1', nextStep: 2 }],
        multiple: false,
        actions: [],
      },
      {
        id: 2,
        title: 'Adım 2',
        description: 'Test adımı 2',
        options: [{ label: 'Seçenek 2', value: 'opt2' }],
        multiple: false,
        actions: [],
      },
    ];

    await setupWithSteps(stepsWithValidNextStep);

    component.selectOption(stepsWithValidNextStep[0], stepsWithValidNextStep[0].options[0]);
    component.next();

    expect(component.isCreatingProgram()).toBeFalse();
    expect(component.currentIndex()).toBe(1);
    expect(component.stepIndex()).toBe(1);
  });
});

describe('ProgramCreateComponent - loadProgramSteps() loading/error states (issue #136)', () => {
  let component: ProgramCreateComponent;
  let fixture: ComponentFixture<ProgramCreateComponent>;
  let programService: jasmine.SpyObj<ProgramService>;
  let snackBarOpenSpy: jasmine.Spy;

  const mockProgramSteps: ProgramStep[] = [
    {
      id: 1,
      title: 'Adım 1',
      description: 'Test adımı',
      options: [{ label: 'Seçenek 1', value: 'opt1' }],
      multiple: false,
      actions: [],
    },
  ];

  async function setup(): Promise<void> {
    programService = jasmine.createSpyObj<ProgramService>('ProgramService', [
      'getProgramSteps',
      'createProgram',
      'getMyPrograms',
      'getProgramById',
      'addStudyPages',
    ]);

    await TestBed.configureTestingModule({
      imports: [ProgramCreateComponent, translocoTesting],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ProgramService, useValue: programService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ProgramCreateComponent);
    component = fixture.componentInstance;
    // MatSnackBarModule (imported by the standalone component) registers its own MatSnackBar
    // provider in the component's own injector tree, which is a different instance from the
    // one TestBed.inject(MatSnackBar) resolves at the root test module level. Read it off the
    // component's own injector so the spy targets the instance actually used by the component.
    const componentSnackBar = fixture.debugElement.injector.get(MatSnackBar);
    snackBarOpenSpy = spyOn(componentSnackBar, 'open').and.stub();
  }

  it('loadProgramSteps_ServiceReturnsError_SetsStepsErrorAndShowsSnackBarWithoutSteps', async () => {
    await setup();
    programService.getProgramSteps.and.returnValue(throwError(() => new Error('network error')));

    fixture.detectChanges(); // triggers ngOnInit -> loadProgramSteps

    expect(component.stepsLoading()).toBeFalse();
    expect(component.stepsError()).toBeTruthy();
    expect(component.programSteps).toEqual([]);
    expect(snackBarOpenSpy).toHaveBeenCalledWith(
      'Sorular yüklenemedi, lütfen sayfayı yenileyin',
      'Tamam',
      jasmine.objectContaining({ duration: jasmine.any(Number) }),
    );
  });

  it('loadProgramSteps_ServiceReturnsEmptyArray_SetsStepsError', async () => {
    await setup();
    programService.getProgramSteps.and.returnValue(of([]));

    fixture.detectChanges();

    expect(component.stepsLoading()).toBeFalse();
    expect(component.stepsError()).toBeTruthy();
    expect(component.programSteps).toEqual([]);
    expect(snackBarOpenSpy).not.toHaveBeenCalled();
  });

  it('loadProgramSteps_ServiceReturnsSteps_ClearsLoadingAndErrorRegression', async () => {
    await setup();
    programService.getProgramSteps.and.returnValue(of(mockProgramSteps));

    fixture.detectChanges();

    expect(component.stepsLoading()).toBeFalse();
    expect(component.stepsError()).toBeFalsy();
    expect(component.programSteps).toEqual(mockProgramSteps);
  });

  it('retryButton_ClickedAfterError_CallsLoadProgramStepsAgainAndRecovers', async () => {
    await setup();
    programService.getProgramSteps.and.returnValue(throwError(() => new Error('network error')));

    fixture.detectChanges();
    expect(component.stepsError()).toBeTruthy();

    programService.getProgramSteps.and.returnValue(of(mockProgramSteps));
    spyOn(component, 'loadProgramSteps').and.callThrough();

    fixture.detectChanges();
    const retryButton = fixture.debugElement.query(By.css('.ms-wizard-state-error button'));
    expect(retryButton).withContext('Tekrar Dene butonu template içinde bulunamadı').toBeTruthy();

    retryButton.nativeElement.click();
    fixture.detectChanges();

    expect(component.loadProgramSteps).toHaveBeenCalled();
    expect(component.stepsLoading()).toBeFalse();
    expect(component.stepsError()).toBeFalsy();
    expect(component.programSteps).toEqual(mockProgramSteps);
  });
});

describe('ProgramCreateComponent - option icons (issue #319)', () => {
  let fixture: ComponentFixture<ProgramCreateComponent>;

  const steps: ProgramStep[] = [
    {
      id: 1,
      title: 'Adım 1',
      description: 'Çalışma türü',
      options: [
        { label: 'Süreli Çalışma', value: 'time', icon: 'timer' },
        { label: 'Eski SVG', value: 'legacy', icon: 'icons/clock.svg' },
        { label: 'Bozuk', value: 'bad', icon: 'Quiz<b>' },
        { label: 'İkonsuz', value: 'none' },
      ],
      multiple: false,
      actions: [],
    },
  ];

  beforeEach(async () => {
    const programService = jasmine.createSpyObj<ProgramService>('ProgramService', [
      'getProgramSteps',
      'createProgram',
      'getMyPrograms',
      'getProgramById',
      'addStudyPages',
    ]);
    programService.getProgramSteps.and.returnValue(of(steps));

    await TestBed.configureTestingModule({
      imports: [ProgramCreateComponent, translocoTesting],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ProgramService, useValue: programService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ProgramCreateComponent);
    fixture.detectChanges();
  });

  function optionButtons(): HTMLElement[] {
    return fixture.debugElement.queryAll(By.css('.ms-option-button')).map((d) => d.nativeElement as HTMLElement);
  }

  it('optionIcon_ValidSymbolName_RendersMatIconWithSymbolsFontSetAndName', () => {
    const [first] = optionButtons();
    const icon = first.querySelector('mat-icon.ms-option-icon') as HTMLElement;

    expect(icon).withContext('mat-icon render edilmedi').toBeTruthy();
    expect(icon.textContent?.trim()).toBe('timer');
    expect(icon.classList).toContain('material-symbols-outlined');
    expect(icon.getAttribute('aria-hidden')).toBe('true');
    expect(first.querySelector('img')).toBeNull();
  });

  it('optionIcon_SvgPathInvalidOrMissing_FallsBackToNeutralIcon', () => {
    const [, legacy, bad, none] = optionButtons();

    for (const button of [legacy, bad, none]) {
      expect(button.querySelector('img')).toBeNull();
      expect(button.querySelector('mat-icon')?.textContent?.trim()).toBe('radio_button_unchecked');
    }
  });

  it('optionLabel_AlwaysVisibleNextToIcon', () => {
    const labels = optionButtons().map((b) => b.querySelector('.ms-option-label')?.textContent?.trim());
    expect(labels).toEqual(['Süreli Çalışma', 'Eski SVG', 'Bozuk', 'İkonsuz']);
  });
});
