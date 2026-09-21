import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { combineLatest, take } from 'rxjs';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';
import { ParentService } from '../../services/parent.service';
import { REGISTER_SCOPE } from '../register/register-scope';

/** Veli kayıt ucunun yanıtı (servis henüz `any` döndürüyor). */
interface ParentRegistrationResult {
  accessToken?: string;
  profileId?: number;
}

@Component({
  selector: 'app-parent-register',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCardModule, MatSnackBarModule, TranslocoDirective],
  providers: [provideTranslocoScope(REGISTER_SCOPE)],
  template: `
    <mat-card class="parent-register" *transloco="let t; prefix: 'register'">
      <h2>{{ t('parent.title') }}</h2>
      <p>{{ t('parent.hint') }}</p>
      <button mat-raised-button color="primary" (click)="complete()" [disabled]="isSubmitting()">
        {{ isSubmitting() ? t('actions.saving') : t('actions.complete') }}
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
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  isSubmitting = signal(false);

  complete() {
    this.isSubmitting.set(true);
    this.parentService.register().subscribe({
      next: (val: ParentRegistrationResult) => {
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
        this.notify('parent.success');
        this.router.navigate(['/dashboard']);
      },
      error: (err: unknown) => {
        this.isSubmitting.set(false);
        this.notify('parent.error');
        console.error('Parent Register Error:', err);
      },
    });
  }

  /**
   * Snackbar metni ve aksiyon etiketi şablon dışında üretildiği için ikisi de `selectTranslate`
   * ile okunur; bu çağrı scope sözlüğünü yükler ve dil değişiminde doğru metni verir.
   */
  private notify(messageKey: string): void {
    combineLatest([
      this.transloco.selectTranslate<string>(messageKey, {}, REGISTER_SCOPE),
      this.transloco.selectTranslate<string>('actions.ok', {}, REGISTER_SCOPE),
    ])
      .pipe(take(1), takeUntilDestroyed(this.destroyRef))
      .subscribe(([message, action]) => {
        this.snackBar.open(message, action, { duration: 3000 });
      });
  }
}
