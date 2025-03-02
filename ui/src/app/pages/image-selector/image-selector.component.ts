import { Component, ElementRef, ViewChild, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { QuestionRegion,AnswerChoice } from '../../models/draws';



@Component({
  selector: 'app-image-selector',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './image-selector.component.html',
  styleUrls: ['./image-selector.component.scss']
})
export class ImageSelectorComponent {
  @ViewChild('canvas', { static: false }) canvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('fileInput', { static: false }) fileInput!: ElementRef<HTMLInputElement>;

  private ctx!: CanvasRenderingContext2D | null;
  public imageSrc = signal<string | null>(null);
  private img = new Image();
  public selectionMode = signal<'question' | 'answer' | null>(null);
  public regions = signal<QuestionRegion[]>([]);
  private startX = 0;
  private startY = 0;
  private isDrawing = false;
  private selectedQuestionIndex = -1;

  handleFileInput(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) {
      const reader = new FileReader();
      reader.onload = (e: ProgressEvent<FileReader>) => {
        this.imageSrc.set(e.target?.result as string);
        this.img.src = this.imageSrc()!;
        this.img.onload = () => this.drawImage();
      };
      reader.readAsDataURL(file);
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

      // Tüm bölgeleri tekrar çiz
      this.drawRegions();
    }
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
    if (!this.selectionMode() || !this.ctx) return;
    const rect = this.canvas.nativeElement.getBoundingClientRect();
    this.startX = event.clientX - rect.left;
    this.startY = event.clientY - rect.top;
    this.isDrawing = true;
  }

  endSelection(event: MouseEvent) {
    if (!this.selectionMode() || !this.ctx || !this.isDrawing) return;
    const rect = this.canvas.nativeElement.getBoundingClientRect();
    const endX = event.clientX - rect.left;
    const endY = event.clientY - rect.top;
    const width = endX - this.startX;
    const height = endY - this.startY;

    if (this.selectionMode() === 'question') {
      const name = `Soru ${this.regions().length + 1}`;
      this.regions.set([...this.regions(), { name, x: this.startX, y: this.startY, width, height, answers: [] }]);
    } else if (this.selectionMode() === 'answer' && this.selectedQuestionIndex !== -1) {
      const label = `Şık ${this.regions()[this.selectedQuestionIndex].answers.length + 1}`;
      this.regions()[this.selectedQuestionIndex].answers.push({ label, x: this.startX, y: this.startY, width, height, isCorrect: false });
    }

    this.isDrawing = false;
    this.drawImage(); // **Seçimi çizmek için tekrar çağır**
  }

  toggleSelectionMode(mode: 'question' | 'answer') {
    this.selectionMode.set(mode);
    if (mode === 'answer') {
      alert("Lütfen önce bir soru seçin ve ardından şıkları ekleyin.");
    }
  }

  selectQuestion(index: number) {
    this.selectedQuestionIndex = index;
    this.selectionMode.set('answer');
  }

  downloadRegions() {
    const dataStr = 'data:text/json;charset=utf-8,' + encodeURIComponent(JSON.stringify(this.regions(), null, 2));
    const downloadAnchor = document.createElement('a');
    downloadAnchor.setAttribute('href', dataStr);
    downloadAnchor.setAttribute('download', 'soru_koordinatlar.json');
    document.body.appendChild(downloadAnchor);
    downloadAnchor.click();
    document.body.removeChild(downloadAnchor);
  }

  setCorrectAnswer(questionIndex: number, answerIndex: number) {
    const region = this.regions()[questionIndex];
  
    // Tüm cevapları yanlış yap
    region.answers.forEach(a => a.isCorrect = false);
  
    // Seçilen cevabı doğru yap
    region.answers[answerIndex].isCorrect = true;
  
    this.regions.set([...this.regions()]); // UI güncelleme
  }

  removeAnswer(questionIndex: number, answerIndex: number) {
    const region = this.regions()[questionIndex];
  
    // Seçilen şıkkı listeden kaldır
    region.answers.splice(answerIndex, 1);
  
    // UI'yi güncelle
    this.regions.set([...this.regions()]);
    this.drawImage();
  }

  alignAnswers(questionIndex: number) {
    const region = this.regions()[questionIndex];
    if (region.answers.length === 0) return;
  
    // Tüm şıklar için X ve Y değerlerini kontrol et
    const minX = Math.min(...region.answers.map(a => a.x));
    const minY = Math.min(...region.answers.map(a => a.y));
    const maxWidth = Math.max(...region.answers.map(a => a.width));
    const maxHeight = Math.max(...region.answers.map(a => a.height));
  
    // **Dikey hizalama mı, yatay hizalama mı?**
    const isVertical = region.answers.every(a => Math.abs(a.x - minX) < 10); // X'ler yakınsa dikey
  
    if (isVertical) {
      // **Dikey Hizalama**
      region.answers.forEach(a => {
        a.x = minX;
        a.width = maxWidth;
      });
    } else {
      // **Yatay Hizalama**
      region.answers.forEach(a => {
        a.y = minY;
        a.height = maxHeight;
      });
    }
  
    // Güncellenmiş şık listesini kaydet
    this.regions.set([...this.regions()]);
    this.drawImage();
  }
  
  
}
