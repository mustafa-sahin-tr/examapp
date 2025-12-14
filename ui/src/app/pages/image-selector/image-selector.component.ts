import { Component, ElementRef, EventEmitter, HostListener, Output, ViewChild, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { QuestionRegion, AnswerChoice, RegionOrAnswerHit } from '../../models/draws';
import { QuestionCanvasForm } from '../../models/question-form';
import { QuillModule } from 'ngx-quill';
import { FormsModule, NgModel } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { QuestionDetectorService } from '../../services/question-detector.service';
import { Prediction } from '../../models/prediction';
import { HttpStatusCode } from '@angular/common/http';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatMenuModule, MatMenuTrigger } from '@angular/material/menu';
import { MatIconModule } from '@angular/material/icon';
import { MatDividerModule } from '@angular/material/divider';
import { QuestionCanvasComponent } from '../question/question-canvas.component';
import { QuestionNavigatorComponent } from '../../shared/components/question-navigator/question-navigator.component';
import { QuestionCanvasViewComponent } from '../../shared/components/question-canvas-view/question-canvas-view.component';
import { QuestionService } from '../../services/question.service';
import { TestService } from '../../services/test.service';
import { MatDialog } from '@angular/material/dialog';
import { QuestionCanvasViewComponentv3 } from '../../shared/components/question-canvas-view-v3/question-canvas-view-v3.component';
import { QuestionCanvasViewComponentv4 } from '../../shared/components/question-canvas-view-v4/question-canvas-view-v4.component';

interface WarningMarker {
  id: number;
  x: number;
  y: number;
  messages: string[];
  type?: 'error' | 'warning' | 'info';
}
@Component({
  selector: 'app-image-selector',
  standalone: true,
  imports: [
    CommonModule,
    QuillModule,
    MatDividerModule,
    MatIconModule,
    FormsModule,
    MatMenuModule,
    MatButtonModule,
    MatSlideToggleModule,
    QuestionNavigatorComponent,
    QuestionCanvasViewComponent,
    //QuestionCanvasViewComponentv4,
  ],
  templateUrl: './image-selector.component.html',
  styleUrls: ['./image-selector.component.scss'],
})
export class ImageSelectorComponent {
  @ViewChild('canvas', { static: false }) canvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('fileInput', { static: false }) fileInput!: ElementRef<HTMLInputElement>;
  @ViewChild('menuTrigger') menuTrigger!: MatMenuTrigger;
  @ViewChild('menuTrigger', { read: ElementRef }) triggerElementRef!: ElementRef;

  private ctx!: CanvasRenderingContext2D | null;
  private img = new Image();

  public autoAlign = signal<boolean>(false);
  public autoMode = signal<boolean>(false);
  public inProgress = signal<boolean>(false);
  public showRegions = signal<boolean>(false);
  public onlyQuestionMode = signal<boolean>(false);
  public answerCount = signal<number>(3);
  public selectionMode = signal<'passage' | 'question' | 'answer' | null>(null);
  public selectionModeLocked = signal<'passage' | 'question' | 'answer' | null>(null);
  public passages = signal<{ id: string; x: number; y: number; width: number; height: number }[]>([]);
  public selectedPassageMap = new Map<string, string>(); // 🗂 Soru ID -> Passage ID eşlemesi
  public imageData = signal<string | null>(null); // 🖼️ Base64 formatında resim verisi
  public regions = signal<QuestionRegion[]>([]);
  public imageSrc = signal<string | null>(null);
  public imagName = signal<string | null>(null);
  questionDetectorService = inject(QuestionDetectorService);
  questionService = inject(QuestionService);
  testService = inject(TestService);
  private snackBar = inject(MatSnackBar);
  private dialog = inject(MatDialog);
  public previewCurrentIndex = signal(0);
  private startX = 0;
  private startY = 0;
  protected isDrawing = false;
  private selectionStarted = false;
  private selectedQuestionIndex = -1;

  contextMenuType: 'region' | 'answer' | 'worksheet' = 'region'; // ✅ Sağ tıklama menüsünün türü
  selectedRegion: number | null = null;
  selectedAnswer: number | null = null; // ✅ Seçili cevap indeksi
  previewMode = signal(false); // ✅ Önizleme açma durumu
  currentTestId = signal<number | null>(null); // Test ID'yi sakla

  public exampleAnswers = new Map<string, string>(); // 📜 Her soru için örnek cevapları sakla
  public exampleFlags = new Map<string, boolean>(); // ✅ Her soru için "isExample" flag'ini sakla

  private currentX = 0;
  private currentY = 0;

  warningIconHeight = 48;

  imageFiles: File[] = [];
  currentImageIndex = 0;
  currentImageSrc: string | null = null;

  contextMenuVisible = false;
  contextMenuX = 0;
  contextMenuY = 0;
  warningMarkers: WarningMarker[] = [];
  selectedWarning: WarningMarker | null = null;
  warningMenuVisible = false;
  warningMenuX = 0;
  warningMenuY = 0;

  // Sayfa içinde herhangi bir yere tıklandığında menüyü kapat
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    console.log('Entered onDocumentClick. IsDrawing :', this.isDrawing);
    const target = event.target as HTMLElement;
    // Eğer tıklanan yer menünün içi değilse menüyü kapat
    if (!target.closest('.custom-context-menu')) {
      this.contextMenuVisible = false;
    }

    this.warningMenuVisible = false;
    this.contextMenuVisible = false;
    console.log('Exited  onDocumentClick. IsDrawing :', this.isDrawing);
  }

  openWarningContextMenu(marker: WarningMarker, event: MouseEvent) {
    event.stopPropagation();
    this.selectedWarning = marker;
    this.warningMenuVisible = true;
    this.warningMenuX = marker.x + 20;
    this.warningMenuY = marker.y;
  }

  dismissWarning(event: MouseEvent) {
    this.stopEvent(event);
    this.warningMenuVisible = false;
  }

  get previewQuestions() {
    let questions: { status: 'correct' | 'incorrect' | 'unknown' }[] = [];
    for (let i = 0; i < this.regions().length; i++) {
      const region = this.regions()[i];
      questions.push({ status: 'unknown' });
    }
    return questions;
  }

  questionSelected(index: number) {
    this.previewCurrentIndex.set(index);
  }

  public togglePreviewMode(testId: number) {
    if (!this.previewMode()) {
      this.previewMode.set(!this.previewMode());
      this.previewCurrentIndex.set(0);
      this.currentTestId.set(testId); // Test ID'yi sakla
      // get questions and sets region
      this.questionService.getAll(testId).subscribe((response) => {
        const regions = this.testService.convertQuestionsToRegions(response);
        this.regions.set(regions);
      });
    } else {
      this.resetRegions();
      this.previewMode.set(!this.previewMode());
      this.currentTestId.set(null); // Test ID'yi temizle
      this.loadCurrentImage();
    }
  }

  getRegionOrAnswerAtPosition(x: number, y: number): RegionOrAnswerHit {
    const regions = this.regions();

    for (let qi = 0; qi < regions.length; qi++) {
      const region = regions[qi];

      // Önce answer'ları kontrol edelim
      for (let ai = 0; ai < region.answers.length; ai++) {
        const answer = region.answers[ai];

        const answerX = answer.x;
        const answerY = answer.y;

        // Eğer answer koordinatları region’a göre relative ise:
        // const answerX = region.x + answer.x;
        // const answerY = region.y + answer.y;

        if (x >= answerX && x <= answerX + answer.width && y >= answerY && y <= answerY + answer.height) {
          return {
            type: 'answer',
            value: {
              questionIndex: qi,
              answerIndex: ai,
            },
          };
        }
      }

      // Answer yok ama region içindeyse
      if (x >= region.x && x <= region.x + region.width && y >= region.y && y <= region.y + region.height) {
        return {
          type: 'question',
          value: {
            questionIndex: qi,
            answerIndex: null,
          },
        };
      }
    }

    return null;
  }

  onRightClick(event: MouseEvent) {
    console.log('Entered onRightClick. IsDrawing :', this.isDrawing);
    event.preventDefault();

    const canvasRect = (event.target as HTMLCanvasElement).getBoundingClientRect();
    const clickX = event.clientX - canvasRect.left;
    const clickY = event.clientY - canvasRect.top;

    const hit = this.getRegionOrAnswerAtPosition(clickX, clickY);
    this.contextMenuX = event.clientX;
    this.contextMenuY = event.clientY;

    if (!hit) {
      this.contextMenuType = 'worksheet';
      this.selectedRegion = null;
      this.selectedAnswer = null;
      console.log('Exited  onRightClick hit === null. IsDrawing :', this.isDrawing);
      this.contextMenuVisible = true;
      return;
    }

    if (hit.type === 'answer') {
      this.contextMenuType = 'answer';
      this.selectedAnswer = hit.value.answerIndex;
      this.selectedRegion = hit.value.questionIndex;
    } else if (hit.type === 'question') {
      this.contextMenuType = 'region';
      this.selectedRegion = hit.value.questionIndex;
      this.selectedAnswer = null;
    }
    console.log('Exited  onRightClick hit.type === question. IsDrawing :', this.isDrawing);

    this.contextMenuVisible = true;
  }

  closeContextMenu() {
    this.contextMenuVisible = false;
    this.selectedRegion = null; // Seçili bölgeyi sıfırla
    this.selectedAnswer = null; // Seçili cevabı sıfırla
  }

  stopEvent(event?: MouseEvent) {
    event?.stopPropagation();
    event?.preventDefault();
  }

  handleMenuAction(action: string, event?: MouseEvent) {
    console.log('Selected action:', action);
    this.stopEvent(event);
    if (action === 'removeQuestion') {
      this.removeQuestion(this.selectedRegion!);
    } else if (action === 'removeAnswer') {
      this.removeAnswer(this.selectedRegion!, this.selectedAnswer!);
    } else if (action === 'selectQuestion') {
      this.selectionMode.set('question');
    } else if (action === 'alignAnswers') {
      this.alignAnswers(this.selectedRegion!, true);
    } else if (action === 'predict') {
      this.predict();
    } else if (action === 'selectPassage') {
      this.selectionMode.set('passage');
    } else if (action === 'removeAll') {
      this.removeAllQuestions();
    }

    this.contextMenuVisible = false;
  }

  handleFilesInput2(event: Event) {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.imageFiles = Array.from(input.files).filter((file) => file.type.startsWith('image/'));
      this.currentImageIndex = 0;
      this.loadCurrentImage();
    }
  }

  loadCurrentImage() {
    this.resetRegions();
    const file = this.imageFiles[this.currentImageIndex];
    if (!file) return;
    const reader = new FileReader();
    this.imagName.set(file.name);
    this.regions.set([]);

    reader.onload = () => {
      //this.currentImageSrc = reader.result as string;
      this.imageSrc.set(reader.result as string);
      this.imageData.set(reader.result as string); // 📂 Resmi base64 olarak sakla
      this.img.src = this.imageSrc()!;

      this.img.onload = () => {
        if (this.selectionModeLocked() !== 'answer') {
          this.predict();
        } else {
          this.regions.set([]);
          const region = {
            name: 'Soru',
            x: 0,
            y: 0,
            width: this.img.width - 1,
            height: this.img.height - 1,
            answers: [],
            passageId: '0',
            imageId: '',
            imageUrl: '',
            id: 0,
            isExample: false,
            exampleAnswer: null,
          };
          this.regions.set([...this.regions(), region]);
          this.selectQuestion(0);
        }
        this.drawImage();
      };
    };
    reader.readAsDataURL(file); // 📦 Sadece o anki resmi belleğe yükle
  }

  nextImage() {
    if (this.isDrawing) {
      return;
    }
    if (this.imageFiles.length === 0) return;

    this.currentImageIndex++;
    if (this.currentImageIndex >= this.imageFiles.length) {
      this.currentImageIndex = 0; // döngüsel olarak başa sar
    }
    this.loadCurrentImage();
  }

  previousImage() {
    if (this.isDrawing) {
      return;
    }
    if (this.imageFiles.length === 0) return;

    this.currentImageIndex--;
    if (this.currentImageIndex < 0) {
      this.currentImageIndex = this.imageFiles.length - 1; // döngüsel olarak sona sar
    }
    this.loadCurrentImage();
  }

  handleFileInput(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];

    if (file) {
      const reader = new FileReader();
      this.imagName.set(file.name);
      reader.onload = (e: ProgressEvent<FileReader>) => {
        this.imageSrc.set(e.target?.result as string);
        this.imageData.set(e.target?.result as string); // 📂 Resmi base64 olarak sakla
        this.img.src = this.imageSrc()!;
        this.img.onload = () => {
          if (this.selectionModeLocked() !== 'answer') {
            this.predict();
          } else {
            this.regions.set([]);
            const region = {
              name: 'Soru',
              x: 0,
              y: 0,
              width: this.img.width - 1,
              height: this.img.height - 1,
              answers: [],
              passageId: '0',
              imageId: '',
              imageUrl: '',
              id: 0,
              isExample: false,
              exampleAnswer: null,
            };
            this.regions.set([...this.regions(), region]);
            this.selectQuestion(0);
          }
          this.drawImage();
        };
      };

      reader.readAsDataURL(file); // ✅ Resmi base64 formatına çevir
      this.regions.set([]);
    }
  }

  drawImage2() {
    if (!this.canvas) return;
    const canvasEl = this.canvas.nativeElement;
    this.ctx = canvasEl.getContext('2d');
    if (this.ctx) {
      canvasEl.width = this.img.width;
      canvasEl.height = this.img.height;
      this.ctx.drawImage(this.img, 0, 0);

      // Tüm bölgeleri tekrar çiz
      this.drawRegions();
    }
  }

  toggleAlignMode() {
    this.autoAlign.set(!this.autoAlign());
  }

  lockSelectionMode(mode: 'passage' | 'question' | 'answer' | null) {
    this.selectionModeLocked.set(mode);
    this.toggleSelectionMode(mode);
  }

  toggleOnlyQuestionMode() {
    this.onlyQuestionMode.set(!this.onlyQuestionMode());
  }

  tapCorrectAnswer(event: MouseEvent) {
    console.log('Entered tapCorrectAnswer.IsDrawing :', this.isDrawing);
    if (this.isDrawing) {
      console.log('Exited  tapCorrectAnswer isDrawing :', this.isDrawing);
      return;
    }

    const mouseX = event.offsetX;
    const mouseY = event.offsetY;

    for (const region of this.regions()) {
      for (const answer of region.answers) {
        if (
          mouseX >= answer.x &&
          mouseX <= answer.x + answer.width &&
          mouseY >= answer.y &&
          mouseY <= answer.y + answer.height
        ) {
          const questionIndex = this.regions().findIndex((q) => q.name === region.name);
          const answerIndex = region.answers.findIndex((a) => a.label === answer.label);
          this.setCorrectAnswer(questionIndex, answerIndex, 1);
        }
      }
    }
    console.log('Exited  tapCorrectAnswer.IsDrawing :', this.isDrawing);
  }

  onMouseMove(event: MouseEvent) {
    if (!this.isDrawing) {
      const canvas = this.canvas.nativeElement;
      const ctx = canvas.getContext('2d');
      if (!ctx) {
        return;
      }

      // Tüm resmi yeniden çiz
      this.drawImage();

      const mouseX = event.offsetX;
      const mouseY = event.offsetY;

      for (const question of this.regions()) {
        for (const answer of question.answers) {
          if (
            mouseX >= answer.x &&
            mouseX <= answer.x + answer.width &&
            mouseY >= answer.y &&
            mouseY <= answer.y + answer.height
          ) {
            // Transparan yeşil ile dikdörtgeni doldur
            ctx.fillStyle = 'rgba(0, 255, 0, 0.3)';
            ctx.fillRect(answer.x, answer.y, answer.width, answer.height);
            break;
          }
        }
      }

      return;
    }

    this.currentX = event.offsetX;
    this.currentY = event.offsetY;
    this.drawTemporaryRectangle();
  }

  drawTemporaryRectangle() {
    if (!this.ctx) return;
    const canvas = this.canvas.nativeElement;

    this.ctx.clearRect(0, 0, canvas.width, canvas.height);

    // Tüm çizimleri tekrar çiz (önceden çizilen dikdörtgenler kaybolmasın)
    this.drawImage();

    const width = this.currentX - this.startX;
    const height = this.currentY - this.startY;

    this.ctx.strokeStyle = this.getStrokeStyle();
    this.ctx.lineWidth = 2;
    this.ctx.strokeRect(this.startX, this.startY, width, height);
  }

  getStrokeStyle() {
    switch (this.selectionMode()) {
      case 'passage':
        return 'purple';
      case 'question':
        return 'red';
      case 'answer':
        return 'blue';
      default:
        return 'black';
    }
  }

  checkRegionWarning(region: QuestionRegion) {
    if (this.warningMarkers.find((w) => w.id === region.id)) {
      // remove warning
      this.warningMarkers = this.warningMarkers.filter((w) => w.id !== region.id);
    }
    let messages: string[] = [];

    if (region.answers.length != this.answerCount()) {
      messages.push(`Şık sayısı ${this.answerCount()} olmalı`);
    }
    if (!region.answers.find((a) => a.isCorrect)) {
      messages.push(`Doğru cevap yok`);
    }

    if (messages.length > 0) {
      this.warningMarkers.push({
        id: region.id,
        x: region.x + region.width - this.warningIconHeight / 2, // mat-icon width
        y: region.y - this.warningIconHeight / 2,
        messages: messages,
        type: 'error',
      });
    }
  }

  drawImage() {
    if (!this.canvas) return;
    const canvasEl = this.canvas.nativeElement;
    this.ctx = canvasEl.getContext('2d');
    if (this.ctx) {
      canvasEl.width = this.img.width;
      canvasEl.height = this.img.height;
      this.ctx.drawImage(this.img, 0, 0);

      // 🟪 **Passage alanlarını mor renkte çiz**
      for (const passage of this.passages()) {
        this.ctx.strokeStyle = 'purple';
        this.ctx.lineWidth = 2;
        this.ctx.strokeRect(passage.x, passage.y, passage.width, passage.height);
      }

      // 🔴 **Soru alanlarını kırmızı renkte çiz**
      for (const region of this.regions()) {
        this.ctx.strokeStyle = 'red';
        this.ctx.lineWidth = 2;
        this.ctx.strokeRect(region.x, region.y, region.width, region.height);
        this.checkRegionWarning(region);
      }

      // 🟦 **Şık alanlarını mavi renkte çiz**
      if (!this.onlyQuestionMode()) {
        for (const region of this.regions()) {
          for (const answer of region.answers) {
            if (answer.isCorrect) {
              this.ctx.fillStyle = 'rgba(0, 255, 0, 0.3)';
              this.ctx.fillRect(answer.x, answer.y, answer.width, answer.height);
            }
            // this.ctx.strokeStyle = 'blue';
            // this.ctx.lineWidth = 2;
            // this.ctx.strokeRect(answer.x, answer.y, answer.width, answer.height);
            const borderRadius = 0; // Math.min(answer.width, answer.height) * 0.3; // ✅ Yuvarlak köşe oranı
            this.ctx.beginPath();
            this.ctx.roundRect(answer.x, answer.y, answer.width, answer.height, borderRadius);
            this.ctx.closePath();

            this.ctx.strokeStyle = 'blue'; // ✅ Mavi Kenarlık
            this.ctx.lineWidth = 2;
            this.ctx.stroke();
          }
        }
      }
    }
  }

  setPassageForQuestion(questionId: string, passageId: string) {
    this.selectedPassageMap.set(questionId, passageId);

    // 🔄 Soruların içindeki passageId'yi güncelle
    const updatedRegions = this.regions().map((region) => {
      if (region.name === questionId) {
        return { ...region, passageId }; // ✅ Yeni passageId ile güncelle
      }
      return region;
    });

    this.regions.set(updatedRegions); // 📌 Güncellenmiş listeyi kaydet
  }

  onPassageChange(event: Event, questionId: string) {
    const selectElement = event.target as HTMLSelectElement;
    const passageId = selectElement.value;
    this.setPassageForQuestion(questionId, passageId);
  }

  drawRegions() {
    if (!this.ctx) return;

    // 🟥 **Soru bölgelerini kırmızı çerçeve ile göster**
    for (const region of this.regions()) {
      this.ctx.strokeStyle = 'red';
      this.ctx.lineWidth = 2;
      this.ctx.strokeRect(region.x, region.y, region.width, region.height);
    }

    // 🟦 **Şık bölgelerini mavi çerçeve ile göster**
    for (const region of this.regions()) {
      for (const answer of region.answers) {
        this.ctx.strokeStyle = 'blue';
        this.ctx.lineWidth = 2;
        this.ctx.strokeRect(answer.x, answer.y, answer.width, answer.height);
      }
    }
  }

  startSelection(event: MouseEvent) {
    console.log('Entered startSelection.IsDrawing :', this.isDrawing);
    // Sağ tık ise hiçbir şey yapma
    if (event.button === 2) {
      console.log('Exited startSelection event.button === 2 .IsDrawing :', this.isDrawing);
      return;
    }
    const rect = this.canvas.nativeElement.getBoundingClientRect();
    this.startX = event.clientX - rect.left;
    this.startY = event.clientY - rect.top;
    var hit = this.getRegionOrAnswerAtPosition(this.startX, this.startY);

    // if (!this.ctx || !this.selectionMode()) {
    //   console.log('Exited startSelection ctx or selectionMode is null. IsDrawing :', this.isDrawing);
    //   return;
    // }

    if (hit) {
      // if(hit.type === 'question' && this.selectionMode() === 'question') {
      //   // bir sorunun içinde tıklanmışsa tekrar bir soru yapmasın.
      //   console.log('Exited startSelection hit.type === question. IsDrawing :', this.isDrawing);
      //   return;
      // }
      if (hit.type === 'question') {
        this.selectedQuestionIndex = hit.value.questionIndex;
        this.selectionMode.set('answer');
      }
    } else {
      // bir hit yoksa cevap giremesin. Cevap girmen için hit olmalı ve question olmalı
      if (this.selectionMode() === 'answer') {
        console.log('Exited startSelection hit === null. IsDrawing :', this.isDrawing);
        return;
      }
    }
    if (this.selectionMode()) this.isDrawing = true;
    console.log('Exited startSelection. IsDrawing :', this.isDrawing);
  }

  generateFirstAvailableName() {
    const prefix = 'Soru ';

    const usedNumbers = this.regions()
      .map((r) => r.name)
      .filter((name) => name.startsWith(prefix))
      .map((name) => parseInt(name.replace(prefix, ''), 10))
      .filter((num) => !isNaN(num))
      .sort((a, b) => a - b);

    // 1'den başlayarak ilk boş olan numarayı bul
    let candidate = 1;
    for (let i = 0; i < usedNumbers.length; i++) {
      if (usedNumbers[i] !== candidate) {
        break;
      }
      candidate++;
    }

    return `${prefix}${candidate}`;
  }

  endSelection(event: MouseEvent) {
    console.log('Entered EndSelection.IsDrawing :', this.isDrawing);
    this.stopEvent(event);
    const questionWidthThreshold = 250;
    const questionHeightThreshold = 100;
    const answerHeightThreshold = 20;
    const answerWidthThreshold = 20;
    if (!this.selectionMode() || !this.ctx || !this.isDrawing) return;
    const rect = this.canvas.nativeElement.getBoundingClientRect();
    const endX = event.clientX - rect.left;
    const endY = event.clientY - rect.top;
    const width = endX - this.startX;
    const height = endY - this.startY;

    if (this.selectionMode() === 'passage') {
      if (questionWidthThreshold > width || questionHeightThreshold > height) {
        console.log(
          'Exited  EndSelection questionWidthThreshold > width || questionHeightThreshold > height. IsDrawing :',
          this.isDrawing
        );
        this.isDrawing = false;
        this.drawImage();
        return;
      }
      const id = `p${this.passages().length + 1}`;
      this.passages.set([...this.passages(), { id, x: this.startX, y: this.startY, width, height }]);
    } else if (this.selectionMode() === 'question') {
      const name = this.generateFirstAvailableName();
      if (questionWidthThreshold > width || questionHeightThreshold > height) {
        console.log(
          'Exited  EndSelection questionWidthThreshold > width || questionHeightThreshold > height. IsDrawing :',
          this.isDrawing
        );
        this.isDrawing = false;
        this.drawImage();
        return;
      }
      this.regions.set([
        ...this.regions(),
        {
          name,
          x: this.startX,
          y: this.startY,
          width,
          height,
          answers: [],
          passageId: '0',
          imageId: '',
          imageUrl: '',
          id: 0,
          isExample: false,
          exampleAnswer: null,
        },
      ]);
      this.sortRegionsByName();
      this.selectQuestionByName(name);
      /// this.selectQuestion(this.regions().length - 1);
    } else if (this.selectionMode() === 'answer' && this.selectedQuestionIndex !== -1) {
      if (answerWidthThreshold > width || answerHeightThreshold > height) {
        console.log(
          'Exited  EndSelection questionWidthThreshold > width || questionHeightThreshold > height. IsDrawing :',
          this.isDrawing
        );
        this.isDrawing = false;
        this.drawImage();
        return;
      }
      const label = `Şık ${this.regions()[this.selectedQuestionIndex].answers.length + 1}`;
      this.regions()[this.selectedQuestionIndex].answers.push({
        label,
        x: this.startX,
        y: this.startY,
        width,
        height,
        isCorrect: false,
        id: 0,
        imageUrl: '',
      });
      const answerCount = this.answerCount();
      if (this.regions()[this.selectedQuestionIndex].answers.length === +answerCount) {
        if (this.autoAlign()) {
          this.alignAnswers(this.selectedQuestionIndex);
        }
        // this.toggleSelectionMode('question');
      }
    }

    this.isDrawing = false;
    this.drawImage();

    console.log('Exited  EndSelection. IsDrawing :', this.isDrawing);
  }

  toggleSelectionMode(mode: 'question' | 'answer' | 'passage' | null) {
    if (this.selectionModeLocked()) {
      this.selectionMode.set(this.selectionModeLocked());
      return;
    }

    this.selectionMode.set(mode); // TODO: Seçim modunu ayarla

    if (mode === 'answer' && this.selectedQuestionIndex < 0) {
      alert('Lütfen önce bir soru seçin ve ardından şıkları ekleyin.');
    }
  }

  selectQuestion(index: number) {
    this.selectedQuestionIndex = index;
    this.toggleSelectionMode('answer');
  }

  selectQuestionByName(name: string) {
    const index = this.regions().findIndex((region) => region.name === name);
    if (index !== -1) {
      this.selectQuestion(index);
    }
  }

  // getJsonData() {
  //   return {
  //     //imageData: this.imageData(), // 🖼️ Resmin base64 verisini JSON'a ekle
  //     passages: this.passages(), // 📜 Passage'ları ekle
  //     questions: this.regions()
  //   };
  // }

  public toggleAutoMode() {
    this.autoMode.set(!this.autoMode());
  }

  public sendToFix() {
    const imageData = { image_base64: this.imageData() };
    const regionData = { question: this.regions() };
    const answerCount = this.answerCount();
    var data = {
      answerCount: answerCount,
      imageData: imageData,
      questions: regionData.question.map((q: any) => ({
        x: q.x,
        y: q.y,
        width: q.width,
        height: q.height,
        answers: q.answers.map((a: any) => ({
          x: a.x,
          y: a.y,
          width: a.width,
          height: a.height,
        })),
      })),
    };
    console.log(data);
    this.questionDetectorService.sendtoFix(data).subscribe((data) => {
      if (data.success) {
        this.snackBar.open(`Successfully appended ${data.added} questions with ${data.imageFile}`, 'Tamam', {
          duration: 3000,
        });
        this.nextImage();
      } else {
        this.snackBar.open(`${data.message}`, 'Tamam', {
          duration: 3000,
        });
      }
    });
  }

  public sendToFixForAnswer() {
    const imageData = { image_base64: this.imageData() };
    const regionData = { question: this.regions() };
    var data = {
      imageData: imageData,
      questions: regionData.question[0].answers.map((q: any) => ({
        x: q.x,
        y: q.y,
        width: q.width,
        height: q.height,
      })),
    };
    console.log(data);
    this.questionDetectorService.sendtoFixForAnswer(data).subscribe((data) => {
      if (data.success) {
        this.snackBar.open(`Successfully appended ${data.added} questions with ${data.imageFile}`, 'Tamam', {
          duration: 3000,
        });
        this.nextImage();
      }
    });
  }

  public predict() {
    this.snackBar.open('Resim analizi başlatılıyor...', 'Tamam', { duration: 2000 });
    this.inProgress.set(true);
    const imageData = { image_base64: this.imageData() };
    if (!imageData) return;

    this.questionDetectorService.readQrData(imageData).subscribe((data) => {
      console.log(data);
    });

    this.questionDetectorService.predict(imageData).subscribe((questions) => {
      console.log(questions);
      const imageWidth = Math.max(...questions.predictions.map((q) => q.x + q.width));
      // soruları sırala
      this.regions.set(
        questions.predictions
          .filter((q: any) => q.class_id === 0)
          .sort((a, b) => {
            // Sayfanın ortasını bulmak için tahmini bir sınır (örneğin width / 2)
            const middleX = imageWidth / 2;

            // Sol sütunları önce al (a ve b'nin sol sütun mu sağ sütun mu olduğunu kontrol et)
            const aIsLeft = a.x < middleX;
            const bIsLeft = b.x < middleX;

            if (aIsLeft !== bIsLeft) {
              return aIsLeft ? -1 : 1; // Sol sütun önde gelir
            }

            // Aynı sütundalarsa yukarıdan aşağıya (y koordinatına göre) sıralama yap
            return a.y - b.y;
          })
          .map((q, index) => ({
            ...q,
            name: `Soru ${index + 1}`,
            answers: q.subpredictions
              .filter((a: any) => a.class_id === 0)
              .map((a: any, index: number) => ({
                label: `Şık ${index + 1}`,
                ...a,
                isCorrect: false,
                id: index,
              })),
            // this.getAnswers(questions.predictions, q).map((a, index) => ({
            //   label: `Şık ${index + 1}`,
            //   ...a,
            //   isCorrect: false,
            //   id: index
            // })),
            isExample: false,
            exampleAnswer: null,
            id: index,
            passageId: '',
            imageId: '',
            imageUrl: '',
          }))
      );

      const questionCount = this.regions().length;

      for (let i = 0; i < questionCount; i++) {
        this.alignAnswers(i);
      }

      console.log(`${this.regions().length} Adet Soru bulundu`);
      console.log(`Auto Mode: ${this.autoMode()}`);
      console.log(`Q Mode:  ${this.onlyQuestionMode()}`);

      if (
        this.autoMode() &&
        this.regions().length > 2 &&
        (this.onlyQuestionMode() || this.regions().every((region) => region.answers && region.answers.length >= 3))
      ) {
        console.log(`Condition: true`);
      } else {
        console.log(`Condition: false`);
      }

      this.snackBar.open('Resim analizi tamamlandı.', 'Tamam', { duration: 2000 });
      if (!this.autoMode()) {
        // resmi çiz ve dur
        this.drawImage();
      } else {
        // auto mode ise question sayısını kontrol et
        if (this.regions().length > 1) {
          // ikiden çok fazla soru varsa durabiliriz.
          if (this.onlyQuestionMode()) {
            // ikiden fazla soru var ve sadece soru modundayız dur!
            this.drawImage();
          } else {
            // ikiden fazla soru var ve soru cevap modundayız.
            if (this.regions().every((region) => region.answers && region.answers.length >= 2)) {
              // ikiden fazla soru var soru cevap modundayız ve her soruda en az 2 şık var.
              this.drawImage();
            } else {
              this.nextImage();
            }
          }
        } else {
          this.nextImage();
        }
      }

      this.inProgress.set(false);
    });
  }

  public getAnswers(predictions: Prediction[], question: Prediction): Prediction[] {
    const answers = predictions.filter((a: any) => a.class_id === 1);
    const result: Prediction[] = [];
    // Şimdi her cevabı ilgili soruya ekle
    for (const answer of answers) {
      const centerX = answer.x + answer.width / 2;
      const centerY = answer.y + answer.height / 2;

      const withinX = centerX >= question.x && centerX <= question.x + question.width;
      const withinY = centerY >= question.y && centerY <= question.y + question.height;

      if (withinX && withinY) {
        result.push(answer);
        break; // Bir cevap yalnızca bir soruya ait olabilir
      }
    }
    return result;
  }

  public downloadRegionsLite() {
    // const jsonData = {
    //   questions: this.regions().map(region => ({
    //     ...region,
    //     imageUrl: '../data/' + this.imagName()
    //     // name: '../data/' + this.imagName(),
    //   })),
    // }

    const jsonData = this.regions().map((region) => ({
      questions: {
        ...region,
        imageUrl: '../data/' + this.imagName(),
      },
    }));

    const dataStr = 'data:text/json;charset=utf-8,' + encodeURIComponent(JSON.stringify(jsonData, null, 2));
    const downloadAnchor = document.createElement('a');
    downloadAnchor.setAttribute('href', dataStr);
    downloadAnchor.setAttribute('download', 'soru_koordinatlar.json');
    document.body.appendChild(downloadAnchor);
    downloadAnchor.click();
    document.body.removeChild(downloadAnchor);
  }

  public downloadRegions(header: any) {
    const jsonData = {
      imageData: this.imageData(),
      passages: this.passages(),
      questions: this.regions().map((region) => ({
        ...region,
        isExample: this.isExample(region.name),
        exampleAnswer: this.isExample(region.name) ? this.getExampleAnswer(region.name) : null,
      })),
      header: header,
    };
    const dataStr = 'data:text/json;charset=utf-8,' + encodeURIComponent(JSON.stringify(jsonData, null, 2));
    const downloadAnchor = document.createElement('a');
    downloadAnchor.setAttribute('href', dataStr);
    downloadAnchor.setAttribute('download', 'soru_koordinatlar.json');
    document.body.appendChild(downloadAnchor);
    downloadAnchor.click();
    document.body.removeChild(downloadAnchor);
  }

  public getRegions(header: any) {
    return {
      imageData: this.imageData(),
      passages: this.passages(),
      questions: this.regions().map((region) => ({
        ...region,
        isExample: this.isExample(region.name),
        exampleAnswer: this.isExample(region.name) ? this.getExampleAnswer(region.name) : null,
      })),
      header: header,
    };
  }

  public resetRegions() {
    this.passages.set([]);
    this.regions.set([]);
    this.imageData.set(null);
    this.imageSrc.set(null);
    this.exampleAnswers.clear();
    this.exampleFlags.clear();
    this.selectedPassageMap.clear();
    this.warningMarkers = [];
    this.selectedQuestionIndex = -1;
    this.selectedAnswer = -1;
    this.selectedRegion = -1;
    this.selectedWarning = null;
  }

  setCorrectAnswer(questionIndex: number, answerIndex: number, scale: number) {
    const region = this.regions()[questionIndex];

    // Tüm cevapları yanlış yap
    region.answers.forEach((a) => (a.isCorrect = false));

    // Seçilen cevabı doğru yap
    region.answers[answerIndex].isCorrect = true;

    this.regions.set([...this.regions()]); // UI güncelleme

    // Eğer preview mode'daysa backend'e güncelleme gönder
    if (this.previewMode()) {
      const questionId = region.id;
      const correctAnswerId = region.answers[answerIndex].id;

      if (questionId && correctAnswerId) {
        this.questionService.updateCorrectAnswer(questionId, correctAnswerId, scale).subscribe({
          next: (response) => {
            if (response.success) {
              this.snackBar.open('Doğru cevap başarıyla güncellendi!', 'Tamam', { duration: 3000 });
            } else {
              this.snackBar.open('Hata: ' + response.message, 'Tamam', { duration: 3000 });
            }
          },
          error: (error) => {
            console.error('Error updating correct answer:', error);
            this.snackBar.open('Doğru cevap güncellenirken hata oluştu!', 'Tamam', { duration: 3000 });
          },
        });
      }
    }

    this.drawImage();
  }

  onPreviewChoiceSelected(selectedChoice: AnswerChoice) {
    if (!this.previewMode()) return;

    const currentRegion = this.regions()[this.previewCurrentIndex()];
    const answerIndex = currentRegion.answers.findIndex((answer) => answer.id === selectedChoice.id);

    if (answerIndex !== -1) {
      this.setCorrectAnswer(this.previewCurrentIndex(), answerIndex, selectedChoice.scale || 1);
    }
  }

  onQuestionRemove(questionId: number) {
    if (!this.previewMode()) return;

    // Confirmation dialog kullanarak kullanıcıdan onay al
    const confirmRemoval = confirm('Bu soruyu testten çıkarmak istediğinizden emin misiniz?');

    if (confirmRemoval && this.currentTestId()) {
      this.questionService.removeQuestionFromTest(this.currentTestId()!, questionId).subscribe({
        next: (response) => {
          if (response.success) {
            this.snackBar.open('Soru başarıyla testten çıkarıldı!', 'Tamam', { duration: 3000 });

            // Local regions array'den de soruyu çıkar
            const currentRegions = this.regions();
            const updatedRegions = currentRegions.filter((region) => region.id !== questionId);
            this.regions.set(updatedRegions);

            // Eğer son soruyu sildik ve index sınır dışı kaldıysa, önceki soruya git
            if (this.previewCurrentIndex() >= updatedRegions.length && updatedRegions.length > 0) {
              this.previewCurrentIndex.set(updatedRegions.length - 1);
            }

            // Eğer hiç soru kalmadıysa preview mode'dan çık
            if (updatedRegions.length === 0) {
              this.togglePreviewMode(0); // Dummy testId, zaten false duruma geçiyor
              this.snackBar.open('Testte soru kalmadığı için önizleme kapatıldı.', 'Tamam', { duration: 3000 });
            }
          } else {
            this.snackBar.open('Hata: ' + response.message, 'Tamam', { duration: 3000 });
          }
        },
        error: (error) => {
          console.error('Error removing question from test:', error);
          this.snackBar.open('Soru silinirken hata oluştu!', 'Tamam', { duration: 3000 });
        },
      });
    }
  }

  sortRegionsByName() {
    const prefix = 'Soru ';
    const regions = this.regions();
    const sortedRegions = regions.slice().sort((a, b) => {
      const aNum = parseInt(a.name.replace(prefix, ''), 10);
      const bNum = parseInt(b.name.replace(prefix, ''), 10);

      return aNum - bNum;
    });

    this.regions.set(sortedRegions);
  }

  removeQuestion(questionIndex: number) {
    if (questionIndex < 0 || questionIndex >= this.regions().length) {
      console.error('Invalid question index');
      return;
    }
    const regionName = this.regions()[questionIndex].name;
    this.exampleAnswers.delete(regionName);
    this.exampleFlags.delete(regionName);
    this.selectedPassageMap.delete(regionName);
    this.warningMarkers = this.warningMarkers.filter((w) => w.id !== this.regions()[questionIndex].id);
    this.regions.set(this.regions().filter((_, index) => index !== questionIndex));

    this.sortRegionsByName();
    this.removeContextMenu();
    this.drawImage();
  }

  removeAllQuestions() {
    for (let i = this.regions().length - 1; i >= 0; i--) {
      this.removeQuestion(i);
    }
  }

  removeContextMenu() {
    this.contextMenuVisible = false;
    this.selectedRegion = -1;
    this.selectedAnswer = -1;
  }

  removeAnswer(questionIndex: number, answerIndex: number) {
    if (questionIndex < 0 || questionIndex >= this.regions().length) {
      console.error('Invalid question index');
      return;
    }
    if (answerIndex < 0 || answerIndex >= this.regions()[questionIndex].answers.length) {
      console.error('Invalid answer index');
      return;
    }
    const region = this.regions()[questionIndex];

    // Seçilen şıkkı listeden kaldır
    region.answers.splice(answerIndex, 1);

    // UI'yi güncelle
    this.regions.set([...this.regions()]);

    this.alignAnswers(questionIndex); // 🔄 Şıkları hizala
    this.removeContextMenu();
    this.drawImage();
  }

  private removeDuplicateAnswers(answers: AnswerChoice[]): AnswerChoice[] {
    const thresholdIntersection = 0.5;

    const isOverlappingEnough = (a: AnswerChoice, b: AnswerChoice): boolean => {
      const x1 = Math.max(a.x, b.x);
      const y1 = Math.max(a.y, b.y);
      const x2 = Math.min(a.x + a.width, b.x + b.width);
      const y2 = Math.min(a.y + a.height, b.y + b.height);

      const intersectionArea = Math.max(0, x2 - x1) * Math.max(0, y2 - y1);
      const aArea = a.width * a.height;
      const bArea = b.width * b.height;

      const overlapRatio = intersectionArea / Math.min(aArea, bArea);
      return overlapRatio >= thresholdIntersection;
    };

    const keep: AnswerChoice[] = [];

    for (const current of answers) {
      let shouldKeep = true;

      for (const existing of keep) {
        if (isOverlappingEnough(existing, current)) {
          const existingArea = existing.width * existing.height;
          const currentArea = current.width * current.height;

          if (currentArea > existingArea) {
            // Remove existing, keep current
            const index = keep.indexOf(existing);
            if (index !== -1) {
              keep.splice(index, 1);
            }
            // Devam et, mevcut current eklenecek
          } else {
            shouldKeep = false;
            break; // current küçüktü, ekleme
          }
        }
      }

      if (shouldKeep) {
        keep.push(current);
      }
    }

    return keep;
  }

  private filterOutFarAnswersByRow(answers: AnswerChoice[], rowThresholdY = 15, rowDistanceFactor = 2): AnswerChoice[] {
    if (answers.length <= 2) return answers;

    // 1. Satırları y koordinatına göre gruplandır
    const sortedAnswers = [...answers].sort((a, b) => a.y - b.y);
    const rows: AnswerChoice[][] = [];

    for (const answer of sortedAnswers) {
      let added = false;
      for (const row of rows) {
        if (Math.abs(row[0].y - answer.y) <= rowThresholdY) {
          row.push(answer);
          added = true;
          break;
        }
      }
      if (!added) {
        rows.push([answer]);
      }
    }

    // 2. Ortalama şık yüksekliğini hesapla
    const averageHeight = answers.reduce((sum, a) => sum + a.height, 0) / answers.length;

    // 3. Satırların y ortalamalarını al
    const rowYValues = rows.map((row) => row.reduce((sum, a) => sum + a.y, 0) / row.length);

    // 4. En yakın komşusuyla farkı çok büyük olanları filtrele
    const validRowIndexes = rowYValues.map((y, i) => {
      if (rowYValues.length === 1) return true; // Tek satır varsa koru ✅
      // bir önceki veya sonraki row ile karşılaştır
      const prevDiff = i > 0 ? Math.abs(y - rowYValues[i - 1]) : Infinity;
      const nextDiff = i < rowYValues.length - 1 ? Math.abs(y - rowYValues[i + 1]) : Infinity;
      const closestDiff = Math.min(prevDiff, nextDiff);
      return closestDiff <= averageHeight * rowDistanceFactor;
    });

    // 5. Valid olan satırları geri döndür
    const validRows = rows.filter((_, i) => validRowIndexes[i]);
    return validRows.flat();
  }

  alignAnswers(questionIndex: number, force: boolean = false) {
    if (!this.autoAlign() && !force) return;
    const region = this.regions()[questionIndex];
    if (region.answers.length === 0) return;

    // 0.a Temizle: Üst üste binen benzer kutuları ayıkla
    region.answers = this.removeDuplicateAnswers(region.answers);
    console.log(JSON.stringify(region.answers));
    // 0.b En yoğun y'ye sahip grubu al (dağılmışları at)
    region.answers = this.filterOutFarAnswersByRow(region.answers);

    const thresholdY = 10;

    // 1. Satırları belirle (y farkına göre grupla)
    const sortedAnswers = [...region.answers].sort((a, b) => a.y - b.y);
    const rows: AnswerChoice[][] = [];

    for (const answer of sortedAnswers) {
      let addedToRow = false;
      for (const row of rows) {
        const avgY = row.reduce((sum, a) => sum + (a.y + a.height) / 2, 0) / row.length;
        if (Math.abs(avgY - (answer.y + answer.height) / 2) <= thresholdY) {
          row.push(answer);
          addedToRow = true;
          break;
        }
      }
      if (!addedToRow) rows.push([answer]);
    }

    // Row bilgisi çıktı: kaç row ve her birinde kaç şık var?
    const rowLengths = rows.map((row) => row.length);
    const allSameLength = rowLengths.every((len) => len === rowLengths[0]);

    // Durum 1: Tek satır varsa, tüm şıkların y ve height değerini eşitle
    if (rows.length === 1) {
      const row = rows[0];
      const minY = Math.min(...row.map((a) => a.y));
      const maxY = Math.max(...row.map((a) => a.y + a.height));
      const h = maxY - minY;
      row.forEach((a) => {
        a.y = minY;
        a.height = h;
      });
    }

    // Durum 2: Tüm satırlarda yalnızca bir answer varsa → x ve width eşitle
    else if (rowLengths.every((len) => len === 1)) {
      const allAnswers = rows.map((r) => r[0]);
      const minX = Math.min(...allAnswers.map((a) => a.x));
      const maxX = Math.max(...allAnswers.map((a) => a.x + a.width));
      const w = maxX - minX;
      allAnswers.forEach((a) => {
        a.x = minX;
        a.width = w;
      });
    }

    // Durum 3: Birden fazla satır ve her satırda eşit sayıda şık varsa
    else if (allSameLength && rowLengths[0] > 1) {
      const columnCount = rowLengths[0];

      // Kolon hizalama (x ve width)
      for (let col = 0; col < columnCount; col++) {
        // Aynı kolondaki şıkları bulmak için: her row'daki şıklar arasında x koordinatları yakın olanları grupla
        // Her row'daki şıklar arasında, bu kolona en yakın x'e sahip olanı seç
        const baseRow = rows[0];
        const baseX = baseRow[col].x;
        const thresholdX = 15; // X yakınlık toleransı (piksel)

        const columnAnswers = rows.map((row) => {
          // O satırda baseX'e en yakın x'e sahip olan şıkkı bul
          let minDiff = Infinity;
          let closest = row[0];
          for (const answer of row) {
            const diff = Math.abs(answer.x - baseX);
            if (diff < minDiff && diff <= thresholdX) {
              minDiff = diff;
              closest = answer;
            }
          }
          return closest;
        });
        const minX = Math.min(...columnAnswers.map((a) => a.x));
        const maxX = Math.max(...columnAnswers.map((a) => a.x + a.width));
        const w = maxX - minX;
        columnAnswers.forEach((a) => {
          a.x = minX;
          a.width = w;
        });
      }

      // Satır hizalama (y ve height)
      for (const row of rows) {
        const minY = Math.min(...row.map((a) => a.y));
        const maxY = Math.max(...row.map((a) => a.y + a.height));
        const h = maxY - minY;
        row.forEach((a) => {
          a.y = minY;
          a.height = h;
        });
      }
    }

    // Durum 4 ve 5: Satırlardaki şık sayıları farklıysa → sadece y ve height hizalama
    else {
      for (const row of rows) {
        const minY = Math.min(...row.map((a) => a.y));
        const maxY = Math.max(...row.map((a) => a.y + a.height));
        const h = maxY - minY;
        row.forEach((a) => {
          a.y = minY;
          a.height = h;
        });
      }
    }

    region.answers = rows.flat();
    this.renameAnswers(questionIndex);
    this.regions.set([...this.regions()]);
    this.drawImage();
  }

  alignAnswers_old(questionIndex: number) {
    if (!this.autoAlign()) return;
    const region = this.regions()[questionIndex];
    if (region.answers.length === 0) return;

    const minX = Math.min(...region.answers.map((a) => a.x));
    const minY = Math.min(...region.answers.map((a) => a.y));
    const maxWidth = Math.max(...region.answers.map((a) => a.width));
    const maxHeight = Math.max(...region.answers.map((a) => a.height));

    const isVertical = region.answers.every((a) => Math.abs(a.x - minX) < 10);

    if (isVertical) {
      region.answers.forEach((a) => {
        a.x = Math.max(region.x, minX); // 🔹 X koordinatını soru alanı içinde tut
        a.width = Math.min(region.width, maxWidth); // 🔹 Genişlik soru alanını aşamaz
      });
    } else {
      region.answers.forEach((a) => {
        a.y = Math.max(region.y, minY); // 🔹 Y koordinatını soru alanı içinde tut
        a.height = Math.min(region.height, maxHeight); // 🔹 Yükseklik soru alanını aşamaz
      });
    }

    // 🔄 Şıkları yeniden adlandır (A, B, C, D)
    this.renameAnswers(questionIndex);

    // 🔥 Güncellenmiş şık listesini kaydet ve UI'yi güncelle
    this.regions.set([...this.regions()]);
    this.drawImage();
  }

  renameAnswers(questionIndex: number) {
    const region = this.regions()[questionIndex];
    if (region.answers.length === 0) return;

    // Tüm şıkların X ve Y değerlerini kontrol et
    const minX = Math.min(...region.answers.map((a) => a.x));
    const minY = Math.min(...region.answers.map((a) => a.y));

    // **Dikey mi, yatay mı olduğunu belirle**
    const isVertical = region.answers.every((a) => Math.abs(a.x - minX) < 10); // X'ler yakınsa dikey hizalanmış demektir.

    // **Şıkları sıralayıp A, B, C, D olarak yeniden isimlendir**
    const sortedAnswers = [...region.answers].sort((a, b) => (isVertical ? a.y - b.y : a.x - b.x));

    const labels = ['A', 'B', 'C', 'D'];
    sortedAnswers.forEach((answer, index) => {
      if (index < labels.length) {
        answer.label = labels[index]; // Yeni isimleri ata
      }
    });
    // 🔁 region.answers'ı yeniden sırala
    region.answers = sortedAnswers;
  }

  toggleExampleMode(questionId: string) {
    const isExample = this.exampleFlags.get(questionId) ?? false;
    this.exampleFlags.set(questionId, !isExample);

    if (!isExample) {
      // 📝 Eğer örnek soru seçildiyse, default boş bir metin ekle
      this.exampleAnswers.set(questionId, '');
    } else {
      // ❌ Eğer kapatıldıysa, metni temizle
      this.exampleAnswers.delete(questionId);
    }
  }

  onTextChanged(questionId: string, event: any) {
    const content = event;
    this.exampleAnswers.set(questionId, content);
  }

  isExample(questionId: string): boolean {
    return this.exampleFlags.get(questionId) ?? false;
  }

  getExampleAnswer(questionId: string): string {
    return this.exampleAnswers.get(questionId) ?? '';
  }
}
