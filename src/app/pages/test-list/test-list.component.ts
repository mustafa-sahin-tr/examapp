import { Component, OnInit } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { TestService } from '../../services/test.service';
import { Exam } from '../../models/test-instance';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';


@Component({
  selector: 'app-test-list',
  templateUrl: './test-list.component.html',
  styleUrls: ['./test-list.component.scss'],
  imports: [CommonModule, MatCardModule, MatButtonModule]
})
export class TestListComponent implements OnInit {
  tests: Exam[] = [];

  constructor(private testService: TestService,
    private router: Router
  ) {}

  ngOnInit() {
    this.loadTests();
  }

  loadTests() {
    const studentId = 1; // TODO: Kullanıcı giriş yaptıysa token'dan çek
    this.testService.loadTest()
      .subscribe(response => {
        this.tests = response;
      });
  }

  startTest(testId: number) {
    this.testService.startTest(testId)
      .subscribe(response => {
        this.router.navigate(['/test', testId]);
      });
    }
    
    continueTest(testId: number) {        
        this.router.navigate(['/test', testId]);          
    }

    navigateToCreateTest() {
      this.router.navigate(['/exam']); // Yeni test oluşturma sayfasına yönlendir
    }
  
}
