import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, Subject, throwError } from 'rxjs';
import type { Mock } from 'vitest';

import { BookImportSharingComponent } from './book-import-sharing.component';
import { AdminTeacherListItem } from '../../../models/admin-teacher.model';
import { Paged, WorksheetTeacherSharing } from '../../../models/test-instance';
import { SchoolOption } from '../../../models/taxonomy';
import { AdminService } from '../../../services/admin.service';
import { SchoolService } from '../../../services/school.service';
import { translocoTestingModule } from '../../../shared/testing/transloco-testing';
import tr from '../../../../../public/i18n/book-imports/tr.json';

const school = (id: number, name: string): SchoolOption => ({ id, name, provinceId: null, provinceName: null, districtId: null, districtName: null }) as SchoolOption;
const teacher = (id: number, fullName: string): AdminTeacherListItem => ({ id, fullName, schoolId: 3, schoolName: 'Atatürk O.O.' }) as AdminTeacherListItem;
const page = (items: AdminTeacherListItem[]): Paged<AdminTeacherListItem> => ({ pageNumber: 1, pageSize: 200, totalCount: items.length, items });

describe('BookImportSharingComponent (#1259)', () => {
  let fixture: ComponentFixture<BookImportSharingComponent>;
  let getTeachers: Mock;
  const el = () => fixture.nativeElement as HTMLElement;
  const c = () => fixture.componentInstance;

  function create(inputs: { school?: boolean; audience?: boolean } = {}): void {
    getTeachers = vi.fn().mockReturnValue(of(page([teacher(41, 'Ayşe'), teacher(42, 'Veli')])));
    TestBed.configureTestingModule({
      imports: [BookImportSharingComponent, translocoTestingModule({ langs: { 'book-imports/tr': tr } })],
      providers: [
        provideNoopAnimations(),
        { provide: AdminService, useValue: { getTeachers } },
        { provide: SchoolService, useValue: { search: vi.fn().mockReturnValue(of([])), getById: vi.fn(), provinces: vi.fn().mockReturnValue(of([])), districts: vi.fn().mockReturnValue(of([])) } },
      ],
    });
    fixture = TestBed.createComponent(BookImportSharingComponent);
    fixture.componentRef.setInput('canShareWithSchool', inputs.school ?? false);
    fixture.componentRef.setInput('canShareWithAudience', inputs.audience ?? false);
    fixture.detectChanges();
  }

  it('teacher without a school sees private and the two public options only', () => {
    create();
    const keys = Array.from(el().querySelectorAll('mat-radio-button')).map((b) => b.getAttribute('data-testid'));
    expect(keys).toEqual(['book-sharing-private', 'book-sharing-publicView', 'book-sharing-publicAssignable']);
    expect(el().querySelector('[data-testid="book-sharing-audience"]')).toBeNull();
  });

  it('admin picks schools once each and removes them', () => {
    create({ audience: true });
    c().sharing.set(WorksheetTeacherSharing.BookAudience);
    fixture.detectChanges();
    expect(el().querySelector('[data-testid="book-sharing-audience"]')).not.toBeNull();
    expect(el().textContent).toContain(tr.sharing.emptyAudience);

    c().addSchool(school(3, 'Atatürk O.O.'));
    c().addSchool(school(3, 'Atatürk O.O.'));
    c().addSchool(school(5, 'Cumhuriyet O.O.'));
    expect(c().schools()).toEqual([{ id: 3, name: 'Atatürk O.O.' }, { id: 5, name: 'Cumhuriyet O.O.' }]);

    c().removeSchool(3);
    expect(c().schools()).toEqual([{ id: 5, name: 'Cumhuriyet O.O.' }]);
  });

  it('admin lists a school\'s teachers and ticks them into the audience', () => {
    create({ audience: true });
    c().sharing.set(WorksheetTeacherSharing.BookAudience);
    c().pickTeacherSchool(school(3, 'Atatürk O.O.'));
    fixture.detectChanges();

    expect(getTeachers).toHaveBeenCalledWith({ page: 1, pageSize: 200, schoolId: 3 });
    const boxes = el().querySelectorAll<HTMLInputElement>('[data-testid="book-sharing-teachers"] input[type="checkbox"]');
    expect(boxes.length).toBe(2);
    boxes[0].click();
    fixture.detectChanges();

    expect(c().teachers()).toEqual([{ id: 41, name: 'Ayşe', schoolName: 'Atatürk O.O.' }]);
    expect(el().textContent).not.toContain(tr.sharing.emptyAudience);
  });

  it('ignores a late teacher list of a school that is no longer picked, and says so when loading fails', () => {
    create({ audience: true });
    const late = new Subject<Paged<AdminTeacherListItem>>();
    getTeachers.mockReturnValueOnce(late).mockReturnValueOnce(throwError(() => new Error('500')));
    c().pickTeacherSchool(school(3, 'Atatürk O.O.'));
    c().pickTeacherSchool(school(5, 'Cumhuriyet O.O.'));
    late.next(page([teacher(41, 'Ayşe')]));

    expect(c()['schoolTeachers']()).toEqual([]);
    expect(c()['teachersFailed']()).toBe(true);
  });
});
