// worksheet-card.component.ts
import { CommonModule } from '@angular/common';
import { AfterViewInit, Component, EventEmitter, inject, Input, OnInit, Output } from '@angular/core';
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
  imports: [CommonModule, IsStudentDirective]
})
export class WorksheetCardComponent implements OnInit {
  @Input() test!: Test;
  @Output() cardClick = new EventEmitter<void>();
  @Input() size: string = '225px'; // Default size
  @Input() color: string = 'purple'; // Default color
  @Input() transform: string = 'scale(1)'; // Default transform
  @Input() centerLabel: string = 'center'; // Default center label  
  @Input() styleConfig: StyleConfig = CARDSTYLES['default'];

  router = inject(Router);
  onClick() {
    this.cardClick.emit();
  }

  public getTestColor(test: Test): string {
  
          if (test.name.toLocaleLowerCase().includes('matema')) {
            return 'blue';
          }
          else if (test.name.toLocaleLowerCase().includes('fen')) {
            return 'green';          
          }
          else if (test.name.toLocaleLowerCase().includes('hayat')) {
            return 'orange';          
          }
          else if (test.name.toLocaleLowerCase().includes('türkçe')) {
          return 'red';
        } 
        return 'default';
      }

  ngOnInit(): void {
    this.styleConfig = CARDSTYLES[this.getTestColor(this.test)];
  }

  edit() {
    this.router.navigate(['/exam', this.test.id]);  
  }

  

}