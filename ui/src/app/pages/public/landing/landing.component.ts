import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  DestroyRef,
  HostListener,
  OnInit,
  ViewEncapsulation,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Meta, Title } from '@angular/platform-browser';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';

import { FaqComponent } from './faq/faq.component';
import { HowItWorksComponent } from './how-it-works/how-it-works.component';
import {
  LANDING_CONTACT_EMAIL,
  LANDING_REGISTER_PARENT_URL,
  LANDING_REGISTER_STUDENT_URL,
  LANDING_REGISTER_TEACHER_URL,
  LANDING_STATS_ARE_SAMPLE,
  LANDING_TESTIMONIALS_ARE_FICTIONAL,
} from './landing-content';
import { LandingCtaComponent } from './landing-cta/landing-cta.component';
import { LandingFeaturesComponent } from './landing-features/landing-features.component';
import { LandingFooterComponent } from './landing-footer/landing-footer.component';
import { LandingHeroComponent } from './landing-hero/landing-hero.component';
import { LandingRolesComponent } from './landing-roles/landing-roles.component';
import { LandingSpotlightComponent } from './landing-spotlight/landing-spotlight.component';
import { LandingStatsComponent } from './landing-stats/landing-stats.component';
import { LandingSubjectsComponent } from './landing-subjects/landing-subjects.component';
import { LandingTestimonialsComponent } from './landing-testimonials/landing-testimonials.component';
import { NavbarComponent } from './navbar/navbar.component';

/**
 * Landing çevirileri kendi Transloco scope'unda (#182); provider burada, alt komponentler devralır.
 * #395: tek stil dosyası `.lp` altında kapsüllenmeden yazılır; alt komponentlerin kendi stili yoktur.
 * Sayfa statiktir: backend çağrısı yapmaz.
 */
const LANDING_SCOPE = 'landing';
const JSON_LD_ID = 'landing-json-ld';

@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [
    MatButtonModule,
    MatIconModule,
    TranslocoDirective,
    NavbarComponent,
    LandingHeroComponent,
    LandingStatsComponent,
    LandingRolesComponent,
    LandingFeaturesComponent,
    LandingSpotlightComponent,
    HowItWorksComponent,
    LandingSubjectsComponent,
    LandingTestimonialsComponent,
    FaqComponent,
    LandingCtaComponent,
    LandingFooterComponent,
  ],
  providers: [provideTranslocoScope(LANDING_SCOPE)],
  templateUrl: './landing.component.html',
  styleUrl: './landing.component.scss',
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingComponent implements OnInit {
  private readonly titleService = inject(Title);
  private readonly metaService = inject(Meta);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);

  readonly registerStudentUrl = LANDING_REGISTER_STUDENT_URL;
  readonly registerTeacherUrl = LANDING_REGISTER_TEACHER_URL;
  readonly registerParentUrl = LANDING_REGISTER_PARENT_URL;
  readonly showBackToTop = signal(false);
  /** #880: örnek rakamlar ve temsili yorumlar yayımlanmaz; gerçekleri girilince bayrakla geri gelir. */
  readonly showStats = !LANDING_STATS_ARE_SAMPLE;
  readonly showTestimonials = !LANDING_TESTIMONIALS_ARE_FICTIONAL;

  ngOnInit(): void {
    // `selectTranslate` scope'u yükler ve dil değişiminde yeniden yayınlar; sözlük hazır olduğunda
    // kalan anahtarlar senkron `translate()` ile okunabilir. `{{ brand.name }}` kök sözlükten gelir (#409).
    this.transloco
      .selectTranslate<string>('meta.title', {}, LANDING_SCOPE)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((title) => {
        const description = this.text('landing.meta.description');
        this.titleService.setTitle(title);
        this.metaService.updateTag({ name: 'description', content: description });
        this.metaService.updateTag({ property: 'og:title', content: this.text('landing.meta.ogTitle') });
        this.metaService.updateTag({ property: 'og:description', content: this.text('landing.meta.ogDescription') });
        this.metaService.updateTag({ property: 'og:url', content: 'https://hedefokul.com/' });
        this.writeJsonLd(description);
      });
    this.destroyRef.onDestroy(() => this.document.getElementById(JSON_LD_ID)?.remove());
  }

  /** Angular şablondaki `<script>`'i atar; yapılandırılmış veri bu yüzden DOM'a elle eklenir. */
  private writeJsonLd(description: string): void {
    let script = this.document.getElementById(JSON_LD_ID);
    if (!script) {
      script = this.document.createElement('script');
      script.id = JSON_LD_ID;
      script.setAttribute('type', 'application/ld+json');
      this.document.head.appendChild(script);
    }
    script.textContent = JSON.stringify({
      '@context': 'https://schema.org',
      '@type': 'EducationalOrganization',
      name: this.text('brand.name'),
      url: 'https://hedefokul.com',
      description,
      contactPoint: { '@type': 'ContactPoint', email: LANDING_CONTACT_EMAIL, contactType: 'customer support' },
    });
  }

  /** Sözlükten okunan metin; anahtar eksikse meta etiketi `undefined` yerine boş string alır. */
  private text(key: string): string {
    return this.transloco.translate<string>(key) ?? '';
  }

  @HostListener('window:scroll')
  onWindowScroll(): void {
    this.showBackToTop.set(window.scrollY > 400);
  }

  scrollToTop(): void {
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }
}
