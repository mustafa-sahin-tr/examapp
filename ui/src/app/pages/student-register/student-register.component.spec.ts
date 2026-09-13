import { ComponentFixture, TestBed } from '@angular/core/testing';

import { StudentRegisterComponent } from './student-register.component';

import { translocoTestingModule } from '../../shared/testing/transloco-testing';
import registerTr from '../../../../public/i18n/register/tr.json';

/**
 * Sayfa cevirileri kendi Transloco scope'undadir (issue #183); testte gercek sozluk verilir,
 * sahte ceviri kullanilmaz - boylece bir anahtar bozulursa test kirilir.
 */
const translocoTesting = translocoTestingModule({ langs: { 'register/tr': registerTr } });

describe('StudentRegisterComponent', () => {
  let component: StudentRegisterComponent;
  let fixture: ComponentFixture<StudentRegisterComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StudentRegisterComponent, translocoTesting]
    })
    .compileComponents();

    fixture = TestBed.createComponent(StudentRegisterComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
