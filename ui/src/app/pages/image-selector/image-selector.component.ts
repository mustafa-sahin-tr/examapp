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
import {MatSlideToggleModule} from '@angular/material/slide-toggle'


@Component({
  selector: 'app-image-selector',
  standalone: true,
  imports: [CommonModule, QuillModule, FormsModule, MatButtonModule,MatSlideToggleModule],
  templateUrl: './image-selector.component.html',
  styleUrls: ['./image-selector.component.scss']
})
export class ImageSelectorComponent {
  @ViewChild('canvas', { static: false }) canvas!: ElementRef<HTMLCanvasElement>;
  @ViewChild('fileInput', { static: false }) fileInput!: ElementRef<HTMLInputElement>;  

  private ctx!: CanvasRenderingContext2D | null;
  private img = new Image();

  public autoAlign = signal<boolean>(false);
  public autoMode = signal<boolean>(false);
  public inProgress = signal<boolean>(false);
  public onlyQuestionMode = signal<boolean>(false);
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

  lockSelectionMode(mode: 'passage' | 'question' | 'answer' | null) {
    this.selectionModeLocked.set(mode);    
    this.toggleSelectionMode(mode);
  }

  toggleOnlyQuestionMode() {
    this.onlyQuestionMode.set(!this.onlyQuestionMode());
  }

  tapCorrectAnswer(event: MouseEvent) {
    if (this.isDrawing) return;

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
          const questionIndex = this.regions().findIndex(q => q.name === region.name);
          const answerIndex = region.answers.findIndex(a => a.label === answer.label);
          this.setCorrectAnswer(questionIndex, answerIndex);
        }
      }
    }
  } 

  onMouseMove(event: MouseEvent) {
    if (!this.isDrawing) {
      const canvas = this.canvas.nativeElement;
      const ctx = canvas.getContext('2d');
      if (!ctx) return;
    
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

  generateFirstAvailableName() {
    const prefix = "Soru ";
  
    const usedNumbers = this.regions()
      .map(r => r.name)
      .filter(name => name.startsWith(prefix))
      .map(name => parseInt(name.replace(prefix, ""), 10))
      .filter(num => !isNaN(num))
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
      const name = this.generateFirstAvailableName();
      
      this.regions.set([...this.regions(), { name, x: this.startX, y: this.startY, width, height, answers: [],passageId:"0",
              imageId: "",imageUrl:"", id:0, isExample: false, exampleAnswer: null }]);
      this.sortRegionsByName();
      this.selectQuestionByName(name);
      /// this.selectQuestion(this.regions().length - 1);

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

  toggleSelectionMode(mode: 'question' | 'answer' | 'passage' | null) {
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

  selectQuestionByName(name: string) {
    const index = this.regions().findIndex(region => region.name === name);
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
    this.inProgress.set(true);
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

      for(let i = 0; i < questionCount; i++) {
        this.alignAnswers(i);
      }

      console.log(`${this.regions().length} Adet Soru bulundu`);
      console.log(`Auto Mode: ${this.autoMode()}`);
      console.log(`Q Mode:  ${this.onlyQuestionMode()}`);
      
      if(
        this.autoMode() 
        && 
        (
          this.regions().length > 2 && 
          (this.onlyQuestionMode() || this.regions().every(region => region.answers && region.answers.length >= 3))
        )
      ) {
        console.log(`Condition: true`);
      }else{
        console.log(`Condition: false`);
      }

      this.snackBar.open('Resim analizi tamamlandı.', 'Tamam', { duration: 2000 });
      if( !this.autoMode() ) {
        // resmi çiz ve dur
        this.drawImage();
      }
      else {
        // auto mode ise question sayısını kontrol et
        if( this.regions().length > 1 ) {
          // ikiden çok fazla soru varsa durabiliriz.
          if( this.onlyQuestionMode()) {
            // ikiden fazla soru var ve sadece soru modundayız dur!
            this.drawImage();
          } else {
            // ikiden fazla soru var ve soru cevap modundayız.
            if(this.regions().every(region => region.answers && region.answers.length >= 2)) {
              // ikiden fazla soru var soru cevap modundayız ve her soruda en az 2 şık var.
              this.drawImage();
            }else{
              this.nextImage();
            }
          }
        }else{
          this.nextImage();
        }
      }

      this.inProgress.set(false);
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

  setCorrectAnswerTap(questionId: number, answerIndex: number) {
    // const region = this.regions()[questionIndex];
  
    // // Tüm cevapları yanlış yap
    // region.answers.forEach(a => a.isCorrect = false);
  
    // // Seçilen cevabı doğru yap
    // region.answers[answerIndex].isCorrect = true;
  
    // this.regions.set([...this.regions()]); // UI güncelleme
  }


  setCorrectAnswer(questionIndex: number, answerIndex: number) {
    const region = this.regions()[questionIndex];
  
    // Tüm cevapları yanlış yap
    region.answers.forEach(a => a.isCorrect = false);
  
    // Seçilen cevabı doğru yap
    region.answers[answerIndex].isCorrect = true;
  
    this.regions.set([...this.regions()]); // UI güncelleme

    this.drawImage();
  }

  sortRegionsByName() {
    const prefix = "Soru ";
    const regions = this.regions();
    const sortedRegions = regions.slice().sort((a, b) => {
      const aNum = parseInt(a.name.replace(prefix, ""), 10);
      const bNum = parseInt(b.name.replace(prefix, ""), 10);
  
      return aNum - bNum;
    });

    this.regions.set(sortedRegions);
  }

  removeQuestion(questionIndex: number) {
    
    const regionName = this.regions()[questionIndex].name;    
    this.exampleAnswers.delete(regionName);
    this.exampleFlags.delete(regionName);
    this.selectedPassageMap.delete(regionName);
    this.regions.set(this.regions().filter((_, index) => index !== questionIndex));


    this.sortRegionsByName();
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
  

  private filterToMainAnswerGroup(answers: AnswerChoice[], thresholdY = 15): AnswerChoice[] {
    const groups: AnswerChoice[][] = [];
  
    const sorted = [...answers].sort((a, b) => a.y - b.y);
  
    for (const answer of sorted) {
      let matched = false;
  
      for (const group of groups) {
        if (Math.abs(group[0].y - answer.y) <= thresholdY) {
          group.push(answer);
          matched = true;
          break;
        }
      }
  
      if (!matched) {
        groups.push([answer]);
      }
    }
  
    // En kalabalık grup = gerçek şıklar
    const mainGroup = groups.reduce((prev, curr) =>
      curr.length > prev.length ? curr : prev
    );
  
    return mainGroup;
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
    const averageHeight =
      answers.reduce((sum, a) => sum + a.height, 0) / answers.length;
  
    // 3. Satırların y ortalamalarını al
    const rowYValues = rows.map(
      (row) => row.reduce((sum, a) => sum + a.y, 0) / row.length
    );
  
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
  
  
  

  alignAnswers(questionIndex: number) {
    if(!this.autoAlign()) return;
    const region = this.regions()[questionIndex];
    if (region.answers.length === 0) return;

    // 0.a Temizle: Üst üste binen benzer kutuları ayıkla
    region.answers = this.removeDuplicateAnswers(region.answers);
    console.log(JSON.stringify(region.answers));
    // 0.b En yoğun y'ye sahip grubu al (dağılmışları at)
    region.answers = this.filterOutFarAnswersByRow(region.answers);
  
    const thresholdY = 10; // Aynı satırda sayılmaları için y farkı eşiği
    const thresholdX = 10; // Aynı kolonda sayılmaları için x farkı (şimdilik kullanılmıyor)
  
    // 1. Şıkları Y'ye göre satırlara ayır
    const sortedAnswers = [...region.answers].sort((a, b) => a.y - b.y);
  
    const rows: AnswerChoice[][] = [];
    for (const answer of sortedAnswers) {
      let rowFound = false;
      for (const row of rows) {
        if (Math.abs(row[0].y - answer.y) <= thresholdY) {
          row.push(answer);
          rowFound = true;
          break;
        }
      }
      if (!rowFound) {
        rows.push([answer]);
      }
    }
  
    // 2. Her satırı X'e göre sırala (soldan sağa)
    for (const row of rows) {
      row.sort((a, b) => a.x - b.x);
    }
  
    // 1. Kolon sayısını belirle (en kısa satır kadar)
    const minColumnCount = Math.min(...rows.map(r => r.length));

    // 2. Her kolon için x ve width ortaklaştır
    for (let col = 0; col < minColumnCount; col++) {
      const columnAnswers = rows.map(row => row[col]); // her satırdan aynı sıradaki eleman

      const minX = Math.min(...columnAnswers.map(a => a.x));
      const maxX = Math.max(...columnAnswers.map(a => a.x + a.width));

      for (const answer of columnAnswers) {
        answer.x = minX;
        answer.width = maxX - minX;
      }
    }

    
    // 3. Satırları tekrar birleştir
    const aligned = rows.flat();    
  
    region.answers = aligned;

    // // // 5. Şıkları normalize et
    // if (rows.length === 1) {
    //   // Tek satır: tüm şıkları yukarıdan hizala ve aynı yükseklik
    //   const minY = Math.min(...region.answers.map(a => a.y));
    //   const maxY = Math.max(...region.answers.map(a => a.y + a.height));
    //   region.answers.forEach(a => {
    //     a.y = minY;
    //     a.height = maxY - minY;
    //   });
    // } else {
    //   // Çok satır: tüm şıkları sola hizala ve aynı genişlik
    //   const minX = Math.min(...region.answers.map(a => a.x));
    //   const maxX = Math.max(...region.answers.map(a => a.x + a.width));
    //   region.answers.forEach(a => {
    //     a.x = minX;
    //     a.width = maxX - minX;
    //   });
    // }

    this.renameAnswers(questionIndex);
  
    // 🔥 Güncellenmiş şık listesini kaydet ve UI'yi güncelle
    this.regions.set([...this.regions()]);
    this.drawImage();
  }
  

  alignAnswers_old(questionIndex: number) {
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
