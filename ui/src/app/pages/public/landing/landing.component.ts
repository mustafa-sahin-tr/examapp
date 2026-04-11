import { Component, HostListener, ChangeDetectionStrategy, OnInit } from '@angular/core';
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
  ],
  templateUrl: './landing.component.html',
  styleUrls: ['./landing.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush, // Performans için
})
export class LandingComponent implements OnInit {
  isScrolled = false;
  appName = 'Hedef Okul';

  constructor(
    private titleService: Title,
    private metaService: Meta
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

  @HostListener('window:scroll', [])
  onWindowScroll() {
    // Sayfa 80px aşağı kaydırıldığında navbar değişsin
    this.isScrolled = window.scrollY > 80;
  }
}
