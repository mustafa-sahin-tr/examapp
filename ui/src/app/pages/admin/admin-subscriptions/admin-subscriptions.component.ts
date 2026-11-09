import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import {
  AdminSubscriptionUser,
  AdminSubscriptionUserDetail,
  FREE_PLAN_KEY,
  PlanAssignment,
  SchoolPlanAudience,
  SchoolSubscriptionMembers,
  SubscriptionPlan,
  SubscriptionSearchRole,
  planNameKey,
} from '../../../models/subscription';
import { SubscriptionService } from '../../../services/subscription.service';
import { PageHeaderComponent } from '../../../shared/components/page-header/page-header.component';
import { SchoolSelectComponent } from '../../../shared/components/school-select/school-select.component';

/** Arama metninin en kısa uzunluğu (sunucu da reddeder). */
export const MIN_SEARCH_LENGTH = 2;

/**
 * Issue #1030: admin plan ataması. Kullanıcı arar, planını ve geçmişini görür, tarihli plan atar ya da atamayı bitirir;
 * okulun öğretmenlerine / öğrencilerine aynı planı toplu atar. Her değişiklik sunucuda audit'lenir (eski → yeni).
 */
@Component({
  selector: 'app-admin-subscriptions',
  standalone: true,
  imports: [
    SchoolSelectComponent,
    DatePipe,
    FormsModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    MatTabsModule,
    TranslocoDirective,
    PageHeaderComponent,
  ],
  providers: [provideTranslocoScope('subscription')],
  templateUrl: './admin-subscriptions.component.html',
  styleUrl: './admin-subscriptions.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminSubscriptionsComponent implements OnInit {
  private readonly subscriptions = inject(SubscriptionService);

  readonly plans = signal<SubscriptionPlan[]>([]);
  readonly activePlans = computed(() => this.plans().filter((p) => p.isActive));

  // ---- Kullanıcı ----
  readonly query = signal('');
  readonly role = signal<SubscriptionSearchRole | null>(null);
  readonly searching = signal(false);
  readonly results = signal<AdminSubscriptionUser[] | null>(null);
  readonly selected = signal<AdminSubscriptionUserDetail | null>(null);
  readonly detailLoading = signal(false);
  readonly confirmEnd = signal(false);

  readonly userPlanKey = signal('');
  readonly userFrom = signal<Date | null>(null);
  readonly userTo = signal<Date | null>(null);

  // ---- Okul ----
  readonly schoolId = signal<number | null>(null);
  readonly audience = signal<SchoolPlanAudience>('All');
  readonly schoolPlanKey = signal('');
  readonly schoolFrom = signal<Date | null>(null);
  readonly schoolTo = signal<Date | null>(null);
  readonly members = signal<SchoolSubscriptionMembers | null>(null);
  readonly confirmSchool = signal(false);
  readonly schoolResult = signal<number | null>(null);

  readonly busy = signal(false);
  /** Kapsam içi hata anahtarı (`admin.errors.*`). */
  readonly error = signal<string | null>(null);

  /** Bitirilecek (etkin ya da planlanmış) atama varsa "Atamayı bitir" görünür. */
  readonly hasOpenAssignment = computed(() => this.selected()?.assignments.some((a) => a.status !== 'Ended') ?? false);
  readonly canSearch = computed(() => this.query().trim().length >= MIN_SEARCH_LENGTH && !this.searching());
  readonly targetCount = computed(() => {
    const m = this.members();
    if (!m) return 0;
    const audience = this.audience();
    return (audience !== 'Students' ? m.teachers : 0) + (audience !== 'Teachers' ? m.students : 0);
  });

  ngOnInit(): void {
    this.subscriptions.adminPlans().subscribe({
      next: (plans) => this.plans.set(plans),
      error: () => this.error.set('admin.errors.load'),
    });
  }

  /** Plan anahtarının scope içi çeviri anahtarı; katalogda yoksa boş (şablon sunucunun verdiği adı gösterir). */
  planLabelKey(planKey: string | null): string {
    return planNameKey(this.plans().find((p) => p.key === (planKey ?? FREE_PLAN_KEY))?.displayNameKey);
  }

  search(): void {
    if (!this.canSearch()) return;
    this.searching.set(true);
    this.error.set(null);
    this.subscriptions.searchUsers(this.query().trim(), this.role()).subscribe({
      next: (users) => {
        this.results.set(users);
        this.searching.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.error.set(this.errorKey(err));
        this.searching.set(false);
      },
    });
  }

  select(userId: number): void {
    this.detailLoading.set(true);
    this.confirmEnd.set(false);
    this.error.set(null);
    this.subscriptions.userDetail(userId).subscribe({
      next: (detail) => {
        this.selected.set(detail);
        // Atama / bitirme sonrası arama sonucundaki plan da güncel kalsın.
        this.results.update((users) =>
          users?.map((u) =>
            u.userId === detail.userId
              ? { ...u, planKey: detail.planKey, planName: detail.planName, validToUtc: detail.validToUtc }
              : u,
          ) ?? null,
        );
        this.detailLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.error.set(this.errorKey(err));
        this.detailLoading.set(false);
      },
    });
  }

  assignUser(): void {
    const user = this.selected();
    if (!user || !this.userPlanKey() || this.busy()) return;
    this.run(this.subscriptions.assignUser(user.userId, this.assignment(this.userPlanKey(), this.userFrom(), this.userTo())), () => {
      this.userPlanKey.set('');
      this.userFrom.set(null);
      this.userTo.set(null);
      this.refreshSelected();
    });
  }

  endPlan(): void {
    const user = this.selected();
    if (!user || this.busy()) return;
    this.run(this.subscriptions.endUserPlan(user.userId), () => {
      this.confirmEnd.set(false);
      this.refreshSelected();
    });
  }

  selectSchool(schoolId: number | null): void {
    this.schoolId.set(schoolId);
    this.members.set(null);
    this.confirmSchool.set(false);
    this.schoolResult.set(null);
    if (schoolId == null) return;
    this.subscriptions.schoolMembers(schoolId).subscribe({
      next: (members) => this.members.set(members),
      error: (err: HttpErrorResponse) => this.error.set(this.errorKey(err)),
    });
  }

  /** İlk tıklama onay ister (kaç kişiye gideceği yazılır); ikincisi atar. Tarayıcı diyaloğu kullanılmaz. */
  assignSchool(): void {
    const schoolId = this.schoolId();
    if (!schoolId || !this.schoolPlanKey() || this.targetCount() === 0 || this.busy()) return;
    if (!this.confirmSchool()) {
      this.confirmSchool.set(true);
      this.error.set(null);
      this.schoolResult.set(null);
      return;
    }
    const body = { ...this.assignment(this.schoolPlanKey(), this.schoolFrom(), this.schoolTo()), audience: this.audience() };
    this.busy.set(true);
    this.error.set(null);
    this.subscriptions.assignSchool(schoolId, body).subscribe({
      next: (result) => {
        this.schoolResult.set(result.assigned);
        this.confirmSchool.set(false);
        this.busy.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.error.set(this.errorKey(err));
        this.confirmSchool.set(false);
        this.busy.set(false);
      },
    });
  }

  private refreshSelected(): void {
    const user = this.selected();
    if (user) this.select(user.userId);
  }

  private run(request: ReturnType<SubscriptionService['endUserPlan']>, done: () => void): void {
    this.busy.set(true);
    this.error.set(null);
    request.subscribe({
      next: () => {
        this.busy.set(false);
        done();
      },
      error: (err: HttpErrorResponse) => {
        this.error.set(this.errorKey(err));
        this.busy.set(false);
      },
    });
  }

  /**
   * Başlangıç boşsa sunucu "şimdi" alır. Bitiş hariç üst sınırdır; seçilen günün son anı gönderilir ki gün dahil olsun ve
   * ekranlarda (ör. Aboneliğim) seçilen gün görünsün (ertesi gün 00:00 "1 Ocak'a kadar" diye okunurdu).
   */
  private assignment(planKey: string, from: Date | null, to: Date | null): PlanAssignment {
    return {
      planKey,
      validFromUtc: from ? startOfDay(from).toISOString() : null,
      validToUtc: to ? endOfDay(to).toISOString() : null,
    };
  }

  private errorKey(err: HttpErrorResponse): string {
    const code = (err.error as { code?: string } | null)?.code;
    switch (code) {
      case 'subscription.invalidRange':
      case 'subscription.audienceMismatch':
      case 'subscription.noMembers':
      case 'subscription.tooManyMembers':
      case 'subscription.noAssignment':
      case 'subscription.unknownPlan':
      case 'subscription.userNotFound':
      case 'subscription.schoolNotFound':
      case 'subscription.queryTooShort':
        return `admin.errors.${code.replace('subscription.', '')}`;
      default:
        return err.status === 429 ? 'admin.errors.rateLimited' : 'admin.errors.generic';
    }
  }
}

function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

function endOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate(), 23, 59, 59, 999);
}
