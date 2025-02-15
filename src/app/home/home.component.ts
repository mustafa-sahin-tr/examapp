import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { WorksheetListComponent } from '../pages/worksheet-list/worksheet-list.component';

@Component({
  selector: 'app-home',
  imports: [CommonModule, WorksheetListComponent],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomeComponent {

}
