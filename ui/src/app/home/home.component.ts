import { Component, ChangeDetectionStrategy } from '@angular/core';

import { WorksheetListEnhancedComponent } from '../pages/worksheet-list/worksheet-list-enhanced.component';

@Component({
  selector: 'app-home',
  imports: [WorksheetListEnhancedComponent],
  templateUrl: './home.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './home.component.scss',
})
export class HomeComponent {}
