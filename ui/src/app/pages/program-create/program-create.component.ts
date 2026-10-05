import {
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { combineLatest, take } from 'rxjs';
import { AbstractControl, FormsModule } from '@angular/forms';
import { ErrorStateMatcher } from '@angular/material/core';
import { DOCUMENT } from '@angular/common';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatExpansionModule } from '@angular/material/expansion';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';

import { Option, ProgramStep } from '../../models/programstep';
import { CreateProgramRequest, UserSelection } from '../../models/program.interfaces';
import { ProgramService } from '../../services/program.service';
import { resolveProgramOptionIcon } from '../../shared/utils/program-option-icon.util';
import { OptionCardComponent } from '../../shared/components/option-card/option-card.component';
import { OptionGroupComponent } from '../../shared/components/option-card/option-group.component';
import { WizardProgressComponent } from '../../shared/components/wizard-progress/wizard-progress.component';
import {
  SelectionSummaryComponent,
  SelectionSummaryItem,
} from '../../shared/components/selection-summary/selection-summary.component';
import { ProgramOptionIconPipe } from './program-option-icon.pipe';
import {
  WizardAnswers,
  hasDistinctBranches,
  isEndAfterStart,
  toLocalDateString,
  reachableFrom,
  remainingRange,
  resolveNext,
  selectedOptions,
  toggleValue,
} from './program-wizard.logic';

/** Sayfanın Transloco scope'u: `public/i18n/program-create/<lang>.json` (issue #183). */
const SCOPE = 'program-create';

const DEFAULT_PROGRAM_DAYS = 30;

export type WizardViewState = 'loading' | 'error' | 'empty' | 'step' | 'form';

/** "Geri al" için bir adıma girildiği andaki durum. */
interface WizardSnapshot {
  path: readonly number[];
  answers: WizardAnswers;
}

/** Dal değişince silinen cevapların bildirimi (Karar 2). */
export interface PathChangeNotice {
  count: number;
  snapshot: WizardSnapshot;
}

/**
 * Program oluşturma sihirbazı (issue #135; dilim 1 adım ekranı, dilim 2 son adım + durum ekranları). Durum:
 * - `steps`: API'den gelen adımlar (değiştirilmez; seçimler `answers`'ta tutulur).
 * - `path`: gezinme geçmişi — ilk adımdan şu anki adıma kadar gezilen adım id'leri. Geri gezinme ve
 *   submit bu diziden okunur; böylece API'ye yalnız geçerli yoldaki adımlar gider.
 * - `answers`: adım id → seçili değerler.
 * - `viewState`: loading / error / empty / step / form.
 */
@Component({
  selector: 'app-program-create',
  standalone: true,
  imports: [
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatFormFieldModule,
    MatInputModule,
    MatDatepickerModule,
    MatExpansionModule,
    TranslocoDirective,
    ProgramOptionIconPipe,
    OptionCardComponent,
    OptionGroupComponent,
    WizardProgressComponent,
    SelectionSummaryComponent,
  ],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './program-create.component.html',
  styleUrl: './program-create.component.scss',
})
export class ProgramCreateComponent implements OnInit {
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly programService = inject(ProgramService);
  private readonly router = inject(Router);

  readonly steps = signal<ProgramStep[]>([]);
  readonly path = signal<readonly number[]>([]);
  readonly answers = signal<WizardAnswers>(new Map());
  readonly viewState = signal<WizardViewState>('loading');
  readonly pathChange = signal<PathChangeNotice | null>(null);
  readonly submitting = signal(false);
  /** Son gönderim başarısız oldu: form üstünde `role=alert` bant; seçimler ve girilenler korunur. */
  readonly submitError = signal(false);

  /** Yükleniyor iskeleti: kartta 4 seçenek karosu, rayda 2 satır. */
  readonly skeletonTiles = [1, 2, 3, 4] as const;
  readonly skeletonRailRows = [1, 2] as const;

  // Form alanları (son adım).
  readonly programName = signal('');
  readonly programDescription = signal('');
  readonly programStartDate = signal<Date | null>(null);
  readonly programEndDate = signal<Date | null>(null);

  /** Şu anki adıma girildiği andaki durum; dal değişince "Geri al" buna döner. */
  private entrySnapshot: WizardSnapshot | null = null;

  /** Soru / form başlığı (h2, tabindex=-1); adım ve form aynı öğeyi paylaşır, gezinmeden sonra odak buraya taşınır. */
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  /** Gönderim hatası bandı (tabindex=-1); hata sonrası odak buraya taşınır. */
  private readonly submitAlert = viewChild<ElementRef<HTMLElement>>('submitAlert');
  /**
   * Bekleyen odak isteği. Hedef öğe henüz çizilmemiş olabilir (ör. tekrar dene sonrası başlık, hata bandı);
   * effect viewChild'ı da izlediği için öğe gelince odaklar ve isteği temizler. İlk yüklemede istek yok.
   */
  private readonly focusRequest = signal<{ target: 'heading' | 'alert' } | null>(null);

  /** `prefers-reduced-motion: reduce` ise buton içi dönen spinner yerine statik ikon gösterilir. */
  readonly reducedMotion =
    inject(DOCUMENT).defaultView?.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;

  readonly nameValid = computed(() => this.programName().trim().length > 0);
  /** Bitiş, başlangıçtan sonraki bir günde olmalı (gün bazında; datepicker gece yarısı verir). */
  readonly dateOrderInvalid = computed(() => {
    const start = this.programStartDate();
    const end = this.programEndDate();
    return !!start && !!end && !isEndAfterStart(start, end);
  });
  readonly formValid = computed(
    () => this.nameValid() && !!this.programStartDate() && !!this.programEndDate() && !this.dateOrderInvalid(),
  );

  /** Ad: dokunulduktan sonra boş (yalnız boşluk dahil) ise hata. */
  readonly nameErrorMatcher: ErrorStateMatcher = {
    isErrorState: (control: AbstractControl | null) => !!control && control.touched && !this.nameValid(),
  };
  /** Başlangıç: dokunulduktan sonra boş / ayrıştırılamıyorsa hata. */
  readonly startErrorMatcher: ErrorStateMatcher = {
    isErrorState: (control: AbstractControl | null) => !!control && control.touched && !this.programStartDate(),
  };
  /** Bitiş: boşsa (dokunulduktan sonra) ya da tarih sırası bozuksa (başlangıç değişince de) hata. */
  readonly endErrorMatcher: ErrorStateMatcher = {
    isErrorState: (control: AbstractControl | null) =>
      !!control && ((control.touched && !this.programEndDate()) || this.dateOrderInvalid()),
  };

  constructor() {
    effect(() => {
      const request = this.focusRequest();
      if (!request) return;
      const target = request.target === 'alert' ? this.submitAlert() : this.heading();
      if (!target) return;
      untracked(() => target.nativeElement.focus());
      this.focusRequest.set(null);
    });
  }

  readonly stepsById = computed(() => new Map(this.steps().map((s) => [s.id, s] as const)));

  readonly currentStep = computed<ProgramStep | undefined>(() => {
    if (this.viewState() !== 'step') return undefined;
    const path = this.path();
    return this.stepsById().get(path[path.length - 1]);
  });

  readonly currentValues = computed<readonly string[]>(() => {
    const step = this.currentStep();
    return step ? (this.answers().get(step.id) ?? []) : [];
  });

  readonly selectedValueSet = computed(() => new Set(this.currentValues()));
  readonly hasSelection = computed(() => this.currentValues().length > 0);

  /** Şu anki sorudan sonra yolda kalan soru sayısı aralığı (`nextStep` grafiği, döngü korumalı). */
  private readonly remaining = computed(() => {
    const step = this.currentStep();
    return step ? remainingRange(this.stepsById(), step.id, this.answers()) : { min: 0, max: 0 };
  });

  /** Kalan yol uzunluğu; dal belirsizse en uzun dal. */
  readonly remainingPathLength = computed(() => this.remaining().max);

  /** Dallar farklı uzunluktaysa toplam "en fazla" olarak gösterilir. */
  readonly totalExact = computed(() => this.remaining().min === this.remaining().max);

  readonly branchUnknown = computed(() => {
    const step = this.currentStep();
    return !!step && !this.hasSelection() && hasDistinctBranches(step);
  });

  readonly atForm = computed(() => this.viewState() === 'form');

  /** Geçilmiş (tamamlanmış) soru sayısı. */
  readonly doneCount = computed(() => (this.atForm() ? this.path().length : Math.max(0, this.path().length - 1)));

  /** 1 tabanlı şu anki durak. */
  readonly position = computed(() => this.doneCount() + 1);

  /** Toplam durak = cevaplanan + şu an + kalan yol + form. */
  readonly total = computed(() =>
    this.atForm() ? this.path().length + 1 : this.path().length + this.remainingPathLength() + 1,
  );

  /** Özet satırları: yoldaki cevaplanmış adımlar (soru ekranında şu anki adım hariç). */
  readonly summaryItems = computed<SelectionSummaryItem[]>(() => {
    const stepsById = this.stepsById();
    const answers = this.answers();
    const path = this.atForm() ? this.path() : this.path().slice(0, -1);
    const items: SelectionSummaryItem[] = [];
    for (const stepId of path) {
      const step = stepsById.get(stepId);
      const options = step ? selectedOptions(step, answers.get(stepId) ?? []) : [];
      if (!step || options.length === 0) continue;
      items.push({
        stepId,
        icon: resolveProgramOptionIcon(options[0].icon),
        caption: step.title,
        value: options.map((o) => o.label).join(', '),
        extraCount: options.length - 1,
      });
    }
    return items;
  });

  /** Mobil katlanır onay özetinin başlığı için cevapların kısa listesi. */
  readonly summaryPreview = computed(() => this.summaryItems().map((item) => item.value).join(' · '));

  ngOnInit(): void {
    this.resetFormFields();
    this.loadProgramSteps();
  }

  /** Hata / boş ekranındaki "Tekrar Dene": yükleme başarılı olursa odak soru başlığına taşınır. */
  retryLoad(): void {
    this.loadProgramSteps(true);
  }

  loadProgramSteps(focusOnSuccess = false): void {
    this.viewState.set('loading');
    this.programService.getProgramSteps().subscribe({
      next: (steps) => {
        const list = steps ?? [];
        this.steps.set(list);
        this.answers.set(new Map());
        this.pathChange.set(null);
        if (list.length === 0) {
          this.path.set([]);
          this.viewState.set('empty');
          return;
        }
        this.path.set([list[0].id]);
        this.viewState.set('step');
        this.captureEntry();
        if (focusOnSuccess) this.requestHeadingFocus();
      },
      error: (error) => {
        console.error('Program steps could not be loaded:', error);
        this.steps.set([]);
        this.path.set([]);
        // Hata ekranı `role=alert` + "Tekrar Dene" taşır; ayrıca snackbar açılmaz.
        this.viewState.set('error');
      },
    });
  }

  isSelected(option: Option): boolean {
    return this.selectedValueSet().has(option.value);
  }

  toggleOption(option: Option): void {
    const step = this.currentStep();
    if (!step) return;
    const values = toggleValue(step, this.currentValues(), option);
    this.answers.update((answers) => {
      const next = new Map(answers);
      if (values.length > 0) next.set(step.id, values);
      else next.delete(step.id);
      return next;
    });
  }

  /**
   * Bir sonraki adıma geç. Hedef değişince (dal değişimi) yeni yolda geçersiz kalan cevaplar silinir:
   * geçerli = yolun bugünkü kısmı + yeni hedeften ulaşılabilen adımlar. Aynı hedefte sonraki cevaplar korunur.
   */
  next(): void {
    const step = this.currentStep();
    if (!step) return;
    const values = this.currentValues();
    if (values.length === 0) {
      this.notify('wizard.noSelection', 2000);
      return;
    }

    const stepsById = this.stepsById();
    const resolved = resolveNext(stepsById, step, values);
    // Döngü savunması: hedef zaten gezilen yoldaysa (tutarsız seed) geçersiz nextStep gibi forma geç.
    const cyclic = resolved.kind === 'step' && this.path().includes(resolved.stepId);
    const target = cyclic && resolved.kind === 'step' ? { kind: 'cycle' as const, nextStep: resolved.stepId } : resolved;

    const valid = new Set(this.path());
    if (target.kind === 'step') reachableFrom(stepsById, target.stepId).forEach((id) => valid.add(id));

    // Yeni yolda geçersiz kalan cevaplar koşulsuz silinir; snapshot yalnız bant + "Geri al" içindir.
    const stale = [...this.answers().keys()].filter((id) => !valid.has(id));
    if (stale.length > 0) {
      this.answers.update((answers) => {
        const next = new Map(answers);
        stale.forEach((id) => next.delete(id));
        return next;
      });
    }
    const snapshot = this.entrySnapshot;
    this.pathChange.set(stale.length > 0 && snapshot ? { count: stale.length, snapshot } : null);

    if (target.kind === 'step') {
      this.path.update((path) => [...path, target.stepId]);
      this.captureEntry();
      this.requestHeadingFocus();
      return;
    }

    if (target.kind === 'invalid' || target.kind === 'cycle') {
      // Savunma: nextStep adımlar içinde yok (issue #136) ya da yolda geri dönüyor (döngü).
      // Sihirbaz takılı kalmasın; son adım gibi davranıp forma geç.
      const reason = target.kind === 'invalid' ? 'geçersiz' : 'döngü oluşturan';
      console.warn(
        `[ProgramCreate] Adım ${step.id} ("${step.title}") ${reason} nextStep=${target.nextStep} ` +
          `değerine işaret ediyor; program oluşturma formuna geçiliyor.`,
      );
    }
    this.submitError.set(false);
    this.viewState.set('form');
    this.requestHeadingFocus();
  }

  /** Gezinme geçmişinde bir adım geri. */
  previous(): void {
    if (this.path().length <= 1) return;
    this.path.update((path) => path.slice(0, -1));
    this.pathChange.set(null);
    this.captureEntry();
    this.requestHeadingFocus();
  }

  /** Özet satırı / tamamlanan durak: yoldaki o adıma dön, önceki seçim işaretli gelir. */
  goToStep(stepId: number): void {
    const index = this.path().indexOf(stepId);
    if (index < 0) return;
    this.goToStop(index);
  }

  goToStop(index: number): void {
    const path = this.path();
    if (index < 0 || index >= path.length) return;
    if (this.submitting()) return;
    this.path.set(path.slice(0, index + 1));
    this.viewState.set('step');
    this.pathChange.set(null);
    this.captureEntry();
    this.requestHeadingFocus();
  }

  /** Dal değişimini geri al: silinen cevaplar ve dal değişmeden önceki adım geri gelir. */
  undoPathChange(): void {
    const notice = this.pathChange();
    if (!notice) return;
    this.path.set(notice.snapshot.path);
    this.answers.set(notice.snapshot.answers);
    this.viewState.set('step');
    this.pathChange.set(null);
    this.captureEntry();
    this.requestHeadingFocus();
  }

  /** Submit isteği: yalnız geçerli yoldaki (gezinme geçmişindeki) cevaplanmış adımlar. */
  buildSelections(): UserSelection[] {
    const stepsById = this.stepsById();
    const answers = this.answers();
    return this.path()
      .map((stepId) => {
        const step = stepsById.get(stepId);
        const values = step ? selectedOptions(step, answers.get(stepId) ?? []).map((o) => o.value) : [];
        return { stepId, selectedValues: values };
      })
      .filter((selection) => selection.selectedValues.length > 0);
  }

  createProgram(): void {
    // Geçersiz form satır içi mat-error ile gösterilir ve buton pasiftir; burası yalnız savunma.
    const start = this.programStartDate();
    const end = this.programEndDate();
    if (this.submitting() || !this.formValid() || !start || !end) return;

    const request: CreateProgramRequest = {
      programName: this.programName().trim(),
      description: this.programDescription().trim(),
      // Yerel takvim günü (yyyy-MM-dd): toISOString() TR'de gece yarısını önceki güne kaydırırdı.
      startDate: toLocalDateString(start),
      endDate: toLocalDateString(end),
      userSelections: this.buildSelections(),
    };

    this.submitting.set(true);
    this.submitError.set(false);
    this.programService.createProgram(request).subscribe({
      next: () => {
        this.submitting.set(false);
        this.notify('form.created');
        this.router.navigate(['/programs']);
      },
      error: (error) => {
        console.error('Program create error:', error);
        this.submitting.set(false);
        // Bant `role=alert` ile duyurulur; ayrıca snackbar açmak aynı hatayı iki kez okutur.
        this.submitError.set(true);
        this.focusRequest.set({ target: 'alert' });
      },
    });
  }

  /** Formdan son soruya dön (girilen değerler korunur). */
  cancelProgramCreation(): void {
    if (this.path().length === 0 || this.submitting()) return;
    this.viewState.set('step');
    this.pathChange.set(null);
    this.captureEntry();
    this.requestHeadingFocus();
  }

  private requestHeadingFocus(): void {
    this.focusRequest.set({ target: 'heading' });
  }

  private captureEntry(): void {
    this.entrySnapshot = { path: this.path(), answers: this.answers() };
  }

  private resetFormFields(): void {
    const today = new Date();
    this.programName.set('');
    this.programDescription.set('');
    this.programStartDate.set(today);
    this.programEndDate.set(new Date(today.getTime() + DEFAULT_PROGRAM_DAYS * 24 * 60 * 60 * 1000));
  }

  /**
   * Snackbar metni ve aksiyon etiketi şablon dışında üretildiği için ikisi de `selectTranslate`
   * ile okunur; bu çağrı scope sözlüğünü yükler ve dil değişiminde doğru metni verir.
   */
  private notify(messageKey: string, duration = 3000): void {
    combineLatest([
      this.transloco.selectTranslate<string>(messageKey, {}, SCOPE),
      this.transloco.selectTranslate<string>('actions.ok', {}, SCOPE),
    ])
      .pipe(take(1), takeUntilDestroyed(this.destroyRef))
      .subscribe(([message, action]) => {
        this.snackBar.open(message, action, { duration });
      });
  }
}
