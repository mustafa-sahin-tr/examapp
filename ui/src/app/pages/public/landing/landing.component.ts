import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  DestroyRef,
  HostListener,
  OnInit,
  PLATFORM_ID,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { isPlatformBrowser } from '@angular/common';
import { Meta, Title } from '@angular/platform-browser';
import { TranslocoDirective, TranslocoService, provideTranslocoScope } from '@jsverse/transloco';

import { NavbarComponent } from './navbar/navbar.component';
import { EducationBannerComponent } from './education-banner/education-banner.component';
import { FunfactsComponent } from './funfacts/funfacts.component';
import { CategoriesComponent } from './categories/categories.component';
import { LandingCoursesComponent } from './landing-courses/landing-courses.component';
import { HowItWorksComponent } from './how-it-works/how-it-works.component';
import { OverviewComponent } from './overview/overview.component';
import { LandingFooterComponent } from './landing-footer/landing-footer.component';
import { EduBlogComponent } from './edu-blog/edu-blog.component';
import { AboutUsComponent } from './about-us/about-us.component';
import { FaqComponent } from './faq/faq.component';
import { PricingComponent } from './pricing/pricing.component';
import { ServiceAreaComponent } from './service-area/service-area.component';
import { DiscoverComponent } from './discover/discover.component';
import { NewsComponent } from './news/news.component';

/**
 * Landing sayfası çevirileri kök sözlükte değil, kendi Transloco scope'unda tutulur (issue #182):
 * `public/i18n/landing/<lang>.json`. Scope provider burada verilir, alt komponentlerin hepsi
 * (navbar, hero, fiyatlandırma, footer …) onu element injector ağacından devralır.
 */
const LANDING_SCOPE = 'landing';

@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [
    NavbarComponent,
    EducationBannerComponent,
    FunfactsComponent,
    CategoriesComponent,
    LandingCoursesComponent,
    HowItWorksComponent,
    OverviewComponent,
    LandingFooterComponent,
    EduBlogComponent,
    AboutUsComponent,
    FaqComponent,
    PricingComponent,
    ServiceAreaComponent,
    DiscoverComponent,
    NewsComponent,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope(LANDING_SCOPE)],
  templateUrl: './landing.component.html',
  styleUrls: ['./landing.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush, // Performans için
})
export class LandingComponent implements OnInit {
  private readonly titleService = inject(Title);
  private readonly metaService = inject(Meta);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly transloco = inject(TranslocoService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  isScrolled = false;
  showBackToTop = false;

  private touchStartY = 0;
  private touchStartTime = 0;
  private readonly NAVBAR_H = 62;
  private readonly VELOCITY_THRESHOLD = 0.45; // px/ms
  private readonly MIN_DISTANCE = 55; // px

  ngOnInit(): void {
    // `selectTranslate` scope'u yükler ve dil değişiminde yeniden yayınlar; sözlük hazır olduğunda
    // kalan anahtarlar senkron `translate()` ile okunabilir.
    this.transloco
      .selectTranslate<string>('meta.title', {}, LANDING_SCOPE)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((title) => {
        this.titleService.setTitle(title);
        this.metaService.updateTag({ name: 'description', content: this.text('landing.meta.description') });
        this.metaService.updateTag({ property: 'og:title', content: this.text('landing.meta.ogTitle') });
        this.metaService.updateTag({ property: 'og:description', content: this.text('landing.meta.ogDescription') });
        this.metaService.updateTag({ property: 'og:url', content: 'https://hedefokul.com/' });
      });
  }

  /** Sözlükten okunan metin; anahtar eksikse meta etiketi `undefined` yerine boş string alır. */
  private text(key: string): string {
    return this.transloco.translate<string>(key) ?? '';
  }

  @HostListener('window:touchstart', ['$event'])
  onTouchStart(e: TouchEvent): void {
    if (!this.isBrowser || window.innerWidth > 767) return;
    this.touchStartY = e.touches[0].clientY;
    this.touchStartTime = Date.now();
  }

  @HostListener('window:touchend', ['$event'])
  onTouchEnd(e: TouchEvent): void {
    if (!this.isBrowser || window.innerWidth > 767) return;
    const deltaY = this.touchStartY - e.changedTouches[0].clientY;
    const elapsed = Date.now() - this.touchStartTime;
    const velocity = Math.abs(deltaY) / elapsed;

    if (velocity < this.VELOCITY_THRESHOLD || Math.abs(deltaY) < this.MIN_DISTANCE) return;

    const sections = Array.from(document.querySelectorAll('main > section')) as HTMLElement[];
    const scrollY = window.scrollY;

    if (deltaY > 0) {
      // Yukarı swipe → sonraki section
      const next = sections.find((s) => s.offsetTop > scrollY + this.NAVBAR_H + 20);
      if (next) window.scrollTo({ top: next.offsetTop - this.NAVBAR_H, behavior: 'smooth' });
    } else {
      // Aşağı swipe → önceki section
      const prev = [...sections].reverse().find((s) => s.offsetTop < scrollY - 20);
      if (prev) window.scrollTo({ top: prev.offsetTop - this.NAVBAR_H, behavior: 'smooth' });
    }
  }

  @HostListener('window:scroll', [])
  onWindowScroll() {
    const scrollY = window.scrollY;
    this.isScrolled = scrollY > 80;
    this.showBackToTop = scrollY > 400;
    this.cdr.markForCheck();
  }

  scrollToTop() {
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }
}
