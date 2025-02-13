import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatOptionModule } from '@angular/material/core';
import { MatFormFieldModule, MatLabel } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatToolbarModule } from '@angular/material/toolbar';
import { ActivatedRoute, Router } from '@angular/router';
import { Exam, Test } from '../../models/test-instance';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { TestService } from '../../services/test.service';
@Component({
  selector: 'app-test-create',
  templateUrl: './test-create.component.html',
  standalone: true,
  imports: [ CommonModule, MatCardModule , FormsModule, MatFormFieldModule, MatOptionModule,
    MatInputModule, MatSelectModule, MatCheckboxModule,
    ReactiveFormsModule, MatToolbarModule,MatLabel],
  styleUrls: ['./test-create.component.scss']
})
export class TestCreateComponent implements OnInit {
  id!: number | null;
  exam!: Test;
  isEditMode: boolean = false;
  testForm!: FormGroup;
  gradeId: number = 1;
  
  grades = [{ id: 1, name: '1. Sınıf' }, { id: 2, name: '2. Sınıf' }, { id: 3, name: '3. Sınıf' }];
  
  constructor(private fb: FormBuilder, private router: Router, private route: ActivatedRoute,
    private testService: TestService) {
    this.testForm = this.fb.group({
      name: ['', Validators.required],
      description: [''],
      gradeId: ['', Validators.required],
      maxDurationMinutes: [600, [Validators.required, Validators.min(10)]], // Varsayılan 10 dakika
      isPracticeTest: [false] // Çalışma testi mi?
    });
  }

  ngOnInit(): void {

    this.id = this.route.snapshot.paramMap.get('id') ? Number(this.route.snapshot.paramMap.get('id')) : null;
    this.isEditMode = this.id !== null;  

    if(this.isEditMode) {
      this.loadTest();
    }
  }

  loadTest() {
      this.testService.get(this.id!).subscribe(exam => {
        this.testForm.patchValue({
          name: exam.name,
          description: exam.description,
          gradeId: exam.gradeId,
          maxDurationMinutes: exam.maxDurationSeconds / 60,
          isPracticeTest: exam.isPracticeTest
        });
      });
    }

  onSubmit() {
    if (this.testForm.valid) {
      console.log('Yeni Test:', this.testForm.value);
      // Backend API çağrısı yapılabilir burada
      // Testi kaydet api'sini çağır.

      const testPayload: Test = {
        id: this.id,
        name: this.testForm.value.name,
        description: this.testForm.value.description,
        gradeId: this.testForm.value.gradeId,
        maxDurationSeconds: +this.testForm.value.maxDurationMinutes * 60,
        isPracticeTest: this.testForm.value.isPracticeTest        
      };

      this.testService.create(testPayload).subscribe(response => {
        this.router.navigate(['/test-list']);
      });
    }
  }

  navigateToTestList() {
    this.router.navigate(['/test-list']); // Yeni test oluşturma sayfasına yönlendir
  }
}
