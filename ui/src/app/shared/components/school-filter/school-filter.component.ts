import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoDirective, provideTranslocoScope } from '@jsverse/transloco';
import { catchError, map, of } from 'rxjs';
import { School } from '../../../models/taxonomy';
import { AdminService } from '../../../services/admin.service';
import { SchoolFilterUnassignedLabel, SchoolFilterValue } from './school-filter.model';

type SchoolsState =
  | { status: 'loading'; schools: School[] }
  | { status: 'ready'; schools: School[] }
  | { status: 'error'; schools: School[] };

/**
 * Admin listeleri için okul filtresi (issue #152; #153 öğrenci listesi de kullanır).
 * Seçenekler: "Tümü", "Bağımsız / okulsuz" ve `GET /api/exam/admin/schools`'tan gelen okullar
 * (ada göre sıralı). Okul listesi alınamazsa "Tümü" ve "Bağımsız" seçenekleri kullanılabilir kalır.
 *
 * Derin linkteki `schoolId` yüklenen okul listesinde yoksa (silinmiş/yanlış id) değer "Tümü"ye çekilir
 * ve `valueChange` yayılır; ebeveyn URL'i/listeyi buna göre günceller. Liste alınamazsa değere dokunulmaz.
 *
 * Kullanım: `<app-school-filter [value]="filter()" (valueChange)="onFilterChange($event)" />`
 * Değer → query dönüşümü için `schoolFilterToQuery()`.
 */
@Component({
  selector: 'app-school-filter',
  standalone: true,
  imports: [MatFormFieldModule, MatSelectModule, TranslocoDirective],
  providers: [provideTranslocoScope('admin')],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './school-filter.component.html',
  styleUrls: ['./school-filter.component.scss'],
})
export class SchoolFilterComponent {
  private readonly adminService = inject(AdminService);

  readonly value = model<SchoolFilterValue>('all');
  readonly disabled = input(false);
  /**
   * "Okula bağlı olmayanlar" seçeneğinin etiketi (`admin.schoolFilter.*` anahtarı).
   * Öğretmen listesi "Bağımsız / okulsuz" (varsayılan), öğrenci listesi "Okulsuz" kullanır.
   */
  readonly unassignedLabel = input<SchoolFilterUnassignedLabel>('unassigned');

  private readonly state = toSignal(
    this.adminService.getSchools().pipe(
      map((list): SchoolsState => ({
        status: 'ready',
        schools: [...list].sort((a, b) => a.name.localeCompare(b.name, 'tr')),
      })),
      catchError(() => of<SchoolsState>({ status: 'error', schools: [] })),
    ),
    { initialValue: { status: 'loading', schools: [] } as SchoolsState },
  );

  readonly schools = computed(() => this.state().schools);
  readonly loading = computed(() => this.state().status === 'loading');
  readonly loadFailed = computed(() => this.state().status === 'error');

  constructor() {
    effect(() => {
      const state = this.state();
      const value = this.value();
      if (state.status !== 'ready' || typeof value !== 'number') return;
      if (!state.schools.some((s) => s.id === value)) {
        untracked(() => this.value.set('all'));
      }
    });
  }

  select(value: SchoolFilterValue): void {
    this.value.set(value);
  }
}
