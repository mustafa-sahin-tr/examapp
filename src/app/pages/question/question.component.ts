import { Component, inject, OnInit } from '@angular/core';
import { FormGroup, Validators, FormArray, FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
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
import { SubjectService } from '../../services/subject.service';
import { Subject } from '../../models/subject';
import { QuestionService } from '../../services/question.service';
import { SubTopic } from '../../models/subtopic';
import { Topic } from '../../models/topic';
import { QuillModule } from 'ngx-quill';
import { ActivatedRoute, Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { Test, TestInstance } from '../../models/test-instance';
import { QuestionListComponent } from '../question-list/question-list.component';
import { TestService } from '../../services/test.service';
import { QuestionForm } from '../../models/question-form';
import { Book, BookTest } from '../../models/book';
import { BookService } from '../../services/book.service';
import { Passage } from '../../models/question';
import { PassageCardComponent } from '../../shared/components/passage-card/passage-card.component';
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
            MatCardModule,
            MatIconModule,
            QuestionListComponent,
            QuillModule,
            PassageCardComponent
          ]
})
export class QuestionComponent implements OnInit {

  testList: Test[] = [];
  subjects: Subject[] = [];
  passages: Passage[] = []; 
  topics: Topic[] = [];
  subTopics: SubTopic[] = [];
  id: number | null = null;
  point: number = 5;
  isEditMode: boolean = false;
  testInstance: TestInstance = {
    id: 0,
    testName: 'DryRun',
    status: 0,
    maxDurationSeconds: 1800,
    testInstanceQuestions: [],
    isPracticeTest: false
  }
  modules = {
    formula: true,
    toolbar: [      
      [{ header: [1, 2, false] }],
      ['bold', 'italic', 'underline'],
      ['formula'], 
      ['image', 'code-block'],
      ['color','background']
    ]
  };
  router = inject(Router);
  route = inject(ActivatedRoute);
  questionService = inject(QuestionService);
  testService = inject(TestService);
  subjectService = inject(SubjectService);
  snackBar = inject(MatSnackBar);
  questionForm!: FormGroup;
  bookService = inject(BookService);
  

  resetFormWithDefaultValues(state: any) {
    this.questionForm = new FormGroup<QuestionForm>({
      text: new FormControl(''),
      subText: new FormControl(''),
      image: new FormControl(''),    
      subjectId: new FormControl(state?.subjectId || 0, { nonNullable: true, validators: [Validators.required] }),
      topicId: new FormControl(state?.topicId || 0, { nonNullable: true, validators: [Validators.required] }),
      subtopicId: new FormControl(state?.subtopicId || 0, { nonNullable: true, validators: [Validators.required] }),
      point: new FormControl(5, { nonNullable: true, validators: [Validators.required] }),
      correctAnswer: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
      isExample: new FormControl(false, { nonNullable: true, validators: [Validators.required] }),
      hasPassage: new FormControl(false, { nonNullable: true }), 
      practiceCorrectAnswer: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
      answerColCount: new FormControl(3, { nonNullable: true, validators: [Validators.required] }),
      passageId: new FormControl(0, { nonNullable: false }),
      passageText: new FormControl(''),
      passageImage: new FormControl(''),
      passageTitle: new FormControl(''),
      testId: new FormControl(state?.testId || 0, { nonNullable: true, validators: [Validators.required] }),
      answers: new FormArray(
          Array(4).fill(0).map(() => new FormGroup({
              text: new FormControl(''),
              image: new FormControl(''),
             })))
    });
  }

  ngOnInit() {

    const navigation = this.router.getCurrentNavigation();
    const state = navigation?.extras.state as { subjectId?: number; topicId?: number, subtopicId?: number, testId?: number };
    this.resetFormWithDefaultValues(state);
    this.id = this.route.snapshot.paramMap.get('id') ? Number(this.route.snapshot.paramMap.get('id')) : null;
    this.isEditMode = this.id !== null;  
    this.loadCategories();
    this.loadTests();
    if(this.isEditMode) {
      this.loadQuestion();
    }
  }


  loadQuestion() {
    this.questionService.get(this.id!).subscribe(question => {
      this.questionForm.patchValue({
        text: question.text,
        subText: question.subText,
        image: question.imageUrl,
        subjectId: question.subjectId,
        topicId: question.topicId,
        subtopicId: question.subjectId,
        point: 10,        
        isExample: question.isExample,
        practiceCorrectAnswer: question.practiceCorrectAnswer,
        answerColCount: question.answerColCount,        
        hasPassage: question.passage && question.passage.id > 0,
        passageId: question.passage?.id || 0,  
        passageText: question.passage?.text,
        passageImage: question.passage?.imageUrl,
        passageTitle: question.passage?.title,  
      });

      const answerArray = this.questionForm.get('answers') as FormArray;
      answerArray.clear();
      for(let i = 0; i < question.answers.length; i++) {
        answerArray.push(
          new FormGroup({
            text: new FormControl(question.answers[i].text),
            image: new FormControl(question.answers[i].imageUrl),
          })
        );
        if(question.answers[i].id === question.correctAnswer?.id) {
          this.questionForm.patchValue({ correctAnswer: i });
        }
      }

      this.onSubjectChange();
      this.onTopicChange();
    });
  }

  addAnswer() {
    this.answers.push(
      new FormGroup({
        text: new FormControl(''),
        image: new FormControl('')            
      })
    );
  }

  removeAnswer(index: number) {
    if (this.answers.length > 2) { // En az iki şık olmalı
      this.answers.removeAt(index);
    }
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

  onImageUploadForPassage(event: any) {
    const file = event.target.files[0];
    if (file) {
      const reader = new FileReader();
      reader.onload = () => {
        this.questionForm.patchValue({ passageImage: reader.result });
      };
      reader.readAsDataURL(file);
    }
  }

  loadTests() {
    this.testService.search('').subscribe(data => {
      this.testList = data.items;
    });
  }
  
  loadCategories() {
    this.subjectService.loadCategories().subscribe(data => {
      this.subjects = data;
      if(this.questionForm.value.subjectId) {
        this.onSubjectChange();
      }
    });

    this.questionService.loadPassages().subscribe(data => {
      this.passages = data;
    });
  }



  onSubjectChange() {
    if(this.questionForm.value.subjectId) {
      this.subjectService.getTopicsBySubject(this.questionForm.value.subjectId).
        subscribe(data => {
          this.topics = data
          if(this.questionForm.value.topicId) {
            this.onTopicChange();
          }
        } );
    } 
    
    this.subTopics = []; // Konu değişince alt konuları sıfırla
  }
  
  onTopicChange() {
    if(this.questionForm.value.topicId) {
      this.subjectService.getSubTopicsByTopic(this.questionForm.value.topicId).subscribe(data => this.subTopics = data);
    }
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

  onSaveAndNew() {
    this.onSave(true);
  }

  onSubmit() {
    this.onSave()
  }

  onSave(navigateNewQuestion: boolean = false) {
    const formData = this.questionForm.value;

    if(formData.hasPassage && formData.passageId <= 0) {      
      if(!formData.passageText && !formData.passageImage) {
        this.snackBar.open('Lütfen metin veya resim içeren bir pasaj ekleyin!', 'Tamam', { duration: 3000 });
        return;
      }
    }

    if (!formData.text && !formData.image) {
      this.snackBar.open('Lütfen soruya metin veya resim ekleyin!', 'Tamam', { duration: 3000 });
      return;
    }

    if(formData.isExample) {
      if(!formData.practiceCorrectAnswer) {
        this.snackBar.open('Lütfen örnek soru için doğru cevabı seçin!', 'Tamam', { duration: 3000 });
        return
      }
    }
    else 
    {
      const validAnswers = formData.answers.filter((ans: any) => ans.text || ans.image);
      if (validAnswers.length < 3) {
        this.snackBar.open('Lütfen en az 3 cevap şıkkını doldurun!', 'Tamam', { duration: 3000 });
        return;
      }

      if (formData.correctAnswer === null) {
        this.snackBar.open('Lütfen doğru cevabı seçin!', 'Tamam', { duration: 3000 });
        return;
      }
    }

    
    

    console.log('Gönderilen Form:', formData);

        
        // ✅ Base64'ten Temizlenmiş Bir Soru Nesnesi Hazırlıyoruz
        const questionPayload = {
          id: this.id,
          text: formData.text,
          subText: formData.subText,
          image: formData.image,
          categoryId: formData.categoryId,
          point: formData.point,
          correctAnswer: formData.correctAnswer,
          testId: formData.testId,
          topicId: formData.topicId,
          subtopicId: formData.subtopicId,
          subjectId : formData.subjectId,
          isExample: formData.isExample,
          practiceCorrectAnswer: formData.practiceCorrectAnswer,
          answerColCount: formData.answerColCount,  
          passage: {
            id: formData.passageId,
            title: formData.passageTitle,
            text: formData.passageText,
            imageUrl: formData.passageImage
          },
          answers: formData.answers.map((answer: any, index: number) => ({
            text: answer.text,
            image: answer.image,
            isCorrect: index == formData.correctAnswer
          }))
        };
  
        // ✅ Backend API'ye POST isteği gönderiyoruz
        this.questionService.saveQuestion(questionPayload).subscribe({
          next: (response) => {
            console.log('Soru Kaydedildi:', response);
            alert('Soru başarıyla kaydedildi!');    
            if(!this.testInstance.testInstanceQuestions.find(q => q.id === response.questionId)) {
              this.testInstance.testInstanceQuestions.push({
                id: response.questionId,
                question: {
                  id: response.questionId,
                  text: questionPayload.text,
                  subText: questionPayload.subText,
                  imageUrl: questionPayload.image,
                  category: { id: questionPayload.categoryId, name: '' },
                  answers: questionPayload.answers.filter((a: any) => a.text || a.image),
                  isExample: questionPayload.isExample,
                  subjectId: questionPayload.subjectId,
                  topicId: questionPayload.topicId,
                  subtTopicId: questionPayload.subtopicId,              
                  answerColCount: questionPayload.answerColCount
                },
                order: this.testInstance.testInstanceQuestions.length + 1,
                selectedAnswerId: 0,
                timeTaken: 0
              })          
            }
            
            if (navigateNewQuestion) {
              this.resetFormWithDefaultValues({
                subjectId: questionPayload.subjectId,
                topicId: questionPayload.topicId,
                subtopicId: questionPayload.subtopicId,
                testId: questionPayload.testId,    
              });              
            } else {
              //);
            }
          },
          error: (err) => {
            console.error('Hata oluştu:', err);
            alert('Soru kaydedilirken hata oluştu!');
          }
        });

    this.snackBar.open('Soru başarıyla kaydedildi!', 'Tamam', { duration: 3000 });
  }
}
