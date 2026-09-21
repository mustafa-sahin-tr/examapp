import { Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { combineLatest, take } from 'rxjs';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { Router } from '@angular/router';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { CommonModule } from '@angular/common';
import { NavigationExtras } from '@angular/router';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';

import { REGISTER_SCOPE } from './register-scope';

/** Rol seçeneği: `value` backend enum'ı, `labelKey` `register` scope'undaki çeviri anahtarı. */
interface RoleOption {
  readonly value: number;
  readonly labelKey: string;
}

@Component({
  selector: 'app-register',
  standalone: true,
  templateUrl: './register.component.html',
  styleUrls: ['./register.component.scss'],
  providers: [provideTranslocoScope(REGISTER_SCOPE)],
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatCardModule,
    MatSnackBarModule,
    MatIconModule,
    MatSelectModule,
    CommonModule,
    TranslocoDirective,
  ],
})
export class RegisterComponent {
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);

  registerForm: FormGroup;
  isLoading = false;
  hidePassword = true;
  hideConfirmPassword = true;
  readonly roles: readonly RoleOption[] = [
    { value: 0, labelKey: 'roles.student' },
    { value: 1, labelKey: 'roles.teacher' },
    { value: 2, labelKey: 'roles.parent' },
  ];

  constructor(
    private fb: FormBuilder,
    private authService: AuthService,
    private router: Router,
    private snackBar: MatSnackBar
  ) {
    this.registerForm = this.fb.group(
      {
        firstName: ['', [Validators.required, Validators.minLength(2)]],
        lastName: ['', [Validators.required, Validators.minLength(2)]],
        email: ['', [Validators.required, Validators.email]],
        role: [null, [Validators.required]],
        password: ['', [Validators.required, Validators.minLength(6)]],
        confirmPassword: ['', [Validators.required]],
      },
      { validator: this.passwordMatchValidator }
    );
  }

  toggleHidePassword() {
    const next = !this.hidePassword;
    this.hidePassword = next;
  }

  toggleHideConfirmPassword() {
    const next = !this.hideConfirmPassword;
    this.hideConfirmPassword = next;
  }

  passwordMatchValidator(group: FormGroup) {
    const password = group.get('password')?.value;
    const confirmPassword = group.get('confirmPassword')?.value;
    return password === confirmPassword ? null : { mismatch: true };
  }

  onSubmit() {
    if (this.registerForm.valid) {
      this.isLoading = true;

      const registerPayload = {
        firstName: this.registerForm.value.firstName,
        lastName: this.registerForm.value.lastName,
        email: this.registerForm.value.email,
        password: this.registerForm.value.password,
        role: this.registerForm.value.role,
      };

      this.authService.register(registerPayload).subscribe({
        next: (res) => {
          if (res.id) {
            this.notify('account.success', 'actions.ok');
            const navigationExtras: NavigationExtras = {
              state: {
                email: registerPayload.email,
                password: registerPayload.password,
              },
            };
            setTimeout(() => {
              this.router.navigate(['/login'], navigationExtras);
            }, 1000);
          } else {
            console.error('Register response is missing an id:', res);
          }
        },
        error: (err) => {
          console.error('Register error:', err);
          this.isLoading = false;
          this.notify('account.error', 'actions.close');
        },
      });
    }
  }

  navigateToLogin() {
    this.router.navigate(['/login']);
  }

  /**
   * Snackbar metni ve aksiyon etiketi şablon dışında üretildiği için ikisi de `selectTranslate`
   * ile okunur; bu çağrı scope sözlüğünü yükler ve dil değişiminde doğru metni verir.
   */
  private notify(messageKey: string, actionKey: string): void {
    combineLatest([
      this.transloco.selectTranslate<string>(messageKey, {}, REGISTER_SCOPE),
      this.transloco.selectTranslate<string>(actionKey, {}, REGISTER_SCOPE),
    ])
      .pipe(take(1), takeUntilDestroyed(this.destroyRef))
      .subscribe(([message, action]) => {
        this.snackBar.open(message, action, { duration: 3000 });
      });
  }
}
