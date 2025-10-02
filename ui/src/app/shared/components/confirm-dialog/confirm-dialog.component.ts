import { Component, Inject } from '@angular/core';
import { MatDialogRef, MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { CommonModule } from '@angular/common';

export interface ConfirmDialogData {
  title: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
  icon?: string;
  confirmColor?: string;
}

@Component({
  selector: 'app-confirm-dialog',
  template: `
    <div class="confirm-dialog">
      <div class="dialog-header">
        <div class="icon-container" *ngIf="data.icon">
          <mat-icon class="dialog-icon" [ngClass]="'icon-' + data.confirmColor">
            {{ data.icon }}
          </mat-icon>
        </div>
        <div class="header-text">
          <h2 mat-dialog-title>{{ data.title }}</h2>
        </div>
      </div>

      <mat-dialog-content class="dialog-content">
        <p>{{ data.message }}</p>
      </mat-dialog-content>

      <mat-dialog-actions class="dialog-actions">
        <button mat-stroked-button (click)="onCancel()" class="cancel-btn">
          {{ data.cancelText || 'İptal' }}
        </button>
        <button mat-raised-button [color]="data.confirmColor || 'warn'" (click)="onConfirm()" class="confirm-btn">
          {{ data.confirmText || 'Onayla' }}
        </button>
      </mat-dialog-actions>
    </div>
  `,
  styles: [
    `
      .confirm-dialog {
        min-width: 450px;
        max-width: 550px;
        padding: 24px;
        border-radius: 16px;
        background: linear-gradient(135deg, #ffffff 0%, #f8f9fa 100%);
        box-shadow: 0 20px 60px rgba(0, 0, 0, 0.1);
        position: relative;
        overflow: hidden;
      }

      .confirm-dialog::before {
        content: '';
        position: absolute;
        top: 0;
        left: 0;
        right: 0;
        height: 4px;
        background: linear-gradient(90deg, #ff6b6b, #ee5a24, #ff9ff3, #54a0ff);
        background-size: 300% 300%;
        animation: gradientShift 3s ease infinite;
      }

      @keyframes gradientShift {
        0% {
          background-position: 0% 50%;
        }
        50% {
          background-position: 100% 50%;
        }
        100% {
          background-position: 0% 50%;
        }
      }

      .dialog-header {
        display: flex;
        align-items: flex-start;
        gap: 16px;
        margin-bottom: 24px;
        padding-top: 8px;
      }

      .icon-container {
        background: rgba(244, 67, 54, 0.1);
        border-radius: 50%;
        padding: 12px;
        display: flex;
        align-items: center;
        justify-content: center;
        box-shadow: 0 4px 12px rgba(244, 67, 54, 0.2);
        animation: iconPulse 2s ease-in-out infinite;
      }

      @keyframes iconPulse {
        0%,
        100% {
          transform: scale(1);
        }
        50% {
          transform: scale(1.05);
        }
      }

      .dialog-icon {
        font-size: 28px;
        width: 28px;
        height: 28px;

        &.icon-warn {
          color: #f44336;
        }

        &.icon-primary {
          color: #2196f3;
        }

        &.icon-accent {
          color: #ff4081;
        }
      }

      .header-text {
        flex: 1;
      }

      h2 {
        margin: 0;
        font-size: 24px;
        font-weight: 600;
        color: #2c3e50;
        letter-spacing: -0.5px;
        line-height: 1.3;
      }

      .dialog-content {
        padding: 0 0 32px 0;
        margin-left: 60px;

        p {
          margin: 0;
          font-size: 16px;
          line-height: 1.6;
          color: #64748b;
          font-weight: 400;
        }
      }

      .dialog-actions {
        display: flex;
        justify-content: flex-end;
        gap: 16px;
        padding: 24px 0 0 0;
        margin: 0;
        border-top: 1px solid rgba(0, 0, 0, 0.05);
      }

      .cancel-btn {
        min-width: 100px;
        height: 44px;
        border-radius: 22px;
        font-weight: 500;
        font-size: 14px;
        color: #64748b;
        border-color: #e2e8f0;
        transition: all 0.3s ease;

        &:hover {
          background-color: #f1f5f9;
          border-color: #cbd5e1;
          color: #475569;
          transform: translateY(-1px);
          box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
        }
      }

      .confirm-btn {
        min-width: 100px;
        height: 44px;
        border-radius: 22px;
        font-weight: 600;
        font-size: 14px;
        transition: all 0.3s ease;
        position: relative;
        overflow: hidden;

        &:hover {
          transform: translateY(-2px);
          box-shadow: 0 8px 25px rgba(244, 67, 54, 0.3);
        }

        &:active {
          transform: translateY(0);
        }
      }

      /* Dark mode support */
      @media (prefers-color-scheme: dark) {
        .confirm-dialog {
          background: linear-gradient(135deg, #1e293b 0%, #0f172a 100%);
          color: #e2e8f0;
        }

        h2 {
          color: #f1f5f9;
        }

        .dialog-content p {
          color: #94a3b8;
        }

        .dialog-actions {
          border-top-color: rgba(255, 255, 255, 0.1);
        }

        .cancel-btn {
          color: #94a3b8;
          border-color: #334155;

          &:hover {
            background-color: #334155;
            border-color: #475569;
            color: #e2e8f0;
          }
        }
      }

      /* Mobile responsive */
      @media (max-width: 768px) {
        .confirm-dialog {
          min-width: 320px;
          max-width: 90vw;
          padding: 20px;
        }

        .dialog-content {
          margin-left: 0;
        }

        .dialog-header {
          flex-direction: column;
          text-align: center;
          gap: 12px;
        }

        .dialog-actions {
          flex-direction: column-reverse;
          gap: 12px;
        }

        .cancel-btn,
        .confirm-btn {
          width: 100%;
        }
      }
    `,
  ],
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatIconModule],
})
export class ConfirmDialogComponent {
  constructor(
    public dialogRef: MatDialogRef<ConfirmDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: ConfirmDialogData
  ) {}

  onCancel(): void {
    this.dialogRef.close(false);
  }

  onConfirm(): void {
    this.dialogRef.close(true);
  }
}
