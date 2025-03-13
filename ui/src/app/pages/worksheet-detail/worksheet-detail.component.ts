import { CommonModule } from '@angular/common';
import { Component, inject, Input, OnInit } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute } from '@angular/router';
import { Exam, Test, TestInstance } from '../../models/test-instance';
import { lastValueFrom } from 'rxjs';
import { TestService } from '../../services/test.service';

@Component({
  selector: 'app-worksheet-detail',
  imports: [CommonModule,MatIconModule],
  templateUrl: './worksheet-detail.component.html',
  styleUrl: './worksheet-detail.component.scss'
})
export class WorksheetDetailComponent implements OnInit {

  
  @Input() exam!: Test; // Test bilgisi ve sorular
  route = inject(ActivatedRoute);
  testService = inject(TestService);  
  testId!: number;

  

  ngOnInit() {
    this.route.paramMap.subscribe(async params => {
      this.testId = Number(params.get('testId'));
      if (this.testId) {
        this.exam = await lastValueFrom(this.testService.get(this.testId));    
      } 
    });
  }
}
