import {
  Component,
  HostListener,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  OnInit,
  OnDestroy,
  PLATFORM_ID,
  Inject,
} from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { Title, Meta } from '@angular/platform-browser';
import { CommonModule } from '@angular/common';
import { NavbarComponent } from './navbar/navbar.component';
import { EducationBannerComponent } from './education-banner/education-banner.component';
import { FunfactsComponent } from './funfacts/funfacts.component';
import { AboutComponent } from '../about/about.component';
import { CategoriesComponent } from './categories/categories.component';
import { LandingCoursesComponent } from './landing-courses/landing-courses.component';
import { PartnersComponent } from './partners/partners.component';
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
import { NewLoginComponent } from './new-login/new-login.component';

@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [
    CommonModule,
    NavbarComponent,
    EducationBannerComponent,
    FunfactsComponent,
    AboutComponent,
    CategoriesComponent,
    LandingCoursesComponent,
    PartnersComponent,
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
    NewLoginComponent,
  ],
  templateUrl: './landing.component.html',
  styleUrls: ['./landing.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush, // Performans için
})
export class LandingComponent implements OnInit, OnDestroy {
  isScrolled = false;
  showBackToTop = false;
  appName = 'Hedef Okul';

  private touchStartY = 0;
  private touchStartTime = 0;
  private readonly NAVBAR_H = 62;
  private readonly VELOCITY_THRESHOLD = 0.45; // px/ms
  private readonly MIN_DISTANCE = 55; // px

  constructor(
    private titleService: Title,
    private metaService: Meta,
    private cdr: ChangeDetectorRef,
    @Inject(PLATFORM_ID) private platformId: object
  ) {}

  ngOnInit(): void {
    this.titleService.setTitle('Hedef Okul | Online Sınav ve Çalışma Yaprağı Platformu');
    this.metaService.updateTag({
      name: 'description',
      content:
        "Hedef Okul, 3-8. sınıf öğrencileri için müfredata uygun çalışma yaprakları, online sınavlar ve yapay zekâ destekli soru tespiti sunan Türkiye'nin eğitim platformu.",
    });
    this.metaService.updateTag({
      property: 'og:title',
      content: 'Hedef Okul | Online Sınav ve Çalışma Yaprağı Platformu',
    });
    this.metaService.updateTag({
      property: 'og:description',
      content:
        '3-8. sınıf öğrencileri için müfredata uygun çalışma yaprakları ve sınavlarla öğrenmeyi kolaylaştırıyoruz.',
    });
    this.metaService.updateTag({ property: 'og:url', content: 'https://hedefokul.com/' });
  }

  @HostListener('window:touchstart', ['$event'])
  onTouchStart(e: TouchEvent): void {
    if (!isPlatformBrowser(this.platformId) || window.innerWidth > 767) return;
    this.touchStartY = e.touches[0].clientY;
    this.touchStartTime = Date.now();
  }

  @HostListener('window:touchend', ['$event'])
  onTouchEnd(e: TouchEvent): void {
    if (!isPlatformBrowser(this.platformId) || window.innerWidth > 767) return;
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

  ngOnDestroy(): void {}
}
