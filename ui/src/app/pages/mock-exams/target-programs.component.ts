import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { AdminTargetProgram, SaveTargetProgram, TargetExam, TargetImportPreview, TargetImportResult } from '../../models/target-goal.model';
import { TargetGoalService } from '../../services/target-goal.service';

const PAGE_SIZE = 50;
const SCORE_TYPES: Record<TargetExam, string[]> = { lgs: ['lgs'], yks: ['tyt', 'say', 'ea', 'soz'] };

const emptyDraft = (exam: TargetExam, year: number): SaveTargetProgram => ({
  scoreType: SCORE_TYPES[exam][0],
  name: '',
  institution: '',
  city: '',
  district: '',
  year,
  minimumScore: 300,
  percentile: null,
  sourceReference: '',
});

/** Sunucunun çevrilen hata kodu; bilinmeyen hata genel mesaja düşer. */
const errorCodeOf = (error: HttpErrorResponse): string => (typeof error.error?.errorCode === 'string' ? error.error.errorCode : 'generic');

/**
 * Issue #695: taban puanları admin resmî kaynaktan girer. Dosya önce önizlenir, onaylanmadan hiçbir şey yazılmaz; onay aynı
 * dosyayı yeniden gönderir. Satırı bir öğrenci seçtiyse silinemez, düzeltilebilir.
 */
@Component({
  selector: 'app-target-programs',
  standalone: true,
  imports: [FormsModule, MatButtonModule, MatButtonToggleModule, MatCardModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslocoDirective],
  providers: [provideTranslocoScope('target-goals')],
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./mock-exams.scss'],
  template: `
    <div class="me-root" *transloco="let t; prefix: 'target-goals.admin'">
      <h1 class="me-title">{{ t('title') }}</h1>
      <p class="me-note">{{ t('subtitle') }}</p>

      <mat-card class="me-card">
        <mat-card-title>{{ t('import.title') }}</mat-card-title>
        <mat-card-content>
          <p class="me-note">{{ t('import.format') }}</p>
          <div class="me-grid">
            <mat-form-field appearance="outline">
              <mat-label>{{ t('year') }}</mat-label>
              <input matInput type="number" min="2000" max="2100" [ngModel]="importYear()" (ngModelChange)="changeImportYear(+$event)" />
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>{{ t('source') }}</mat-label>
              <input matInput maxlength="300" autocomplete="off" [ngModel]="importSource()" (ngModelChange)="changeImportSource($event)" />
            </mat-form-field>
          </div>
          <div class="me-actions">
            <mat-button-toggle-group [value]="exam()" (change)="changeExam($event.value)" [attr.aria-label]="t('exam')" hideSingleSelectionIndicator>
              <mat-button-toggle value="lgs">LGS</mat-button-toggle>
              <mat-button-toggle value="yks">YKS</mat-button-toggle>
            </mat-button-toggle-group>
            <input #fileInput type="file" accept=".csv,text/csv" hidden (change)="pickFile(fileInput.files)" />
            <button mat-stroked-button type="button" (click)="fileInput.click()">{{ file()?.name ?? t('import.pick') }}</button>
            <button mat-flat-button type="button" [disabled]="!canPreview() || busy()" (click)="preview()">{{ t('import.preview') }}</button>
          </div>

          @if (importError(); as code) {
            <div class="me-error" role="alert">{{ t('errors.' + code) }}</div>
          }
          @if (imported(); as result) {
            <p class="me-note" role="status">{{ t('import.done', { created: result.created, updated: result.updated, unchanged: result.unchanged, skipped: result.skipped }) }}</p>
          }

          @if (previewed(); as report) {
            <p data-testid="import-counts">{{ t('import.counts', { fresh: report.newCount, changed: report.changedCount, unchanged: report.unchangedCount, invalid: report.invalidCount }) }}</p>
            @if (report.changed.length) {
              <h3>{{ t('import.changed') }}</h3>
              @for (row of report.changed; track row.line) {
                <div class="me-row"><span>{{ row.name }} · {{ row.city }}</span><span>{{ row.previousMinimumScore }} → {{ row.minimumScore }}</span></div>
              }
            }
            @if (report.new.length) {
              <h3>{{ t('import.fresh') }}</h3>
              @for (row of report.new; track row.line) {
                <div class="me-row"><span>{{ row.name }} · {{ row.institution }} {{ row.city }}</span><span>{{ row.minimumScore }}</span></div>
              }
            }
            @if (report.invalid.length) {
              <h3>{{ t('import.invalid') }}</h3>
              @for (row of report.invalid; track row.line) {
                <div class="me-row"><span>{{ t('import.line', { line: row.line }) }} {{ row.name }}</span><span class="me-error">{{ t('errors.' + row.code) }}</span></div>
              }
            }
            @if (listCapped()) {
              <p class="me-note">{{ t('import.capped') }}</p>
            }
            <div class="me-actions">
              <button mat-button type="button" (click)="previewed.set(null)">{{ t('cancel') }}</button>
              <button mat-flat-button type="button" [disabled]="busy() || report.newCount + report.changedCount === 0" (click)="confirm()">{{ t('import.confirm') }}</button>
            </div>
          }
        </mat-card-content>
      </mat-card>

      <div class="me-grid">
        <mat-form-field appearance="outline">
          <mat-label>{{ t('search') }}</mat-label>
          <input matInput maxlength="100" autocomplete="off" [ngModel]="term()" (ngModelChange)="term.set($event)" (keyup.enter)="load(0)" />
        </mat-form-field>
      </div>
      <div class="me-actions">
        <button mat-button type="button" (click)="load(0)">{{ t('searchAction') }}</button>
        <button mat-stroked-button type="button" (click)="add()">{{ t('add') }}</button>
      </div>

      @if (draft(); as form) {
        <mat-card class="me-card">
          <mat-card-content>
            <div class="me-grid">
              <mat-form-field appearance="outline">
                <mat-label>{{ t('scoreType') }}</mat-label>
                <mat-select [ngModel]="form.scoreType" (ngModelChange)="change({ scoreType: $event })">
                  @for (type of allScoreTypes; track type) {
                    <mat-option [value]="type">{{ type.toUpperCase() }}</mat-option>
                  }
                </mat-select>
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>{{ t('name') }}</mat-label>
                <input matInput maxlength="200" autocomplete="off" [ngModel]="form.name" (ngModelChange)="change({ name: $event })" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>{{ t('institution') }}</mat-label>
                <input matInput maxlength="200" autocomplete="off" [ngModel]="form.institution" (ngModelChange)="change({ institution: $event })" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>{{ t('city') }}</mat-label>
                <input matInput maxlength="100" autocomplete="off" [ngModel]="form.city" (ngModelChange)="change({ city: $event })" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>{{ t('district') }}</mat-label>
                <input matInput maxlength="100" autocomplete="off" [ngModel]="form.district" (ngModelChange)="change({ district: $event })" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>{{ t('year') }}</mat-label>
                <input matInput type="number" min="2000" max="2100" [ngModel]="form.year" (ngModelChange)="change({ year: +$event })" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>{{ t('minimumScore') }}</mat-label>
                <input matInput type="number" min="100" max="500" step="0.001" [ngModel]="form.minimumScore" (ngModelChange)="change({ minimumScore: +$event })" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>{{ t('percentile') }}</mat-label>
                <input matInput type="number" min="0" max="100" step="0.01" [ngModel]="form.percentile" (ngModelChange)="change({ percentile: $event === null || $event === '' ? null : +$event })" />
              </mat-form-field>
              <mat-form-field appearance="outline">
                <mat-label>{{ t('source') }}</mat-label>
                <input matInput maxlength="300" autocomplete="off" [ngModel]="form.sourceReference" (ngModelChange)="change({ sourceReference: $event })" />
              </mat-form-field>
            </div>
            @if (saveError(); as code) {
              <div class="me-error" role="alert">{{ t('errors.' + code) }}</div>
            }
            <div class="me-actions">
              <button mat-button type="button" (click)="draft.set(null)">{{ t('cancel') }}</button>
              <button mat-flat-button type="button" [disabled]="busy() || !canSave()" (click)="save()">{{ t('save') }}</button>
            </div>
          </mat-card-content>
        </mat-card>
      }

      @if (loadFailed()) {
        <div class="me-error" role="alert">{{ t('loadFailed') }}</div>
      } @else if (!items().length) {
        <p class="me-note">{{ t('empty') }}</p>
      }
      @for (item of items(); track item.id) {
        <div class="me-row">
          <span>
            <b>{{ item.name }}</b>
            <span class="me-note"> · {{ item.scoreType.toUpperCase() }} {{ item.year }} · {{ item.minimumScore }}</span>
            <div class="me-note">{{ item.institution }} {{ item.district }} {{ item.city }}</div>
            <div class="me-note">{{ item.sourceReference }}</div>
          </span>
          <span>
            <button mat-button type="button" (click)="edit(item)">{{ t('edit') }}</button>
            @if (item.inUse) {
              <span class="me-note">{{ t('inUse') }}</span>
            } @else {
              <button mat-button type="button" (click)="remove(item)">{{ t('delete') }}</button>
            }
          </span>
        </div>
      }
      @if (total() > items().length + skip()) {
        <div class="me-actions">
          <button mat-button type="button" (click)="load(skip() + pageSize)">{{ t('next') }}</button>
        </div>
      }
    </div>
  `,
})
export class TargetProgramsComponent {
  private readonly service = inject(TargetGoalService);

  protected readonly pageSize = PAGE_SIZE;
  protected readonly allScoreTypes = [...SCORE_TYPES.lgs, ...SCORE_TYPES.yks];

  protected readonly exam = signal<TargetExam>('lgs');
  protected readonly term = signal('');
  protected readonly items = signal<AdminTargetProgram[]>([]);
  protected readonly total = signal(0);
  protected readonly skip = signal(0);
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal(false);

  protected readonly importYear = signal(new Date().getFullYear());
  protected readonly importSource = signal('');
  protected readonly file = signal<File | null>(null);
  protected readonly previewed = signal<TargetImportPreview | null>(null);
  protected readonly imported = signal<TargetImportResult | null>(null);
  protected readonly importError = signal<string | null>(null);

  protected readonly draft = signal<SaveTargetProgram | null>(null);
  protected readonly draftId = signal<number | null>(null);
  protected readonly saveError = signal<string | null>(null);

  protected readonly canPreview = computed(() => this.file() !== null && this.importSource().trim().length > 0 && Number.isInteger(this.importYear()));
  protected readonly canSave = computed(() => {
    const form = this.draft();
    return !!form && form.name.trim().length > 0 && form.city.trim().length > 0 && form.sourceReference.trim().length > 0 && form.minimumScore >= 100 && form.minimumScore <= 500;
  });
  protected readonly listCapped = computed(() => {
    const report = this.previewed();
    return !!report && (report.new.length < report.newCount || report.changed.length < report.changedCount || report.invalid.length < report.invalidCount);
  });

  constructor() {
    this.load(0);
  }

  protected load(skip: number): void {
    this.loadFailed.set(false);
    this.service.adminList({ exam: this.exam(), search: this.term().trim(), skip, take: PAGE_SIZE }).subscribe({
      next: (page) => {
        this.items.set(page.items);
        this.total.set(page.total);
        this.skip.set(skip);
      },
      error: () => this.loadFailed.set(true),
    });
  }

  protected changeExam(exam: TargetExam): void {
    this.exam.set(exam);
    this.previewed.set(null);
    this.load(0);
  }

  protected changeImportYear(year: number): void {
    this.importYear.set(year);
    this.resetImport();
  }

  protected changeImportSource(source: string): void {
    this.importSource.set(source);
    this.resetImport();
  }

  protected pickFile(files: FileList | null): void {
    this.file.set(files?.[0] ?? null);
    this.resetImport();
  }

  /** Önizleme hangi seçimler için üretildiyse onay onlarla gider: seçim değişince önizleme düşer. */
  private resetImport(): void {
    this.previewed.set(null);
    this.imported.set(null);
    this.importError.set(null);
  }

  private importRequest() {
    return { exam: this.exam(), year: this.importYear(), source: this.importSource().trim(), file: this.file()! };
  }

  protected preview(): void {
    this.busy.set(true);
    this.importError.set(null);
    this.imported.set(null);
    this.service.previewImport(this.importRequest()).subscribe({
      next: (report) => {
        this.previewed.set(report);
        this.busy.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.importError.set(errorCodeOf(error));
        this.busy.set(false);
      },
    });
  }

  protected confirm(): void {
    this.busy.set(true);
    this.importError.set(null);
    this.service.confirmImport(this.importRequest()).subscribe({
      next: (result) => {
        this.imported.set(result);
        this.previewed.set(null);
        this.busy.set(false);
        this.load(0);
      },
      error: (error: HttpErrorResponse) => {
        this.importError.set(errorCodeOf(error));
        this.busy.set(false);
      },
    });
  }

  protected add(): void {
    this.draftId.set(null);
    this.saveError.set(null);
    this.draft.set(emptyDraft(this.exam(), this.importYear()));
  }

  protected edit(item: AdminTargetProgram): void {
    this.draftId.set(item.id);
    this.saveError.set(null);
    this.draft.set({
      scoreType: item.scoreType,
      name: item.name,
      institution: item.institution,
      city: item.city,
      district: item.district,
      year: item.year,
      minimumScore: item.minimumScore,
      percentile: item.percentile,
      sourceReference: item.sourceReference ?? '',
    });
  }

  protected change(patch: Partial<SaveTargetProgram>): void {
    this.draft.update((form) => (form ? { ...form, ...patch } : form));
  }

  protected save(): void {
    const form = this.draft();
    if (!form) {
      return;
    }
    this.busy.set(true);
    this.saveError.set(null);
    this.service.adminSave(form, this.draftId()).subscribe({
      next: () => {
        this.draft.set(null);
        this.busy.set(false);
        this.load(this.skip());
      },
      error: (error: HttpErrorResponse) => {
        this.saveError.set(errorCodeOf(error));
        this.busy.set(false);
      },
    });
  }

  protected remove(item: AdminTargetProgram): void {
    this.loadFailed.set(false);
    this.service.adminDelete(item.id).subscribe({
      next: () => this.load(this.skip()),
      error: () => this.loadFailed.set(true),
    });
  }
}
