import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { BadgeDefinitionDialogComponent, BadgeDefinitionDialogData } from './badge-definition-dialog.component';
import { BadgeDefinitionAdminService } from '../../../../services/badge-definition-admin.service';
import { SubjectService } from '../../../../services/subject.service';
import { BadgeDefinitionAdmin, BadgeRuleTypeSchema } from '../../../../models/badge-definition-admin.model';
import { translocoTestingModule } from '../../../../shared/testing/transloco-testing';
import adminTr from '../../../../../../public/i18n/admin/tr.json';

const target = { name: 'target', type: 'integer', required: true, min: 1, max: 1_000_000, allowedValues: null };
const RULE_TYPES: BadgeRuleTypeSchema[] = [
  { ruleType: 'AnswerCount', description: 'Toplam çözülen soru sayısı.', fields: [target] },
  {
    ruleType: 'SubjectAnswerCount',
    description: 'Belirli bir derste çözülen soru sayısı.',
    fields: [
      target,
      { name: 'subjectId', type: 'integer', required: false, min: 1, max: null, allowedValues: null },
      { name: 'subjectName', type: 'string', required: false, min: null, max: null, allowedValues: null },
    ],
  },
];
const SUBJECTS = [
  { id: 3, name: 'Matematik' },
  { id: 5, name: 'Türkçe' },
];

function dto(overrides: Partial<BadgeDefinitionAdmin> = {}): BadgeDefinitionAdmin {
  return {
    id: 'b1',
    code: 'subject-matematik-mastery',
    name: 'Matematik Ustası',
    description: 'desc',
    iconUrl: 'achievements/m.svg',
    category: 'Ders Bazlı',
    ruleType: 'SubjectAnswerCount',
    ruleConfigJson: '{"target":100,"subjectName":"Matematik"}',
    pathKey: 'subject-matematik-answers',
    pathName: 'Matematik Yolculuğu',
    pathOrder: 1,
    isActive: true,
    createdBy: 'sub-1',
    createdByName: 'admin',
    createdAtUtc: '2026-09-01T10:00:00Z',
    updatedBy: null,
    updatedByName: null,
    updatedAtUtc: null,
    ...overrides,
  };
}

describe('BadgeDefinitionDialogComponent', () => {
  let fixture: ComponentFixture<BadgeDefinitionDialogComponent>;
  let component: BadgeDefinitionDialogComponent;
  let service: jasmine.SpyObj<BadgeDefinitionAdminService>;
  let subjects: jasmine.SpyObj<SubjectService>;
  let dialogRef: jasmine.SpyObj<MatDialogRef<BadgeDefinitionDialogComponent, BadgeDefinitionAdmin>>;

  function init(data: BadgeDefinitionDialogData = {}): void {
    service = jasmine.createSpyObj<BadgeDefinitionAdminService>('BadgeDefinitionAdminService', [
      'getRuleTypes',
      'create',
      'update',
    ]);
    service.getRuleTypes.and.returnValue(of(RULE_TYPES));
    subjects = jasmine.createSpyObj<SubjectService>('SubjectService', ['loadCategories']);
    subjects.loadCategories.and.returnValue(of(SUBJECTS));
    dialogRef = jasmine.createSpyObj('MatDialogRef', ['close']);

    TestBed.configureTestingModule({
      imports: [BadgeDefinitionDialogComponent, translocoTestingModule({ langs: { 'admin/tr': adminTr } })],
      providers: [
        { provide: BadgeDefinitionAdminService, useValue: service },
        { provide: SubjectService, useValue: subjects },
        { provide: MatDialogRef, useValue: dialogRef },
        { provide: MAT_DIALOG_DATA, useValue: data },
        provideNoopAnimations(),
      ],
    });
    fixture = TestBed.createComponent(BadgeDefinitionDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function fillCreate(): void {
    component.form.patchValue({ code: 'question-hunter-9', name: ' Soru Avcısı ', category: 'Çözüm' });
    component.form.controls.ruleType.setValue('AnswerCount');
    component.ruleForm.controls['target'].setValue(25);
    // Kural girdileri render edilsin (gerçek kullanımda kaydetmeden önce zaten ekrandadır).
    fixture.detectChanges();
  }

  function el(testId: string): HTMLElement | null {
    return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
  }

  // ── dinamik kural formu ─────────────────────────────────────────────────

  it('ruleType_Simple_BuildsTargetControlWithSchemaRange', () => {
    init();
    component.form.controls.ruleType.setValue('AnswerCount');
    fixture.detectChanges();

    expect(Object.keys(component.ruleForm.controls)).toEqual(['target']);
    const ctrl = component.ruleForm.controls['target'];
    ctrl.setValue(0);
    expect(ctrl.hasError('min')).toBeTrue();
    ctrl.setValue(1_000_001);
    expect(ctrl.hasError('max')).toBeTrue();
    ctrl.setValue(null);
    expect(ctrl.hasError('required')).toBeTrue();
    expect(el('rule-target')).withContext('target input render edilmeli').toBeTruthy();
    expect(el('rule-subject')).toBeNull();
  });

  it('ruleType_Subject_BuildsSubjectControlsAndSelector_KeepsTarget', () => {
    init();
    component.form.controls.ruleType.setValue('AnswerCount');
    component.ruleForm.controls['target'].setValue(40);
    component.form.controls.ruleType.setValue('SubjectAnswerCount');
    fixture.detectChanges();

    expect(Object.keys(component.ruleForm.controls)).toEqual(['target', 'subjectId', 'subjectName']);
    expect(component.ruleForm.controls['target'].value).toBe(40);
    expect(el('rule-subject')).toBeTruthy();
    expect(el('rule-subjectId')).withContext('ders alanları tek tek render edilmez').toBeNull();
    expect(component.ruleForm.hasError('subjectRequired')).toBeTrue();
  });

  it('selectSubject_SetsBothSubjectIdAndSubjectName', () => {
    init();
    component.form.controls.ruleType.setValue('SubjectAnswerCount');

    component.selectSubject(5);

    expect(component.ruleForm.controls['subjectId'].value).toBe(5);
    expect(component.ruleForm.controls['subjectName'].value).toBe('Türkçe');
    expect(component.ruleForm.valid).toBeFalse(); // target henüz boş
    component.ruleForm.controls['target'].setValue(10);
    expect(component.ruleForm.valid).toBeTrue();
  });

  it('save_Create_SendsTrimmedBodyAndSchemaOnlyRuleConfigJson', () => {
    init();
    const saved = dto({ id: 'new' });
    service.create.and.returnValue(of(saved));
    fillCreate();
    component.form.controls.ruleType.setValue('SubjectAnswerCount');
    component.selectSubject(3);

    component.save();

    expect(service.create).toHaveBeenCalledOnceWith({
      code: 'question-hunter-9',
      name: 'Soru Avcısı',
      description: '',
      iconUrl: null,
      category: 'Çözüm',
      ruleType: 'SubjectAnswerCount',
      ruleConfigJson: '{"target":25,"subjectId":3,"subjectName":"Matematik"}',
      pathKey: null,
      pathName: null,
      pathOrder: null,
    });
    expect(dialogRef.close).toHaveBeenCalledOnceWith(saved);
  });

  it('save_SwitchFromSubjectToSimple_DropsSubjectFieldsFromJson', () => {
    init();
    service.create.and.returnValue(of(dto()));
    fillCreate();
    component.form.controls.ruleType.setValue('SubjectAnswerCount');
    component.selectSubject(3);
    component.form.controls.ruleType.setValue('AnswerCount');

    component.save();

    expect(service.create.calls.mostRecent().args[0].ruleConfigJson).toBe('{"target":25}');
  });

  it('save_Invalid_DoesNotCallApi', () => {
    init();
    component.form.patchValue({ code: 'Bad Code', name: 'x', category: 'c' });
    component.form.controls.ruleType.setValue('AnswerCount');
    component.ruleForm.controls['target'].setValue(5);

    component.save();

    expect(component.form.controls.code.hasError('pattern')).toBeTrue();
    expect(service.create).not.toHaveBeenCalled();
  });

  // ── düzenleme ───────────────────────────────────────────────────────────

  it('edit_PrefillsForm_MatchesSeededSubjectNameToId_CodeDisabled', () => {
    init({ definition: dto() });

    expect(component.form.controls.code.disabled).toBeTrue();
    expect(component.form.controls.ruleType.value).toBe('SubjectAnswerCount');
    expect(component.ruleForm.getRawValue()).toEqual({ target: 100, subjectId: 3, subjectName: 'Matematik' });
    expect(component.unmatchedSubjectName()).toBeNull();
  });

  it('edit_UnknownSubjectName_KeepsNameAndShowsHint', () => {
    init({ definition: dto({ ruleConfigJson: '{"target":7,"subjectName":"Felsefe"}' }) });

    expect(component.ruleForm.getRawValue()).toEqual({ target: 7, subjectId: null, subjectName: 'Felsefe' });
    expect(component.unmatchedSubjectName()).toBe('Felsefe');
    expect(component.ruleForm.valid).toBeTrue();
  });

  it('save_Edit_PutsWithoutCode', () => {
    init({ definition: dto() });
    service.update.and.returnValue(of(dto()));

    component.save();

    const [id, body] = service.update.calls.mostRecent().args;
    expect(id).toBe('b1');
    expect('code' in body).toBeFalse();
    expect(body.ruleConfigJson).toBe('{"target":100,"subjectId":3,"subjectName":"Matematik"}');
    expect(body.pathOrder).toBe(1);
  });

  // ── sunucu hataları ─────────────────────────────────────────────────────

  it('save_400_MapsFieldErrorsToControls_RuleConfigJsonToRuleSection', () => {
    init();
    fillCreate();
    service.create.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: {
              errors: {
                name: ['name zorunludur.'],
                iconUrl: ['iconUrl biçimi'],
                target: ['target 1 ile 1000000 arasında olmalıdır.'],
                ruleConfigJson: ['RuleConfigJson geçersiz.'],
              },
            },
          })
      )
    );

    component.save();
    fixture.detectChanges();

    expect(component.form.controls.name.getError('server')).toBe('name zorunludur.');
    expect(component.form.controls.iconUrl.getError('server')).toBe('iconUrl biçimi');
    expect(component.ruleForm.controls['target'].getError('server')).toBe('target 1 ile 1000000 arasında olmalıdır.');
    expect(component.ruleError()).toBe('RuleConfigJson geçersiz.');
    expect(el('rule-error')?.textContent).toContain('RuleConfigJson geçersiz.');
    expect(dialogRef.close).not.toHaveBeenCalled();
    expect(component.submitting()).toBeFalse();
  });

  it('save_400_SubjectError_ShownUnderSubjectSelector', () => {
    init();
    fillCreate();
    component.form.controls.ruleType.setValue('SubjectAnswerCount');
    component.selectSubject(3);
    fixture.detectChanges();
    service.create.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { errors: { subjectId: ['subjectId pozitif'] } } }))
    );

    component.save();
    fixture.detectChanges();

    expect(el('subject-error')?.textContent).toContain('subjectId pozitif');
  });

  it('save_409Code_MapsToCodeControl', () => {
    init();
    fillCreate();
    service.create.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { errors: { code: ["'x' koduna sahip bir rozet zaten var."] } } }))
    );

    component.save();

    expect(component.form.controls.code.getError('server')).toBe("'x' koduna sahip bir rozet zaten var.");
    expect(component.error()).toBeNull();
  });

  it('save_409ActiveCap_ShowsGeneralError_NotOnCode', () => {
    init();
    fillCreate();
    service.create.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { errors: { isActive: ['Aktif rozet sayısı üst sınıra ulaştı.'] } } }))
    );

    component.save();

    expect(component.form.controls.code.hasError('server')).toBeFalse();
    expect(component.error()).toBe('Aktif rozet sayısı üst sınıra ulaştı.');
  });

  it('save_500_ShowsGenericError', () => {
    init();
    fillCreate();
    service.create.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    component.save();

    expect(component.error()).toBe(adminTr.badgeDefinitions.dialog.errors.saveFailed);
  });

  // ── yükleme durumları ───────────────────────────────────────────────────

  it('ruleTypesError_ShowsRetry_AndDisablesSave', () => {
    init();
    component.ruleTypesError.set(true);
    fixture.detectChanges();

    const save = el('save') as HTMLButtonElement;
    expect(save.disabled).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain(adminTr.badgeDefinitions.dialog.ruleTypesFailed);
  });
});
