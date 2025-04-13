// completed-worksheet-card.component.ts
import { Component, Input, OnInit } from '@angular/core';
import { InstanceSummary } from '../../models/test-instance';

@Component({
  selector: 'app-completed-worksheet-card',
  templateUrl: './completed-worksheet-card.component.html',
  styleUrls: ['./completed-worksheet-card.component.scss']
})
export class CompletedWorksheetCardComponent implements OnInit {
  @Input() completedTest!: InstanceSummary;

  ngOnInit(): void {
    console.log('completedTest:', this.completedTest);
  }
}  