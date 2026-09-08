import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { TaxonomyManagerComponent } from './taxonomy-manager.component';
import { AdminService } from '../../../services/admin.service';
import { ApiResult, School, TaxonomySubject, TaxonomyTree } from '../../../models/taxonomy';

describe('TaxonomyManagerComponent', () => {
  let fixture: ComponentFixture<TaxonomyManagerComponent>;
  let component: TaxonomyManagerComponent;
  let adminService: jasmine.SpyObj<AdminService>;
  let dialog: jasmine.SpyObj<MatDialog>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;

  const okResult: ApiResult = { success: true, message: 'İşlem başarılı' };

  const subjects: TaxonomySubject[] = [
    {
      id: 1,
      name: 'Matematik',
      gradeIds: [5],
      topics: [
        {
          id: 10,
          name: 'Kesirler',
          subjectId: 1,
          gradeId: 5,
          gradeName: '5. Sınıf',
          subTopics: [{ id: 100, name: 'Basit Kesirler', topicId: 10, questionCount: 3 }],
        },
      ],
    },
  ];

  const tree: TaxonomyTree = {
    subjects,
    grades: [{ id: 5, name: '5. Sınıf' }],
  };

  const schools: School[] = [{ id: 1, name: 'Atatürk İlkokulu', city: 'Ankara' }];

  function configure(): ComponentFixture<TaxonomyManagerComponent> {
    adminService = jasmine.createSpyObj<AdminService>('AdminService', [
      'getTaxonomy',
      'getSchools',
      'createSubject',
      'updateSubject',
      'deleteSubject',
      'createTopic',
      'updateTopic',
      'deleteTopic',
      'createSubTopic',
      'updateSubTopic',
      'deleteSubTopic',
      'createSchool',
      'updateSchool',
      'deleteSchool',
      'addSubjectGrade',
      'removeSubjectGrade',
    ]);
    adminService.getTaxonomy.and.returnValue(of(tree));
    adminService.getSchools.and.returnValue(of(schools));

    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    dialog.open.and.returnValue({ afterClosed: () => of(true) } as any);

    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    TestBed.configureTestingModule({
      imports: [TaxonomyManagerComponent],
      providers: [
        { provide: AdminService, useValue: adminService },
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
        provideNoopAnimations(),
      ],
    });

    // Komponent MatDialogModule/MatSnackBarModule import ettiği için standalone injector gerçek
    // servisleri sağlar ve root-level mock'lar gölgelenir; mock'ları komponent seviyesinde ver.
    TestBed.overrideComponent(TaxonomyManagerComponent, {
      add: {
        providers: [
          { provide: MatDialog, useValue: dialog },
          { provide: MatSnackBar, useValue: snackBar },
        ],
      },
    });

    return TestBed.createComponent(TaxonomyManagerComponent);
  }

  // ── CRUD çağrıları ────────────────────────────────────────────────────────

  it('addSubject_ValidName_CallsCreateSubjectWithTrimmedName', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();

    adminService.createSubject.and.returnValue(of(okResult));
    component.newSubjectName = '  Fizik  ';

    await component.addSubject();

    expect(adminService.createSubject).toHaveBeenCalledOnceWith({ name: 'Fizik' });
    expect(component.newSubjectName).toBe('');
  });

  it('addTopic_ValidNameAndGrade_CallsCreateTopicWithSelectedSubject', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();
    component.selectSubject(1);

    adminService.createTopic.and.returnValue(of(okResult));
    component.newTopicName = 'Geometri';
    component.newTopicGradeId = 5;

    await component.addTopic();

    expect(adminService.createTopic).toHaveBeenCalledOnceWith({
      name: 'Geometri',
      subjectId: 1,
      gradeId: 5,
    });
    expect(component.newTopicName).toBe('');
  });

  it('addTopic_MissingGrade_DoesNotCallCreateTopic', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();
    component.selectSubject(1);

    component.newTopicName = 'Geometri';
    component.newTopicGradeId = null;

    await component.addTopic();

    expect(adminService.createTopic).not.toHaveBeenCalled();
    expect(snackBar.open).toHaveBeenCalled();
  });

  it('addSubTopic_ValidName_CallsCreateSubTopicWithSelectedTopic', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();
    component.selectSubject(1);
    component.selectTopic(10);

    adminService.createSubTopic.and.returnValue(of(okResult));
    component.newSubTopicName = 'Bileşik Kesirler';

    await component.addSubTopic();

    expect(adminService.createSubTopic).toHaveBeenCalledOnceWith({
      name: 'Bileşik Kesirler',
      topicId: 10,
    });
    expect(component.newSubTopicName).toBe('');
  });

  it('addSchool_ValidNameAndCity_CallsCreateSchoolWithTrimmedFields', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();

    adminService.createSchool.and.returnValue(of(okResult));
    component.newSchoolName = '  Cumhuriyet Ortaokulu ';
    component.newSchoolCity = ' İzmir ';

    await component.addSchool();

    expect(adminService.createSchool).toHaveBeenCalledOnceWith({
      name: 'Cumhuriyet Ortaokulu',
      city: 'İzmir',
    });
  });

  it('addSchool_EmptyCity_SendsNullCity', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();

    adminService.createSchool.and.returnValue(of(okResult));
    component.newSchoolName = 'Yeni Okul';
    component.newSchoolCity = '';

    await component.addSchool();

    expect(adminService.createSchool).toHaveBeenCalledOnceWith({ name: 'Yeni Okul', city: null });
  });

  it('remove_ConfirmDialogAccepted_CallsDeleteSubject', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();

    adminService.deleteSubject.and.returnValue(of(okResult));

    await component.remove('subject', { id: 1, name: 'Matematik' });

    expect(dialog.open).toHaveBeenCalled();
    expect(adminService.deleteSubject).toHaveBeenCalledOnceWith(1);
  });

  it('remove_ConfirmDialogRejected_DoesNotCallDelete', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();
    dialog.open.and.returnValue({ afterClosed: () => of(false) } as any);

    await component.remove('subject', { id: 1, name: 'Matematik' });

    expect(adminService.deleteSubject).not.toHaveBeenCalled();
  });

  it('remove_TopicLevel_CallsDeleteTopic', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();
    adminService.deleteTopic.and.returnValue(of(okResult));

    await component.remove('topic', { id: 10, name: 'Kesirler' });

    expect(adminService.deleteTopic).toHaveBeenCalledOnceWith(10);
  });

  it('remove_SubTopicLevel_CallsDeleteSubTopic', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();
    adminService.deleteSubTopic.and.returnValue(of(okResult));

    await component.remove('subtopic', { id: 100, name: 'Basit Kesirler' });

    expect(adminService.deleteSubTopic).toHaveBeenCalledOnceWith(100);
  });

  it('remove_SchoolLevel_CallsDeleteSchool', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();
    adminService.deleteSchool.and.returnValue(of(okResult));

    await component.remove('school', { id: 1, name: 'Atatürk İlkokulu' });

    expect(adminService.deleteSchool).toHaveBeenCalledOnceWith(1);
  });

  it('saveEdit_SubjectLevel_CallsUpdateSubjectWithTrimmedName', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();
    adminService.updateSubject.and.returnValue(of(okResult));

    component.startEdit('subject', subjects[0]);
    component.editName = '  Matematik 2 ';

    await component.saveEdit('subject', subjects[0]);

    expect(adminService.updateSubject).toHaveBeenCalledOnceWith(1, { name: 'Matematik 2' });
    expect(component.editing()).toBeNull();
  });

  it('saveEdit_TopicLevel_CallsUpdateTopicWithGradeAndSubject', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();
    adminService.updateTopic.and.returnValue(of(okResult));

    const topic = subjects[0].topics[0];
    component.startEdit('topic', topic);
    component.editName = 'Kesirler 2';
    component.editGradeId = 5;

    await component.saveEdit('topic', topic);

    expect(adminService.updateTopic).toHaveBeenCalledOnceWith(10, {
      name: 'Kesirler 2',
      subjectId: 1,
      gradeId: 5,
    });
  });

  it('saveEdit_SubTopicLevel_CallsUpdateSubTopicWithSelectedTopicId', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();
    component.selectSubject(1);
    component.selectTopic(10);
    adminService.updateSubTopic.and.returnValue(of(okResult));

    const subTopic = subjects[0].topics[0].subTopics[0];
    component.startEdit('subtopic', subTopic);
    component.editName = 'Basit Kesirler 2';

    await component.saveEdit('subtopic', subTopic);

    expect(adminService.updateSubTopic).toHaveBeenCalledOnceWith(100, {
      name: 'Basit Kesirler 2',
      topicId: 10,
    });
  });

  it('saveEdit_SchoolLevel_CallsUpdateSchoolWithNameAndCity', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();
    adminService.updateSchool.and.returnValue(of(okResult));

    component.startEdit('school', schools[0]);
    component.editName = 'Atatürk İlkokulu 2';
    component.editCity = 'Ankara';

    await component.saveEdit('school', schools[0]);

    expect(adminService.updateSchool).toHaveBeenCalledOnceWith(1, {
      name: 'Atatürk İlkokulu 2',
      city: 'Ankara',
    });
  });

  // ── Sınıf filtresi / GradeSubject (Issue #119) ────────────────────────────

  it('load_DefaultFilter_CallsGetTaxonomyWithoutParams', () => {
    fixture = configure();
    fixture.detectChanges();

    expect(adminService.getTaxonomy).toHaveBeenCalledOnceWith(undefined);
  });

  it('setGradeFilter_SpecificGrade_ReloadsWithGradeIdAndResetsSelections', () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();
    component.selectSubject(1);
    component.selectTopic(10);

    component.setGradeFilter(5);

    expect(adminService.getTaxonomy).toHaveBeenCalledWith({ gradeId: 5 });
    expect(component.selectedSubjectId()).toBeNull();
    expect(component.selectedTopicId()).toBeNull();
  });

  it('setGradeFilter_Unassigned_ReloadsWithUnassignedFlag', () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();

    component.setGradeFilter('unassigned');

    expect(adminService.getTaxonomy).toHaveBeenCalledWith({ unassigned: true });
  });

  it('setGradeFilter_SameValue_DoesNotReload', () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();

    component.setGradeFilter('all');

    expect(adminService.getTaxonomy).toHaveBeenCalledTimes(1);
  });

  it('visibleSubjects_AllFilter_HidesSubjectsWithoutGrades', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getTaxonomy.and.returnValue(
      of({
        grades: [{ id: 5, name: '5. Sınıf' }],
        subjects: [
          { id: 1, name: 'Matematik', gradeIds: [5], topics: [] },
          { id: 2, name: 'Sınıfsız', gradeIds: [], topics: [] },
        ],
      })
    );
    fixture.detectChanges();

    expect(component.visibleSubjects().map((s) => s.id)).toEqual([1]);
    const emptyState = fixture.nativeElement.querySelector('.column:nth-child(1) .empty-state');
    expect(emptyState).toBeNull();
  });

  it('subjectRow_NoGrades_ShowsUnassignedWarnChip', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getTaxonomy.and.returnValue(
      of({
        grades: [{ id: 5, name: '5. Sınıf' }],
        subjects: [{ id: 2, name: 'Sınıfsız', gradeIds: [], topics: [] }],
      })
    );
    fixture.detectChanges();
    component.setGradeFilter('unassigned');
    fixture.detectChanges();

    const chip: HTMLElement = fixture.nativeElement.querySelector('.grade-chip--warn');
    expect(chip).toBeTruthy();
    expect(chip.textContent).toContain('Sınıf atanmamış');
  });

  it('subjectRow_WithGrades_ShowsGradeNameChips', () => {
    fixture = configure();
    fixture.detectChanges();

    const chips: NodeListOf<HTMLElement> = fixture.nativeElement.querySelectorAll(
      '.column:nth-child(1) .grade-chip'
    );
    expect(chips.length).toBe(1);
    expect(chips[0].textContent).toContain('5. Sınıf');
  });

  it('manageGrades_DialogReportsChange_ReloadsTaxonomy', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();
    dialog.open.and.returnValue({ afterClosed: () => of(true) } as any);

    await component.manageGrades(subjects[0]);

    expect(dialog.open).toHaveBeenCalled();
    expect(adminService.getTaxonomy).toHaveBeenCalledTimes(2);
  });

  it('manageGrades_OpensDialog_WithDisableCloseSoPartialSuccessIsNotLost', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();

    await component.manageGrades(subjects[0]);

    // ESC/backdrop `close()`'u argümansız çağırır; `applied` bilgisi kaybolmasın diye kapalı olmalı.
    const config = dialog.open.calls.mostRecent().args[1];
    expect(config?.disableClose).toBeTrue();
  });

  it('manageGrades_DialogReportsNoChange_DoesNotReload', async () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();
    dialog.open.and.returnValue({ afterClosed: () => of(false) } as any);

    await component.manageGrades(subjects[0]);

    expect(adminService.getTaxonomy).toHaveBeenCalledTimes(1);
  });

  // ── Breadcrumb ────────────────────────────────────────────────────────────

  it('breadcrumb_NoSelection_NotRendered', () => {
    fixture = configure();
    // İlk yükleme ilk dersi otomatik seçer; seçimsiz durumu boş ağaçla kur.
    adminService.getTaxonomy.and.returnValue(of({ subjects: [], grades: [] }));
    fixture.detectChanges();

    const crumbs = fixture.nativeElement.querySelectorAll('.crumb');
    expect(crumbs.length).toBe(0);
  });

  it('breadcrumb_SubjectSelected_ShowsSubjectNameInTopicsHeader', () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();

    component.selectSubject(1);
    fixture.detectChanges();

    const header: HTMLElement = fixture.nativeElement.querySelector('.column:nth-child(2) .column-header h3');
    expect(header.textContent).toContain('Matematik');
  });

  it('breadcrumb_TopicSelected_ShowsTopicNameInSubTopicsHeader', () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();

    component.selectSubject(1);
    component.selectTopic(10);
    fixture.detectChanges();

    const header: HTMLElement = fixture.nativeElement.querySelector('.column:nth-child(3) .column-header h3');
    expect(header.textContent).toContain('Kesirler');
  });

  // ── Empty state ───────────────────────────────────────────────────────────

  it('subjects_EmptyList_ShowsNoSubjectsEmptyState', () => {
    fixture = configure();
    adminService.getTaxonomy.and.returnValue(of({ subjects: [], grades: [] }));
    fixture.detectChanges();

    const emptyState = fixture.nativeElement.querySelector('.column:nth-child(1) .empty-state');
    expect(emptyState).toBeTruthy();
    expect(emptyState.textContent).toContain('Henüz ders eklenmemiş');
  });

  it('topics_NoSubjectSelected_ShowsSelectSubjectFirstEmptyState', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getTaxonomy.and.returnValue(of({ subjects: [], grades: [] }));
    fixture.detectChanges();

    const emptyState = fixture.nativeElement.querySelector('.column:nth-child(2) .empty-state');
    expect(emptyState).toBeTruthy();
    expect(emptyState.textContent).toContain('Önce bir ders seç');
  });

  it('subTopics_NoTopicSelected_ShowsSelectTopicFirstEmptyState', () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();
    component.selectSubject(1);
    fixture.detectChanges();

    const emptyState = fixture.nativeElement.querySelector('.column:nth-child(3) .empty-state');
    expect(emptyState).toBeTruthy();
    expect(emptyState.textContent).toContain('Önce bir konu seç');
  });

  // ── Hata durumu ───────────────────────────────────────────────────────────

  it('load_RequestFails_ShowsErrorBannerWithRetryButton', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getTaxonomy.and.returnValue(throwError(() => new Error('boom')));

    fixture.detectChanges();

    expect(component.error()).toBe('Taksonomi yüklenemedi');
    const errorBox: HTMLElement = fixture.nativeElement.querySelector('.state-box--error');
    expect(errorBox).toBeTruthy();
    expect(errorBox.querySelector('button')).toBeTruthy();
  });

  it('retryButtonClick_AfterLoadError_CallsLoadAgain', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getTaxonomy.and.returnValue(throwError(() => new Error('boom')));
    fixture.detectChanges();
    expect(adminService.getTaxonomy).toHaveBeenCalledTimes(1);

    adminService.getTaxonomy.and.returnValue(of(tree));
    const retry: HTMLButtonElement = fixture.nativeElement.querySelector('.state-box--error button');
    retry.click();
    fixture.detectChanges();

    expect(adminService.getTaxonomy).toHaveBeenCalledTimes(2);
    expect(component.error()).toBeNull();
  });

  it('loadSchools_RequestFails_ShowsSchoolsErrorBanner', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getSchools.and.returnValue(throwError(() => new Error('boom')));

    fixture.detectChanges();

    expect(component.schoolsError()).toBe('Okullar yüklenemedi');
    const errorBox: HTMLElement = fixture.nativeElement.querySelector('.schools-section .state-box--error');
    expect(errorBox).toBeTruthy();
    expect(errorBox.querySelector('button')).toBeTruthy();
  });

  it('schoolsRetryButtonClick_AfterLoadSchoolsError_CallsLoadSchoolsAgain', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getSchools.and.returnValue(throwError(() => new Error('boom')));
    fixture.detectChanges();
    expect(adminService.getSchools).toHaveBeenCalledTimes(1);

    adminService.getSchools.and.returnValue(of(schools));
    const retry: HTMLButtonElement = fixture.nativeElement.querySelector('.schools-section .state-box--error button');
    retry.click();
    fixture.detectChanges();

    expect(adminService.getSchools).toHaveBeenCalledTimes(2);
    expect(component.schoolsError()).toBeNull();
  });
});
