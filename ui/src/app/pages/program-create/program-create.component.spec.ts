import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { ProgramCreateComponent } from './program-create.component';
import { ProgramService } from '../../services/program.service';
import { ProgramStep } from '../../models/programstep';

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
      imports: [ProgramCreateComponent],
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
