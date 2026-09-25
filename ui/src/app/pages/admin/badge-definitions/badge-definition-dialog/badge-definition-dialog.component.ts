import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormControl,
  FormRecord,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { Observable, map, startWith, take } from 'rxjs';
import {
  BADGE_CODE_PATTERN,
  BADGE_DEFINITION_LIMITS as LIMITS,
  BADGE_ICON_PATTERN,
  BadgeDefinitionAdmin,
  BadgeRuleFieldSchema,
  BadgeRuleTypeSchema,
  KNOWN_BADGE_CATEGORIES,
  UpdateBadgeDefinitionRequest,
} from '../../../../models/badge-definition-admin.model';
import { Subject } from '../../../../models/subject';
import { BadgeDefinitionAdminService } from '../../../../services/badge-definition-admin.service';
import { SubjectService } from '../../../../services/subject.service';
import {
  RuleFieldValue,
  SUBJECT_ID_FIELD,
  SUBJECT_NAME_FIELD,
  TRANSLATED_RULE_TYPES,
  buildRuleConfigJson,
  findRuleTypeSchema,
  isIntegerField,
  isSubjectField,
  parseRuleConfig,
  ruleFieldValues,
  serverFieldErrors,
} from '../badge-rule.util';

const ADMIN_SCOPE = 'admin';

export interface BadgeDefinitionDialogData {
  /** Doluysa düzenleme modu (kod değiştirilemez). */
  definition?: BadgeDefinitionAdmin;
  /** Listede görülen kategoriler; bilinen seeder kategorileriyle birleştirilip öneri olarak sunulur. */
  categories?: string[];
}

type MainControlName =
  | 'code'
  | 'name'
  | 'description'
  | 'category'
  | 'iconUrl'
  | 'pathKey'
  | 'pathName'
  | 'pathOrder'
  | 'ruleType';

/**
 * Issue #148 — rozet tanımı oluştur / düzenle. Kural bölümü `GET .../rule-types` şemasından dinamik kurulur:
 * integer alanlar min/max'lı sayı girişi, `subjectId`/`subjectName` tek bir ders seçicisi (ikisini birden set eder).
 * `ruleConfigJson` yalnız şema alanlarından üretilir. 400 alan hataları ilgili kontrole, `ruleConfigJson` hatası
 * kural bölümüne, 409 kod alanına yazılır; dialog açık kalır, girdi kaybolmaz. Başarıda kaydedilen DTO ile kapanır.
 */
@Component({
  selector: 'app-badge-definition-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(ADMIN_SCOPE)],
  templateUrl: './badge-definition-dialog.component.html',
  styleUrls: ['./badge-definition-dialog.component.scss'],
})
export class BadgeDefinitionDialogComponent {
  private readonly data = inject<BadgeDefinitionDialogData | null>(MAT_DIALOG_DATA, { optional: true }) ?? {};
  private readonly dialogRef = inject<MatDialogRef<BadgeDefinitionDialogComponent, BadgeDefinitionAdmin>>(MatDialogRef);
  private readonly service = inject(BadgeDefinitionAdminService);
  private readonly subjectService = inject(SubjectService);
  private readonly transloco = inject(TranslocoService);
  private readonly fb = inject(NonNullableFormBuilder);

  readonly definition = this.data.definition ?? null;
  readonly isEdit = !!this.definition;

  readonly limits = LIMITS;

  readonly form = this.fb.group({
    code: [
      { value: this.definition?.code ?? '', disabled: this.isEdit },
      this.isEdit
        ? []
        : [Validators.required, notBlank, Validators.maxLength(LIMITS.codeMax), Validators.pattern(BADGE_CODE_PATTERN)],
    ],
    name: [this.definition?.name ?? '', [Validators.required, notBlank, Validators.maxLength(LIMITS.nameMax)]],
    description: [this.definition?.description ?? '', [Validators.maxLength(LIMITS.descriptionMax)]],
    category: [this.definition?.category ?? '', [Validators.required, notBlank, Validators.maxLength(LIMITS.categoryMax)]],
    iconUrl: [this.definition?.iconUrl ?? '', [Validators.pattern(BADGE_ICON_PATTERN)]],
    pathKey: [this.definition?.pathKey ?? '', [Validators.maxLength(LIMITS.pathKeyMax)]],
    pathName: [this.definition?.pathName ?? '', [Validators.maxLength(LIMITS.pathNameMax)]],
    pathOrder: this.fb.control<number | null>(this.definition?.pathOrder ?? null, [
      integerValidator,
      Validators.min(LIMITS.pathOrderMin),
      Validators.max(LIMITS.pathOrderMax),
    ]),
    ruleType: [this.definition?.ruleType ?? '', [Validators.required]],
  });

  /** Seçili RuleType şemasının alanları; şema değişince yeniden kurulur. */
  readonly ruleForm = new FormRecord<FormControl<RuleFieldValue>>({});

  // ---- rule types ----
  readonly ruleTypes = signal<BadgeRuleTypeSchema[]>([]);
  readonly ruleTypesLoading = signal(false);
  readonly ruleTypesError = signal(false);
  private readonly ruleTypeValue = toSignal(this.form.controls.ruleType.valueChanges, {
    initialValue: this.form.controls.ruleType.value,
  });
  readonly schema = computed(() => findRuleTypeSchema(this.ruleTypes(), this.ruleTypeValue()));
  /** Ders seçicisi dışında kalan, tek tek render edilen şema alanları. */
  readonly plainFields = computed(() => this.schema()?.fields.filter((f) => !isSubjectField(f)) ?? []);
  readonly hasSubject = computed(() => this.schema()?.fields.some((f) => isSubjectField(f)) ?? false);
  /** Düzenlenen rozetin tipi artık sunulmuyorsa (eski alias, ör. "StudyStreak") kullanıcı yeni tip seçmeli. */
  readonly legacyRuleType = computed(() =>
    this.isEdit && this.ruleTypes().length > 0 && !this.schema() && this.ruleTypeValue() === this.definition?.ruleType
      ? this.definition?.ruleType ?? null
      : null
  );

  // ---- subjects ----
  readonly subjects = signal<Subject[]>([]);
  readonly subjectsLoading = signal(false);
  readonly subjectsError = signal(false);
  /** Config'te yalnız `subjectName` var ve listede eşleşmiyorsa gösterilir (seeder rozetleri adla tanımlı). */
  readonly unmatchedSubjectName = signal<string | null>(null);

  // ---- category / icon ----
  readonly categoryOptions = computed(() => {
    const all = [...KNOWN_BADGE_CATEGORIES, ...(this.data.categories ?? [])].map((c) => c.trim()).filter(Boolean);
    return [...new Set(all)].sort((a, b) => a.localeCompare(b, 'tr'));
  });
  private readonly categoryValue = toSignal(this.form.controls.category.valueChanges, {
    initialValue: this.form.controls.category.value,
  });
  readonly filteredCategories = computed(() => {
    const q = this.categoryValue().trim().toLocaleLowerCase('tr');
    return q ? this.categoryOptions().filter((c) => c.toLocaleLowerCase('tr').includes(q)) : this.categoryOptions();
  });
  private readonly iconValue = toSignal(
    this.form.controls.iconUrl.valueChanges.pipe(
      startWith(this.form.controls.iconUrl.value),
      map((v) => v.trim())
    ),
    { requireSync: true }
  );
  readonly iconPreviewFailed = signal(false);
  /** Geçerli biçimdeki ikon için kök-göreli önizleme yolu (`public/achievements/...`). */
  readonly iconPreview = computed(() => {
    const value = this.iconValue();
    return value && BADGE_ICON_PATTERN.test(value) ? `/${value}` : null;
  });

  // ---- submit ----
  readonly submitting = signal(false);
  /** Kontrole bağlanamayan sunucu hatası (404/403/5xx veya bilinmeyen alan). */
  readonly error = signal<string | null>(null);
  /** `ruleConfigJson` alanına dönen 400 hatası: kural bölümünün altında gösterilir. */
  readonly ruleError = signal<string | null>(null);

  constructor() {
    this.transloco.load(`${ADMIN_SCOPE}/${this.transloco.getActiveLang()}`).pipe(take(1)).subscribe();

    this.form.controls.ruleType.valueChanges.pipe(takeUntilDestroyed()).subscribe((ruleType) => {
      this.ruleError.set(null);
      this.rebuildRuleForm(findRuleTypeSchema(this.ruleTypes(), ruleType));
    });
    this.form.controls.iconUrl.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.iconPreviewFailed.set(false));

    this.loadRuleTypes();
    this.loadSubjects();
  }

  loadRuleTypes(): void {
    this.ruleTypesLoading.set(true);
    this.ruleTypesError.set(false);
    this.service.getRuleTypes().subscribe({
      next: (list) => {
        this.ruleTypes.set(list);
        this.ruleTypesLoading.set(false);
        const schema = findRuleTypeSchema(list, this.form.controls.ruleType.value);
        // Kanonik ada çevir (ör. "answercount" → "AnswerCount") ki select seçili görünsün.
        if (schema && schema.ruleType !== this.form.controls.ruleType.value) {
          this.form.controls.ruleType.setValue(schema.ruleType, { emitEvent: false });
        }
        const initial =
          schema && this.definition ? ruleFieldValues(schema, parseRuleConfig(this.definition.ruleConfigJson)) : {};
        this.rebuildRuleForm(schema, initial);
      },
      error: () => {
        this.ruleTypesLoading.set(false);
        this.ruleTypesError.set(true);
      },
    });
  }

  loadSubjects(): void {
    this.subjectsLoading.set(true);
    this.subjectsError.set(false);
    this.subjectService.loadCategories().subscribe({
      next: (list) => {
        this.subjects.set(list);
        this.subjectsLoading.set(false);
        this.matchSubjectByName();
      },
      error: () => {
        this.subjectsLoading.set(false);
        this.subjectsError.set(true);
      },
    });
  }

  /** Ders seçimi: `subjectId` ve `subjectName` birlikte set edilir. */
  selectSubject(subjectId: number | null): void {
    const subject = this.subjects().find((s) => s.id === subjectId) ?? null;
    this.ruleForm.get(SUBJECT_ID_FIELD)?.setValue(subject?.id ?? null);
    this.ruleForm.get(SUBJECT_NAME_FIELD)?.setValue(subject?.name ?? null);
    this.unmatchedSubjectName.set(null);
    this.ruleForm.markAsTouched();
  }

  /** Ders seçicisinin değeri (`subjectId` kontrolü). */
  selectedSubjectId(): RuleFieldValue {
    return this.ruleForm.controls[SUBJECT_ID_FIELD]?.value ?? null;
  }

  ruleTypeLabel(schema: BadgeRuleTypeSchema): string {
    return TRANSLATED_RULE_TYPES.includes(schema.ruleType)
      ? this.text(`ruleTypes.${schema.ruleType}.label`)
      : schema.ruleType;
  }

  ruleTypeHint(schema: BadgeRuleTypeSchema): string {
    return TRANSLATED_RULE_TYPES.includes(schema.ruleType)
      ? this.text(`ruleTypes.${schema.ruleType}.hint`)
      : schema.description;
  }

  /** Şema alanının etiketi: bilinen alan (`target`) çevrilir, diğerleri ham ad. */
  fieldLabel(field: BadgeRuleFieldSchema): string {
    return field.name === 'target' ? this.text('dialog.rule.target') : field.name;
  }

  isInteger(field: BadgeRuleFieldSchema): boolean {
    return isIntegerField(field);
  }

  ruleControl(name: string): FormControl<RuleFieldValue> | null {
    return this.ruleForm.controls[name] ?? null;
  }

  /** Kontrolün ilk hatası için gösterilecek metin; hata yoksa null. */
  errorText(control: AbstractControl | null): string | null {
    const errors = control?.errors;
    if (!errors) return null;
    if (typeof errors['server'] === 'string') return errors['server'];
    if (errors['required']) return this.text('dialog.errors.required');
    if (errors['min']) return this.text('dialog.errors.min', { min: errors['min'].min });
    if (errors['max']) return this.text('dialog.errors.max', { max: errors['max'].max });
    if (errors['integer']) return this.text('dialog.errors.integer');
    if (errors['maxlength']) return this.text('dialog.errors.maxLength', { max: errors['maxlength'].requiredLength });
    if (errors['pattern']) {
      if (control === this.form.controls.code) return this.text('dialog.errors.codePattern');
      if (control === this.form.controls.iconUrl) return this.text('dialog.errors.iconPattern');
    }
    return this.text('dialog.errors.invalid');
  }

  /** Ders seçicisinin hata metni: ders alanlarından birine gelen sunucu hatası ya da "en az biri" kuralı. */
  subjectErrorText(): string | null {
    const idErr = this.errorText(this.ruleControl(SUBJECT_ID_FIELD));
    const nameErr = this.errorText(this.ruleControl(SUBJECT_NAME_FIELD));
    if (idErr || nameErr) return idErr ?? nameErr;
    return this.ruleForm.errors?.['subjectRequired'] && this.ruleForm.touched ? this.text('dialog.errors.subjectRequired') : null;
  }

  cancel(): void {
    if (this.submitting()) return;
    this.dialogRef.close();
  }

  save(): void {
    if (this.submitting()) return;
    const schema = this.schema();
    if (this.form.invalid || this.ruleForm.invalid || !schema) {
      this.form.markAllAsTouched();
      this.ruleForm.markAllAsTouched();
      return;
    }

    const v = this.form.getRawValue();
    const body: UpdateBadgeDefinitionRequest = {
      name: v.name.trim(),
      description: v.description.trim(),
      iconUrl: v.iconUrl.trim() || null,
      category: v.category.trim(),
      ruleType: schema.ruleType,
      ruleConfigJson: buildRuleConfigJson(schema, this.ruleForm.getRawValue()),
      pathKey: v.pathKey.trim() || null,
      pathName: v.pathName.trim() || null,
      pathOrder: v.pathOrder ?? null,
    };

    const request$: Observable<BadgeDefinitionAdmin> = this.definition
      ? this.service.update(this.definition.id, body)
      : this.service.create({ ...body, code: v.code.trim() });

    this.submitting.set(true);
    this.error.set(null);
    this.ruleError.set(null);
    request$.subscribe({
      next: (saved) => {
        this.submitting.set(false);
        this.dialogRef.close(saved);
      },
      error: (err: unknown) => {
        this.submitting.set(false);
        this.applyServerError(err);
      },
    });
  }

  /** 400/409 alan hatalarını kontrollere dağıtır; eşleşmeyenler ve diğer durumlar genel hata olur. */
  applyServerError(err: unknown): void {
    const status = err instanceof HttpErrorResponse ? err.status : 0;
    const fieldErrors = status === 400 || status === 409 ? serverFieldErrors(err) : null;

    if (status === 409) {
      // `code` → kod benzersizliği (alanın altında); diğer anahtarlar (ör. `isActive`: aktif rozet üst sınırı) genel hata.
      const codeMessage = fieldErrors?.['code']?.join(' ');
      if (codeMessage || !fieldErrors) {
        this.setServerError(this.form.controls.code, codeMessage ?? this.text('dialog.errors.codeTaken'));
      }
      const other = Object.entries(fieldErrors ?? {})
        .filter(([key]) => key !== 'code')
        .map(([, messages]) => messages.join(' '));
      if (other.length) this.error.set(other.join(' '));
      return;
    }

    if (status === 400 && fieldErrors) {
      const unmatched: string[] = [];
      for (const [key, messages] of Object.entries(fieldErrors)) {
        const message = messages.join(' ');
        if (key === 'ruleConfigJson') {
          this.ruleError.set(message);
        } else if (key in this.form.controls) {
          this.setServerError(this.form.controls[key as MainControlName], message);
        } else if (this.ruleForm.controls[key]) {
          this.setServerError(this.ruleForm.controls[key], message);
        } else {
          unmatched.push(message);
        }
      }
      if (unmatched.length) this.error.set(unmatched.join(' '));
      return;
    }

    const key =
      status === 404 ? 'notFound' : status === 403 ? 'forbidden' : status === 400 ? 'invalid' : 'saveFailed';
    this.error.set(this.text(`dialog.errors.${key}`));
  }

  private setServerError(control: AbstractControl, message: string): void {
    control.setErrors({ ...(control.errors ?? {}), server: message });
    control.markAsTouched();
  }

  /**
   * Kural kontrollerini şemaya göre yeniden kurar. Aynı adlı alanın değeri tip değişiminde korunur (ör. `target`);
   * şemada olmayan alanlar kaldırılır — `ruleConfigJson`'a yalnız şema alanları gider.
   */
  private rebuildRuleForm(schema: BadgeRuleTypeSchema | null, initial?: Record<string, RuleFieldValue>): void {
    const previous = this.ruleForm.getRawValue();
    for (const name of Object.keys(this.ruleForm.controls)) this.ruleForm.removeControl(name, { emitEvent: false });
    this.unmatchedSubjectName.set(null);
    this.ruleForm.clearValidators();
    if (!schema) {
      this.ruleForm.updateValueAndValidity();
      return;
    }

    for (const field of schema.fields) {
      const value = initial ? initial[field.name] ?? null : previous[field.name] ?? null;
      this.ruleForm.addControl(field.name, new FormControl<RuleFieldValue>(value, fieldValidators(field)), {
        emitEvent: false,
      });
    }
    if (schema.fields.some((f) => isSubjectField(f))) this.ruleForm.setValidators(subjectRequired);
    this.ruleForm.updateValueAndValidity();
    this.matchSubjectByName();
  }

  /** Config'te yalnız ders adı varsa (seeder), ders listesinden id'yi bulup seçiciyi doldurur. */
  private matchSubjectByName(): void {
    const idControl = this.ruleForm.controls[SUBJECT_ID_FIELD];
    const nameControl = this.ruleForm.controls[SUBJECT_NAME_FIELD];
    if (!idControl || !nameControl) return;
    const name = typeof nameControl.value === 'string' ? nameControl.value.trim() : '';
    if (idControl.value != null || !name) {
      this.unmatchedSubjectName.set(null);
      return;
    }
    const match = this.subjects().find((s) => s.name.trim().toLocaleLowerCase('tr') === name.toLocaleLowerCase('tr'));
    if (match) {
      idControl.setValue(match.id);
      nameControl.setValue(match.name);
      this.unmatchedSubjectName.set(null);
    } else {
      this.unmatchedSubjectName.set(name);
    }
  }

  private text(key: string, params?: Record<string, unknown>): string {
    return this.transloco.translate<string>(`${ADMIN_SCOPE}.badgeDefinitions.${key}`, params) ?? '';
  }
}

function fieldValidators(field: BadgeRuleFieldSchema): ValidatorFn[] {
  const validators: ValidatorFn[] = [];
  // Ders alanlarının zorunluluğu tek tek değil, grup seviyesinde "en az biri" kuralıyla kontrol edilir.
  if (field.required && !isSubjectField(field)) validators.push(Validators.required);
  if (isIntegerField(field)) {
    validators.push(integerValidator);
    if (field.min != null) validators.push(Validators.min(field.min));
    if (field.max != null) validators.push(Validators.max(field.max));
  }
  return validators;
}

/** Backend kuralı: ders kurallarında `subjectId` veya `subjectName`'den en az biri zorunlu. */
function subjectRequired(group: AbstractControl): ValidationErrors | null {
  const id = group.get(SUBJECT_ID_FIELD)?.value;
  const name = group.get(SUBJECT_NAME_FIELD)?.value;
  const hasName = typeof name === 'string' && name.trim().length > 0;
  return id != null || hasName ? null : { subjectRequired: true };
}

function integerValidator(control: AbstractControl): ValidationErrors | null {
  const value = control.value;
  if (value === null || value === undefined || value === '') return null;
  return Number.isInteger(typeof value === 'number' ? value : Number(value)) ? null : { integer: true };
}

function notBlank(control: AbstractControl<string | null>): ValidationErrors | null {
  const value = control.value ?? '';
  return value.length > 0 && !value.trim() ? { required: true } : null;
}
