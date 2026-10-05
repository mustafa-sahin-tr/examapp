import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { BadgeDefinitionDialogComponent, BadgeDefinitionDialogData } from './badge-definition-dialog.component';
import { BadgeDefinitionAdminService } from '../../../../services/badge-definition-admin.service';
import { SubjectService } from '../../../../services/subject.service';
import { BadgeDefinitionAdmin, BadgeIconEntry, BadgeRuleTypeSchema } from '../../../../models/badge-definition-admin.model';
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
/** 10 ikon: 8 sütunlu ızgarada iki satır (Aşağı ok testi için). */
const ICONS: BadgeIconEntry[] = [
  { name: 'flag', category: 'achievement' },
  { name: 'done_all', category: 'achievement' },
  { name: 'gps_fixed', category: 'achievement' },
  { name: 'psychology', category: 'learning' },
  { name: 'school', category: 'learning' },
  { name: 'schedule', category: 'time' },
  { name: 'timer', category: 'time' },
  { name: 'local_fire_department', category: 'streak' },
  { name: 'event_available', category: 'streak' },
  { name: 'visibility', category: 'other' },
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
    icon: null,
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
      'getIcons',
      'create',
      'update',
    ]);
    service.getRuleTypes.and.returnValue(of(RULE_TYPES));
    service.getIcons.and.returnValue(of(ICONS));
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
      icon: null,
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

  it('edit_LegacyAliasConfig_PrefillsTarget_AndSavesCanonicalTarget', () => {
    init({ definition: dto({ ruleType: 'AnswerCount', ruleConfigJson: '{"count":75}' }) });
    service.update.and.returnValue(of(dto()));

    expect(component.ruleForm.getRawValue()).toEqual({ target: 75 });
    component.save();
    expect(service.update.calls.mostRecent().args[1].ruleConfigJson).toBe('{"target":75}');
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
                icon: ["'nope' izinli ikon listesinde değil."],
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
    expect(component.iconError()).toBe("'nope' izinli ikon listesinde değil.");
    expect(el('icon-error')?.getAttribute('role')).toBe('alert');
    expect(el('icon-error')?.textContent).toContain("'nope' izinli ikon listesinde değil.");
    expect(component.error()).withContext('icon hatası genel hataya düşmez').toBeNull();
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

  // ── ikon seçici (issue #149) ────────────────────────────────────────────

  function iconButtons(): HTMLButtonElement[] {
    return [...fixture.nativeElement.querySelectorAll('[data-testid="icon-grid"] [role="radio"]')];
  }

  function keydown(target: HTMLElement, key: string): void {
    target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }

  it('icons_LoadedOnceFromService_RenderedAsRadiogroupWithRovingTabindex', () => {
    init();

    expect(service.getIcons).toHaveBeenCalledTimes(1);
    expect(el('icon-url')).withContext('serbest metin ikon alanı kalktı').toBeNull();
    const grid = el('icon-grid');
    expect(grid?.getAttribute('role')).toBe('radiogroup');
    const buttons = iconButtons();
    expect(buttons.length).toBe(ICONS.length);
    expect(buttons.every((b) => b.getAttribute('aria-checked') === 'false')).toBeTrue();
    expect(buttons.map((b) => b.tabIndex)).toEqual([0, -1, -1, -1, -1, -1, -1, -1, -1, -1]);
    expect(buttons[3].getAttribute('aria-label')).toBe(
      `psychology, ${adminTr.badgeDefinitions.dialog.icon.categories.learning}`
    );
    expect(el('icon-selected-none')).toBeTruthy();
  });

  it('icons_Click_SelectsAndMarksAriaChecked_ShowsSelectedName', () => {
    init();

    iconButtons()[2].click();
    fixture.detectChanges();

    expect(component.selectedIcon()).toBe('gps_fixed');
    const buttons = iconButtons();
    expect(buttons[2].getAttribute('aria-checked')).toBe('true');
    expect(buttons[2].tabIndex).withContext('seçili ikon Tab durağı').toBe(0);
    expect(buttons[0].tabIndex).toBe(-1);
    expect(el('icon-selected')?.textContent?.trim()).toBe('gps_fixed');
  });

  it('icons_Search_FiltersIgnoringCaseAndUnderscore', () => {
    init();
    const input = el('icon-search') as HTMLInputElement;

    input.value = 'FIRE dep';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(iconButtons().map((b) => b.getAttribute('data-icon'))).toEqual(['local_fire_department']);
  });

  it('icons_SearchWithoutResults_ShowsInfoNote_NotError', () => {
    init();
    component.iconQuery.set('zzz');
    fixture.detectChanges();

    expect(el('icon-grid')).toBeNull();
    const note = el('icons-no-results');
    expect(note?.getAttribute('role')).toBe('status');
    expect(note?.textContent).toContain('"zzz"');
  });

  it('icons_CategoryChip_FiltersGrid_AllRestores', () => {
    init();

    // Tıklanabilir öğe chip'in iç eylem düğmesi (role=option).
    (el('icon-category-time')?.querySelector('[role="option"]') as HTMLElement).click();
    fixture.detectChanges();
    expect(component.iconCategory()).toBe('time');
    expect(iconButtons().map((b) => b.getAttribute('data-icon'))).toEqual(['schedule', 'timer']);

    component.setIconCategory('all');
    fixture.detectChanges();
    expect(iconButtons().length).toBe(ICONS.length);
  });

  it('icons_CategoryAndSearchCombine_UnknownCategoryCountsAsOther', () => {
    init();
    component.icons.set([...ICONS, { name: 'extension', category: 'mystery' }]);
    component.setIconCategory('other');
    fixture.detectChanges();
    expect(iconButtons().map((b) => b.getAttribute('data-icon'))).toEqual(['visibility', 'extension']);

    component.iconQuery.set('ext');
    fixture.detectChanges();
    expect(iconButtons().map((b) => b.getAttribute('data-icon'))).toEqual(['extension']);
  });

  it('icons_ArrowKeys_MoveFocusAndSelect_UpDownJumpEightColumns_ClampAtEdges', () => {
    init();
    fixture.autoDetectChanges(true);
    let buttons = iconButtons();
    buttons[0].focus();

    keydown(buttons[0], 'ArrowRight');
    expect(component.selectedIcon()).toBe('done_all');
    expect(document.activeElement).toBe(iconButtons()[1]);

    keydown(iconButtons()[1], 'ArrowDown');
    expect(component.selectedIcon()).withContext('+8').toBe('visibility');
    expect(document.activeElement).toBe(iconButtons()[9]);

    keydown(iconButtons()[9], 'ArrowDown');
    expect(component.selectedIcon()).withContext('son öğede durur').toBe('visibility');

    keydown(iconButtons()[9], 'ArrowUp');
    expect(component.selectedIcon()).toBe('done_all');

    keydown(iconButtons()[1], 'Home');
    expect(component.selectedIcon()).toBe('flag');
    keydown(iconButtons()[0], 'ArrowLeft');
    expect(component.selectedIcon()).withContext('ilk öğede durur').toBe('flag');

    keydown(iconButtons()[0], 'End');
    expect(component.selectedIcon()).toBe('visibility');
    buttons = iconButtons();
    expect(buttons[9].getAttribute('aria-checked')).toBe('true');
    expect(document.activeElement).toBe(buttons[9]);
    fixture.autoDetectChanges(false);
  });

  it('icons_VerticalArrows_StopAtEdges_NoColumnJump', () => {
    init();
    component.selectIcon('gps_fixed');
    fixture.detectChanges();

    const up = new KeyboardEvent('keydown', { key: 'ArrowUp', cancelable: true });
    component.onIconKeydown(up, 2);
    expect(component.selectedIcon()).withContext('ilk satırda Yukarı').toBe('gps_fixed');
    expect(up.defaultPrevented).withContext('sayfa kaymasın').toBeTrue();

    // 10 ikon: index 5'in altında (13) hücre yok → son öğeye (9) sıçramaz.
    component.selectIcon('schedule');
    const down = new KeyboardEvent('keydown', { key: 'ArrowDown', cancelable: true });
    component.onIconKeydown(down, 5);
    expect(component.selectedIcon()).withContext('eksik son satır').toBe('schedule');
    expect(down.defaultPrevented).toBeTrue();
  });

  it('icons_ReclickSelectedCategoryChip_KeepsItSelected', async () => {
    init();
    // Listbox ilk değeri bir mikro görevde uygular.
    await fixture.whenStable();
    fixture.detectChanges();
    const allOption = () => el('icon-category-all')?.querySelector('[role="option"]') as HTMLElement;
    expect(allOption().getAttribute('aria-selected')).toBe('true');

    allOption().click();
    fixture.detectChanges();

    expect(component.iconCategory()).toBe('all');
    expect(allOption().getAttribute('aria-selected')).withContext('görsel seçim kaybolmaz').toBe('true');

    const timeOption = el('icon-category-time')?.querySelector('[role="option"]') as HTMLElement;
    timeOption.click();
    fixture.detectChanges();
    timeOption.click();
    fixture.detectChanges();
    expect(component.iconCategory()).toBe('time');
    expect(timeOption.getAttribute('aria-selected')).toBe('true');
    expect(iconButtons().map((b) => b.getAttribute('data-icon'))).toEqual(['schedule', 'timer']);
  });

  it('icons_OtherKeys_AreIgnored', () => {
    init();
    const event = new KeyboardEvent('keydown', { key: 'a', cancelable: true });
    component.onIconKeydown(event, 0);
    expect(component.selectedIcon()).toBeNull();
    expect(event.defaultPrevented).toBeFalse();
  });

  it('icons_Clear_ResetsSelection_AndDisablesClear', () => {
    init({ definition: dto({ icon: 'school', iconUrl: null }) });
    expect(component.selectedIcon()).toBe('school');
    expect((el('icon-clear') as HTMLButtonElement).disabled).toBeFalse();

    (el('icon-clear') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(component.selectedIcon()).toBeNull();
    expect((el('icon-clear') as HTMLButtonElement).disabled).toBeTrue();
    expect(el('icon-selected-none')).toBeTruthy();
  });

  it('icons_LegacySvgOnEdit_ShowsWarning_UntilNewIconPicked', () => {
    init({ definition: dto({ icon: null, iconUrl: 'achievements/m.svg' }) });

    expect(el('icon-legacy-warning')?.textContent).toContain('achievements/m.svg');
    component.selectIcon('flag');
    fixture.detectChanges();
    expect(el('icon-legacy-warning')).toBeNull();
  });

  it('icons_Preview_RendersFourMedallionStatesWithSelectedIcon', () => {
    init();
    component.selectIcon('timer');
    fixture.detectChanges();

    const medallions = [...(el('icon-preview') as HTMLElement).querySelectorAll('app-badge-medallion')] as HTMLElement[];
    expect(medallions.map((m) => m.getAttribute('data-state'))).toEqual(['earned', 'new', 'in-progress', 'locked']);
    expect(medallions.every((m) => m.textContent?.includes('timer'))).toBeTrue();
  });

  it('icons_LoadError_ShowsRetry_RetryCallsServiceAgain', () => {
    init();
    service.getIcons.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    component.loadIcons();
    fixture.detectChanges();

    expect(el('icons-error')?.getAttribute('role')).toBe('alert');
    expect(el('icon-grid')).toBeNull();
    expect((el('save') as HTMLButtonElement).disabled).withContext('ikon opsiyonel; kayıt engellenmez').toBeFalse();

    service.getIcons.and.returnValue(of(ICONS));
    (el('icons-error')?.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(service.getIcons).toHaveBeenCalledTimes(3);
    expect(iconButtons().length).toBe(ICONS.length);
  });

  it('save_Create_SendsSelectedIcon', () => {
    init();
    service.create.and.returnValue(of(dto()));
    fillCreate();
    component.selectIcon('rocket_launch');

    component.save();

    const body = service.create.calls.mostRecent().args[0];
    expect(body.icon).toBe('rocket_launch');
    expect(body.iconUrl).toBeNull();
  });

  it('save_Edit_AlwaysSendsIcon_PreservesLegacyIconUrl', () => {
    init({ definition: dto({ icon: 'school', iconUrl: 'achievements/m.svg' }) });
    service.update.and.returnValue(of(dto()));

    component.save();
    let body = service.update.calls.mostRecent().args[1];
    expect('icon' in body).toBeTrue();
    expect(body.icon).toBe('school');
    expect(body.iconUrl).withContext('PUT tam üzerine yazar; eski yol aynen geri gider').toBe('achievements/m.svg');

    component.clearIcon();
    component.save();
    body = service.update.calls.mostRecent().args[1];
    expect('icon' in body).toBeTrue();
    expect(body.icon).withContext('temizle → açık null').toBeNull();
  });

  it('save_Edit_InvalidLegacyIconUrl_SentAsNull_NoLegacyWarning', () => {
    init({ definition: dto({ icon: null, iconUrl: 'https://evil.example/x.svg' }) });
    service.update.and.returnValue(of(dto()));

    expect(component.legacyIcon()).withContext('geçersiz yol "eski SVG" sayılmaz').toBeNull();
    expect(el('icon-legacy-warning')).toBeNull();

    component.save();

    expect(service.update.calls.mostRecent().args[1].iconUrl).toBeNull();
  });

  it('save_ClearsPreviousIconError', () => {
    init();
    fillCreate();
    component.iconError.set('eski hata');
    service.create.and.returnValue(of(dto()));

    component.save();

    expect(component.iconError()).toBeNull();
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
