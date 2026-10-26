import { Component, DestroyRef, DOCUMENT, inject, OnInit, signal, ChangeDetectionStrategy } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { LocaleHintService } from '../../services/locale-hint.service';
import { OidcFlowService } from '../../services/oidc-flow.service';
import { runWhenPageActivated } from '../../shared/utils/when-activated.util';

/**
 * `/app/login`: tek işi Keycloak girişini başlatmaktır (issue #347). Eski e-posta/şifre formu (BFF password
 * grant) şablonsuz ölü koddu ve kaldırıldı; giriş yalnızca OIDC authorization code + PKCE ile yapılır.
 */
@Component({
  selector: 'app-login',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    @if (startError(); as message) {
      <p role="alert">{{ message }}</p>
    } @else {
      <p>Giriş yapılıyor, lütfen bekleyin...</p>
    }
  `,
})
export class LoginComponent implements OnInit {
  private readonly localeHint = inject(LocaleHintService);
  private readonly route = inject(ActivatedRoute);
  private readonly oidcFlow = inject(OidcFlowService);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  /** Login başlatılamadıysa (ör. tarayıcı depolaması kapalı) gösterilen mesaj. */
  readonly startError = signal<string | null>(null);

  /**
   * Issue #595: Chrome adres çubuğu sayfayı ön-yüklerse (prerender) state/PKCE kaydı ön-yükleme bağlamının
   * sessionStorage'ına yazılır ve etkinleşince kaybolur → callback "Oturum doğrulanamadı". Bu yüzden login
   * yalnız sayfa etkinken başlatılır; ön-yükleme yoksa hemen.
   */
  ngOnInit(): void {
    runWhenPageActivated(() => void this.startOidcLogin(), this.document, this.destroyRef);
  }

  /**
   * Issue #347: her login'de rastgele `state` + PKCE `code_verifier` üretilir ve sessionStorage'a yazılır;
   * gateway `/oidc-login` ucu bunları Keycloak'a iletir. Kayıt bağlantılarının `?intent=` niyeti ve
   * `?returnUrl=` (yalnızca aynı origin göreli yol) state'e gömülmez, kayıtta tutulur.
   */
  protected async startOidcLogin(): Promise<void> {
    const query = this.route.snapshot.queryParamMap;
    try {
      const url = await this.oidcFlow.begin({
        intent: query.get('intent'),
        returnPath: query.get('returnUrl'),
      });
      this.redirect(this.withLoginLocale(url));
    } catch {
      this.startError.set('Giriş başlatılamadı. Tarayıcınızın depolama/çerez ayarlarını kontrol edip tekrar deneyin.');
    }
  }

  /**
   * Çözümlenen dili standart OIDC `ui_locales` parametresi olarak URL'e ekler (issue #186); mevcut query
   * parametreleri korunur. Gateway'in `/oidc-login` middleware'i (`OidcLoginRedirect`) bu değeri biçimini
   * doğrulayarak Keycloak authorization isteğine kendisi ekler — Ocelot route'u (`AddQueriesToRequest`)
   * devreye girmez, middleware isteği ondan önce yanıtlar.
   *
   * Not: Keycloak'a özel `kc_locale` bilinçli olarak kullanılmıyor — session'sız ilk
   * `/auth` isteğinde yok sayılıyor, `ui_locales` ise ilk istekte doğru çalışıyor.
   */
  protected withLoginLocale(url: string): string {
    const [path, existingQuery = ''] = url.split('?');
    const params = new URLSearchParams(existingQuery);
    params.set('ui_locales', this.localeHint.resolveLoginLocale());
    return `${path}?${params.toString()}`;
  }

  /** Tarayıcı navigasyonu — test edilebilirlik için ayrı metot (spec bunu spy'lar). */
  protected redirect(url: string): void {
    window.location.href = url;
  }
}
