import { Component } from '@angular/core';

import { WorksheetListEnhancedComponent } from '../pages/worksheet-list/worksheet-list-enhanced.component';

@Component({
  selector: 'app-home',
  imports: [WorksheetListEnhancedComponent],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
})
export class HomeComponent {}
