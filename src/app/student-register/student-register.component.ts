import { Component } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { AuthService } from '../services/auth.service';
import { Router } from '@angular/router';

@Component({
  selector: 'app-student-register',
  standalone: true,
  templateUrl: './student-register.component.html',
  imports: []
})
export class StudentRegisterComponent {
  studentForm: FormGroup;

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private router: Router
  ) {
    this.studentForm = this.fb.group({
      studentNumber: ['', Validators.required],
      schoolName: ['', Validators.required]
    });
  }

  onSubmit() {
    if (this.studentForm.valid) {
      this.authService.registerStudent(this.studentForm.value).subscribe(() => {
        alert('Öğrenci bilgileri kaydedildi.');
        this.router.navigate(['/dashboard']);
      });
    }
  }
}
