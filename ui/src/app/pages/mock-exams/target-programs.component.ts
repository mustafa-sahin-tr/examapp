import { ChangeDetectionStrategy, Component, DestroyRef, TemplateRef, computed, inject, signal, viewChild } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';

import { AdminTargetProgram, SaveTargetProgram, TargetCatalogSummary, TargetExam, TargetImportPreview, TargetImportResult } from '../../models/target-goal.model';
import { TargetGoalService } from '../../services/target-goal.service';

const PAGE_SIZE = 50;
const SEARCH_DEBOUNCE_MS = 300;
/** Önizleme adımında grup başına gösterilen satır; sayılar tüm dosya içindir. */
const PREVIEW_ROWS = 10;

/** Format paragrafındaki başlıklarla örnek dosya; sunucu bu başlıkları tanır (TargetProgramCsv). */
const SAMPLE_CSV: Record<TargetExam, string> = {
  lgs: 'Okul;İl;İlçe;Taban Puan;Yüzdelik\nÖrnek Fen Lisesi;İstanbul;Kadıköy;478,20;0,15\nÖrnek Anadolu Lisesi;Ankara;Çankaya;452,70;2,40\n',
  yks: 'Program;Üniversite;İl;Puan Türü;Taban Puan;Yüzdelik\nBilgisayar Mühendisliği;Örnek Üniversitesi;Ankara;SAY;480,50;1,20\nHukuk;Örnek Üniversitesi;İstanbul;EA;455,30;3,10\n',
};
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
 * #900: üstte sınav başına yüklü veri durumu; CSV yükleme üç adımlı diyalog (dosya → önizleme → sonuç); arama yazarken süzer.
 */
@Component({
  selector: 'app-target-programs',
  standalone: true,
  imports: [FormsModule, MatButtonModule, MatButtonToggleModule, MatCardModule, MatDialogModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, TranslocoDirective],
  providers: [provideTranslocoScope('target-goals')],
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrls: ['./mock-exams.scss'],
  template: `
    <div class="me-root" *transloco="let t; prefix: 'target-goals.admin'">
      <div class="tp-head">
        <div>
          <h1 class="me-title">{{ t('title') }}</h1>
          <p class="me-note">{{ t('subtitle') }}</p>
        </div>
        <div class="me-actions">
          <button mat-stroked-button type="button" (click)="add()">{{ t('add') }}</button>
          <button mat-flat-button type="button" data-testid="open-import" (click)="openImport()">
            <mat-icon>upload_file</mat-icon>{{ t('import.title') }}
          </button>
        </div>
      </div>

      @if (summary(); as rows) {
        <div class="tp-status" data-testid="catalog-status">
          @for (row of rows; track row.exam) {
            <div class="tp-status-item" [class.tp-status-item--empty]="row.total === 0" [attr.data-exam]="row.exam">
              <b>{{ row.exam.toUpperCase() }}{{ row.latestYear ? ' ' + row.latestYear : '' }}</b>
              @if (row.total === 0) {
                <span>{{ t('status.none') }}</span>
                <span class="tp-status-warn"><mat-icon aria-hidden="true">warning</mat-icon>{{ t('status.studentsBlocked') }}</span>
              } @else {
                <span>{{ t('status.' + row.exam, { count: formatCount(row.latestYearCount) }) }}</span>
                @if (row.total > row.latestYearCount) {
                  <span class="me-note">{{ t('status.allYears', { count: formatCount(row.total) }) }}</span>
                }
              }
            </div>
          }
        </div>
      }

      @if (imported(); as result) {
        <p class="me-note" role="status">{{ t('import.done', { created: result.created, updated: result.updated, unchanged: result.unchanged, skipped: result.skipped }) }}</p>
      }

      <div class="tp-filter">
        <mat-button-toggle-group [value]="exam()" (change)="changeExam($event.value)" [attr.aria-label]="t('exam')" hideSingleSelectionIndicator>
          <mat-button-toggle value="lgs">LGS</mat-button-toggle>
          <mat-button-toggle value="yks">YKS</mat-button-toggle>
        </mat-button-toggle-group>
        <mat-form-field appearance="outline" subscriptSizing="dynamic" class="tp-search">
          <mat-label>{{ t('search') }}</mat-label>
          <mat-icon matPrefix aria-hidden="true">search</mat-icon>
          <input matInput maxlength="100" autocomplete="off" [ngModel]="term()" (ngModelChange)="search($event)" (keyup.enter)="load(0)" />
        </mat-form-field>
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
        <p class="me-note">{{ term().trim() ? t('noMatch') : t('empty') }}</p>
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

    <ng-template #importDialog>
      <ng-container *transloco="let t; prefix: 'target-goals.admin'">
        <h2 mat-dialog-title>{{ t('import.stepTitle', { step: importStep(), total: 3 }) }}</h2>
        <mat-dialog-content class="tp-dialog">
          @switch (importStep()) {
            @case (1) {
              <p class="me-note">{{ t('import.format') }}</p>
              <p><button mat-button type="button" class="tp-sample" (click)="downloadSample()"><mat-icon>download</mat-icon>{{ t('import.sample', { exam: exam().toUpperCase() }) }}</button></p>
              <div class="tp-step-row">
                <mat-button-toggle-group [value]="exam()" (change)="changeImportExam($event.value)" [attr.aria-label]="t('exam')" hideSingleSelectionIndicator>
                  <mat-button-toggle value="lgs">LGS</mat-button-toggle>
                  <mat-button-toggle value="yks">YKS</mat-button-toggle>
                </mat-button-toggle-group>
              </div>
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
              <input #fileInput type="file" accept=".csv,text/csv" hidden (change)="pickFile(fileInput.files)" />
              <button mat-stroked-button type="button" (click)="fileInput.click()"><mat-icon>attach_file</mat-icon>{{ file()?.name ?? t('import.pick') }}</button>
            }
            @case (2) {
              @if (previewed(); as report) {
                <p class="tp-counts" data-testid="import-counts">{{ t('import.counts', { fresh: report.newCount, changed: report.changedCount, unchanged: report.unchangedCount, invalid: report.invalidCount }) }}</p>
                <p class="me-note">{{ file()?.name }} · {{ exam().toUpperCase() }} {{ importYear() }}</p>
                @if (report.changed.length) {
                  <h3 class="tp-h3">{{ t('import.changed') }}</h3>
                  @for (row of report.changed.slice(0, previewRows); track row.line) {
                    <div class="tp-line"><span>{{ row.name }} · {{ row.city }}</span><span>{{ row.previousMinimumScore }} → {{ row.minimumScore }}</span></div>
                  }
                }
                @if (report.new.length) {
                  <h3 class="tp-h3">{{ t('import.fresh') }}</h3>
                  @for (row of report.new.slice(0, previewRows); track row.line) {
                    <div class="tp-line"><span>{{ row.name }} · {{ row.institution }} {{ row.city }}</span><span>{{ row.minimumScore }}</span></div>
                  }
                }
                @if (report.invalid.length) {
                  <h3 class="tp-h3">{{ t('import.invalid') }}</h3>
                  @for (row of report.invalid.slice(0, previewRows); track row.line) {
                    <div class="tp-line"><span>{{ t('import.line', { line: row.line }) }} {{ row.name }}</span><span class="me-error">{{ t('errors.' + row.code) }}</span></div>
                  }
                }
                @if (listCapped()) {
                  <p class="me-note">{{ t('import.firstRows', { count: previewRows }) }}</p>
                }
              }
            }
            @case (3) {
              @if (imported(); as result) {
                <p class="tp-done" role="status"><mat-icon aria-hidden="true">check_circle</mat-icon>{{ t('import.done', { created: result.created, updated: result.updated, unchanged: result.unchanged, skipped: result.skipped }) }}</p>
              }
            }
          }
          @if (importError(); as code) {
            <div class="me-error" role="alert">{{ t('errors.' + code) }}</div>
          }
        </mat-dialog-content>
        <mat-dialog-actions align="end">
          @switch (importStep()) {
            @case (1) {
              <button mat-button type="button" mat-dialog-close>{{ t('cancel') }}</button>
              <button mat-flat-button type="button" [disabled]="!canPreview() || busy()" (click)="preview()">{{ t('import.preview') }}</button>
            }
            @case (2) {
              <button mat-button type="button" (click)="backToFile()">{{ t('import.back') }}</button>
              @if (previewed(); as report) {
                <button mat-flat-button type="button" [disabled]="busy() || report.newCount + report.changedCount === 0" (click)="confirm()">
                  {{ t('import.confirmCount', { count: report.newCount + report.changedCount }) }}
                </button>
              }
            }
            @case (3) {
              <button mat-flat-button type="button" mat-dialog-close>{{ t('import.close') }}</button>
            }
          }
        </mat-dialog-actions>
      </ng-container>
    </ng-template>
  `,
})
export class TargetProgramsComponent {
  private readonly service = inject(TargetGoalService);
  private readonly dialog = inject(MatDialog);
  private readonly transloco = inject(TranslocoService);
  private readonly importDialog = viewChild.required<TemplateRef<unknown>>('importDialog');
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly pageSize = PAGE_SIZE;
  protected readonly previewRows = PREVIEW_ROWS;
  protected readonly allScoreTypes = [...SCORE_TYPES.lgs, ...SCORE_TYPES.yks];

  protected readonly exam = signal<TargetExam>('lgs');
  protected readonly term = signal('');
  protected readonly items = signal<AdminTargetProgram[]>([]);
  protected readonly total = signal(0);
  protected readonly skip = signal(0);
  protected readonly loadFailed = signal(false);
  protected readonly busy = signal(false);
  protected readonly summary = signal<TargetCatalogSummary[] | null>(null);
  protected readonly importStep = signal<1 | 2 | 3>(1);

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
    return !!report && (report.new.length > PREVIEW_ROWS || report.changed.length > PREVIEW_ROWS || report.invalid.length > PREVIEW_ROWS
      || report.new.length < report.newCount || report.changed.length < report.changedCount || report.invalid.length < report.invalidCount);
  });

  constructor() {
    this.load(0);
    this.loadSummary();
    inject(DestroyRef).onDestroy(() => clearTimeout(this.searchTimer));
  }

  private loadSummary(): void {
    this.service.adminSummary().subscribe({ next: (rows) => this.summary.set(rows), error: () => this.summary.set(null) });
  }

  protected formatCount(value: number): string {
    return value.toLocaleString(this.transloco.getActiveLang() === 'en' ? 'en-US' : 'tr-TR');
  }

  /** Yazarken süzer; Enter beklemeden hemen arar. */
  protected search(term: string): void {
    this.term.set(term);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.load(0), SEARCH_DEBOUNCE_MS);
  }

  protected load(skip: number): void {
    clearTimeout(this.searchTimer);
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

  /** Diyalogdaki sınav seçimi listeyi de o sınava çevirir: yüklenen satırlar hemen görünür. */
  protected changeImportExam(exam: TargetExam): void {
    this.changeExam(exam);
    this.resetImport();
  }

  protected openImport(): void {
    this.file.set(null);
    this.resetImport();
    this.dialog.open(this.importDialog(), { width: '640px', maxWidth: '95vw', autoFocus: 'dialog' }).afterClosed().subscribe(() => {
      this.previewed.set(null);
      this.importStep.set(1);
    });
  }

  protected backToFile(): void {
    this.previewed.set(null);
    this.importError.set(null);
    this.importStep.set(1);
  }

  protected downloadSample(): void {
    const exam = this.exam();
    const url = URL.createObjectURL(new Blob(['\uFEFF' + SAMPLE_CSV[exam]], { type: 'text/csv;charset=utf-8' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = `taban-puan-ornek-${exam}.csv`;
    link.click();
    URL.revokeObjectURL(url);
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
    this.importStep.set(1);
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
        this.importStep.set(2);
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
        this.importStep.set(3);
        this.busy.set(false);
        this.load(0);
        this.loadSummary();
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
        this.loadSummary();
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
      next: () => {
        this.load(this.skip());
        this.loadSummary();
      },
      error: () => this.loadFailed.set(true),
    });
  }
}
