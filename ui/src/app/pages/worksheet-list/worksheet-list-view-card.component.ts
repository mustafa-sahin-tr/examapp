import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { RouterModule } from '@angular/router';
import { Test } from '../../models/test-instance';

@Component({
  selector: 'app-worksheet-list-view-card',
  standalone: true,
  imports: [CommonModule, RouterModule, MatIconModule],
  templateUrl: './worksheet-list-view-card.component.html',
  styleUrl: './worksheet-list-view-card.component.scss',
})
export class WorksheetListViewCardComponent {
  @Input({ required: true }) course!: Test;

  private readonly images = ['honey-back.png', 'rect-back.png', 'triangle-back.png', 'diamond-back.png'];

  protected backgroundImageUrl(): string {
    const id = this.course?.id ?? 0;
    const randomIndex = Math.abs(id) % this.images.length;
    return `/${this.images[randomIndex]}`;
  }
}
