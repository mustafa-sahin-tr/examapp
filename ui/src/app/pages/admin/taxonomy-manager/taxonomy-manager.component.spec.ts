import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, Subject, throwError } from 'rxjs';

import { TaxonomyManagerComponent } from './taxonomy-manager.component';
import { AdminService } from '../../../services/admin.service';
import { GradesService } from '../../../services/grades.service';
import { ApiResult, TaxonomyFilter, TaxonomySubject, TaxonomyTree } from '../../../models/taxonomy';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import adminTr from '../../../../../public/i18n/admin/tr.json';

describe('TaxonomyManagerComponent', () => {
  let fixture: ComponentFixture<TaxonomyManagerComponent>;
  let component: TaxonomyManagerComponent;
  let adminService: jasmine.SpyObj<AdminService>;
  let gradesService: jasmine.SpyObj<GradesService>;
  let dialog: jasmine.SpyObj<MatDialog>;
  let snackBar: jasmine.SpyObj<MatSnackBar>;

  const okResult: ApiResult = { success: true, message: 'İşlem başarılı' };

  const subjects: TaxonomySubject[] = [
    {
      id: 1,
      name: 'Matematik',
      gradeIds: [5, 11],
      topics: [
        {
          id: 10,
          name: 'Kesirler',
          subjectId: 1,
          gradeId: 5,
          gradeName: '5. Sınıf',
          subTopics: [{ id: 100, name: 'Basit Kesirler', topicId: 10, questionCount: 3 }],
        },
        {
          id: 11,
          name: 'Türev',
          subjectId: 1,
          gradeId: 11,
          gradeName: '11. Sınıf',
          subTopics: [],
        },
      ],
    },
  ];

  const grades = [
    { id: 5, name: '5. Sınıf' },
    { id: 11, name: '11. Sınıf' },
  ];

  const physics: TaxonomySubject = { id: 2, name: 'Fizik', gradeIds: [11], topics: [] };

  /** Backend gibi: `gradeId` yalnız dersleri süzer (konular ders içinde tüm sınıflarla gelir). */
  const treesByGrade: Record<number, TaxonomyTree> = {
    5: { subjects, grades },
    11: { subjects: [...subjects, physics], grades },
  };
  const treeFor = (filter?: TaxonomyFilter): TaxonomyTree =>
    (filter?.gradeId != null ? treesByGrade[filter.gradeId] : undefined) ?? { subjects: [], grades };

  function configure(): ComponentFixture<TaxonomyManagerComponent> {
    adminService = jasmine.createSpyObj<AdminService>('AdminService', [
      'getTaxonomy',
      'createSubject',
      'updateSubject',
      'deleteSubject',
      'createTopic',
      'updateTopic',
      'deleteTopic',
      'createSubTopic',
      'updateSubTopic',
      'deleteSubTopic',
      'addSubjectGrade',
      'removeSubjectGrade',
    ]);
    adminService.getTaxonomy.and.callFake((filter?: TaxonomyFilter) => of(treeFor(filter)));

    gradesService = jasmine.createSpyObj<GradesService>('GradesService', ['getGrades']);
    gradesService.getGrades.and.returnValue(of(grades));

    dialog = jasmine.createSpyObj<MatDialog>('MatDialog', ['open']);
    dialog.open.and.returnValue({ afterClosed: () => of(true) } as any);

    snackBar = jasmine.createSpyObj<MatSnackBar>('MatSnackBar', ['open']);

    TestBed.configureTestingModule({
      imports: [TaxonomyManagerComponent, translocoTestingModule({
        langs: { 'admin/tr': adminTr },
        // app.config.ts ile aynı: tireli scope önekleri camelCase'e çevrilmez.
        translocoConfig: { scopes: { keepCasing: true } },
      })],
      providers: [
        { provide: AdminService, useValue: adminService },
        { provide: GradesService, useValue: gradesService },
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

  /** Issue #151: ilk açılışta sınıf seçili değil; CRUD testleri önce bir sınıf seçer. */
  function configureWithGrade(gradeId = 5): ComponentFixture<TaxonomyManagerComponent> {
    const f = configure();
    f.detectChanges();
    f.componentInstance.setGradeFilter(gradeId);
    f.detectChanges();
    return f;
  }

  // ── CRUD çağrıları ────────────────────────────────────────────────────────

  it('addSubject_ValidName_CallsCreateSubjectWithTrimmedNameLinkedToSelectedGrade', async () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;

    adminService.createSubject.and.returnValue(of(okResult));
    component.newSubjectName = '  Fizik  ';

    await component.addSubject();

    expect(adminService.createSubject).toHaveBeenCalledOnceWith({ name: 'Fizik', gradeIds: [5] });
    expect(component.newSubjectName).toBe('');
  });

  it('addTopic_ValidName_CallsCreateTopicWithSelectedSubjectAndFilterGrade', async () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;
    component.selectSubject(1);

    adminService.createTopic.and.returnValue(of(okResult));
    component.newTopicName = '  Geometri ';

    await component.addTopic();

    expect(adminService.createTopic).toHaveBeenCalledOnceWith({
      name: 'Geometri',
      subjectId: 1,
      gradeId: 5,
    });
    expect(component.newTopicName).toBe('');
  });

  it('addTopic_OtherGradeSelected_UsesThatGradeId', async () => {
    fixture = configureWithGrade(11);
    component = fixture.componentInstance;
    component.selectSubject(1);

    adminService.createTopic.and.returnValue(of(okResult));
    component.newTopicName = 'İntegral';

    await component.addTopic();

    expect(adminService.createTopic).toHaveBeenCalledOnceWith({
      name: 'İntegral',
      subjectId: 1,
      gradeId: 11,
    });
  });

  it('addTopic_BlankName_DoesNotCallCreateTopicAndShowsNameRequired', async () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;
    component.selectSubject(1);

    component.newTopicName = '   ';

    await component.addTopic();

    expect(adminService.createTopic).not.toHaveBeenCalled();
    expect(snackBar.open).toHaveBeenCalledWith('Konu adı gerekli', jasmine.any(String), jasmine.any(Object));
  });

  it('addTopicForm_SubjectSelected_HasNoGradeSelect', () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;
    component.selectSubject(1);
    fixture.detectChanges();

    const topicsColumn: HTMLElement = fixture.nativeElement.querySelector('.column:nth-child(2)');
    expect(topicsColumn.querySelector('.add-row input')).toBeTruthy();
    expect(topicsColumn.querySelector('mat-select')).toBeNull();
    expect(fixture.nativeElement.querySelector('mat-select')).toBeNull();
  });

  it('addSubTopic_ValidName_CallsCreateSubTopicWithSelectedTopic', async () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;
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

  it('remove_ConfirmDialogAccepted_CallsDeleteSubject', async () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;

    adminService.deleteSubject.and.returnValue(of(okResult));

    await component.remove('subject', { id: 1, name: 'Matematik' });

    expect(dialog.open).toHaveBeenCalled();
    expect(adminService.deleteSubject).toHaveBeenCalledOnceWith(1);
  });

  it('remove_ConfirmDialogRejected_DoesNotCallDelete', async () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;
    dialog.open.and.returnValue({ afterClosed: () => of(false) } as any);

    await component.remove('subject', { id: 1, name: 'Matematik' });

    expect(adminService.deleteSubject).not.toHaveBeenCalled();
  });

  it('remove_TopicLevel_CallsDeleteTopic', async () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;
    adminService.deleteTopic.and.returnValue(of(okResult));

    await component.remove('topic', { id: 10, name: 'Kesirler' });

    expect(adminService.deleteTopic).toHaveBeenCalledOnceWith(10);
  });

  it('remove_SubTopicLevel_CallsDeleteSubTopic', async () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;
    adminService.deleteSubTopic.and.returnValue(of(okResult));

    await component.remove('subtopic', { id: 100, name: 'Basit Kesirler' });

    expect(adminService.deleteSubTopic).toHaveBeenCalledOnceWith(100);
  });

  it('saveEdit_SubjectLevel_CallsUpdateSubjectWithTrimmedName', async () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;
    adminService.updateSubject.and.returnValue(of(okResult));

    component.startEdit('subject', subjects[0]);
    component.editName = '  Matematik 2 ';

    await component.saveEdit('subject', subjects[0]);

    expect(adminService.updateSubject).toHaveBeenCalledOnceWith(1, { name: 'Matematik 2' });
    expect(component.editing()).toBeNull();
  });

  it('saveEdit_TopicLevel_CallsUpdateTopicWithTopicsExistingGradeAndSubject', async () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;
    adminService.updateTopic.and.returnValue(of(okResult));

    const topic = subjects[0].topics[0];
    component.startEdit('topic', topic);
    component.editName = 'Kesirler 2';

    await component.saveEdit('topic', topic);

    expect(adminService.updateTopic).toHaveBeenCalledOnceWith(10, {
      name: 'Kesirler 2',
      subjectId: 1,
      gradeId: 5,
    });
  });

  it('topicEditRow_Editing_HasNoGradeSelect', () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;
    component.selectSubject(1);
    component.startEdit('topic', subjects[0].topics[0]);
    fixture.detectChanges();

    const editRow: HTMLElement = fixture.nativeElement.querySelector('.column:nth-child(2) .edit-row');
    expect(editRow).toBeTruthy();
    expect(editRow.querySelector('input')).toBeTruthy();
    expect(editRow.querySelector('mat-select')).toBeNull();
  });

  it('saveEdit_SubTopicLevel_CallsUpdateSubTopicWithSelectedTopicId', async () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;
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

  // ── Sınıf filtresi (Issue #119, #151) ────────────────────────────────────

  it('init_NoGradeSelected_LoadsOnlyGradesAndShowsSelectGradeEmptyState', () => {
    fixture = configure();
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.selectedGradeFilter()).toBeNull();
    expect(gradesService.getGrades).toHaveBeenCalledTimes(1);
    expect(adminService.getTaxonomy).not.toHaveBeenCalled();
    expect(component.subjects()).toEqual([]);

    const subjectsColumn: HTMLElement = fixture.nativeElement.querySelector('.column:nth-child(1)');
    expect(subjectsColumn.querySelector('.empty-state')?.textContent).toContain(
      'Devam etmek için bir sınıf seç.'
    );
    // Sınıf belirsizken ekleme formu ve liste yok.
    expect(subjectsColumn.querySelector('.add-row')).toBeNull();
    expect(subjectsColumn.querySelector('.list')).toBeNull();
  });

  it('gradeFilter_Render_ShowsOnlyGradeOptionsWithoutAllOrUnassigned', () => {
    fixture = configure();
    fixture.detectChanges();

    const toggles: HTMLElement[] = Array.from(
      fixture.nativeElement.querySelectorAll('.grade-filter mat-button-toggle')
    );
    expect(toggles.map((t) => t.textContent?.trim())).toEqual(['5. Sınıf', '11. Sınıf']);
    const text = (fixture.nativeElement.querySelector('.grade-filter') as HTMLElement).textContent;
    expect(text).not.toContain('Tüm Sınıflar');
    expect(text).not.toContain('Sınıf atanmamış');
    expect(fixture.nativeElement.querySelector('.unassigned-toggle')).toBeNull();
  });

  it('setGradeFilter_FirstSelection_CallsGetTaxonomyWithGradeId', () => {
    fixture = configureWithGrade();

    expect(adminService.getTaxonomy).toHaveBeenCalledOnceWith({ gradeId: 5 });
  });

  it('setGradeFilter_OtherGrade_LoadsWithGradeIdAndResetsSelections', () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;
    component.selectSubject(1);
    component.selectTopic(10);

    component.setGradeFilter(11);

    expect(adminService.getTaxonomy).toHaveBeenCalledWith({ gradeId: 11 });
    expect(adminService.getTaxonomy).toHaveBeenCalledTimes(2);
    expect(component.selectedGradeFilter()).toBe(11);
    expect(component.selectedSubjectId()).toBeNull();
    expect(component.selectedTopicId()).toBeNull();
  });

  it('setGradeFilter_SameValue_DoesNotReload', () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;

    component.setGradeFilter(5);

    expect(adminService.getTaxonomy).toHaveBeenCalledTimes(1);
  });

  it('setGradeFilter_NullOrUndefined_Ignored', () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;

    component.setGradeFilter(null);
    component.setGradeFilter(undefined);

    expect(component.selectedGradeFilter()).toBe(5);
    expect(adminService.getTaxonomy).toHaveBeenCalledTimes(1);
  });

  it('topics_GradeSelected_ShowsOnlyTopicsOfThatGrade', () => {
    fixture = configureWithGrade(5);
    component = fixture.componentInstance;
    component.selectSubject(1);

    expect(component.topics().map((t) => t.id)).toEqual([10]);

    component.setGradeFilter(11);
    component.selectSubject(1);

    expect(component.topics().map((t) => t.id)).toEqual([11]);
  });

  it('setGradeFilter_OtherGrade_ListsThatGradesSubjects', () => {
    fixture = configureWithGrade(5);
    component = fixture.componentInstance;
    expect(component.subjects().map((x) => x.id)).toEqual([1]);

    component.setGradeFilter(11);

    expect(component.subjects().map((x) => x.id)).toEqual([1, 2]);
  });

  it('setGradeFilter_BeforeResponse_ClearsSubjectsSelectionsAndDrafts', () => {
    fixture = configureWithGrade(5);
    component = fixture.componentInstance;
    component.selectSubject(1);
    component.selectTopic(10);
    component.newTopicName = 'Taslak';
    component.startEdit('topic', subjects[0].topics[0]);

    const pending = new Subject<TaxonomyTree>();
    adminService.getTaxonomy.and.returnValue(pending.asObservable());
    component.setGradeFilter(11);

    // Yanıt gelmeden eski sınıfın verisi temizlenmiş olmalı.
    expect(component.subjects()).toEqual([]);
    expect(component.selectedSubjectId()).toBeNull();
    expect(component.selectedTopicId()).toBeNull();
    expect(component.newTopicName).toBe('');
    expect(component.editing()).toBeNull();
  });

  it('setGradeFilter_RequestFails_LeavesSubjectsEmpty', () => {
    fixture = configureWithGrade(5);
    component = fixture.componentInstance;
    component.selectSubject(1);
    expect(component.subjects().length).toBe(1);

    adminService.getTaxonomy.and.returnValue(throwError(() => new Error('boom')));
    component.setGradeFilter(11);
    fixture.detectChanges();

    expect(component.subjects()).toEqual([]);
    expect(component.selectedSubject()).toBeNull();
    expect(component.error()).toBe('Taksonomi yüklenemedi');
  });

  it('gradeFilter_LoadingOrBusy_ToggleGroupDisabled', () => {
    fixture = configureWithGrade(5);
    component = fixture.componentInstance;
    const group = (): HTMLElement => fixture.nativeElement.querySelector('.grade-filter mat-button-toggle-group');
    const firstToggleButton = (): HTMLButtonElement =>
      fixture.nativeElement.querySelector('.grade-filter mat-button-toggle button');

    expect(firstToggleButton().disabled).toBeFalse();

    component.busy.set(true);
    fixture.detectChanges();
    expect(firstToggleButton().disabled).toBeTrue();
    expect(group()).toBeTruthy();

    component.busy.set(false);
    component.loading.set(true);
    fixture.detectChanges();
    expect(firstToggleButton().disabled).toBeTrue();
  });

  it('topicRow_Render_DoesNotShowGradeNameMeta', () => {
    fixture = configureWithGrade(5);
    component = fixture.componentInstance;
    component.selectSubject(1);
    fixture.detectChanges();

    const meta: HTMLElement = fixture.nativeElement.querySelector('.column:nth-child(2) .item .meta');
    expect(meta.textContent?.trim()).toBe('1 alt');
  });

  it('subjectRow_WithGrades_ShowsGradeNameChips', () => {
    fixture = configureWithGrade();

    const chips: NodeListOf<HTMLElement> = fixture.nativeElement.querySelectorAll(
      '.column:nth-child(1) .grade-chip'
    );
    expect(Array.from(chips).map((c) => c.textContent?.trim())).toEqual(['5. Sınıf', '11. Sınıf']);
  });

  it('manageGrades_DialogReportsChange_ReloadsTaxonomy', async () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;
    dialog.open.and.returnValue({ afterClosed: () => of(true) } as any);

    await component.manageGrades(subjects[0]);

    expect(dialog.open).toHaveBeenCalled();
    expect(adminService.getTaxonomy).toHaveBeenCalledTimes(2);
  });

  it('manageGrades_OpensDialog_WithDisableCloseSoPartialSuccessIsNotLost', async () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;

    await component.manageGrades(subjects[0]);

    // ESC/backdrop `close()`'u argümansız çağırır; `applied` bilgisi kaybolmasın diye kapalı olmalı.
    const config = dialog.open.calls.mostRecent().args[1];
    expect(config?.disableClose).toBeTrue();
  });

  it('manageGrades_DialogReportsNoChange_DoesNotReload', async () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;
    dialog.open.and.returnValue({ afterClosed: () => of(false) } as any);

    await component.manageGrades(subjects[0]);

    expect(adminService.getTaxonomy).toHaveBeenCalledTimes(1);
  });

  // ── Breadcrumb ────────────────────────────────────────────────────────────

  it('breadcrumb_NoGradeSelected_NotRendered', () => {
    fixture = configure();
    fixture.detectChanges();

    const crumbs = fixture.nativeElement.querySelectorAll('.crumb');
    expect(crumbs.length).toBe(0);
  });

  it('breadcrumb_SubjectSelected_ShowsSubjectNameInTopicsHeader', () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;

    component.selectSubject(1);
    fixture.detectChanges();

    const header: HTMLElement = fixture.nativeElement.querySelector('.column:nth-child(2) .column-header h3');
    expect(header.textContent).toContain('Matematik');
  });

  it('breadcrumb_TopicSelected_ShowsTopicNameInSubTopicsHeader', () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;

    component.selectSubject(1);
    component.selectTopic(10);
    fixture.detectChanges();

    const header: HTMLElement = fixture.nativeElement.querySelector('.column:nth-child(3) .column-header h3');
    expect(header.textContent).toContain('Kesirler');
  });

  // ── Empty state ───────────────────────────────────────────────────────────

  it('breadcrumb_GradeSelected_ShowsGradeNameInSubjectsHeader', () => {
    fixture = configureWithGrade(11);

    const header: HTMLElement = fixture.nativeElement.querySelector('.column:nth-child(1) .column-header h3');
    expect(header.textContent).toContain('11. Sınıf');
  });

  it('subjects_GradeSelectedButEmpty_ShowsNoSubjectsForGradeEmptyState', () => {
    fixture = configure();
    adminService.getTaxonomy.and.returnValue(of({ subjects: [], grades }));
    fixture.detectChanges();
    fixture.componentInstance.setGradeFilter(5);
    fixture.detectChanges();

    const emptyState = fixture.nativeElement.querySelector('.column:nth-child(1) .empty-state');
    expect(emptyState).toBeTruthy();
    expect(emptyState.textContent).toContain('Bu sınıfa bağlı ders yok');
    // Sınıf seçiliyken ders ekleme formu görünür.
    expect(fixture.nativeElement.querySelector('.column:nth-child(1) .add-row')).toBeTruthy();
  });

  it('topics_NoSubjectSelected_ShowsSelectSubjectFirstEmptyState', () => {
    fixture = configure();
    fixture.detectChanges();

    const emptyState = fixture.nativeElement.querySelector('.column:nth-child(2) .empty-state');
    expect(emptyState).toBeTruthy();
    expect(emptyState.textContent).toContain('Önce bir ders seç');
  });

  it('subTopics_NoTopicSelected_ShowsSelectTopicFirstEmptyState', () => {
    fixture = configureWithGrade();
    component = fixture.componentInstance;
    component.selectSubject(1);
    fixture.detectChanges();

    const emptyState = fixture.nativeElement.querySelector('.column:nth-child(3) .empty-state');
    expect(emptyState).toBeTruthy();
    expect(emptyState.textContent).toContain('Önce bir konu seç');
  });

  // ── Hata durumu ───────────────────────────────────────────────────────────

  it('loadGrades_RequestFails_ShowsErrorAndRetryReloadsGrades', () => {
    fixture = configure();
    component = fixture.componentInstance;
    gradesService.getGrades.and.returnValue(throwError(() => new Error('boom')));
    fixture.detectChanges();

    expect(component.error()).toBe('Sınıflar yüklenemedi');

    gradesService.getGrades.and.returnValue(of(grades));
    const retry: HTMLButtonElement = fixture.nativeElement.querySelector('.state-box--error button');
    retry.click();
    fixture.detectChanges();

    expect(gradesService.getGrades).toHaveBeenCalledTimes(2);
    expect(adminService.getTaxonomy).not.toHaveBeenCalled();
    expect(component.error()).toBeNull();
  });

  it('load_RequestFails_ShowsErrorBannerWithRetryButton', () => {
    fixture = configure();
    component = fixture.componentInstance;
    adminService.getTaxonomy.and.returnValue(throwError(() => new Error('boom')));
    fixture.detectChanges();
    component.setGradeFilter(5);
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
    component.setGradeFilter(5);
    fixture.detectChanges();
    expect(adminService.getTaxonomy).toHaveBeenCalledTimes(1);

    adminService.getTaxonomy.and.callFake((filter?: TaxonomyFilter) => of(treeFor(filter)));
    const retry: HTMLButtonElement = fixture.nativeElement.querySelector('.state-box--error button');
    retry.click();
    fixture.detectChanges();

    expect(adminService.getTaxonomy).toHaveBeenCalledTimes(2);
    expect(adminService.getTaxonomy).toHaveBeenCalledWith({ gradeId: 5 });
    expect(component.error()).toBeNull();
  });


  // ── Okul bölümü (Issue #150: SchoolManagerComponent'e taşındı) ────────────

  it('render_AfterSchoolsMovedOut_DoesNotRenderSchoolsSection', () => {
    fixture = configure();
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.schools-section')).toBeNull();
    expect(el.querySelector('.schools-list')).toBeNull();
    expect(el.textContent).not.toContain('Okullar');
    expect(el.textContent).not.toContain('Yeni okul adı');
  });
});
