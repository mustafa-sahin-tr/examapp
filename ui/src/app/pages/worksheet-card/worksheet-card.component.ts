// worksheet-card.component.ts
import { CommonModule } from '@angular/common';
import { Component, EventEmitter, inject, Input, Output } from '@angular/core';
import { Test } from '../../models/test-instance';
import { HasRoleDirective } from '../../shared/directives/has-role.directive';
import { IsStudentDirective } from '../../shared/directives/is-student.directive';
import { Router } from '@angular/router';
import { CARDSTYLES, StyleConfig } from './worksheet-card-styles';



@Component({
  selector: 'app-worksheet-card',
  templateUrl: './worksheet-card.component.html',
  styleUrls: ['./worksheet-card.component.scss'],
  standalone: true,
  imports: [CommonModule, HasRoleDirective, IsStudentDirective]
})
export class WorksheetCardComponent {
  @Input() test!: Test;
  @Output() cardClick = new EventEmitter<void>();
  @Input() size: string = '225px'; // Default size
  @Input() color: string = 'inherit'; // Default color
  @Input() transform: string = 'scale(1)'; // Default transform
  @Input() centerLabel: string = 'center'; // Default center label
  @Input() styleConfig: StyleConfig = CARDSTYLES['primary'];

  router = inject(Router);
  onClick() {
    this.cardClick.emit();
  }

  edit() {
    this.router.navigate(['/exam', this.test.id]);  
  }

  

}