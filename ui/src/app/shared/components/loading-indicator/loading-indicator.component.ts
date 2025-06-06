import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

@Component({
  selector: 'app-loading-indicator',
  standalone: true,
  imports: [CommonModule, MatProgressSpinnerModule],
  template: `
    <div *ngIf="isLoading" class="loading-container">
      <mat-spinner [diameter]="diameter"></mat-spinner>
      <p *ngIf="message" class="loading-message">{{ message }}</p>
    </div>
  `,
  styles: [
    `
      .loading-container {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        padding: 1rem;
      }

      .loading-message {
        margin-top: 0.5rem;
        color: #666;
      }
    `,
  ],
})
export class LoadingIndicatorComponent {
  @Input() isLoading = false;
  @Input() message?: string;
  @Input() diameter = 40;
}
