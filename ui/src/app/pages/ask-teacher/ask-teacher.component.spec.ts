import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { BehaviorSubject, of, throwError } from 'rxjs';

import { AskTeacherComponent } from './ask-teacher.component';
import { AskTeacherQuestion, AskTeacherQuota } from '../../models/ask-teacher.model';
import { AskTeacherService } from '../../services/ask-teacher.service';
import { LocaleService } from '../../services/locale.service';
import { SubjectService } from '../../services/subject.service';
import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import askTr from '../../../../public/i18n/ask-teacher/tr.json';
import subscriptionTr from '../../../../public/i18n/subscription/tr.json';

/** Issue #1111: öğrencinin öğretmene sor sayfası — hak, form, liste, iptal, puan, planda yok. */
describe('AskTeacherComponent (issue #1111)', () => {
  let fixture: ComponentFixture<AskTeacherComponent>;
  let service: Record<string, ReturnType<typeof vi.fn>>;

  function question(id: number, overrides: Partial<AskTeacherQuestion> = {}): AskTeacherQuestion {
    return {
      id,
      status: 'Open',
      teacherId: 4,
      teacherName: 'Elif Öğretmen',
      studentName: '',
      subjectId: null,
      subjectName: null,
      note: `Not ${id}`,
      createdAt: '2030-10-01T08:00:00Z',
      dueAt: '2030-10-03T08:00:00Z',
      answeredAt: null,
      answerText: null,
      hasAnswerImage: false,
      rating: null,
      inNotebook: false,
      ...overrides,
    };
  }

  async function setup(quota: AskTeacherQuota, questions: AskTeacherQuestion[], queryQ: string | null = null) {
    service = {
      quota: vi.fn().mockReturnValue(of(quota)),
      list: vi.fn().mockReturnValue(of(questions)),
      teachers: vi.fn().mockReturnValue(of([{ teacherId: 4, fullName: 'Elif Öğretmen', teachesSubject: true }])),
      send: vi.fn(),
      cancel: vi.fn().mockReturnValue(of(undefined)),
      rate: vi.fn().mockReturnValue(of(undefined)),
      setNotebook: vi.fn().mockReturnValue(of(undefined)),
      image: vi.fn().mockReturnValue(of(new Blob(['x'], { type: 'image/jpeg' }))),
    };
    TestBed.configureTestingModule({
      imports: [
        AskTeacherComponent,
        NoopAnimationsModule,
        translocoTestingModule({ langs: { 'ask-teacher/tr': askTr, 'subscription/tr': subscriptionTr } }),
      ],
      providers: [
        provideRouter([]),
        { provide: AskTeacherService, useValue: service },
        { provide: SubjectService, useValue: { loadCategories: () => of([{ id: 1, name: 'Matematik' }]) } },
        { provide: LocaleService, useValue: { localeDefinition: signal({ angularLocale: 'tr-TR' }) } },
        {
          provide: ActivatedRoute,
          useValue: { queryParamMap: new BehaviorSubject(convertToParamMap(queryQ ? { q: queryQ } : {})) },
        },
      ],
    });
    TestBed.inject(TranslocoService).setActiveLang('tr');
    fixture = TestBed.createComponent(AskTeacherComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const q = (el: HTMLElement, id: string) => el.querySelector<HTMLElement>(`[data-testid="${id}"]`);

  it('shows the remaining quota and the only teacher is preselected; sending needs a photo', async () => {
    const el = await setup({ limit: 10, used: 3, pending: 1, resetsAtUtc: null, planKey: 'student-essential' }, []);

    expect(q(el, 'ask-quota')!.textContent).toContain('3 / 10');
    expect(q(el, 'ask-quota')!.textContent).toContain('Kalan hakkın: 6');
    expect(fixture.componentInstance.teacherId()).toBe(4);
    expect((q(el, 'ask-send') as HTMLButtonElement).disabled).toBe(true);
    expect(q(el, 'ask-empty')).not.toBeNull();
  });

  it('sends the photo with the note and counts it as pending', async () => {
    await setup({ limit: 10, used: 0, pending: 0, resetsAtUtc: null, planKey: 'student-pro' }, []);
    const component = fixture.componentInstance;
    service['send'].mockReturnValue(of(question(9)));
    const file = new File(['x'], 'q.jpg', { type: 'image/jpeg' });
    component.onPhotoPicked({ target: { files: [file], value: '' } } as unknown as Event);
    component.note.set('  Kesirler  ');
    fixture.detectChanges();

    component.send();
    fixture.detectChanges();

    expect(service['send']).toHaveBeenCalledWith({ teacherId: 4, subjectId: null, note: '  Kesirler  ', photo: file });
    expect(component.questions().map((x) => x.id)).toEqual([9]);
    expect(component.quota()!.pending).toBe(1);
    expect(component.photo()).toBeNull();
  });

  it('a full quota disables sending and a 429 shows the inline message', async () => {
    const el = await setup({ limit: 2, used: 1, pending: 1, resetsAtUtc: null, planKey: 'student-essential' }, []);
    expect(q(el, 'ask-quota-full')).not.toBeNull();

    const component = fixture.componentInstance;
    component.quota.set({ limit: 2, used: 0, pending: 0, resetsAtUtc: null, planKey: 'student-essential' });
    component.onPhotoPicked({ target: { files: [new File(['x'], 'q.jpg', { type: 'image/jpeg' })], value: '' } } as unknown as Event);
    service['send'].mockReturnValue(throwError(() => new HttpErrorResponse({ status: 429, error: { code: 'entitlement.limitReached' } })));
    component.send();
    fixture.detectChanges();

    expect(q(el, 'ask-form-error')!.textContent).toContain('soru hakkın doldu');
  });

  it('plan without the feature shows the locked card instead of the form', async () => {
    const el = await setup({ limit: 0, used: 0, pending: 0, resetsAtUtc: null, planKey: 'student-basic' }, []);

    expect(el.querySelector('[data-test="plan-locked"]')).not.toBeNull();
    expect(q(el, 'ask-send')).toBeNull();
    expect(service['teachers']).not.toHaveBeenCalled();
  });

  it('lists answers with images and rating, cancels open ones and highlights the linked question', async () => {
    const el = await setup({ limit: null, used: 0, pending: 1, resetsAtUtc: null, planKey: 'trial' }, [
      question(1),
      question(2, { status: 'Answered', answerText: 'Paydaları eşitle.', hasAnswerImage: true, rating: 2 }),
      question(3, { status: 'Expired' }),
    ], '2');

    const items = el.querySelectorAll<HTMLElement>('[data-testid="ask-question"]');
    expect(items).toHaveLength(3);
    expect(items[1].classList).toContain('at__item--highlight');
    expect(q(el, 'ask-answer')!.textContent).toContain('Paydaları eşitle.');
    // Soru görselleri + bir cevap görseli.
    expect(service['image']).toHaveBeenCalledWith(2, true);
    expect(items[2].textContent).toContain('hakkından düşülmedi');

    (items[1].querySelector('[data-testid="ask-star-5"]') as HTMLButtonElement).click();
    expect(service['rate']).toHaveBeenCalledWith(2, 5);

    (items[1].querySelector('[data-testid="ask-notebook-toggle"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(service['setNotebook']).toHaveBeenCalledWith(2, true);
    expect(fixture.componentInstance.questions()[1].inNotebook).toBe(true);
    expect(items[1].querySelector('[data-testid="ask-notebook-toggle"]')!.textContent).toContain('Defterimde');
    // Yalnız cevaplanan soru deftere eklenir.
    expect(items[0].querySelector('[data-testid="ask-notebook-toggle"]')).toBeNull();

    (items[0].querySelector('[data-testid="ask-cancel"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(service['cancel']).toHaveBeenCalledWith(1);
    expect(fixture.componentInstance.questions()[0].status).toBe('Cancelled');
    expect(fixture.componentInstance.quota()!.pending).toBe(0);
  });
});
