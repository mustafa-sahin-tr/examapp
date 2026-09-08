import {
  Component,
  Input,
  Output,
  EventEmitter,
  signal,
  ElementRef,
  ViewChild,
  AfterViewInit,
  OnDestroy,
  inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { AnswerChoice, QuestionRegion } from '../../../models/draws';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

const EMPTY_REGION: QuestionRegion = {
  id: 0,
  name: '',
  x: 0,
  y: 0,
  width: 0,
  height: 0,
  passageId: '',
  answers: [],
  imageId: '',
  imageUrl: '',
  exampleAnswer: null,
  isExample: false,
};

type LayoutType = 'side-1col' | 'side-2col' | 'top-1row' | 'top-2row' | 'top-4row';

interface LayoutResult {
  layout: LayoutType;
  currentMarginLeft: number;
  answerMarginLeft: number;
  answerMinWidth: number;
}

/** visualScale güncellemesi için minimum anlamlı fark (~%1). */
const VISUAL_SCALE_EPSILON = 0.01;
/**
 * İçerik hâlâ taşarken küçültme adımları için daha ince eşik: küçültme yönü monoton olduğundan
 * salınım riski yoktur; %1 eşiğiyle durulursa birkaç piksellik artık taşma kalabiliyor.
 */
const VISUAL_SCALE_FINE_EPSILON = 0.002;
/** Ölçek alt/üst sınırı: 0.2'nin altına inilmez, doğal boyutun üstüne çıkılmaz. */
const MIN_VISUAL_SCALE = 0.2;
const MAX_VISUAL_SCALE = 1;
/** Yükseklik kıyasında piksel yuvarlama toleransı. */
const HEIGHT_FIT_TOLERANCE_PX = 1;

function clampVisualScale(value: number): number {
  return Math.max(MIN_VISUAL_SCALE, Math.min(MAX_VISUAL_SCALE, value));
}

@Component({
  selector: 'app-question-canvas-view-v5',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule],
  templateUrl: './question-canvas-view-v5.component.html',
  styleUrls: ['./question-canvas-view-v5.component.scss'],
})
export class QuestionCanvasViewComponentv5 {
  @ViewChild('questionImage') private questionImageRef?: ElementRef<HTMLImageElement>;
  @ViewChild('questionImageBox') private questionImageBoxRef?: ElementRef<HTMLDivElement>;
  @ViewChild('qcvRoot') private rootRef?: ElementRef<HTMLDivElement>;

  /** Host elementi: tüketici buna yükseklik sınırı verirse içerik dikeyde de sığdırılır. */
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  private resizeObserver?: ResizeObserver;

  public contentScale = 1;
  public visualScale = signal<number>(1);
  /**
   * Soru görseline uygulanan açık genişlik (px). Yalnızca yükseklik sınırı genişlik oranından
   * daha kısıtlayıcı olduğunda dolu; aksi hâlde null → eski CSS davranışı (max-width: 100%).
   */
  public questionImageWidth = signal<number | null>(null);

  public questionImageSource = signal<string | null>(null);
  public passageImageSource = signal<string | null>(null);
  public _questionRegion = signal<QuestionRegion>(EMPTY_REGION);
  readonly currentLayout = signal<LayoutType>('top-4row');
  readonly currentMarginLeft = signal<number>(0);
  readonly answerMarginLeft = signal<number>(0);
  readonly answerMinWidth = signal<number>(0);

  // Sıralı cevaplar getter'ı: order > tag > id
  public get sortedAnswers(): AnswerChoice[] {
    const answers = this._questionRegion().answers || [];
    return [...answers].sort((a, b) => {
      // Öncelik: order, sonra label, sonra id
      if (a.order != null && b.order != null && a.order !== b.order) return a.order - b.order;
      if (a.label && b.label && a.label !== b.label) return a.label.localeCompare(b.label);
      return (a.id ?? 0) - (b.id ?? 0);
    });
  }
  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    const region = value ?? EMPTY_REGION;
    // Veri elimizde olduğu için direkt hesaplıyoruz; saf hesap sonucu component state'ine burada yazılır
    const result = this.calculateBestLayout(region);
    this.currentLayout.set(result.layout);
    this.currentMarginLeft.set(result.currentMarginLeft);
    this.answerMarginLeft.set(result.answerMarginLeft);
    this.answerMinWidth.set(result.answerMinWidth);

    // 2. Cevapları ID'ye göre sırala
    if (region.answers) {
      region.answers = [...region.answers].sort((a, b) => a.label.localeCompare(b.label) || a.id - b.id);
    }

    this._questionRegion.set(region);
    const transformedQuestionUrl = this.transformQuestionImageUrl(region.imageUrl);
    this.questionImageSource.set(transformedQuestionUrl);
    const passageUrl = region?.passage?.imageUrl ?? null;
    this.passageImageSource.set(passageUrl && passageUrl.trim().length > 0 ? passageUrl : null);
    queueMicrotask(() => this.updateVisualScale());
  }
  @Input() correctAnswerVisible: boolean = false;
  @Input() isPreviewMode: boolean = false;
  /**
   * Yükseklik-bazlı sığdırma (issue #74) için açık opt-in. Varsayılan false: ölçek yalnızca
   * genişlik oranından hesaplanır ve host'un display/clientHeight'ı ne olursa olsun hiçbir
   * yükseklik ölçümü yapılmaz (practice-solve / image-selector / v2 kod yolu eskisiyle aynı).
   * Tüketici host'a definite yükseklik veriyorsa (test-solve-canvas-v3) true verir.
   */
  @Input() enableHeightFit: boolean = false;
  @Input() hidePassage: boolean = false;
  @Input() set selectedChoice(choice: AnswerChoice | undefined) {
    this._selectedChoice.set(choice);
  }
  @Input() set correctChoice(choice: AnswerChoice | undefined) {
    this._correctChoice.set(choice);
  }
  @Input() set mode(m: 'exam' | 'result' | null) {
    this._mode.set(m || 'exam');
  }

  ngAfterViewInit(): void {
    const imageBox = this.questionImageBoxRef?.nativeElement;
    if (!imageBox) {
      return;
    }

    this.resizeObserver = new ResizeObserver(() => this.updateVisualScale());
    this.resizeObserver.observe(imageBox);
    if (this.enableHeightFit) {
      // Host'un kullanılabilir yüksekliği değişince (pencere/dock/panel) yeniden sığdır.
      this.resizeObserver.observe(this.host.nativeElement);
    }
    this.updateVisualScale();
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
  }

  /**
   * Saf hesap fonksiyonu: region'dan layout + margin/genişlik değerlerini üretir.
   * Component state'ine yan etki yapmaz; sonucu setter kendi signal'larına yazar.
   * Eşikler (1.1, 800, 0.6, %10 tolerans, maxAns) docs/question-layout-algorithm-analysis.md ile doğrulanmıştır.
   */
  private calculateBestLayout(region: QuestionRegion): LayoutResult {
    const { width: qW, sanitizedHeight, height: qH, answers } = region;
    const effectiveHeight = sanitizedHeight || qH;
    const qRatio = qW / effectiveHeight;

    const maxAns = answers?.reduce((max, ans) => (ans.width > (max?.width || 0) ? ans : max), answers?.[0]);
    const aW = 20 + (maxAns?.width || 0); // 2 × (padding 8 + border ~2) — iki taraflı pay + en geniş şık görseli
    const answerMinWidth = aW;
    let currentMarginLeft = 0;
    let answerMarginLeft = 0;
    const gap = 12; // CSS'teki gap değeriyle uyumlu olmalı

    // 1. Durum: Soru Dikey veya Kareyse (Yan yana yerleşim)
    if (qRatio < 1.1) {
      const layout: LayoutType = effectiveHeight > 800 || qRatio < 0.6 ? 'side-1col' : 'side-2col';
      return { layout, currentMarginLeft, answerMarginLeft, answerMinWidth };
    }

    // 2. Durum: Soru Yatay ise (Genişlik bazlı hiyerarşik kontrol)
    // 4 şık yan yana sığar mı? (%10 tolerans)
    const totalWidth4 = aW * 4 + gap * 3;
    if (totalWidth4 < qW * 1.1) {
      if (totalWidth4 > qW) {
        currentMarginLeft = Math.max(0, (totalWidth4 - qW) / 2);
      } else {
        currentMarginLeft = 0;
        answerMarginLeft = Math.max(0, (qW - totalWidth4) / 2);
      }
      return { layout: 'top-1row', currentMarginLeft, answerMarginLeft, answerMinWidth }; // 4 tane yan yana
    }

    // 2 şık yan yana sığar mı?
    const totalWidth2 = aW * 2 + gap;
    if (totalWidth2 < qW * 1.1) {
      if (totalWidth2 > qW) {
        currentMarginLeft = Math.max(0, (totalWidth2 - qW) / 2);
      } else {
        currentMarginLeft = 0;
        answerMarginLeft = Math.max(0, (qW - totalWidth2) / 2);
      }
      return { layout: 'top-2row', currentMarginLeft, answerMarginLeft, answerMinWidth }; // 2 satır 2 sütun (2x2)
    }

    // Sığmıyorsa alt alta
    return { layout: 'top-4row', currentMarginLeft, answerMarginLeft, answerMinWidth };
  }

  @Output() hoverRegion = new EventEmitter<MouseEvent>();
  @Output() selectRegion = new EventEmitter<MouseEvent>();
  @Output() choiceSelected = new EventEmitter<AnswerChoice>();
  @Output() answerOpened = new EventEmitter<number>();
  @Output() questionRemove = new EventEmitter<number>();

  public hoveredChoice = signal<AnswerChoice | null>(null);
  private _selectedChoice = signal<AnswerChoice | undefined>(undefined);
  private _correctChoice = signal<AnswerChoice | undefined>(undefined);
  private _mode = signal<'exam' | 'result'>('exam');

  public getAnswerClasses(answer: AnswerChoice): Record<string, boolean> {
    const mode = this._mode();
    const isSelected = this._selectedChoice() === answer;
    const isCorrect = !!answer.isCorrect;
    const isHovered = this.hoveredChoice() === answer;
    return {
      'is-selected': isSelected,
      'is-correct': mode === 'exam' ? isCorrect || isSelected : isCorrect,
      'is-hovered': mode === 'exam' && !isCorrect && !isSelected && isHovered,
      'is-incorrect': mode === 'result' && isSelected && !isCorrect,
    };
  }

  /** Klavye/ekran okuyucu için `aria-pressed` durumu. */
  isSelected(answer: AnswerChoice): boolean {
    return this._selectedChoice() === answer;
  }

  hoverAnswer(answer: AnswerChoice | null) {
    this.hoveredChoice.set(answer);
  }

  selectAnswer(index: number) {
    const answer = this.sortedAnswers?.[index];
    if (!answer) return;
    this._selectedChoice.set(answer);
    this.choiceSelected.emit(answer);
  }

  showCorrectAnswer() {
    this.answerOpened.emit(1);
  }

  removeQuestion() {
    this.questionRemove.emit(this._questionRegion().id);
  }

  private transformQuestionImageUrl(url?: string | null): string | null {
    if (!url) {
      return null;
    }

    return url.replace(/question(\.[^/?#]+)?$/i, (_match, ext) => `question-v2${ext ?? ''}`);
  }

  /**
   * Görsel ölçek = min(genişlik oranı, yükseklik oranı).
   *
   * - Genişlik oranı (eski davranış): görselin kutuya sığan genişliği / region genişliği.
   * - Yükseklik oranı: yalnızca host (app-question-canvas-view-v5) tüketici tarafından dikeyde
   *   SINIRLANDIRILMIŞSA devreye girer (host.clientHeight < içerik yüksekliği). Sınır yoksa host
   *   içerikle birlikte büyür, içerik ≤ host olur ve ölçek genişlik oranında kalır; inline host'ta
   *   clientHeight 0'dır ve yine kısıt yok sayılır. Böylece practice-solve / image-selector / v2
   *   tüketicilerinin davranışı değişmez (issue #74).
   *
   * Yükseklik uyumu iteratiftir: ölçek → içerik yüksekliği → ResizeObserver → yeniden hesap.
   * İçerik = a·ölçek + b (b: gap/padding gibi ölçeklenmeyen paylar) olduğundan her adım hedefe
   * üstten yaklaşır, salınım yapmaz; EPSILON altı farklar yok sayılarak döngü durur.
   */
  public updateVisualScale(): void {
    if (!this.enableHeightFit) {
      this.updateWidthOnlyScale();
      return;
    }

    const questionWidth = this._questionRegion().width || 0;
    if (!questionWidth) {
      this.visualScale.set(1);
      this.questionImageWidth.set(null);
      return;
    }

    const img = this.questionImageRef?.nativeElement;
    const box = this.questionImageBoxRef?.nativeElement;
    if (!img || !box) {
      return;
    }

    // Açık width uygulanmamışken CSS'in (max-width: 100%, height: auto) ürettiği genişlik.
    // Uygulanmışsa rect bizim yazdığımız değeri gösterir; o durumda CSS'in üreteceği genişlik
    // min(doğal genişlik, kutu genişliği) ile yeniden türetilir.
    const unconstrainedWidth =
      this.questionImageWidth() === null
        ? img.getBoundingClientRect().width
        : Math.min(img.naturalWidth || 0, box.clientWidth || 0);
    if (!unconstrainedWidth) {
      return;
    }

    const widthRatio = clampVisualScale(unconstrainedWidth / questionWidth);
    const fit = this.measureHeightFit();
    const heightRatio = fit ? this.heightFitRatio(fit, widthRatio) : widthRatio;
    const ratio = clampVisualScale(Math.min(widthRatio, heightRatio));

    // Eşik altı farklarda signal'ı güncelleme: şık genişliği → sayfa yüksekliği → scrollbar →
    // konteyner genişliği → ResizeObserver zinciriyle oluşabilecek piksellik salınımı keser
    // ve (load) + microtask + ResizeObserver'ın aynı değeri art arda yazmasını engeller.
    // İçerik hâlâ taşıyorsa ve küçülüyorsak (monoton yön) ince eşik kullanılır.
    const current = this.visualScale();
    const shrinkingToFit = fit !== null && !fit.fits && ratio < current;
    const threshold = shrinkingToFit ? VISUAL_SCALE_FINE_EPSILON : VISUAL_SCALE_EPSILON;
    if (Math.abs(ratio - current) < threshold) {
      return;
    }
    this.visualScale.set(ratio);

    // Soru görseline açık genişlik yalnızca yükseklik sınırlayıcıyken yazılır; aksi hâlde null
    // bırakılır ki DOM eski davranışla birebir aynı kalsın.
    const heightLimited = ratio < widthRatio - VISUAL_SCALE_EPSILON;
    this.questionImageWidth.set(heightLimited ? Math.round((unconstrainedWidth * ratio) / widthRatio) : null);
  }

  /**
   * Eski (yalnızca genişlik) hesaplama — `enableHeightFit=false` iken birebir bu yol çalışır.
   * Soru görseline açık genişlik yazılmaz, hiçbir yükseklik ölçümü yapılmaz.
   */
  private updateWidthOnlyScale(): void {
    const questionWidth = this._questionRegion().width || 0;
    if (!questionWidth) {
      this.visualScale.set(1);
      return;
    }

    const renderedWidth = this.questionImageRef?.nativeElement.getBoundingClientRect().width || 0;
    if (!renderedWidth) {
      return;
    }

    const ratio = clampVisualScale(renderedWidth / questionWidth);
    // Eşik altı farklarda signal'ı güncelleme: şık genişliği → sayfa yüksekliği → scrollbar →
    // konteyner genişliği → ResizeObserver zinciriyle oluşabilecek piksellik salınımı keser
    // ve (load) + microtask + ResizeObserver'ın aynı değeri art arda yazmasını engeller.
    if (Math.abs(ratio - this.visualScale()) < VISUAL_SCALE_EPSILON) {
      return;
    }
    this.visualScale.set(ratio);
  }

  /**
   * Host'un kullanılabilir yüksekliği ile içeriğin yüksekliğini ölçer.
   * Yükseklik kısıtı yoksa (inline host → clientHeight 0, ya da root yok) null döner.
   */
  private measureHeightFit(): { available: number; content: number; fits: boolean } | null {
    const available = this.host.nativeElement.clientHeight;
    const root = this.rootRef?.nativeElement;
    if (!root || available <= 0) {
      return null;
    }
    const content = root.getBoundingClientRect().height;
    if (content <= 0) {
      return null;
    }
    return { available, content, fits: content <= available + HEIGHT_FIT_TOLERANCE_PX };
  }

  /** Ölçüme göre hedef ölçek; sığıyorsa widthRatio'ya (eski davranış) geri döner. */
  private heightFitRatio(fit: { available: number; content: number; fits: boolean }, widthRatio: number): number {
    const current = this.visualScale();
    const step = current * (fit.available / fit.content);
    if (fit.fits) {
      // Sığıyor: daha önce küçültüldüyse boş alana göre geri büyümeyi dene (widthRatio'yu aşma).
      return current < widthRatio ? Math.min(widthRatio, step) : widthRatio;
    }
    // Taşıyor: taşma oranı kadar küçült (clamp çağıran tarafta).
    return step;
  }

  public getScaledAnswerWidth(answer: AnswerChoice): number | null {
    const answerWidth = answer.width || 0;
    if (!answerWidth) {
      return null;
    }

    return Math.round(answerWidth * this.visualScale());
  }

  enlargeImage() {
    this.rescaleQuestion(1.05);
  }

  shrinkImage() {
    this.rescaleQuestion(0.95);
  }

  retsetImageScale() {
    this.contentScale = 1;
  }

  public rescaleQuestion(factor: number) {
    const next = this.contentScale * factor;
    this.contentScale = Math.max(0.2, Math.min(3, next));
  }
}
