import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { Route, provideRouter } from '@angular/router';

import { AdminCommentReportsComponent } from './admin-comment-reports.component';
import { routes } from '../../../app.routes';
import { adminGuard } from '../../../shared/guards/admin.guard';
import { authGuard } from '../../../shared/guards/auth.guard';
import { LocaleService } from '../../../services/locale.service';
import { localeDefinitionOf } from '../../../models/locale';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import rootTr from '../../../../../public/i18n/tr.json';
import adminTr from '../../../../../public/i18n/admin/tr.json';
import commentsTr from '../../../../../public/i18n/comments/tr.json';

const ADMIN_URL = '/api/exam/admin/comments/reports';

function findRoute(list: Route[], path: string): Route | undefined {
  for (const route of list) {
    if (route.path === path) {
      return route;
    }
    const child = route.children ? findRoute(route.children, path) : undefined;
    if (child) {
      return child;
    }
  }
  return undefined;
}

describe('AdminCommentReportsComponent (issue #305)', () => {
  let fixture: ComponentFixture<AdminCommentReportsComponent>;
  let http: HttpTestingController;

  const el = () => fixture.nativeElement as HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        AdminCommentReportsComponent,
        NoopAnimationsModule,
        translocoTestingModule({
          langs: {
            tr: { ...rootTr, admin: adminTr, comments: commentsTr },
            'admin/tr': adminTr,
            'comments/tr': commentsTr,
          },
        }),
      ],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: LocaleService,
          useValue: { locale: signal('tr').asReadonly(), localeDefinition: signal(localeDefinitionOf('tr')).asReadonly() },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AdminCommentReportsComponent);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('rendersHeaderAndLoadsAllReportsFromAdminEndpoint', () => {
    expect(el().querySelector('h1')?.textContent).toContain(adminTr.commentReports.title);

    http.expectOne((r) => r.url === ADMIN_URL).flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    fixture.detectChanges();

    expect(el().querySelector('[data-testid="reports-empty"]')).not.toBeNull();
    // Sayfa kendi başlığını çizer; liste başlığı tekrar edilmez.
    expect(el().querySelector('[data-testid="reports-title"]')).toBeNull();
  });

  it('refresh_ReloadsList', () => {
    http.expectOne((r) => r.url === ADMIN_URL).flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    fixture.detectChanges();

    el().querySelector<HTMLButtonElement>('[data-testid="admin-reports-refresh"]')!.click();
    fixture.detectChanges();

    const again = http.expectOne((r) => r.url === ADMIN_URL);
    expect(again.request.params.get('page')).toBe('1');
    again.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  });

  it('route_IsGuardedByAuthAndAdminGuards_AndLazyLoadsPage', async () => {
    http.expectOne((r) => r.url === ADMIN_URL).flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    const route = findRoute(routes, 'admin/comment-reports');

    expect(route).toBeDefined();
    expect(route!.canActivate).toEqual([authGuard, adminGuard]);
    const loaded = await (route!.loadComponent as () => Promise<unknown>)();
    expect(loaded).toBe(AdminCommentReportsComponent);
  });
});
