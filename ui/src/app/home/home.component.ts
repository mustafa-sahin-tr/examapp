import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { WorksheetListEnhancedComponent } from '../pages/worksheet-list/worksheet-list-enhanced.component';

@Component({
  selector: 'app-home',
  imports: [CommonModule, WorksheetListEnhancedComponent],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss',
})
export class HomeComponent {}
