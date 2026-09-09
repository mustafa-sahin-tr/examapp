import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';

import { MyProgramsComponent } from './my-programs.component';
import { ProgramService } from '../../services/program.service';
import { UserProgram } from '../../models/program.interfaces';

describe('MyProgramsComponent', () => {
  let component: MyProgramsComponent;
  let fixture: ComponentFixture<MyProgramsComponent>;
  let programService: jasmine.SpyObj<ProgramService>;
  let router: jasmine.SpyObj<Router>;
  let dialog: jasmine.SpyObj<MatDialog>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;

  function makeProgram(overrides: Partial<UserProgram> = {}): UserProgram {
    return {
      id: 1,
      userId: 'u1',
      programName: 'Test Programı',
      description: 'desc',
      createdDate: '2024-01-01',
      isActive: true,
      studyType: 'regular',
      subjectsPerDay: 2,
      restDays: '',
      difficultSubjects: '',
      completedPageCount: 3,
      totalPageCount: 10,
      progressPercentage: 30,
      schedules: [],
      studyPageSchedules: [],
      ...overrides,
    };
  }

  function configure(programs: UserProgram[] = [makeProgram()]): ComponentFixture<MyProgramsComponent> {
    programService = jasmine.createSpyObj<ProgramService>('ProgramService', ['getMyPrograms', 'deleteProgram']);
    programService.getMyPrograms.and.returnValue(of(programs));
    router = jasmine.createSpyObj<Router>('Router', ['navigate']);
    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    TestBed.configureTestingModule({
      imports: [MyProgramsComponent],
      providers: [
        { provide: ProgramService, useValue: programService },
        { provide: Router, useValue: router },
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    });
    // MyProgramsComponent imports MatDialogModule/MatSnackBarModule, which
    // re-declare their own providers — those module-scoped providers win over
    // a plain TestBed root provider, so the real services would run without this.
    TestBed.overrideProvider(MatDialog, { useValue: dialog });
    TestBed.overrideProvider(MatSnackBar, { useValue: snackBar });

    const created = TestBed.createComponent(MyProgramsComponent);
    created.detectChanges();
    return created;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('should create', () => {
    fixture = configure();
    component = fixture.componentInstance;
    expect(component).toBeTruthy();
  });

  describe('getProgressPercentage', () => {
    beforeEach(() => {
      fixture = configure();
      component = fixture.componentInstance;
    });

    it('getProgressPercentage_ZeroFromBackend_ReturnsZero', () => {
      expect(component.getProgressPercentage(makeProgram({ progressPercentage: 0 }))).toBe(0);
    });

    it('getProgressPercentage_MidValueFromBackend_ReturnsSameValue', () => {
      expect(component.getProgressPercentage(makeProgram({ progressPercentage: 45 }))).toBe(45);
    });

    it('getProgressPercentage_HundredFromBackend_ReturnsHundred', () => {
      expect(component.getProgressPercentage(makeProgram({ progressPercentage: 100 }))).toBe(100);
    });

    it('getProgressPercentage_ValueAboveHundred_ClampsToHundred', () => {
      expect(component.getProgressPercentage(makeProgram({ progressPercentage: 150 }))).toBe(100);
    });

    it('getProgressPercentage_NegativeValue_ClampsToZero', () => {
      expect(component.getProgressPercentage(makeProgram({ progressPercentage: -10 }))).toBe(0);
    });
  });

  describe('getPageProgressText', () => {
    beforeEach(() => {
      fixture = configure();
      component = fixture.componentInstance;
    });

    it('getPageProgressText_WithCompletedAndTotal_ReturnsFormattedString', () => {
      const program = makeProgram({ completedPageCount: 3, totalPageCount: 10 });
      expect(component.getPageProgressText(program)).toBe('3/10 sayfa tamamlandı');
    });

    it('getPageProgressText_MissingCounts_DefaultsToZero', () => {
      const program = makeProgram({ completedPageCount: undefined as any, totalPageCount: undefined as any });
      expect(component.getPageProgressText(program)).toBe('0/0 sayfa tamamlandı');
    });
  });

  describe('continueProgram', () => {
    beforeEach(() => {
      fixture = configure();
      component = fixture.componentInstance;
    });

    it('continueProgram_Called_NavigatesToProgramDetail', () => {
      const program = makeProgram({ id: 42 });

      component.continueProgram(program);

      expect(router.navigate).toHaveBeenCalledWith(['/programs', 42, 'detail']);
    });
  });

  describe('deleteProgram', () => {
    let program: UserProgram;

    beforeEach(() => {
      program = makeProgram({ id: 7 });
      fixture = configure([program]);
      component = fixture.componentInstance;
    });

    it('deleteProgram_ConfirmedAndSucceeds_CallsServiceRemovesFromListsAndShowsSnackBar', () => {
      dialog.open.and.returnValue({ afterClosed: () => of(true) } as any);
      programService.deleteProgram.and.returnValue(of(undefined));

      component.deleteProgram(program);

      expect(dialog.open).toHaveBeenCalled();
      expect(programService.deleteProgram).toHaveBeenCalledWith(7);
      expect(component.programs.find((p) => p.id === 7)).toBeUndefined();
      expect(component.filteredPrograms.find((p) => p.id === 7)).toBeUndefined();
      expect(snackBar.open).toHaveBeenCalled();
    });

    it('deleteProgram_CancelledInDialog_DoesNotCallServiceAndKeepsProgram', () => {
      dialog.open.and.returnValue({ afterClosed: () => of(false) } as any);

      component.deleteProgram(program);

      expect(dialog.open).toHaveBeenCalled();
      expect(programService.deleteProgram).not.toHaveBeenCalled();
      expect(component.programs.find((p) => p.id === 7)).toBeTruthy();
    });

    it('deleteProgram_BackendError_KeepsProgramInListAndShowsErrorSnackBar', () => {
      dialog.open.and.returnValue({ afterClosed: () => of(true) } as any);
      programService.deleteProgram.and.returnValue(throwError(() => new Error('boom')));

      component.deleteProgram(program);

      expect(component.programs.find((p) => p.id === 7)).toBeTruthy();
      expect(snackBar.open).toHaveBeenCalledWith(
        'Program silinemedi. Lütfen tekrar deneyin.',
        'Tamam',
        jasmine.any(Object)
      );
    });
  });

  describe('loadMyPrograms error/retry state', () => {
    it('loadMyPrograms_ServiceErrors_SetsLoadErrorTrueAndClearsLists', () => {
      programService = jasmine.createSpyObj<ProgramService>('ProgramService', ['getMyPrograms', 'deleteProgram']);
      programService.getMyPrograms.and.returnValue(throwError(() => new Error('network')));

      TestBed.configureTestingModule({
        imports: [MyProgramsComponent],
        providers: [
          { provide: ProgramService, useValue: programService },
          { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
          { provide: MatDialog, useValue: jasmine.createSpyObj<MatDialog>('MatDialog', ['open']) },
          { provide: MatSnackBar, useValue: jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']) },
        ],
      });

      const created = TestBed.createComponent(MyProgramsComponent);
      created.detectChanges();
      component = created.componentInstance;

      expect(component.loadError).toBeTrue();
      expect(component.loading).toBeFalse();
      expect(component.programs).toEqual([]);
      expect(component.filteredPrograms).toEqual([]);
    });

    it('loadMyPrograms_RetryAfterError_ResetsLoadErrorAndSucceeds', () => {
      programService = jasmine.createSpyObj<ProgramService>('ProgramService', ['getMyPrograms', 'deleteProgram']);
      programService.getMyPrograms.and.returnValue(throwError(() => new Error('network')));

      TestBed.configureTestingModule({
        imports: [MyProgramsComponent],
        providers: [
          { provide: ProgramService, useValue: programService },
          { provide: Router, useValue: jasmine.createSpyObj<Router>('Router', ['navigate']) },
          { provide: MatDialog, useValue: jasmine.createSpyObj<MatDialog>('MatDialog', ['open']) },
          { provide: MatSnackBar, useValue: jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']) },
        ],
      });

      const created = TestBed.createComponent(MyProgramsComponent);
      created.detectChanges();
      component = created.componentInstance;
      expect(component.loadError).toBeTrue();

      const program = makeProgram();
      programService.getMyPrograms.and.returnValue(of([program]));

      component.loadMyPrograms();

      expect(component.loadError).toBeFalse();
      expect(component.loading).toBeFalse();
      expect(component.programs).toEqual([program]);
    });
  });

  describe('completed filter', () => {
    it('isCompleted_ProgressAtLeastHundred_ReturnsTrue', () => {
      fixture = configure();
      component = fixture.componentInstance;
      expect(component.isCompleted(makeProgram({ progressPercentage: 100 }))).toBeTrue();
      expect(component.isCompleted(makeProgram({ progressPercentage: 150 }))).toBeTrue();
      expect(component.isCompleted(makeProgram({ progressPercentage: 99 }))).toBeFalse();
    });

    it('setFilter_Completed_FilteredProgramsOnlyContainsCompletedPrograms', () => {
      const completed = makeProgram({ id: 1, progressPercentage: 100 });
      const active = makeProgram({ id: 2, progressPercentage: 40 });
      const notStarted = makeProgram({ id: 3, progressPercentage: 0 });
      fixture = configure([completed, active, notStarted]);
      component = fixture.componentInstance;

      component.setFilter('completed');

      expect(component.filteredPrograms.map((p) => p.id)).toEqual([1]);
    });
  });
});
