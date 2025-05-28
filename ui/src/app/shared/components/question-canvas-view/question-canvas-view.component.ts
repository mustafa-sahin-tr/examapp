import {
  AfterViewChecked,
  AfterViewInit,
  Component,
  computed,
  effect,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  signal,
  ViewChild,
} from '@angular/core';
import { AnswerChoice, QuestionRegion } from '../../../models/draws';
import { SafeHtmlPipe } from '../../../services/safehtml';

@Component({
  selector: 'app-question-canvas-view',
  imports: [SafeHtmlPipe],
  templateUrl: './question-canvas-view.component.html',
  styleUrl: './question-canvas-view.component.scss',
})
export class QuestionCanvasViewComponent implements AfterViewInit, AfterViewChecked {
  @ViewChild('passagecanvas', { static: false }) passageCanvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('canvas', { static: true }) canvas!: ElementRef<HTMLCanvasElement>;

  private psgctx!: CanvasRenderingContext2D | null;
  private canvasCtx!: CanvasRenderingContext2D | null;
  private img = new Image();
  public imageCache = new Map<string, HTMLImageElement>(); // 📂 Resimleri önbellekte sakla
  public currentImageId = signal<string | null>(null); // 🔄 Mevcut resmin ID'sini takip et,

  public _questionRegion = signal<QuestionRegion>({} as QuestionRegion);
  public isImageLoaded = signal(false); // Resim yüklendi mi?

  private shouldInitCanvas = false;

  @Input({ required: true }) set questionRegion(value: QuestionRegion) {
    this._questionRegion.set(value);
    if (value?.passage) {
      this.shouldInitCanvas = true;
    }
  }

  @Input() correctAnswerVisible: boolean = false;

  @Input() set selectedChoice(choice: AnswerChoice | undefined) {
    this._selectedChoice.set(choice);
  }

  @Input() set correctChoice(choice: AnswerChoice | undefined) {
    this._correctChoice.set(choice);
  }

  @Input() set mode(m: 'exam' | 'result' | null) {
    this._mode.set(m || 'exam');
  }

  @Output() hoverRegion = new EventEmitter<MouseEvent>();
  @Output() selectRegion = new EventEmitter<MouseEvent>();
  @Output() choiceSelected = new EventEmitter<AnswerChoice>();
  @Output() answerOpened = new EventEmitter<number>(); // 🆕 Event tanımlandı

  public hoveredChoice = signal<AnswerChoice | null>(null); // 🟦 Hangi şık üzerinde geziliyorsa
  private _selectedChoice = signal<AnswerChoice | undefined>(undefined); // 🔄 Her soru için seçilen şıkkı sakla
  private _correctChoice = signal<AnswerChoice | undefined>(undefined); // 🔄 Her soru için seçilen şıkkı sakla
  private _mode = signal<'exam' | 'result'>('exam'); // Sınav modu
  constructor() {
    effect(() => {
      if (this._questionRegion()) {
        this.loadImageForQuestion();
      }
    });
  }

  ngAfterViewChecked() {
    if (this.shouldInitCanvas) {
      this.shouldInitCanvas = false;
      this.psgctx = this.passageCanvas.nativeElement.getContext('2d');
      // canvas işlemi burada
      this.loadImageForQuestion();
    }
  }

  ngAfterViewInit() {
    if (this.passageCanvas) {
      this.psgctx = this.passageCanvas.nativeElement.getContext('2d');
    }
    if (this.canvas) {
      this.canvasCtx = this.canvas.nativeElement.getContext('2d');
    }
    this.loadImageForQuestion();
  }

  drawPassageSection() {
    if (!this.isImageLoaded()) return;
    if (!this.psgctx) return;
    const passageCanvasEl = this.passageCanvas.nativeElement;
    this.psgctx = passageCanvasEl.getContext('2d');

    if (this.psgctx) {
      const region = this._questionRegion();
      passageCanvasEl.width = region?.passage?.width || 0;
      passageCanvasEl.height = region?.passage?.height || 0;

      this.psgctx.clearRect(0, 0, passageCanvasEl.width, passageCanvasEl.height);
      this.psgctx.drawImage(
        this.img,
        region?.passage?.x || 0,
        region?.passage?.y || 0,
        region?.passage?.width || 0,
        region?.passage?.height || 0,
        0,
        0,
        region?.passage?.width || 0,
        region?.passage?.height || 0
      );
    }
  }

  async loadImageForQuestion() {
    // if (this.currentImageId() === this._questionRegion().imageId) return; // ✅ Zaten bu resim yüklüyse, tekrar yükleme
    if (!this._questionRegion()) return; // Eğer region tanımlı değilse, çık
    if (this.imageCache.has(this._questionRegion().imageId)) {
      // 🔄 Önbellekte varsa, direkt kullan
      this.img = this.imageCache.get(this._questionRegion().imageId)!;
    } else {
      // 📥 Yeni resmi yükle
      this.img = new Image();
      this.img.src = this._questionRegion().imageUrl;
      await new Promise((resolve, reject) => {
        this.img.onload = () => {
          this.imageCache.set(this._questionRegion().imageId, this.img); // 📂 Önbelleğe kaydet
          this.isImageLoaded.set(true);
          resolve(true);
        };
        this.img.onerror = reject;
      });
    }

    this.currentImageId.set(this._questionRegion().imageId);
    this.drawImageSection();
  }

  drawAnswer(answer: AnswerChoice, color: string) {
    if (!this.canvasCtx) return;

    const gradient = this.canvasCtx.createLinearGradient(
      answer.x,
      answer.y,
      answer.x + answer.width,
      answer.y + answer.height
    );
    gradient.addColorStop(0, color); // ✅ Yeşil başlangıç
    gradient.addColorStop(1, color); // ✅ Koyu yeşil bitiş

    this.canvasCtx.fillStyle = gradient;
    this.canvasCtx.fillRect(
      answer.x - this._questionRegion().x,
      answer.y - this._questionRegion().y,
      answer.width,
      answer.height
    );

    this.canvasCtx.fill();
    this.canvasCtx.strokeStyle = 'black'; // Çerçeve siyah
    this.canvasCtx.lineWidth = 2;
    this.canvasCtx.strokeRect(
      answer.x - this._questionRegion().x,
      answer.y - this._questionRegion().y,
      answer.width,
      answer.height
    );
    this.canvasCtx.stroke();
  }

  drawImageSection() {
    if (!this.isImageLoaded()) return;
    this.drawPassageSection();

    const canvasEl = this.canvas.nativeElement;
    this.canvasCtx = canvasEl.getContext('2d');

    if (this.canvasCtx) {
      canvasEl.width = this._questionRegion().width;
      canvasEl.height = this._questionRegion().height;

      this.canvasCtx.clearRect(0, 0, canvasEl.width, canvasEl.height);
      this.canvasCtx.drawImage(
        this.img,
        this._questionRegion().x,
        this._questionRegion().y,
        this._questionRegion().width,
        this._questionRegion().height,
        0,
        0,
        this._questionRegion().width,
        this._questionRegion().height
      );

      // **Seçili ve hover edilen şıkları farklı renklerde göster**
      for (const answer of this._questionRegion().answers) {
        const isSelected = this._selectedChoice() === answer;
        const isHovered = this.hoveredChoice() === answer;
        const isCorrect = this._correctChoice() === answer;

        const borderRadius = 0; // Math.min(answer.width, answer.height) * 0.3; // ✅ Dinamik yuvarlak köşe
        if (this._mode() === 'exam') {
          if (isSelected) {
            this.drawAnswer(answer, 'rgba(76, 195, 80, 0.3)'); // ✅ Yeşil başlangıç);
          } else if (isHovered) {
            this.canvasCtx.fillStyle = 'rgba(0, 0, 255, 0.3)'; // 🟦 Mavi hover efekti
            this.canvasCtx.fillRect(
              answer.x - this._questionRegion().x,
              answer.y - this._questionRegion().y,
              answer.width,
              answer.height
            );
          } else {
            this.canvasCtx.fillStyle = 'rgba(255, 255, 255, 0)'; // Varsayılan şeffaf
          }
        } else if (this._mode() === 'result') {
          console.log('regions:', this._questionRegion());
          console.log('selected answer:', this._selectedChoice());

          if (isCorrect) {
            this.drawAnswer(answer, 'rgba(76, 195, 80, 0.3)'); // ✅ Yeşil başlangıç);
          }
          if (isSelected) {
            if (isCorrect) {
              this.drawAnswer(answer, 'rgba(76, 195, 80, 0.3)'); // ✅ Yeşil başlangıç);
            } else {
              this.drawAnswer(answer, 'rgba(234, 21, 21, 0.46)'); // ✅ Yeşil başlangıç);
            }
          }
        }

        // this.ctx.fillRect(answer.x - region.x, answer.y - region.y, answer.width, answer.height);
        // this.canvasCtx.fill();
        // this.canvasCtx.strokeStyle = 'black'; // Çerçeve siyah
        // this.canvasCtx.lineWidth = 2;
        // this.canvasCtx.strokeRect(answer.x - this._questionRegion().x, answer.y - this._questionRegion().y, answer.width, answer.height);
        // this.canvasCtx.stroke();
      }
    }
  }

  onMouseMove(event: MouseEvent) {
    if (!this.canvas?.nativeElement) return;

    const rect = this.canvas.nativeElement.getBoundingClientRect();
    const mouseX = event.clientX - rect.left;
    const mouseY = event.clientY - rect.top;

    let found = false;

    for (const answer of this._questionRegion().answers) {
      if (
        mouseX >= answer.x - this._questionRegion().x &&
        mouseX <= answer.x - this._questionRegion().x + answer.width &&
        mouseY >= answer.y - this._questionRegion().y &&
        mouseY <= answer.y - this._questionRegion().y + answer.height
      ) {
        this.hoveredChoice.set(answer);
        this.canvas.nativeElement.style.cursor = 'pointer'; // 🖱️ Mouse pointer değiştirildi
        found = true;
        break;
      }
    }

    if (!found) {
      this.hoveredChoice.set(null);
      this.canvas.nativeElement.style.cursor = 'auto'; // Geri varsayılana döndür
    }

    this.drawImageSection(); // UI'yı güncelle
  }

  selectChoice(event: MouseEvent) {
    if (!this.canvas?.nativeElement) return;

    const rect = this.canvas.nativeElement.getBoundingClientRect();
    const mouseX = event.clientX - rect.left;
    const mouseY = event.clientY - rect.top;

    for (const answer of this._questionRegion().answers) {
      if (
        mouseX >= answer.x - this._questionRegion().x &&
        mouseX <= answer.x - this._questionRegion().x + answer.width &&
        mouseY >= answer.y - this._questionRegion().y &&
        mouseY <= answer.y - this._questionRegion().y + answer.height
      ) {
        this._selectedChoice.set(answer);
        //this.selectedChoice.set(answer);
        this.choiceSelected.emit(answer);
        break;
      }
    }

    this.drawImageSection(); // UI'yı güncelle
  }

  showCorrectAnswer() {
    // console.log('selected index: ',this.selectedAnswerId);
    this.answerOpened.emit(1); // 🆕 Seçilen cevap üst componente gönderiliyor
  }
}
