// completed-worksheet-card.component.ts
import { Component, Input, OnInit } from '@angular/core';
import { CompletedTest } from '../../models/test-instance';

@Component({
  selector: 'app-completed-worksheet-card',
  templateUrl: './completed-worksheet-card.component.html',
  styleUrls: ['./completed-worksheet-card.component.scss']
})
export class CompletedWorksheetCardComponent implements OnInit {
  @Input() completedTest!: CompletedTest;

  ngOnInit(): void {
    console.log('completedTest:', this.completedTest);
  }
}  