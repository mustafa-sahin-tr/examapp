import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { forkJoin } from 'rxjs';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';

import { SchoolLicense, SubscriptionPlan, planNameKey } from '../../../../models/subscription';
import { SubscriptionService } from '../../../../services/subscription.service';

export interface SchoolLicenseDialogData {
  schoolId: number;
  schoolName: string;
}

/** Sunucunun döndüğü hata kodları → `subscription` scope'unda `admin.license.errors.*`. */
const ERROR_KEYS: Readonly<Record<string, string>> = {
  'subscription.invalidRange': 'invalidRange',
  'subscription.unknownPlan': 'unknownPlan',
  'subscription.audienceMismatch': 'audienceMismatch',
  'subscription.noteTooLong': 'noteTooLong',
  'subscription.noLicense': 'noLicense',
};

export const SCHOOL_LICENSE_NOTE_MAX_LENGTH = 200;

/**
 * Issue #1092: okul lisansı — özel anlaşmayla okulun bütün öğretmen ve öğrencilerine plan (varsayılan iki kitlenin de Pro'su).
 * Kimseye atama yazılmaz; okula sonradan katılan da lisansı alır, ayrılan düşer. Kaydet oluşturur ya da uzatır, Bitir şimdi bitirir.
 */
@Component({
  selector: 'app-school-license-dialog',
  standalone: true,
  imports: [
    DatePipe,
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    TranslocoDirective,
  ],
  providers: [provideTranslocoScope('subscription')],
  templateUrl: './school-license-dialog.component.html',
  styleUrl: './school-license-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SchoolLicenseDialogComponent implements OnInit {
  readonly data = inject<SchoolLicenseDialogData>(MAT_DIALOG_DATA);
  private readonly subscriptions = inject(SubscriptionService);

  readonly noteMaxLength = SCHOOL_LICENSE_NOTE_MAX_LENGTH;

  readonly loading = signal(true);
  readonly loadError = signal(false);
  readonly busy = signal(false);
  /** `admin.license.errors.*` anahtarı. */
  readonly error = signal<string | null>(null);
  /** Son işlemin sonucu: `saved` ya da `ended` (`admin.license.*`). */
  readonly done = signal<'saved' | 'ended' | null>(null);
  readonly confirmEnd = signal(false);

  readonly license = signal<SchoolLicense | null>(null);
  readonly plans = signal<SubscriptionPlan[]>([]);

  readonly teacherPlanKey = signal<string>('teacher-pro');
  readonly studentPlanKey = signal<string>('student-pro');
  readonly from = signal<Date | null>(null);
  readonly to = signal<Date | null>(null);
  readonly note = signal('');

  /** Lisansın durumu: yok, geçerli, ileride başlayacak ya da bitmiş. */
  readonly status = computed<'none' | 'active' | 'scheduled' | 'ended'>(() => {
    const license = this.license();
    if (!license?.exists) return 'none';
    if (license.active) return 'active';
    const from = license.validFromUtc ? new Date(license.validFromUtc).getTime() : 0;
    const to = license.validToUtc ? new Date(license.validToUtc).getTime() : Infinity;
    return from > Date.now() && from < to ? 'scheduled' : 'ended';
  });

  /** Lisans planı olabilecekler: etkin, ücretli, deneme değil; kitleye göre. */
  readonly teacherPlans = computed(() => this.plans().filter((p) => isLicensePlan(p) && p.audience !== 'Student'));
  readonly studentPlans = computed(() => this.plans().filter((p) => isLicensePlan(p) && p.audience !== 'Teacher'));

  ngOnInit(): void {
    forkJoin({ license: this.subscriptions.schoolLicense(this.data.schoolId), plans: this.subscriptions.adminPlans() }).subscribe({
      next: ({ license, plans }) => {
        this.plans.set(plans);
        this.apply(license);
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set(true);
        this.loading.set(false);
      },
    });
  }

  planName(t: (key: string) => string, key: string | null): string {
    const plan = this.plans().find((p) => p.key === key);
    return plan ? t(planNameKey(plan.displayNameKey)) : (key ?? '');
  }

  save(): void {
    if (this.busy()) return;
    const from = this.from();
    const to = this.to();
    this.run(
      this.subscriptions.saveSchoolLicense(this.data.schoolId, {
        teacherPlanKey: this.teacherPlanKey(),
        studentPlanKey: this.studentPlanKey(),
        validFromUtc: from ? startOfDay(from).toISOString() : null,
        // Bitiş günü dahil: hariç üst sınır ertesi günün başı.
        validToUtc: to ? startOfDay(addDays(to, 1)).toISOString() : null,
        note: this.note().trim() || null,
      }),
      'saved',
    );
  }

  end(): void {
    if (this.busy()) return;
    if (!this.confirmEnd()) {
      this.confirmEnd.set(true);
      return;
    }
    this.run(this.subscriptions.endSchoolLicense(this.data.schoolId), 'ended');
  }

  private run(request: ReturnType<SubscriptionService['schoolLicense']>, outcome: 'saved' | 'ended'): void {
    this.busy.set(true);
    this.error.set(null);
    this.done.set(null);
    request.subscribe({
      next: (license) => {
        this.busy.set(false);
        this.confirmEnd.set(false);
        this.done.set(outcome);
        this.apply(license);
      },
      error: (err: unknown) => {
        this.busy.set(false);
        this.confirmEnd.set(false);
        const code = err instanceof HttpErrorResponse ? (err.error as { code?: string } | null)?.code : undefined;
        this.error.set(ERROR_KEYS[code ?? ''] ?? 'generic');
      },
    });
  }

  private apply(license: SchoolLicense): void {
    this.license.set(license);
    if (!license.exists) return;
    this.teacherPlanKey.set(license.teacherPlanKey ?? 'teacher-pro');
    this.studentPlanKey.set(license.studentPlanKey ?? 'student-pro');
    this.from.set(license.validFromUtc ? new Date(license.validFromUtc) : null);
    // Hariç üst sınır → son geçerli gün.
    this.to.set(license.validToUtc ? addDays(new Date(license.validToUtc), -1) : null);
    this.note.set(license.note ?? '');
    // Bitmiş lisansın tarihleri formda kalmaz: Kaydet bugünden başlayan yeni dönem kurar.
    if (this.status() === 'ended') {
      this.from.set(null);
      this.to.set(null);
    }
  }
}

function isLicensePlan(plan: SubscriptionPlan): boolean {
  return plan.isActive && plan.monthlyPrice > 0 && plan.trialDays == null;
}

function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

function addDays(date: Date, days: number): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days);
}
