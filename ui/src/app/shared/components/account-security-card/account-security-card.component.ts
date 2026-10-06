import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { KEYCLOAK_ACCOUNT_CONSOLE_PATH } from '../../../models/account-settings.model';

const SCOPE = 'account-security';

/**
 * Issue #417: tüm rollerin Ayarlar'ında ortak "Hesabım / Şifre değiştir" kartı — Keycloak hesap konsolunu
 * gateway origin'inde (göreli yol) yeni sekmede açar. `/settings` (öğretmen/admin) ve `/student-profile` kullanır.
 */
@Component({
  selector: 'app-account-security-card',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslocoDirective],
  providers: [provideTranslocoScope(SCOPE)],
  templateUrl: './account-security-card.component.html',
  styleUrl: './account-security-card.component.scss',
})
export class AccountSecurityCardComponent {
  protected readonly accountConsoleUrl = KEYCLOAK_ACCOUNT_CONSOLE_PATH;
}
