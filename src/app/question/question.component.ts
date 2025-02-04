import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators, FormArray, FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatButtonModule } from '@angular/material/button';
import { MatSelectModule } from '@angular/material/select';
import { MatRadioModule } from '@angular/material/radio';
import { MatSliderModule } from '@angular/material/slider';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { CategoryService } from '../services/category.service';
import { Category } from '../models/category';
import { QuestionService } from '../services/question.service';

@Component({
  selector: 'app-question',
  standalone: true,
  templateUrl: './question.component.html',
  styleUrls: ['./question.component.scss'],
  imports: [MatInputModule,
            MatFormFieldModule,
            MatButtonModule,
            MatSelectModule,
            MatRadioModule,
            MatSliderModule,
            MatSnackBarModule,
            FormsModule,
            ReactiveFormsModule,
            CommonModule,
            MatCardModule]
})
export class QuestionComponent implements OnInit {
  questionForm: FormGroup;
  categories: Category[] = [];
  point: number = 5;
  constructor(private fb: FormBuilder, private snackBar: MatSnackBar,
      private catService: CategoryService, private questionService: QuestionService
  ) {
    this.questionForm = this.fb.group({
      text: ['', Validators.required],
      image: [null],
      categoryId: ['', Validators.required],
      point: [5, Validators.required],
      correctAnswer: [null, Validators.required], // ✅ Tek bir doğru cevap tutulacak
      answers: this.fb.array(
        Array(4).fill(0).map(() => this.fb.group({
          text: [''],
          image: [null]
        }))
      )
    });
  }

  ngOnInit() {
    this.loadCategories();
  }

  get answers(): FormArray {
    return this.questionForm.get('answers') as FormArray;
  }

  onImageUploadForQuestion(event: any) {
    const file = event.target.files[0];
    if (file) {
      const reader = new FileReader();
      reader.onload = () => {
        this.questionForm.patchValue({ image: reader.result });
      };
      reader.readAsDataURL(file);
    }
  }
  
  loadCategories() {
    this.catService.loadCategories().subscribe(data => {
      this.categories = data;
    });
  }

  getFormControl(control: any): FormControl {
    return control as FormControl;
  }

  onImageUpload(event: any, field: string, index?: number) {
    const file = event.target.files[0];
    if (file) {
      const reader = new FileReader();
      reader.onload = () => {
        if (index !== undefined) {
          this.answers.at(index).patchValue({ image: reader.result });
        } else {
          this.questionForm.patchValue({ [field]: reader.result });
        }
      };
      reader.readAsDataURL(file);
    }
  }

  getAnswerLabel(index: number): string {
    return `Şık ${String.fromCharCode(65 + index)}`; // 65 = 'A', 66 = 'B' ...
  }

  onSubmit() {
    const formData = this.questionForm.value;

    if (!formData.text && !formData.image) {
      this.snackBar.open('Lütfen soruya metin veya resim ekleyin!', 'Tamam', { duration: 3000 });
      return;
    }

    const validAnswers = formData.answers.filter((ans: any) => ans.text || ans.image);
    if (validAnswers.length < 4) {
      this.snackBar.open('Lütfen tüm cevap şıklarını doldurun!', 'Tamam', { duration: 3000 });
      return;
    }

    if (!formData.correctAnswer) {
      this.snackBar.open('Lütfen doğru cevabı seçin!', 'Tamam', { duration: 3000 });
      return;
    }

    console.log('Gönderilen Form:', formData);

    
        // ✅ Base64'ten Temizlenmiş Bir Soru Nesnesi Hazırlıyoruz
        const questionPayload = {
          text: formData.text,
          image: formData.image,
          categoryId: formData.categoryId,
          point: formData.point,
          correctAnswer: formData.correctAnswer,
          answers: formData.answers.map((answer: any) => ({
            text: answer.text,
            image: answer.image
          }))
        };
  
        // ✅ Backend API'ye POST isteği gönderiyoruz
        this.questionService.createQuestion(questionPayload).subscribe({
          next: (response) => {
            console.log('Soru Kaydedildi:', response);
            alert('Soru başarıyla kaydedildi!');
            this.questionForm.reset();
          },
          error: (err) => {
            console.error('Hata oluştu:', err);
            alert('Soru kaydedilirken hata oluştu!');
          }
        });

    this.snackBar.open('Soru başarıyla kaydedildi!', 'Tamam', { duration: 3000 });
  }
}
