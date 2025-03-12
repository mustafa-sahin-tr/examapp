import { Component, ElementRef, inject, OnInit, ViewChild } from '@angular/core';
import { FormGroup, Validators, FormArray, FormControl, FormsModule, ReactiveFormsModule, FormBuilder } from '@angular/forms';
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
import { QuestionCanvasForm, QuestionForm } from '../../models/question-form';
import { Book, BookTest } from '../../models/book';
import { BookService } from '../../services/book.service';
import { Passage } from '../../models/question';
import { PassageCardComponent } from '../../shared/components/passage-card/passage-card.component';
import { ImageSelectorComponent } from '../image-selector/image-selector.component';
import { debounceTime, map, Observable, startWith, switchMap, tap } from 'rxjs';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { WorksheetCardComponent } from '../worksheet-card/worksheet-card.component';
@Component({
  selector: 'app-question-canvas',
  standalone: true,
  templateUrl: './question-canvas.component.html',
  styleUrls: ['./question-canvas.component.scss'],
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
            QuillModule,
            ImageSelectorComponent, MatAutocompleteModule,WorksheetCardComponent
          ]
})
export class QuestionCanvasComponent implements OnInit {

  @ViewChild(ImageSelectorComponent) imageSelector!: ImageSelectorComponent; // 🔥 Alt bileşene erişim

  testList: Test[] = [];
  subjects: Subject[] = [];
  passages: Passage[] = []; 
  topics: Topic[] = [];
  subTopics: SubTopic[] = [];
  id: number | null = null;
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

  // 🟢 Form Kontrolleri


  // 🟢 Filtrelenmiş listeler

  constructor() {
    
  }


  

  resetFormWithDefaultValues(state: any) {
    this.questionForm = new FormGroup<QuestionCanvasForm>({
      subjectId: new FormControl(state?.subjectId || 0, { nonNullable: true, validators: [Validators.required] }),
      topicId: new FormControl(state?.topicId || 0, { nonNullable: true, validators: [Validators.required] }),
      subtopicId: new FormControl(state?.subtopicId || 0, { nonNullable: true, validators: [Validators.required] }),
      isExample: new FormControl(false, { nonNullable: true, validators: [Validators.required] }),
      practiceCorrectAnswer: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
      testId: new FormControl(state?.testId || '', { nonNullable: true, validators: [Validators.required] })      
    });

    this.questionForm.get('testId')?.valueChanges.pipe(
      debounceTime(300),
      switchMap(value => {
        console.log('Search value:', value); // Gelen değeri kontrol et
        return this.testService.search(value || '',1)
      }),
      tap(results => {
        this.testList = results.items;  
      })
    ).subscribe();

  }

   
  displayFn = (selectedoption: any): string => {    
    return selectedoption ? selectedoption.name + '-' + selectedoption.subtitle + '-' +  selectedoption.description : '';
  };

  // Kullanıcı seçim yaptığında `FormControl` içine nesneyi set et
  onOptionSelected(event: any) {
    console.log(event);
    this.questionForm.get('testId')?.setValue(event.option.value);
  }

  ngOnInit() {

    // 🟢 Backend'den test listesini getir
    this.testService.search('').subscribe(data => {
      this.testList = data.items;
    });

    const navigation = this.router.getCurrentNavigation();
    const state = navigation?.extras.state as { subjectId?: number; topicId?: number, subtopicId?: number, testId?: number };
    this.resetFormWithDefaultValues(state);
    this.id = this.route.snapshot.paramMap.get('id') ? Number(this.route.snapshot.paramMap.get('id')) : null;
    this.isEditMode = this.id !== null;  
    this.loadTests();
    this.loadCategories();

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



  onSaveAndNew() {
    this.onSave(true);
  }

  onSubmit() {
    this.onSave()
  }

  onSave(navigateNewQuestion: boolean = false) {
    const formData = this.questionForm.value;

    if(formData.isExample) {
      if(!formData.practiceCorrectAnswer) {
        this.snackBar.open('Lütfen örnek soru için doğru cevabı seçin!', 'Tamam', { duration: 3000 });
        return
      }
    }
   

    const questionPayload = {            
      testId: formData.testId ? formData.testId.id : 0,
      topicId: formData.topicId,
      subtopicId: formData.subtopicId,
      subjectId : formData.subjectId,
    }

    var payload = this.imageSelector.getRegions(questionPayload);
    this.questionService.saveBulk(payload).subscribe({
       next: (data) => {
        console.log('Soru Kaydedildi:', data);
        this.snackBar.open('sorular Başarıyla Kaydedildi', 'Tamam', { duration: 2000 });
       },
        error: (err) => {
          console.log(err);
        }
    });

  }
}
