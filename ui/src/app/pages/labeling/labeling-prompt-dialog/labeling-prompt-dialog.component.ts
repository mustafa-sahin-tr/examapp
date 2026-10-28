import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { FormControl, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import {
  ActivityStructureType,
  LabelingActivityPrompt,
  LabelingActivityPromptType,
  LabelingPrompt,
  LabelingPromptState,
} from '../../../models/labeling';
import { CROSSWORD } from '../../../models/crossword';
import { DRAG_PLACE } from '../../../models/drag-place';
import { MATCH } from '../../../models/match';
import { MATRIX_CHOICE } from '../../../models/matrix-choice';
import { SLOT_FILL } from '../../../models/slot-fill';
import { TEXT_SELECT } from '../../../models/text-select';
import { LabelingService } from '../../../services/labeling.service';

const MAX_INSTRUCTION = 4000;
const MAX_PADDING = 50;

/** Issue #579: prompt ekranında sayfa sekmesinden sonra sırayla gösterilen etkinlik tipleri (her biri ayrı sürümlenir). */
export const ACTIVITY_PROMPT_TYPES: readonly ActivityStructureType[] = [
  MATRIX_CHOICE,
  SLOT_FILL,
  DRAG_PLACE,
  MATCH,
  TEXT_SELECT,
  CROSSWORD, // issue #586 (dilim e): görselsiz editörde görselden çıkarma
];

/** Bir etkinlik tipinin talimatı kaydedildi (diyalog bununla kapanır). */
export interface LabelingActivityPromptSaved {
  kind: 'activity';
  interactionType: ActivityStructureType;
  /** Tipin yeni etkin sürümü. */
  prompt: LabelingActivityPrompt;
}

/** Kaydedilen içerik etkin sürümle aynıydı: sunucu yeni sürüm açmadı (yeniden sorma önerilmez). */
export interface LabelingPromptUnchanged {
  kind: 'unchanged';
  version: number;
}

/**
 * Kapanış sonucu: sayfa prompt'u kaydedildiyse yeni durum, etkinlik talimatı kaydedildiyse o tipin yeni sürümü, içerik
 * değişmediyse `unchanged`.
 */
export type LabelingPromptDialogResult = LabelingPromptState | LabelingActivityPromptSaved | LabelingPromptUnchanged;

export function isActivityPromptSaved(result: LabelingPromptDialogResult): result is LabelingActivityPromptSaved {
  return (result as LabelingActivityPromptSaved).kind === 'activity';
}

export function isPromptUnchanged(result: LabelingPromptDialogResult): result is LabelingPromptUnchanged {
  return (result as LabelingPromptUnchanged).kind === 'unchanged';
}

/** Sekme anahtarı: `page` ya da etkinlik tipi (çeviri `labeling.prompt.tabs.<anahtar>`). */
export type PromptTabKey = 'page' | ActivityStructureType;

/**
 * Claude ön-etiketleme prompt'u. "Sayfa" sekmesi: iki aşamanın talimatı + kutu payları. Issue #579: etkinlik tipi başına
 * (Matris seçim, Cevap kutusu, Sürükle-yerleştir, Eşleştirme) bir sekme daha; her tipin talimatı ayrı sürümlenir. Kayıt yeni sürüm oluşturur (geçmiş silinmez);
 * geçmişten ya da varsayılandan bir sürüm forma yüklenip düzenlenebilir. Koordinat kuralı ve JSON şeması sunucuda
 * sabittir, burada düzenlenmez. Kapanış sonucu: kaydedildiyse yeni durum, değilse `undefined`.
 */
@Component({
  selector: 'app-labeling-prompt-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    DatePipe,
    MatButtonModule,
    MatCheckboxModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatTabsModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope('labeling')],
  templateUrl: './labeling-prompt-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./labeling-prompt-dialog.component.scss'],
})
export class LabelingPromptDialogComponent {
  private readonly api = inject(LabelingService);
  private readonly dialogRef = inject(MatDialogRef<LabelingPromptDialogComponent, LabelingPromptDialogResult>);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(NonNullableFormBuilder);

  readonly maxInstruction = MAX_INSTRUCTION;
  readonly activityTypes = ACTIVITY_PROMPT_TYPES;
  readonly state = signal<LabelingPromptState | null>(null);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly loadFailed = signal(false);
  readonly saveError = signal<string | null>(null);

  readonly form = this.fb.group({
    questionInstruction: ['', [Validators.required, Validators.maxLength(MAX_INSTRUCTION)]],
    optionInstruction: ['', [Validators.required, Validators.maxLength(MAX_INSTRUCTION)]],
    questionPadding: [0, [Validators.required, Validators.min(0), Validators.max(MAX_PADDING)]],
    optionPadding: [0, [Validators.required, Validators.min(0), Validators.max(MAX_PADDING)]],
    expandQuestionToOptions: [true],
  });

  // --- Issue #579: etkinlik tipi sekmeleri ---
  /** Seçili sekme: 0 = Sayfa, 1.. = `activityTypes` sırası. */
  readonly selectedTab = signal(0);
  readonly activityLoading = signal(true);
  readonly activityLoadFailed = signal(false);
  /** Tip → sunucudaki durum (etkin, varsayılan, geçmiş). */
  readonly activityStates = signal<Partial<Record<ActivityStructureType, LabelingActivityPromptType>>>({});
  readonly activityForms: Record<ActivityStructureType, FormControl<string>> = Object.fromEntries(
    ACTIVITY_PROMPT_TYPES.map((type) => [
      type,
      this.fb.control('', [Validators.required, Validators.maxLength(MAX_INSTRUCTION)]),
    ])
  ) as Record<ActivityStructureType, FormControl<string>>;
  /**
   * Kaydedilecek sekme dışında kaydedilmemiş değişikliği olan sekmeler; doluysa uyarı gösterilir, "Yine de kaydet"
   * onaylar (diyalog kapanınca o değişiklikler kaybolur).
   */
  readonly unsavedOthers = signal<PromptTabKey[]>([]);
  /** Seçili sekmenin etkinlik tipi (Sayfa sekmesinde null). */
  readonly selectedActivityType = computed<ActivityStructureType | null>(
    () => ACTIVITY_PROMPT_TYPES[this.selectedTab() - 1] ?? null
  );

  constructor() {
    this.load();
    this.loadActivity();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.api
      .getPrompt()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (state) => {
          this.state.set(state);
          this.fill(state.active);
          this.loading.set(false);
        },
        error: (error) => {
          console.error('labeling prompt load failed', error);
          this.loadFailed.set(true);
          this.loading.set(false);
        },
      });
  }

  /** Etkinlik tiplerinin talimatları; başarısız olursa yalnız etkinlik sekmeleri hata gösterir. */
  loadActivity(): void {
    this.activityLoading.set(true);
    this.activityLoadFailed.set(false);
    this.api
      .getActivityPrompts()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (state) => {
          const byType: Partial<Record<ActivityStructureType, LabelingActivityPromptType>> = {};
          for (const entry of state?.types ?? []) {
            if (ACTIVITY_PROMPT_TYPES.includes(entry.interactionType)) {
              byType[entry.interactionType] = entry;
              this.activityForms[entry.interactionType].setValue(entry.active.instruction);
            }
          }
          this.activityStates.set(byType);
          this.activityLoading.set(false);
        },
        error: (error) => {
          console.error('labeling activity prompts load failed', error);
          this.activityLoadFailed.set(true);
          this.activityLoading.set(false);
        },
      });
  }

  activityState(type: ActivityStructureType): LabelingActivityPromptType | null {
    return this.activityStates()[type] ?? null;
  }

  fillActivity(type: ActivityStructureType, prompt: LabelingActivityPrompt): void {
    this.activityForms[type].setValue(prompt.instruction);
    this.activityForms[type].markAsDirty();
  }

  loadActivityVersion(type: ActivityStructureType, version: number): void {
    const prompt = this.activityState(type)?.history.find((p) => p.version === version);
    if (prompt) {
      this.fillActivity(type, prompt);
    }
  }

  resetActivityToDefaults(type: ActivityStructureType): void {
    const defaults = this.activityState(type)?.defaults;
    if (defaults) {
      this.fillActivity(type, defaults);
    }
  }

  /** Sekmede etkin sürümden farklı (kaydedilmemiş) içerik var mı. */
  hasUnsavedChanges(tab: PromptTabKey): boolean {
    if (tab === 'page') {
      const active = this.state()?.active;
      if (!active) {
        return false;
      }
      const value = this.form.getRawValue();
      return (
        value.questionInstruction !== active.questionInstruction ||
        value.optionInstruction !== active.optionInstruction ||
        value.questionPadding !== active.questionPadding ||
        value.optionPadding !== active.optionPadding ||
        value.expandQuestionToOptions !== active.expandQuestionToOptions
      );
    }
    const active = this.activityState(tab)?.active;
    return !!active && this.activityForms[tab].value !== active.instruction;
  }

  /**
   * Alttaki Kaydet: seçili sekmeyi kaydeder. Diğer sekmelerde kaydedilmemiş değişiklik varsa önce uyarır; `confirmed`
   * ("Yine de kaydet") ile kaydeder.
   */
  saveSelected(confirmed = false): void {
    const type = this.selectedActivityType();
    const current: PromptTabKey = type ?? 'page';
    const others = (['page', ...ACTIVITY_PROMPT_TYPES] as PromptTabKey[]).filter(
      (tab) => tab !== current && this.hasUnsavedChanges(tab)
    );
    if (others.length > 0 && !confirmed) {
      this.unsavedOthers.set(others);
      return;
    }
    this.unsavedOthers.set([]);
    if (type) {
      this.saveActivity(type);
    } else {
      this.save();
    }
  }

  /** Seçili sekmenin kaydı yapılabilir mi (alttaki Kaydet düğmesi). */
  canSaveSelected(): boolean {
    if (this.saving()) {
      return false;
    }
    const type = this.selectedActivityType();
    if (type) {
      return !this.activityLoading() && !this.activityLoadFailed() && !!this.activityState(type) && this.activityForms[type].valid;
    }
    return !this.loading() && !this.loadFailed() && this.form.valid;
  }

  /** Tipin talimatını yeni sürüm olarak kaydeder; yalnız o tipin önbelleği geçersiz olur. */
  saveActivity(type: ActivityStructureType): void {
    const control = this.activityForms[type];
    if (control.invalid || this.saving() || !this.activityState(type)) {
      control.markAsTouched();
      return;
    }
    this.saving.set(true);
    this.saveError.set(null);
    this.dialogRef.disableClose = true;
    const previous = this.activityState(type)?.active.version;
    this.api.saveActivityPrompt(type, { instruction: control.value }).subscribe({
      next: (prompt) => {
        this.saving.set(false);
        this.dialogRef.close(
          prompt.version === previous
            ? { kind: 'unchanged', version: prompt.version }
            : { kind: 'activity', interactionType: type, prompt }
        );
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        this.dialogRef.disableClose = false;
        this.saveError.set((error.error as { message?: string } | null)?.message ?? '');
      },
    });
  }

  /** Sekme değişince önceki sekmenin kayıt hatası gizlenir. */
  onTabChange(index: number): void {
    this.selectedTab.set(index);
    this.saveError.set(null);
    this.unsavedOthers.set([]);
  }

  /** Geçmişteki bir sürümü ya da varsayılanı forma yükler (kaydedilene kadar etkin olmaz). */
  fill(prompt: LabelingPrompt): void {
    this.form.setValue({
      questionInstruction: prompt.questionInstruction,
      optionInstruction: prompt.optionInstruction,
      questionPadding: prompt.questionPadding,
      optionPadding: prompt.optionPadding,
      expandQuestionToOptions: prompt.expandQuestionToOptions,
    });
    this.form.markAsDirty();
  }

  loadVersion(version: number): void {
    const prompt = this.state()?.history.find((p) => p.version === version);
    if (prompt) {
      this.fill(prompt);
    }
  }

  resetToDefaults(): void {
    const defaults = this.state()?.defaults;
    if (defaults) {
      this.fill(defaults);
    }
  }

  save(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.saveError.set(null);
    // Kayıt sürerken Esc / arka plan tıklaması kapatmasın: yeni sürüm etkin olur ama sonuç kaybolurdu.
    this.dialogRef.disableClose = true;
    const previous = this.state()?.active.version;
    this.api.savePrompt(this.form.getRawValue()).subscribe({
      next: (state) => {
        this.saving.set(false);
        // Aynı içerik: sunucu yeni sürüm açmadı.
        this.dialogRef.close(
          state.active.version === previous ? { kind: 'unchanged', version: state.active.version } : state
        );
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        this.dialogRef.disableClose = false;
        this.saveError.set((error.error as { message?: string } | null)?.message ?? '');
      },
    });
  }

  cancel(): void {
    if (!this.saving()) {
      this.dialogRef.close();
    }
  }

  /** Talimatta `{width}`/`{height}` yoksa Claude piksel uzayını bilmez; kutular kayabilir (uyarı, engel değil). */
  missingPlaceholders(value: string): boolean {
    return !value.includes('{width}') || !value.includes('{height}');
  }
}
