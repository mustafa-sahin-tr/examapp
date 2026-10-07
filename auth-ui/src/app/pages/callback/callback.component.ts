import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { take } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { OidcFlowService, OidcLoginRecord } from '../../services/oidc-flow.service';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { httpErrorMessage } from '../../shared/utils/http-error-message';
import { safeRedirectTarget } from '../../shared/utils/safe-redirect.util';

/** state doğrulaması başarısız olduğunda gösterilen mesaj (kod değişimi yapılmaz). */
export const STATE_MISMATCH_MESSAGE = 'Oturum doğrulanamadı. Lütfen yeniden giriş yapın.';

/**
 * Keycloak'ın callback'e `?error=` ile döndüğü durumlar (RFC 6749 §4.1.2.1). `error_description` saldırgan
 * tarafından serbestçe verilebildiği için gösterilmez; yalnızca bilinen kodlar için sabit metin seçilir.
 */
export function oidcErrorMessage(error: string): string {
  switch (error) {
    case 'access_denied':
      return 'Giriş iptal edildi.';
    case 'temporarily_unavailable':
      return 'Giriş servisi şu anda kullanılamıyor. Lütfen biraz sonra tekrar deneyin.';
    case 'login_required':
    case 'interaction_required':
      return 'Oturumunuz bulunamadı. Lütfen tekrar giriş yapın.';
    default:
      return 'Giriş tamamlanamadı. Lütfen tekrar deneyin.';
  }
}

/**
 * Login sonrası hedef (issue #86, #347). Hiçbir parça `state`'ten okunmaz: rol niyeti ve istenen yol
 * login başlatılırken sessionStorage'a yazılan kayıttan gelir; yol yine de allowlist'ten geçirilir.
 *
 * - Admin → istenen yol, yoksa `/admin/dashboard`
 * - Veli (öğrenci/öğretmen rolü yok) → istenen yol, yoksa `/parent` (veli paneli, issue #420)
 * - Uygulama rolü olan kullanıcı → istenen yol, yoksa `/dashboard`
 * - Rolü olmayan (yeni) kullanıcı → profil tamamlama (niyet varsa rol ön seçili)
 */
export function postLoginDestination(
  roles: readonly string[],
  record: Pick<OidcLoginRecord, 'intent' | 'returnPath'>
): string {
  const isAdmin = roles.includes('Admin');
  const hasAppRole = isAdmin || ['Student', 'Teacher', 'Parent'].some((r) => roles.includes(r));
  if (!hasAppRole) {
    return record.intent ? `/app/complete-profile?role=${record.intent}` : '/app/complete-profile';
  }
  const isParentOnly = roles.includes('Parent') && !roles.includes('Student') && !roles.includes('Teacher');
  return safeRedirectTarget(record.returnPath) ?? (isAdmin ? '/admin/dashboard' : isParentOnly ? '/parent' : '/dashboard');
}

@Component({
  standalone: true,
  selector: 'app-callback',
  imports: [MatIconModule, MatButtonModule],
  templateUrl: './callback.component.html',
  styleUrl: './callback.component.scss',
})
export class CallbackComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);
  private readonly oidcFlow = inject(OidcFlowService);
  private readonly snackBar = inject(MatSnackBar);

  readonly currentStep = signal(1);
  readonly currentMessage = signal('Kimlik doğrulanıyor...');
  readonly userRole = signal<string | null>(null);
  /** Keycloak hata dönüşünde gösterilen mesaj; doluysa yükleme yerine "Tekrar dene" kartı görünür. */
  readonly errorMessage = signal<string | null>(null);

  private readonly messages = ['Kimlik doğrulanıyor...', 'Profil bilgileri kontrol ediliyor...', 'Yönlendiriliyor...'];

  private updateStep(step: number): void {
    this.currentStep.set(step);
    if (step <= this.messages.length) {
      this.currentMessage.set(this.messages[step - 1]);
    }
  }

  private delay(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }

  getRoleIcon(role: string): string {
    switch (role) {
      case 'Student':
        return 'school';
      case 'Teacher':
        return 'person';
      default:
        return 'account_circle';
    }
  }

  getRoleMessage(role: string): string {
    switch (role) {
      case 'Student':
        return 'Öğrenci paneline yönlendiriliyorsunuz...';
      case 'Teacher':
        return 'Öğretmen paneline yönlendiriliyorsunuz...';
      default:
        return 'Sisteme yönlendiriliyorsunuz...';
    }
  }

  /** Tam sayfa yönlendirme; spec gerçek navigasyonu engellemek için bunu spy'lar. */
  protected redirect(url: string): void {
    window.location.href = url;
  }

  ngOnInit(): void {
    this.route.queryParams.pipe(take(1)).subscribe(async (params) => {
      // Keycloak hata dönüşü (ör. kullanıcı iptal etti): kayıt tüketilir ki tekrar kullanılamasın; otomatik
      // /login yönlendirmesi YAPILMAZ (aynı hatayla sonsuz döngü olmasın), kullanıcı "Tekrar dene"ye basar.
      const error: unknown = params['error'];
      if (typeof error === 'string' && error.length > 0) {
        this.oidcFlow.consume(params['state']);
        this.errorMessage.set(oidcErrorMessage(error));
        return;
      }

      const code: unknown = params['code'];
      if (typeof code !== 'string' || code.length === 0) {
        this.router.navigate(['/login']);
        return;
      }

      // Login CSRF koruması: state, bu sekmede başlatılmış bir login'in kaydıyla eşleşmeli (tek kullanımlık).
      // Eşleşmezse code hiç kullanılmaz.
      const record = this.oidcFlow.consume(params['state']);
      if (!record) {
        // Başarılı girişten sonra geri tuşuyla buraya dönüldüyse kayıt zaten tüketilmiştir; oturum geçerliyse
        // hata göstermeden (ve code'u kullanmadan) mevcut oturumun hedefine git.
        if (this.authService.hasValidSessionToken()) {
          this.redirect(postLoginDestination(this.authService.getRealmRoles(), { intent: null, returnPath: null }));
          return;
        }
        this.failAndReturnToLogin(STATE_MISMATCH_MESSAGE);
        return;
      }

      await this.delay(100);

      this.authService.exchangeCodeForToken(code, record.codeVerifier).subscribe({
        next: async (res) => {
          this.snackBar.open('Giriş başarılı! Yönlendiriliyorsunuz...', 'Tamam', { duration: 3000 });
          const roles: string[] = res?.roles ?? [];
          this.userRole.set(roles.find((r) => ['Student', 'Teacher', 'Parent', 'Admin'].includes(r)) ?? null);
          this.updateStep(3);
          await this.delay(100);
          this.redirect(postLoginDestination(roles, record));
        },
        error: (error: unknown) => {
          this.failAndReturnToLogin(httpErrorMessage(error, 'Giriş başarısız! Lütfen bilgilerinizi kontrol edin.'));
        },
      });
    });
  }

  /** "Tekrar dene": yeni state + PKCE ile login'i baştan başlatır. */
  retry(): void {
    this.router.navigate(['/login']);
  }

  private failAndReturnToLogin(message: string): void {
    this.snackBar.open(message, 'Kapat', { duration: 3000 });
    setTimeout(() => {
      this.router.navigate(['/login']);
    }, 2000);
  }
}
