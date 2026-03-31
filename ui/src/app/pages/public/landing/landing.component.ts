import { Component, HostListener, ChangeDetectionStrategy } from '@angular/core';
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
export class LandingComponent {
  isScrolled = false;
  appName = 'Hedef Okul';

  @HostListener('window:scroll', [])
  onWindowScroll() {
    // Sayfa 80px aşağı kaydırıldığında navbar değişsin
    this.isScrolled = window.scrollY > 80;
  }
}
