import { Component, ElementRef, EventEmitter, Output, ViewChild, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { QuestionRegion,AnswerChoice } from '../../models/draws';
import { QuestionCanvasForm } from '../../models/question-form';
import { QuillModule } from 'ngx-quill';
import { FormsModule, NgModel } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBar } from '@angular/material/snack-bar';
import { QuestionDetectorService } from '../../services/question-detector.service';
import { Prediction } from '../../models/prediction';
import { HttpStatusCode } from '@angular/common/http';



@Component({
  selector: 'app-image-selector',
  standalone: true,
  imports: [CommonModule, QuillModule, FormsModule, MatButtonModule],
  templateUrl: './image-selector.component.html',
  styleUrls: ['./image-selector.component.scss']
})
export class ImageSelectorComponent {
  @ViewChild('canvas', { static: false }) canvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('fileInput', { static: false }) fileInput!: ElementRef<HTMLInputElement>;  

  private ctx!: CanvasRenderingContext2D | null;
  private img = new Image();

  public autoAlign = signal<boolean>(true);
  public autoMode = signal<boolean>(false);
  public onlyQuestionMode = signal<boolean>(true);
  public selectionMode = signal<'passage' | 'question' | 'answer' | null>(null);
  public selectionModeLocked = signal<'passage' | 'question' | 'answer' | null>(null);
  public passages = signal<{ id: string; x: number; y: number; width: number; height: number }[]>([]);
  public selectedPassageMap = new Map<string, string>(); // 🗂 Soru ID -> Passage ID eşlemesi
  public imageData = signal<string | null>(null); // 🖼️ Base64 formatında resim verisi
  public regions = signal<QuestionRegion[]>([]);
  public imageSrc = signal<string | null>(null);
  public imagName = signal<string | null>(null);
  questionDetectorService = inject(QuestionDetectorService);
  private snackBar = inject(MatSnackBar);

  private startX = 0;
  private startY = 0;
  private isDrawing = false;
  private selectedQuestionIndex = -1;


  public exampleAnswers = new Map<string, string>(); // 📜 Her soru için örnek cevapları sakla
  public exampleFlags = new Map<string, boolean>();  // ✅ Her soru için "isExample" flag'ini sakla

  private currentX = 0;
  private currentY = 0;

  imageFiles: File[] = [];
  currentImageIndex = 0;
  currentImageSrc: string | null = null;
  
  handleFilesInput2(event: Event) {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      this.imageFiles = Array.from(input.files)
        .filter(file => file.type.startsWith('image/'));
      this.currentImageIndex = 0;
      this.loadCurrentImage();
    }
  }

  loadCurrentImage() {
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
          if(this.selectionModeLocked() !== 'answer') {
            this.predict();
          } else {
            this.regions.set([]);
            const region = { name: "Soru", x: 0, y: 0, width: this.img.width -1, height: this.img.height -1, answers: [],passageId:"0",
              imageId: "", imageUrl:"", id:0, isExample: false, exampleAnswer: null }    
            this.regions.set([...this.regions(),region]);
            this.selectQuestion(0);
          }
          this.drawImage(); 
        }
    };
    reader.readAsDataURL(file); // 📦 Sadece o anki resmi belleğe yükle
    
  }

  nextImage() {
    if (this.imageFiles.length === 0) return;
  
    this.currentImageIndex++;
    if (this.currentImageIndex >= this.imageFiles.length) {
      this.currentImageIndex = 0; // döngüsel olarak başa sar
    }
    this.loadCurrentImage();
  }
  
  previousImage() {
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
          if(this.selectionModeLocked() !== 'answer') {
            this.predict();
          } else {
            this.regions.set([]);
            const region = { name: "Soru", x: 0, y: 0, width: this.img.width -1, height: this.img.height -1, answers: [],passageId:"0",
              imageId: "", imageUrl:"", id:0, isExample: false, exampleAnswer: null }    
            this.regions.set([...this.regions(),region]);
            this.selectQuestion(0);
          }
          this.drawImage();
        }
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

  lockSelectionMode(mode: 'passage' | 'question' | 'answer') {
    this.selectionModeLocked.set(mode);    
    this.toggleSelectionMode(mode);
  }

  toggleOnlyQuestionMode() {
    this.onlyQuestionMode.set(!this.onlyQuestionMode());
  }

  onMouseMove(event: MouseEvent) {
    if (!this.isDrawing) return;
  
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
      case 'passage': return 'purple';
      case 'question': return 'red';
      case 'answer': return 'blue';
      default: return 'black';  
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
      }
  
      // 🟦 **Şık alanlarını mavi renkte çiz**
      if(!this.onlyQuestionMode()) {
        for (const region of this.regions()) {
          for (const answer of region.answers) {
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
    const updatedRegions = this.regions().map(region => {
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
  
    if (this.selectionMode() === 'passage') {
      const id = `p${this.passages().length + 1}`;
      this.passages.set([...this.passages(), { id, x: this.startX, y: this.startY, width, height }]);
    } else if (this.selectionMode() === 'question') {      
      const name = `Soru ${this.regions().length + 1}`;
      this.regions.set([...this.regions(), { name, x: this.startX, y: this.startY, width, height, answers: [],passageId:"0",
              imageId: "",imageUrl:"", id:0, isExample: false, exampleAnswer: null }]);
      this.selectQuestion(this.regions().length - 1);
    } else if (this.selectionMode() === 'answer' && this.selectedQuestionIndex !== -1) {
      const label = `Şık ${this.regions()[this.selectedQuestionIndex].answers.length + 1}`;
      this.regions()[this.selectedQuestionIndex].answers.push({ label, x: this.startX, y: this.startY, width, height, isCorrect: false, id: 0 });
      if(this.regions()[this.selectedQuestionIndex].answers.length === 3) {
        if(this.autoAlign()) {
          this.alignAnswers(this.selectedQuestionIndex);
        }
        this.toggleSelectionMode('question');
      }
    }
    
  
    this.isDrawing = false;
    this.drawImage();
  }

  toggleSelectionMode(mode: 'question' | 'answer' | 'passage') {
    if(this.selectionModeLocked()) 
    {
      this.selectionMode.set(this.selectionModeLocked());
      return;
    }

    this.selectionMode.set(mode); // TODO: Seçim modunu ayarla
    
    if (mode === 'answer' && this.selectedQuestionIndex < 0) {
      alert("Lütfen önce bir soru seçin ve ardından şıkları ekleyin.");
    }
  }

  selectQuestion(index: number) {
    this.selectedQuestionIndex = index;
    this.toggleSelectionMode('answer');    
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
    const imageData = { "image_base64" :  this.imageData() };
    const regionData = { "question" : this.regions() };  
    var data = { "imageData" : imageData, "questions" : regionData.question.map((q: any) => ({
      x: q.x,
      y: q.y,
      width: q.width,
      height: q.height,
      answers : q.answers.map((a: any) => ({
        x: a.x,
        y: a.y,
        width: a.width,
        height: a.height
      }))
    }))};
    console.log(data);
    this.questionDetectorService.sendtoFix(data).subscribe((data) => {
      if(data.success) {
        this.snackBar.open(`Successfully appended ${data.added} questions with ${data.imageFile}`, 'Tamam', { duration: 3000 });
        this.nextImage();
      }      
    });
  }

  public sendToFixForAnswer() {
    const imageData = { "image_base64" :  this.imageData() };
    const regionData = { "question" : this.regions() };  
    var data = { "imageData" : imageData, "questions" : regionData.question[0].answers.map((q: any) => ({
      x: q.x,
      y: q.y,
      width: q.width,
      height: q.height
    }))};
    console.log(data);
    this.questionDetectorService.sendtoFixForAnswer(data).subscribe((data) => {
      if(data.success) {
        this.snackBar.open(`Successfully appended ${data.added} questions with ${data.imageFile}`, 'Tamam', { duration: 3000 });
        this.nextImage();
      }      
    });
  }



  public predict() {
    this.snackBar.open('Resim analizi başlatılıyor...', 'Tamam', { duration: 2000 });
    const imageData = { "image_base64" :  this.imageData() };
    if (!imageData) return;
    

    this.questionDetectorService.readQrData(imageData).subscribe(
      (data) => {
        console.log(data);
      });

    this.questionDetectorService.predict(imageData).subscribe((questions) => {
      console.log(questions);      
      const imageWidth = Math.max(...questions.predictions.map(q => q.x + q.width));
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
              id: index
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
            passageId: "",
            imageId: "",
            imageUrl: ""
          }))
      );

      const questionCount = this.regions().length;

      // for(let i = 0; i < questionCount; i++) {
      //   this.alignAnswers(i);
      // }
      this.snackBar.open('Resim analizi tamamlandı.', 'Tamam', { duration: 2000 });
      if(
        !this.autoMode() 
        || (
          this.regions().length > 2 &&
          this.onlyQuestionMode() && 
          this.regions().every(region => region.answers && region.answers.length >= 3)
        )
      ) 
      {
        this.drawImage();
      }else{
        this.nextImage();
      }
    }
  );
  }

  public getAnswers(predictions: Prediction[], question: Prediction) : Prediction[] {
     const answers = predictions.filter((a: any) => a.class_id === 1);
     const result : Prediction[] = [];
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

    const jsonData = this.regions().map(region => ({
      questions: {
        ...region,
        imageUrl: '../data/' + this.imagName()
      }
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
      questions: this.regions().map(region => ({
        ...region,
        isExample: this.isExample(region.name),
        exampleAnswer: this.isExample(region.name) ? this.getExampleAnswer(region.name) : null
      })),
      header: header
    }
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
      questions: this.regions().map(region => ({
        ...region,
        isExample: this.isExample(region.name),
        exampleAnswer: this.isExample(region.name) ? this.getExampleAnswer(region.name) : null
      })),
      header: header
    }
  }

  public resetRegions() {
    this.passages.set([]);
    this.regions.set([]);
    this.imageData.set(null);
    this.imageSrc.set(null);
    this.exampleAnswers.clear();
    this.exampleFlags.clear();
    this.selectedPassageMap.clear();    
  }


  setCorrectAnswer(questionIndex: number, answerIndex: number) {
    const region = this.regions()[questionIndex];
  
    // Tüm cevapları yanlış yap
    region.answers.forEach(a => a.isCorrect = false);
  
    // Seçilen cevabı doğru yap
    region.answers[answerIndex].isCorrect = true;
  
    this.regions.set([...this.regions()]); // UI güncelleme
  }

  removeQuestion(questionIndex: number) {
    this.regions.set(this.regions().filter((_, index) => index !== questionIndex));
    this.drawImage (); 
    this.toggleSelectionMode('question');
  }

  removeAnswer(questionIndex: number, answerIndex: number) {
    const region = this.regions()[questionIndex];
  
    // Seçilen şıkkı listeden kaldır
    region.answers.splice(answerIndex, 1);
  
    // UI'yi güncelle
    this.regions.set([...this.regions()]);

    this.alignAnswers(questionIndex); // 🔄 Şıkları hizala

    this.drawImage();
  }

  alignAnswers(questionIndex: number) {
    if(!this.autoAlign()) return;
    const region = this.regions()[questionIndex];
    if (region.answers.length === 0) return;
  
    const minX = Math.min(...region.answers.map(a => a.x));
    const minY = Math.min(...region.answers.map(a => a.y));
    const maxWidth = Math.max(...region.answers.map(a => a.width));
    const maxHeight = Math.max(...region.answers.map(a => a.height));
  
    const isVertical = region.answers.every(a => Math.abs(a.x - minX) < 10);
  
    if (isVertical) {
      region.answers.forEach(a => {
        a.x = Math.max(region.x, minX); // 🔹 X koordinatını soru alanı içinde tut
        a.width = Math.min(region.width, maxWidth); // 🔹 Genişlik soru alanını aşamaz
      });
    } else {
      region.answers.forEach(a => {
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
    const minX = Math.min(...region.answers.map(a => a.x));
    const minY = Math.min(...region.answers.map(a => a.y));
  
    // **Dikey mi, yatay mı olduğunu belirle**
    const isVertical = region.answers.every(a => Math.abs(a.x - minX) < 10); // X'ler yakınsa dikey hizalanmış demektir.
  
    // **Şıkları sıralayıp A, B, C, D olarak yeniden isimlendir**
    const sortedAnswers = [...region.answers].sort((a, b) =>
      isVertical ? a.y - b.y : a.x - b.x
    );
  
    const labels = ["A", "B", "C", "D"];
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
