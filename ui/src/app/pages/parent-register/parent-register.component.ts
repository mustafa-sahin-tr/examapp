import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ParentService } from '../../services/parent.service';

@Component({
  selector: 'app-parent-register',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCardModule, MatSnackBarModule],
  template: `
    <mat-card class="parent-register">
      <h2>Veli Kaydı</h2>
      <p>
        Hesabınızı veli olarak tamamlamak üzeresiniz. Çocuklarınızı daha sonra profilinizden
        ekleyebilirsiniz.
      </p>
      <button mat-raised-button color="primary" (click)="complete()" [disabled]="isSubmitting()">
        {{ isSubmitting() ? 'Kaydediliyor…' : 'Kaydı Tamamla' }}
      </button>
    </mat-card>
  `,
  styles: [
    `
      .parent-register {
        max-width: 420px;
        margin: 48px auto;
        padding: 24px;
        display: flex;
        flex-direction: column;
        gap: 16px;
      }
    `,
  ],
})
export class ParentRegisterComponent {
  private parentService = inject(ParentService);
  private router = inject(Router);
  private snackBar = inject(MatSnackBar);

  isSubmitting = signal(false);

  complete() {
    this.isSubmitting.set(true);
    this.parentService.register().subscribe({
      next: (val) => {
        this.isSubmitting.set(false);
        if (val?.accessToken) {
          localStorage.setItem('auth_token', val.accessToken);
        }
        localStorage.setItem('user_role', 'Parent');
        const user = localStorage.getItem('user');
        if (user) {
          try {
            const userObj = JSON.parse(user);
            userObj.role = 'Parent';
            if (val?.profileId) userObj.parent = { id: val.profileId };
            localStorage.setItem('user', JSON.stringify(userObj));
          } catch (e) {
            console.error('User data parsing error:', e);
          }
        }
        this.snackBar.open('Veli kaydı başarılı!', 'Tamam', { duration: 3000 });
        this.router.navigate(['/dashboard']);
      },
      error: (err) => {
        this.isSubmitting.set(false);
        this.snackBar.open('Veli kaydı başarısız! Tekrar deneyin.', 'Tamam', { duration: 3000 });
        console.error('Parent Register Error:', err);
      },
    });
  }
}
